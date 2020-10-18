using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using TypeGen.Core.Extensions;
using TypeGen.Core.Logging;
using TypeGen.Core.Metadata;
using TypeGen.Core.TypeAnnotations;
using TypeGen.Core.Utils;
using TypeGen.Core.Validation;

namespace TypeGen.Core.Generator.Services
{
    /// <summary>
    /// Generates TypeScript file contents
    /// </summary>
    internal class TsContentGenerator : ITsContentGenerator
    {
        private readonly ITypeDependencyService _typeDependencyService;
        private readonly ITypeService _typeService;
        private readonly ITemplateService _templateService;
        private readonly ITsContentParser _tsContentParser;
        private readonly IMetadataReaderFactory _metadataReaderFactory;
        private readonly IGeneratorOptionsProvider _generatorOptionsProvider;
        private readonly ILogger _logger;

        private const string KeepTsTagName = "keep-ts";
        private const string CustomHeadTagName = "custom-head";
        private const string CustomBodyTagName = "custom-body";

        private GeneratorOptions GeneratorOptions => _generatorOptionsProvider.GeneratorOptions;

        public TsContentGenerator(ITypeDependencyService typeDependencyService,
            ITypeService typeService,
            ITemplateService templateService,
            ITsContentParser tsContentParser,
            IMetadataReaderFactory metadataReaderFactory,
            IGeneratorOptionsProvider generatorOptionsProvider,
            ILogger logger)
        {
            _typeDependencyService = typeDependencyService;
            _typeService = typeService;
            _templateService = templateService;
            _tsContentParser = tsContentParser;
            _metadataReaderFactory = metadataReaderFactory;
            _generatorOptionsProvider = generatorOptionsProvider;
            _logger = logger;
        }

        /// <summary>
        /// Gets code for the 'imports' section for a given type
        /// </summary>
        /// <param name="type"></param>
        /// <param name="outputDir">ExportTs... attribute's output dir</param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException">Thrown when one of: type, fileNameConverters or typeNameConverters is null</exception>
        public string GetImportsText(Type type, string outputDir)
        {
            Requires.NotNull(type, nameof(type));
            Requires.NotNull(GeneratorOptions.FileNameConverters, nameof(GeneratorOptions.FileNameConverters));
            Requires.NotNull(GeneratorOptions.TypeNameConverters, nameof(GeneratorOptions.TypeNameConverters));

            string result = GetTypeDependencyImportsText(type, outputDir);
            result += GetCustomImportsText(type);

            if (!string.IsNullOrEmpty(result))
            {
                result += "\r\n";
            }

            return result;
        }

        public string GetImportsText(IEnumerable<MethodInfo> methodInfos, string outputDir)
        {
            Requires.NotNull(methodInfos, nameof(methodInfos));
            Requires.NotNull(GeneratorOptions.FileNameConverters, nameof(GeneratorOptions.FileNameConverters));
            Requires.NotNull(GeneratorOptions.TypeNameConverters, nameof(GeneratorOptions.TypeNameConverters));

            var result = string.Join(Environment.NewLine, methodInfos.Select(x => GetImportsText(x, outputDir)));
            result = DistinctImports(result);
            return result;
        }

        public string GetImportsText(MethodInfo methodInfo, string outputDir)
        {
            Requires.NotNull(methodInfo, nameof(methodInfo));
            Requires.NotNull(GeneratorOptions.FileNameConverters, nameof(GeneratorOptions.FileNameConverters));
            Requires.NotNull(GeneratorOptions.TypeNameConverters, nameof(GeneratorOptions.TypeNameConverters));

            var result = GetTypeDependencyImportsText(methodInfo, outputDir);
            //result += GetCustomImportsText(type);

            if (!string.IsNullOrEmpty(result))
            {
                result += "\r\n";
            }

            result = DistinctImports(result);
            return result;
        }

        private string DistinctImports(string importText)
        {
            var importSplit = importText.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Distinct();
            importText = string.Join(Environment.NewLine, importSplit);
            return importText;
        }

        /// <summary>
        /// Gets the text for the "extends" section
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public string GetExtendsText(Type type)
        {
            Requires.NotNull(type, nameof(type));
            Requires.NotNull(GeneratorOptions.TypeNameConverters, nameof(GeneratorOptions.TypeNameConverters));

            var baseTypeName = GetBaseTypeNames(type);
            if (string.IsNullOrEmpty(baseTypeName))
                return string.Empty;
            return _templateService.GetExtendsText(baseTypeName);
        }

        /// <summary>
        /// Gets the text for the "extends" section for interfaces.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public string GetExtendsForInterfacesText(Type type)
        {
            var baseTypeNames = GetInterfaceNames(type);
            var baseTypeName = GetBaseTypeNames(type);
            if (!string.IsNullOrEmpty(baseTypeName))
                baseTypeNames = baseTypeNames.Concat(new[] { baseTypeName });

            if (!baseTypeNames.Any())
                return string.Empty;
            return _templateService.GetExtendsText(baseTypeNames);
        }

        /// <summary>
        /// Gets the text for the "implements" section
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public string GetImplementsText(Type type)
        {
            var baseTypeNames = GetInterfaceNames(type);
            if (!baseTypeNames.Any())
                return string.Empty;
            return _templateService.GetImplementsText(baseTypeNames);
        }

        private string GetBaseTypeNames(Type type)
        {
            Requires.NotNull(type, nameof(type));
            Requires.NotNull(GeneratorOptions.TypeNameConverters, nameof(GeneratorOptions.TypeNameConverters));

            Type baseType = _typeService.GetBaseType(type);
            if (baseType == null)
                return string.Empty;

            string baseTypeName = _typeService.GetTsTypeName(baseType, false);
            return baseTypeName;
        }

        private IEnumerable<string> GetInterfaceNames(Type type)
        {
            Requires.NotNull(type, nameof(type));
            Requires.NotNull(GeneratorOptions.TypeNameConverters, nameof(GeneratorOptions.TypeNameConverters));

            IEnumerable<Type> baseTypes = type.GetInterfaces(false);
            if (!baseTypes.Any()) return Enumerable.Empty<string>();

            IEnumerable<string> baseTypeNames = baseTypes.Select(baseType => _typeService.GetTsTypeName(baseType, true));
            return baseTypeNames;
        }

        /// <summary>
        /// Returns TypeScript imports source code related to type dependencies
        /// </summary>
        /// <param name="type"></param>
        /// <param name="outputDir"></param>
        /// <returns></returns>
        private string GetTypeDependencyImportsText(Type type, string outputDir)
        {
            if (!string.IsNullOrEmpty(outputDir) && !outputDir.EndsWith("/") && !outputDir.EndsWith("\\")) outputDir += "\\";
            var result = "";
            var typeDependencies = _typeDependencyService.GetTypeDependencies(type, false);

            // exclude base type dependency if TsCustomBaseAttribute is specified (it will be added in custom imports)
            if (_metadataReaderFactory.GetInstance().GetAttribute<TsCustomBaseAttribute>(type) != null)
            {
                typeDependencies = typeDependencies.Where(td => !td.IsBase);
            }

            foreach (var typeDependencyInfo in typeDependencies)
            {
                result += GetTypeImportsText(typeDependencyInfo, outputDir);
            }

            return result;
        }

        private string GetTypeDependencyImportsText(MethodInfo methodInfo, string outputDir)
        {
            if (!string.IsNullOrEmpty(outputDir) && !outputDir.EndsWith("/") && !outputDir.EndsWith("\\")) outputDir += "\\";
            var result = "";
            var typeDependencies = _typeDependencyService.GetMethodDependencies(methodInfo);

            foreach (var typeDependencyInfo in typeDependencies)
            {
                result += GetTypeImportsText(typeDependencyInfo, outputDir);
            }

            return result;
        }

        private string GetTypeImportsText(TypeDependencyInfo typeDependencyInfo, string outputDir)
        {
            var typeDependency = typeDependencyInfo.Type;
            string dependencyOutputDir = GetTypeDependencyOutputDir(typeDependencyInfo, outputDir);

            // get path diff
            string pathDiff = FileSystemUtils.GetPathDiff(outputDir, dependencyOutputDir);
            pathDiff = pathDiff.StartsWith("..\\") || pathDiff.StartsWith("../") ? pathDiff : $"./{pathDiff}";

            // get type & file name
            string typeDependencyName = typeDependency.Name.RemoveTypeArity();
            string fileName = GeneratorOptions.FileNameConverters.Convert(typeDependencyName, typeDependency);

            // get file path
            string dependencyPath = Path.Combine(pathDiff.EnsurePostfix("/"), fileName);
            dependencyPath = dependencyPath.Replace('\\', '/');

            string typeName = GeneratorOptions.TypeNameConverters.Convert(typeDependencyName, typeDependency);

            var result = _typeService.UseDefaultExport(typeDependency) ?
                _templateService.FillImportDefaultExportTemplate(typeName, dependencyPath) :
                _templateService.FillImportTemplate(typeName, "", dependencyPath);

            return result;
        }

        public string GetTypeImportsText(Type type, string outputDir)
        {
            return GetTypeImportsText(new TypeDependencyInfo(type), outputDir);
        }

        /// <summary>
        /// Gets code for imports that are specified in TsTypeAttribute.ImportPath or TsCustomBaseAttribute.ImportPath properties
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        private string GetCustomImportsText(Type type)
        {
            var resultLines = new List<string>();

            resultLines.AddRange(GetCustomImportsFromCustomBase(type));
            resultLines.AddRange(GetCustomImportsFromMembers(type));

            return string.Join("", resultLines.Distinct());
        }

        private IEnumerable<string> GetCustomImportsFromMembers(Type type)
        {
            IEnumerable<MemberInfo> members = type.GetTsExportableMembers(_metadataReaderFactory.GetInstance(), _generatorOptionsProvider.GeneratorOptions);

            IEnumerable<TsTypeAttribute> typeAttributes = members
                .Select(memberInfo => _metadataReaderFactory.GetInstance().GetAttribute<TsTypeAttribute>(memberInfo))
                .Where(tsTypeAttribute => !string.IsNullOrEmpty(tsTypeAttribute?.ImportPath))
                .Distinct(new TsTypeAttributeComparer());

            foreach (TsTypeAttribute attribute in typeAttributes)
            {
                yield return FillCustomImportTemplate(attribute.FlatTypeName, attribute.ImportPath, attribute.OriginalTypeName, attribute.IsDefaultExport);
            }
        }

        private IEnumerable<string> GetCustomImportsFromCustomBase(Type type)
        {
            var tsCustomBaseAttribute = _metadataReaderFactory.GetInstance().GetAttribute<TsCustomBaseAttribute>(type);
            if (tsCustomBaseAttribute == null || string.IsNullOrEmpty(tsCustomBaseAttribute.ImportPath)) yield break;

            yield return FillCustomImportTemplate(tsCustomBaseAttribute.Base, tsCustomBaseAttribute.ImportPath, tsCustomBaseAttribute.OriginalTypeName, tsCustomBaseAttribute.IsDefaultExport);
        }

        private string FillCustomImportTemplate(string typeName, string importPath, string originalTypeName, bool isDefaultExport)
        {
            bool withOriginalTypeName = !string.IsNullOrEmpty(originalTypeName);

            string name = withOriginalTypeName ? originalTypeName : typeName;
            string typeAlias = withOriginalTypeName ? typeName : null;

            return isDefaultExport ? _templateService.FillImportDefaultExportTemplate(name, importPath) :
                _templateService.FillImportTemplate(name, typeAlias, importPath);
        }

        /// <summary>
        /// Gets the output directory for a type dependency
        /// </summary>
        /// <param name="typeDependencyInfo"></param>
        /// <param name="parentTypeOutputDir"></param>
        /// <returns></returns>
        private string GetTypeDependencyOutputDir(TypeDependencyInfo typeDependencyInfo, string parentTypeOutputDir)
        {
            var classAttribute = _metadataReaderFactory.GetInstance().GetAttribute<ExportTsClassAttribute>(typeDependencyInfo.Type);
            var interfaceAttribute = _metadataReaderFactory.GetInstance().GetAttribute<ExportTsInterfaceAttribute>(typeDependencyInfo.Type);
            var enumAttribute = _metadataReaderFactory.GetInstance().GetAttribute<ExportTsEnumAttribute>(typeDependencyInfo.Type);

            if (classAttribute == null && enumAttribute == null && interfaceAttribute == null)
            {
                TsDefaultTypeOutputAttribute defaultTypeOutputAttribute = typeDependencyInfo.MemberAttributes
                    ?.SingleOrDefault(a => a.GetType() == typeof(TsDefaultTypeOutputAttribute))
                    as TsDefaultTypeOutputAttribute;

                return defaultTypeOutputAttribute?.OutputDir ?? parentTypeOutputDir;
            }

            return classAttribute?.OutputDir
                    ?? interfaceAttribute?.OutputDir
                    ?? enumAttribute?.OutputDir;
        }

        /// <summary>
        /// Gets custom code for a TypeScript file given by filePath.
        /// Returns an empty string if a file does not exist.
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="indentSize"></param>
        /// <returns></returns>
        public string GetCustomBody(string filePath, int indentSize)
        {
            Requires.NotNull(filePath, nameof(filePath));

            string content = _tsContentParser.GetTagContent(filePath, indentSize, KeepTsTagName, CustomBodyTagName);
            string tab = StringUtils.GetTabText(indentSize);

            return string.IsNullOrEmpty(content)
                ? ""
                : $"\r\n\r\n{tab}//<{CustomBodyTagName}>\r\n{tab}{content}{tab}//</{CustomBodyTagName}>";
        }

        /// <summary>
        /// Gets custom code for a TypeScript file given by filePath.
        /// Returns an empty string if a file does not exist.
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public string GetCustomHead(string filePath)
        {
            Requires.NotNull(filePath, nameof(filePath));

            string content = _tsContentParser.GetTagContent(filePath, 0, CustomHeadTagName);
            return string.IsNullOrEmpty(content)
                ? ""
                : $"//<{CustomHeadTagName}>\r\n{content}//</{CustomHeadTagName}>\r\n\r\n";
        }

        /// <summary>
        /// Gets text to be used as a member value
        /// </summary>
        /// <param name="memberInfo"></param>
        /// <returns>The text to be used as a member value. Null if the member has no value or value cannot be determined.</returns>
        public string GetMemberValueText(MemberInfo memberInfo)
        {
            if (memberInfo.DeclaringType == null) return null;

            try
            {
                object instance = memberInfo.IsStatic() ? null : ActivatorUtils.CreateInstanceAutoFillGenericParameters(memberInfo.DeclaringType);
                var valueObj = new object();
                object valueObjGuard = valueObj;

                switch (memberInfo)
                {
                    case FieldInfo fieldInfo:
                        valueObj = fieldInfo.GetValue(instance);
                        break;
                    case PropertyInfo propertyInfo:
                        valueObj = propertyInfo.GetValue(instance);
                        break;
                }

                // if valueObj hasn't been assigned in the switch
                if (valueObj == valueObjGuard) return null;

                // if valueObj's value is the default value for its type
                if (valueObj == null || valueObj.Equals(TypeUtils.GetDefaultValue(valueObj.GetType()))) return null;

                string memberType = _typeService.GetTsTypeName(memberInfo).GetTsTypeUnion(0);
                string quote = GeneratorOptions.SingleQuotes ? "'" : "\"";

                switch (valueObj)
                {
                    case Guid valueGuid when memberType == "string":
                        return quote + valueGuid + quote;
                    case DateTime valueDateTime when memberType == "Date":
                        return $@"new Date({quote}{valueDateTime}{quote})";
                    case DateTime valueDateTime when memberType == "string":
                        return quote + valueDateTime + quote;
                    case DateTimeOffset valueDateTimeOffset when memberType == "Date":
                        return $@"new Date({quote}{valueDateTimeOffset}{quote})";
                    case DateTimeOffset valueDateTimeOffset when memberType == "string":
                        return quote + valueDateTimeOffset + quote;
                    default:
                        return JsonConvert.SerializeObject(valueObj).Replace("\"", quote);
                }
            }
            catch (MissingMethodException e)
            {
                _logger?.Log($"Cannot determine the default value for member '{memberInfo.DeclaringType.FullName}.{memberInfo.Name}', because type '{memberInfo.DeclaringType.FullName}' has no default constructor.", LogLevel.Debug);
            }
            catch (ArgumentException e) when (e.InnerException is TypeLoadException)
            {
                _logger?.Log($"Cannot determine the default value for member '{memberInfo.DeclaringType.FullName}.{memberInfo.Name}', because type '{memberInfo.DeclaringType.FullName}' has generic parameters with base class or interface constraints.", LogLevel.Debug);
            }
            catch (Exception e)
            {
                _logger?.Log($"Cannot determine the default value for member '{memberInfo.DeclaringType.FullName}.{memberInfo.Name}', because an unknown exception occurred: '{e.Message}'", LogLevel.Debug);
            }

            return null;
        }
    }
}
