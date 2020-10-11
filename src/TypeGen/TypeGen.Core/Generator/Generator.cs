using Namotion.Reflection;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TypeGen.Core.Extensions;
using TypeGen.Core.Generator.Services;
using TypeGen.Core.Logging;
using TypeGen.Core.Metadata;
using TypeGen.Core.SpecGeneration;
using TypeGen.Core.Storage;
using TypeGen.Core.TypeAnnotations;
using TypeGen.Core.Validation;

namespace TypeGen.Core.Generator
{
    /// <summary>
    /// Class used for generating TypeScript files from C# types
    /// </summary>
    public class Generator
    {
        /// <summary>
        /// An event that fires when a file's content is generated
        /// </summary>
        public event EventHandler<FileContentGeneratedArgs> FileContentGenerated;

        /// <summary>
        /// An event that fires when all type dependencies are resolved
        /// </summary>
        public event EventHandler<AfterDependencyResolveArgs> AfterDependencyResolved;

        /// <summary>
        /// A logger instance used to log messages raised by a Generator instance
        /// </summary>
        public ILogger Logger { get; }

        /// <summary>
        /// Generator options. Cannot be null.
        /// </summary>
        public GeneratorOptions Options { get; }

        private readonly MetadataReaderFactory _metadataReaderFactory;
        private readonly ITypeService _typeService;
        private readonly ITypeDependencyService _typeDependencyService;
        private readonly ITemplateService _templateService;
        private readonly ITsContentGenerator _tsContentGenerator;
        private readonly IFileSystem _fileSystem;

        // keeps track of what types have been generated in the current session
        // private readonly GenerationContext _generationContext;

        public Generator(GeneratorOptions options, ILogger logger = null)
        {
            Requires.NotNull(options, nameof(options));

            //_generationContext = new GenerationContext();
            FileContentGenerated += OnFileContentGenerated;

            Options = options;
            Logger = logger;

            var generatorOptionsProvider = new GeneratorOptionsProvider { GeneratorOptions = options };

            var internalStorage = new InternalStorage();
            _fileSystem = new FileSystem();
            _metadataReaderFactory = new MetadataReaderFactory();
            _typeService = new TypeService(_metadataReaderFactory, generatorOptionsProvider);
            _typeDependencyService = new TypeDependencyService(_typeService, _metadataReaderFactory);
            _templateService = new TemplateService(internalStorage, generatorOptionsProvider);

            _tsContentGenerator = new TsContentGenerator(_typeDependencyService,
                _typeService,
                _templateService,
                new TsContentParser(_fileSystem),
                _metadataReaderFactory,
                generatorOptionsProvider,
                logger);
        }

        public Generator(ILogger logger) : this(new GeneratorOptions(), logger)
        {
        }

        public Generator() : this(new GeneratorOptions())
        {
        }

        /// <summary>
        /// For unit testing (mocking FileSystem)
        /// </summary>
        /// <param name="options"></param>
        /// <param name="fileSystem"></param>
        internal Generator(GeneratorOptions options, IFileSystem fileSystem) : this(options)
        {
            _fileSystem = fileSystem;
        }

        /// <summary>
        /// The default event handler for the FileContentGenerated event
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        protected virtual void OnFileContentGenerated(object sender, FileContentGeneratedArgs args)
        {
            _fileSystem.SaveFile(args.FilePath, args.FileContent);
        }

        /// <summary>
        /// Subscribes the default FileContentGenerated event handler, which saves generated sources to the file system
        /// </summary>
        public void SubscribeDefaultFileContentGeneratedHandler()
        {
            FileContentGenerated -= OnFileContentGenerated;
            FileContentGenerated += OnFileContentGenerated;
        }

        /// <summary>
        /// Unsubscribes the default FileContentGenerated event handler, which saves generated sources to the file system
        /// </summary>
        public void UnsubscribeDefaultFileContentGeneratedHandler()
        {
            FileContentGenerated -= OnFileContentGenerated;
        }

        private void InitializeGeneration(IDictionary<Type, TypeSpec> spec)
        {
            _metadataReaderFactory.Specs = spec;
        }

        /// <summary>
        /// Generates TypeScript files from a GenerationSpec
        /// </summary>
        /// <param name="generationSpecs"></param>
        /// <returns>Generated TypeScript file paths (relative to the Options.BaseOutputDirectory)</returns>
        public Task<GenerationResult> GenerateAsync(IEnumerable<GenerationSpec> generationSpecs)
        {
            return Task.Run(() => Generate(generationSpecs));
        }

        /// <summary>
        /// Generates TypeScript files from a GenerationSpec
        /// </summary>
        /// <param name="generationSpecs"></param>
        /// <returns>Generated TypeScript file paths (relative to the Options.BaseOutputDirectory)</returns>
        public GenerationResult Generate(IEnumerable<GenerationSpec> generationSpecs)
        {
            Requires.NotNullOrEmpty(generationSpecs, nameof(generationSpecs));

            var ret = new GenerationResult();

            //_generationContext.InitializeGroupGeneratedTypes();

            //OnBeforeGeneration
            Parallel.ForEach(generationSpecs, (generationSpec) =>
             {
                 generationSpec.OnBeforeGeneration(new OnBeforeGenerationArgs(Options));
             });

            //Merge TypeSpecs
            var specs = new Dictionary<Type, TypeSpec>();
            foreach (GenerationSpec generationSpec in generationSpecs)
            {
                //add all parameter and return types to specs
                foreach (var methodInfo in generationSpec.MethodSpecs.Keys)
                {
                    generationSpec.AddType(methodInfo.ReturnParameter.ParameterType);
                    foreach (var parameter in methodInfo.GetParameters())
                        generationSpec.AddType(parameter.ParameterType);
                }

                specs.Merge(generationSpec.TypeSpecs);
            }
            InitializeGeneration(specs);

            //Add Dependencies
            var specDependencies = new ConcurrentDictionary<Type, TypeSpec>();
            Parallel.ForEach(specs, (spec) =>
            {
                AddTypeDependencies(spec.Key, spec.Value, specs, specDependencies);
            });
            specs.Merge(specDependencies);
            AfterDependencyResolved?.Invoke(this, new AfterDependencyResolveArgs(specs));

            //Generate Files
            var typesToGenerate = specs.Where(x => !(_typeService.IsTsSimpleType(x.Key) ||
                        _typeService.IsCollectionType(x.Key) ||
                        _typeService.IsDictionaryType(x.Key) ||
                        x.Key.IsSystemType()))
                .Select(x => x.Key.AsGenericTypeDefinition())
                .Distinct()
                .ToList();

            //TODO Types to generate EVENT!

            var typeFiles = new ConcurrentBag<string>();
            Parallel.ForEach(typesToGenerate, (type) =>
            {
                var file = GenerateType(type);
                if (!file.IsNullOrEmpty())
                    typeFiles.Add(file);
            });

            var distfiles = typeFiles.Distinct().ToList();
            if (distfiles.Count != typeFiles.Count())
            {
                throw new CoreException("distfiles != files");
            }
            ret.Types = distfiles;

            //_generationContext.ClearGroupGeneratedTypes();

            //generate barrels

            if (Options.CreateIndexFile)
            {
                ret.Index = GenerateIndexFile(typeFiles);
            }

            Parallel.ForEach(generationSpecs, (generationSpec) =>
            {
                generationSpec.OnBeforeBarrelGeneration(new OnBeforeBarrelGenerationArgs(Options, typeFiles));
            });

            var barrelFiles = new ConcurrentBag<string>();
            Parallel.ForEach(generationSpecs, (generationSpec) =>
            {
                Parallel.ForEach(generationSpec.BarrelSpecs, (barrelSpec) =>
                {
                    foreach (var barrelFile in GenerateBarrel(barrelSpec))
                        barrelFiles.Add(barrelFile);
                });
            });
            ret.Barrels = barrelFiles.ToList();

            return ret;
        }

        private IEnumerable<string> GenerateBarrel(BarrelSpec barrelSpec)
        {
            string directory = Path.Combine(Options.BaseOutputDirectory?.EnsurePostfix("/") ?? "", barrelSpec.Directory);

            var fileName = "index";
            if (!string.IsNullOrWhiteSpace(Options.TypeScriptFileExtension)) fileName += $".{Options.TypeScriptFileExtension}";
            string filePath = Path.Combine(directory.EnsurePostfix("/"), fileName);

            var entries = new List<string>();

            if (barrelSpec.BarrelScope.HasFlag(BarrelScope.Files))
            {
                entries.AddRange(_fileSystem.GetDirectoryFiles(directory)
                    .Where(x => Path.GetFileName(x) != fileName && x.EndsWith($".{Options.TypeScriptFileExtension}"))
                    .Select(Path.GetFileNameWithoutExtension));
            }

            if (barrelSpec.BarrelScope.HasFlag(BarrelScope.Directories))
            {
                entries.AddRange(
                    _fileSystem.GetDirectoryDirectories(directory)
                        .Select(dir => dir.Replace("\\", "/").Split('/').Last())
                    );
            }

            string indexExportsContent = entries.Aggregate("", (acc, entry) => acc += _templateService.FillIndexExportTemplate(entry));
            string content = _templateService.FillIndexTemplate(indexExportsContent);

            FileContentGenerated?.Invoke(this, new FileContentGeneratedArgs(null, filePath, content));
            return new[] { Path.Combine(barrelSpec.Directory.EnsurePostfix("/"), fileName) };
        }

        /// <summary>
        /// DEPRECATED, will be removed in the future.
        /// Generates an `index.ts` file which exports all types within the generated files
        /// </summary>
        /// <param name="generatedFiles"></param>
        /// <returns>Generated TypeScript file paths (relative to the Options.BaseOutputDirectory)</returns>
        private string GenerateIndexFile(IEnumerable<string> generatedFiles)
        {
            var typeScriptFileExtension = "";
            if (!string.IsNullOrEmpty(Options.TypeScriptFileExtension))
            {
                typeScriptFileExtension = "." + Options.TypeScriptFileExtension;
            }

            string exports = generatedFiles.Aggregate("", (prevExports, file) =>
            {
                string fileNameWithoutExt = file.Remove(file.Length - typeScriptFileExtension.Length).Replace("\\", "/");
                return prevExports + _templateService.FillIndexExportTemplate(fileNameWithoutExt);
            });
            string content = _templateService.FillIndexTemplate(exports);

            string filename = "index" + typeScriptFileExtension;
            FileContentGenerated?.Invoke(this, new FileContentGeneratedArgs(null, Path.Combine(Options.BaseOutputDirectory, filename), content));

            return filename;
        }

        /// <summary>
        /// Contains the actual logic of generating TypeScript files for a given type
        /// Should only be used inside GenerateTypeInit(), otherwise use GenerateTypeInit()
        /// </summary>
        /// <param name="type"></param>
        /// <returns>Generated TypeScript file paths (relative to the Options.BaseOutputDirectory)</returns>
        private string GenerateType(Type type)
        {
            var classAttribute = _metadataReaderFactory.GetInstance().GetAttribute<ExportTsClassAttribute>(type);
            var interfaceAttribute = _metadataReaderFactory.GetInstance().GetAttribute<ExportTsInterfaceAttribute>(type);
            var enumAttribute = _metadataReaderFactory.GetInstance().GetAttribute<ExportTsEnumAttribute>(type);

            if (classAttribute != null || interfaceAttribute != null)
            {
                return GenerateClassOrInterface(type, (classAttribute as ExportAttribute) ?? interfaceAttribute);
            }

            if (enumAttribute != null)
            {
                return GenerateEnum(type, enumAttribute);
            }

            return string.Empty;

            //throw new CoreException($"Generated type must be either a C# class, interface or enum. Error when generating type {type.FullName}");
        }

        /// <summary>
        /// Generates TypeScript files from an assembly
        /// </summary>
        /// <param name="assembly"></param>
        /// <returns>Generated TypeScript file paths (relative to the Options.BaseOutputDirectory)</returns>
        public Task<GenerationResult> GenerateAsync(Assembly assembly)
        {
            return Task.Run(() => Generate(assembly));
        }

        /// <summary>
        /// Generates TypeScript files from an assembly
        /// </summary>
        /// <param name="assembly"></param>
        /// <returns>Generated TypeScript file paths (relative to the Options.BaseOutputDirectory)</returns>
        public GenerationResult Generate(Assembly assembly)
        {
            Requires.NotNull(assembly, nameof(assembly));
            return Generate(new[] { assembly });
        }

        /// <summary>
        /// Generates TypeScript files from multiple assemblies
        /// </summary>
        /// <param name="assemblies"></param>
        /// <returns>Generated TypeScript file paths (relative to the Options.BaseOutputDirectory)</returns>
        public Task<GenerationResult> GenerateAsync(IEnumerable<Assembly> assemblies)
        {
            return Task.Run(() => Generate(assemblies));
        }

        /// <summary>
        /// Generates TypeScript files from multiple assemblies
        /// </summary>
        /// <param name="assemblies"></param>
        /// <returns>Generated TypeScript file paths (relative to the Options.BaseOutputDirectory)</returns>
        public GenerationResult Generate(IEnumerable<Assembly> assemblies)
        {
            Requires.NotNullOrEmpty(assemblies, nameof(assemblies));

            var generationSpecProvider = new GenerationSpecProvider();
            GenerationSpec generationSpec = generationSpecProvider.GetGenerationSpec(assemblies);

            return Generate(new[] { generationSpec });
        }

        /// <summary>
        /// Generates TypeScript files from a type
        /// </summary>
        /// <param name="type"></param>
        /// <returns>Generated TypeScript file paths (relative to the Options.BaseOutputDirectory)</returns>
        public Task<GenerationResult> GenerateAsync(Type type)
        {
            return Task.Run(() => Generate(type));
        }

        /// <summary>
        /// Generates TypeScript files from a type
        /// </summary>
        /// <param name="type"></param>
        /// <returns>Generated TypeScript file paths (relative to the Options.BaseOutputDirectory)</returns>
        public GenerationResult Generate(Type type)
        {
            Requires.NotNull(type, nameof(type));

            var generationSpecProvider = new GenerationSpecProvider();
            GenerationSpec generationSpec = generationSpecProvider.GetGenerationSpec(type);

            return Generate(new[] { generationSpec });
        }

        /// <summary>
        /// Generates type dependencies' for a given type
        /// </summary>
        /// <param name="type"></param>
        /// <param name="typeSpec"></param>
        /// <param name="typeSpecs"></param>
        /// <param name="typeSpecsDependencies"></param>
        private void AddTypeDependencies(Type type, TypeSpec typeSpec,
            IDictionary<Type, TypeSpec> typeSpecs, ConcurrentDictionary<Type, TypeSpec> typeSpecsDependencies)
        {
            var typeDependencies = _typeDependencyService.GetTypeDependencies(type, true);

            foreach (var typeDependencyInfo in typeDependencies)
            {
                var dependencyType = typeDependencyInfo.Type;

                if (!typeSpecs.ContainsKey(dependencyType))
                {
                    if (typeSpecsDependencies.TryAdd(dependencyType, null))
                    {
                        var dependencyTypeSpec = TypeSpecFactory.CreateTypeSpec(dependencyType, typeSpec.ExportAttribute?.OutputDir);
                        typeSpecsDependencies[dependencyType] = dependencyTypeSpec;

                        AddTypeDependencies(dependencyType, dependencyTypeSpec, typeSpecs, typeSpecsDependencies);
                    }
                }
            }
        }

        private string GenerateClassOrInterface(Type type, ExportAttribute attribute)
        {
            if (Options.ClassAsInterface)
            {
                attribute = (attribute as ExportTsInterfaceAttribute) ?? new ExportTsInterfaceAttribute { OutputDir = attribute.OutputDir };
            }

            var outputDir = attribute.OutputDir;

            // generate the file content
            var importsText = _tsContentGenerator.GetImportsText(type, outputDir);
            var tsTypeName = _typeService.GetTsTypeName(type, true);
            var tsTypeNameFirstPart = tsTypeName.RemoveTypeGenericComponent();
            var filePath = GetFilePath(type, outputDir);
            var filePathRelative = GetRelativeFilePath(type, outputDir);
            var customHead = _tsContentGenerator.GetCustomHead(filePath);
            var customBody = _tsContentGenerator.GetCustomBody(filePath, Options.TabLength);

            string content;
            if (attribute is ExportTsClassAttribute)
            {
                string implementsText = _tsContentGenerator.GetImplementsText(type);
                var propertiesText = GetClassPropertiesText(type);

                var tsCustomBaseAttribute = _metadataReaderFactory.GetInstance().GetAttribute<TsCustomBaseAttribute>(type);
                var extendsText = string.Empty;
                if (tsCustomBaseAttribute != null)
                {
                    extendsText = string.IsNullOrEmpty(tsCustomBaseAttribute.Base) ? "" : _templateService.GetExtendsText(tsCustomBaseAttribute.Base);
                }
                else if (_metadataReaderFactory.GetInstance().GetAttribute<TsIgnoreBaseAttribute>(type) == null)
                {
                    extendsText = _tsContentGenerator.GetExtendsText(type);
                }

                content = _typeService.UseDefaultExport(type) ?
                    _templateService.FillClassDefaultExportTemplate(importsText, tsTypeName, tsTypeNameFirstPart, extendsText, implementsText, propertiesText, customHead, customBody, GenerateComment(type), Options.FileHeading) :
                    _templateService.FillClassTemplate(importsText, tsTypeName, extendsText, implementsText, propertiesText, customHead, customBody, GenerateComment(type), Options.FileHeading);
            }
            else if (attribute is ExportTsInterfaceAttribute)
            {
                var extendsText = _tsContentGenerator.GetExtendsForInterfacesText(type);
                var propertiesText = GetInterfacePropertiesText(type);

                content = _typeService.UseDefaultExport(type) ?
                    _templateService.FillInterfaceDefaultExportTemplate(importsText, tsTypeName, tsTypeNameFirstPart, extendsText, propertiesText, customHead, customBody, GenerateComment(type), Options.FileHeading) :
                    _templateService.FillInterfaceTemplate(importsText, tsTypeName, extendsText, propertiesText, customHead, customBody, GenerateComment(type), Options.FileHeading);
            }
            else
            {
                throw new CoreException($"Type {type.Name} has neither an ExportTsClassAttribute or an ExportTsInterfaceAttribute!");
            }

            // write TypeScript file
            FileContentGenerated?.Invoke(this, new FileContentGeneratedArgs(type, filePath, content));
            return filePathRelative;
        }

        /// <summary>
        /// Generates a TypeScript enum file from a class type
        /// </summary>
        /// <param name="type"></param>
        /// <param name="enumAttribute"></param>
        /// <returns>Generated TypeScript file paths (relative to the Options.BaseOutputDirectory)</returns>
        private string GenerateEnum(Type type, ExportTsEnumAttribute enumAttribute)
        {
            string valuesText = GetEnumMembersText(type);

            // create TypeScript source code for the enum
            string tsEnumName = _typeService.GetTsTypeName(type, true);
            string filePath = GetFilePath(type, enumAttribute.OutputDir);
            string filePathRelative = GetRelativeFilePath(type, enumAttribute.OutputDir);

            string enumText = _typeService.UseDefaultExport(type) ?
                _templateService.FillEnumDefaultExportTemplate("", tsEnumName, valuesText, enumAttribute.IsConst, GenerateComment(type), Options.FileHeading) :
                _templateService.FillEnumTemplate("", tsEnumName, valuesText, enumAttribute.IsConst, GenerateComment(type), Options.FileHeading);

            // write TypeScript file
            FileContentGenerated?.Invoke(this, new FileContentGeneratedArgs(type, filePath, enumText));
            return filePathRelative;
        }

        private bool IsStaticTsProperty(MemberInfo memberInfo)
        {
            if (_metadataReaderFactory.GetInstance().GetAttribute<TsNotStaticAttribute>(memberInfo) != null) return false;
            return _metadataReaderFactory.GetInstance().GetAttribute<TsStaticAttribute>(memberInfo) != null || memberInfo.IsStatic();
        }

        private bool IsReadonlyTsProperty(MemberInfo memberInfo)
        {
            if (_metadataReaderFactory.GetInstance().GetAttribute<TsNotReadonlyAttribute>(memberInfo) != null) return false;
            return _metadataReaderFactory.GetInstance().GetAttribute<TsReadonlyAttribute>(memberInfo) != null || (memberInfo is FieldInfo fi && (fi.IsInitOnly || fi.IsLiteral));
        }

        /// <summary>
        /// Gets TypeScript class property definition source code
        /// </summary>
        /// <param name="memberInfo"></param>
        /// <returns></returns>
        private string GetClassPropertyText(MemberInfo memberInfo)
        {
            LogClassPropertyWarnings(memberInfo);

            string modifiers = Options.ExplicitPublicAccessor ? "public " : "";

            if (IsStaticTsProperty(memberInfo)) modifiers += "static ";
            if (IsReadonlyTsProperty(memberInfo)) modifiers += "readonly ";

            var nameAttribute = _metadataReaderFactory.GetInstance().GetAttribute<TsMemberNameAttribute>(memberInfo);
            string name = nameAttribute?.Name ?? Options.PropertyNameConverters.Convert(memberInfo.Name, memberInfo);
            string typeName = _typeService.GetTsTypeName(memberInfo);
            IEnumerable<string> typeUnions = _typeService.GetTypeUnions(memberInfo);

            // try to get default value from TsDefaultValueAttribute
            var defaultValueAttribute = _metadataReaderFactory.GetInstance().GetAttribute<TsDefaultValueAttribute>(memberInfo);
            if (defaultValueAttribute != null)
                return _templateService.FillClassPropertyTemplate(modifiers, name, typeName, GenerateComment(memberInfo, true), typeUnions, defaultValueAttribute.DefaultValue);

            // try to get default value from the member's default value
            string valueText = _tsContentGenerator.GetMemberValueText(memberInfo);
            if (!string.IsNullOrWhiteSpace(valueText))
                return _templateService.FillClassPropertyTemplate(modifiers, name, typeName, GenerateComment(memberInfo, true), typeUnions, valueText);

            // try to get default value from Options.DefaultValuesForTypes
            if (Options.DefaultValuesForTypes.Any() && Options.DefaultValuesForTypes.ContainsKey(typeName))
                return _templateService.FillClassPropertyTemplate(modifiers, name, typeName, GenerateComment(memberInfo, true), typeUnions, Options.DefaultValuesForTypes[typeName]);

            return _templateService.FillClassPropertyTemplate(modifiers, name, typeName, GenerateComment(memberInfo, true), typeUnions, null);
        }

        private void LogClassPropertyWarnings(MemberInfo memberInfo)
        {
            if (Logger == null) return;

            if (_metadataReaderFactory.GetInstance().GetAttribute<TsOptionalAttribute>(memberInfo) != null)
                Logger.Log($"TsOptionalAttribute used for a class property ({memberInfo.DeclaringType?.FullName}.{memberInfo.Name}). The attribute will be ignored.", LogLevel.Warning);
        }

        /// <summary>
        /// Gets TypeScript class properties definition source code
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private string GetClassPropertiesText(Type type)
        {
            var propertiesText = "";
            IEnumerable<MemberInfo> memberInfos = type.GetTsExportableMembers(_metadataReaderFactory.GetInstance());

            // create TypeScript source code for properties' definition

            propertiesText += memberInfos
                .Aggregate(propertiesText, (current, memberInfo) => current + GetClassPropertyText(memberInfo));

            return RemoveLastLineEnding(propertiesText);
        }

        /// <summary>
        /// Gets TypeScript interface property definition source code
        /// </summary>
        /// <param name="memberInfo"></param>
        /// <returns></returns>
        private string GetInterfacePropertyText(MemberInfo memberInfo)
        {
            LogInterfacePropertyWarnings(memberInfo);

            string modifiers = "";
            if (IsReadonlyTsProperty(memberInfo)) modifiers += "readonly ";

            var nameAttribute = _metadataReaderFactory.GetInstance().GetAttribute<TsMemberNameAttribute>(memberInfo);
            string name = nameAttribute?.Name ?? Options.PropertyNameConverters.Convert(memberInfo.Name, memberInfo);

            string typeName = _typeService.GetTsTypeName(memberInfo);
            IEnumerable<string> typeUnions = _typeService.GetTypeUnions(memberInfo);
            bool isOptional = _metadataReaderFactory.GetInstance().GetAttribute<TsOptionalAttribute>(memberInfo) != null;

            return _templateService.FillInterfacePropertyTemplate(modifiers, name, typeName, GenerateComment(memberInfo, true), typeUnions, isOptional);
        }

        private void LogInterfacePropertyWarnings(MemberInfo memberInfo)
        {
            if (Logger == null) return;

            if (_metadataReaderFactory.GetInstance().GetAttribute<TsStaticAttribute>(memberInfo) != null)
                Logger.Log($"TsStaticAttribute used for an interface property ({memberInfo.DeclaringType?.FullName}.{memberInfo.Name}). The attribute will be ignored.", LogLevel.Warning);

            if (_metadataReaderFactory.GetInstance().GetAttribute<TsNotStaticAttribute>(memberInfo) != null)
                Logger.Log($"TsNotStaticAttribute used for an interface property ({memberInfo.DeclaringType?.FullName}.{memberInfo.Name}). The attribute will be ignored.", LogLevel.Warning);

            if (_metadataReaderFactory.GetInstance().GetAttribute<TsDefaultValueAttribute>(memberInfo) != null)
                Logger.Log($"TsDefaultValueAttribute used for an interface property ({memberInfo.DeclaringType?.FullName}.{memberInfo.Name}). The attribute will be ignored.", LogLevel.Warning);
        }

        /// <summary>
        /// Gets TypeScript interface properties definition source code
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private string GetInterfacePropertiesText(Type type)
        {
            var propertiesText = "";
            IEnumerable<MemberInfo> memberInfos = type.GetTsExportableMembers(_metadataReaderFactory.GetInstance());

            // create TypeScript source code for properties' definition

            propertiesText += memberInfos
                .Aggregate(propertiesText, (current, memberInfo) => current + GetInterfacePropertyText(memberInfo));

            return RemoveLastLineEnding(propertiesText);
        }

        /// <summary>
        /// Gets TypeScript enum member definition source code
        /// </summary>
        /// <param name="fieldInfo">MemberInfo for an enum value</param>
        /// <returns></returns>
        private string GetEnumMemberText(FieldInfo fieldInfo)
        {
            Type type = fieldInfo.DeclaringType;

            string name = Options.EnumValueNameConverters.Convert(fieldInfo.Name, fieldInfo);
            var stringInitializersAttribute = _metadataReaderFactory.GetInstance().GetAttribute<TsStringInitializersAttribute>(type);

            if ((Options.EnumStringInitializers && (stringInitializersAttribute == null || stringInitializersAttribute.Enabled)) ||
                (stringInitializersAttribute != null && stringInitializersAttribute.Enabled))
            {
                string enumValueString = Options.EnumStringInitializersConverters.Convert(fieldInfo.Name, fieldInfo);
                return _templateService.FillEnumValueTemplate(name, enumValueString, GenerateComment(fieldInfo, true));
            }

            object enumValue = fieldInfo.GetValue(null);
            object enumValueAsUnderlyingType = Convert.ChangeType(enumValue, Enum.GetUnderlyingType(type));
            return _templateService.FillEnumValueTemplate(name, enumValueAsUnderlyingType, GenerateComment(fieldInfo, true));
        }

        /// <summary>
        /// Gets TypeScript enum member definition source code
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private string GetEnumMembersText(Type type)
        {
            var valuesText = "";
            IEnumerable<FieldInfo> fieldInfos = type.GetFields(BindingFlags.Public | BindingFlags.Static);

            valuesText += fieldInfos.Aggregate(valuesText, (current, fieldInfo) => current + GetEnumMemberText(fieldInfo));

            return RemoveLastLineEnding(valuesText);
        }

        /// <summary>
        /// Gets the output TypeScript file path based on a type.
        /// The path is relative to the base output directory.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="outputDir"></param>
        /// <returns></returns>
        private string GetRelativeFilePath(Type type, string outputDir)
        {
            string typeName = type.Name.RemoveTypeArity();
            string fileName = Options.FileNameConverters.Convert(typeName, type);

            if (!string.IsNullOrEmpty(Options.TypeScriptFileExtension))
            {
                fileName += $".{Options.TypeScriptFileExtension}";
            }

            return string.IsNullOrEmpty(outputDir)
                ? fileName
                : Path.Combine(outputDir.EnsurePostfix("/"), fileName);
        }

        /// <summary>
        /// Gets the output TypeScript file path based on a type.
        /// The path includes base output directory.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="outputDir"></param>
        /// <returns></returns>
        private string GetFilePath(Type type, string outputDir)
        {
            string fileName = GetRelativeFilePath(type, outputDir);
            return Path.Combine(Options.BaseOutputDirectory?.EnsurePostfix("/") ?? "", fileName);
        }

        private static string RemoveLastLineEnding(string propertiesText)
        {
            return propertiesText.TrimEnd('\r', '\n');
        }

        private string GenerateComment(MemberInfo memberInfo, bool withTab = false)
        {
            var tab = withTab ? "$tg{tab}" : string.Empty;
            var comment = StripNewLine(memberInfo.GetXmlDocsSummary() ?? "");
            if (!string.IsNullOrEmpty(comment))
            {
                comment = tab + " * " + comment + Environment.NewLine;
            }

            var obsoleteStr = string.Empty;
            var obsolete = memberInfo.GetCustomAttributes<ObsoleteAttribute>().FirstOrDefault();
            if (obsolete != null)
            {
                var message = StripNewLine(obsolete.Message ?? "");
                obsoleteStr = tab + " * @deprecated " + message + Environment.NewLine;
            }

            if (!string.IsNullOrEmpty(comment) || !string.IsNullOrEmpty(obsoleteStr))
            {
                var fullComment = tab + "/**" + Environment.NewLine +
                                  comment +
                                  obsoleteStr +
                                  tab + " */ " + Environment.NewLine;
                return fullComment;
            }

            return string.Empty;
        }

        private string StripNewLine(string str)
        {
            return str
                .Replace(Environment.NewLine, "")
                .Replace('\r', ' ')
                .Replace('\n', ' ');
        }
    }

    internal static class TypeSpecFactory
    {
        public static TypeSpec CreateTypeSpec(Type type, string outputDirectory = null, bool isConst = false)
        {
            var typeInfo = type.GetTypeInfo();
            if (typeInfo.IsClass)
            {
                return CreateClassTypeSpec(type, outputDirectory);
            }

            if (typeInfo.IsInterface)
            {
                return CreateInterfaceTypeSpec(type, outputDirectory);
            }

            if (typeInfo.IsEnum)
            {
                return CreateEnumTypeSpec(type, outputDirectory, isConst);
            }

            return new TypeSpec(null);
        }

        public static TypeSpec CreateClassTypeSpec(Type type, string outputDirectory = null)
        {
            return new TypeSpec(new ExportTsClassAttribute { OutputDir = outputDirectory });
        }

        public static TypeSpec CreateInterfaceTypeSpec(Type type, string outputDirectory = null)
        {
            return new TypeSpec(new ExportTsInterfaceAttribute { OutputDir = outputDirectory });
        }

        public static TypeSpec CreateEnumTypeSpec(Type type, string outputDirectory = null, bool isConst = false)
        {
            return new TypeSpec(new ExportTsEnumAttribute { OutputDir = outputDirectory, IsConst = isConst });
        }
    }

    public class GenerationResult
    {
        public List<string> Types { get; set; }
        public List<string> Services { get; set; }
        public List<string> Barrels { get; set; }
        public string Index { get; set; }
    }
}