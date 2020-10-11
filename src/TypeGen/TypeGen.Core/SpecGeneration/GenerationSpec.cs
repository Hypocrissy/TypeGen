using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using TypeGen.Core.Generator;
using TypeGen.Core.TypeAnnotations;

namespace TypeGen.Core.SpecGeneration
{
    /// <summary>
    /// Base class for generation specs
    /// </summary>
    public abstract class GenerationSpec
    {
        internal GeneratorOptions Options { get; set; }
        internal IDictionary<MethodInfo, MethodSpec> MethodSpecs { get; }
        internal IDictionary<Type, TypeSpec> TypeSpecs { get; }
        internal IList<BarrelSpec> BarrelSpecs { get; }

        protected GenerationSpec()
        {
            TypeSpecs = new Dictionary<Type, TypeSpec>();
            BarrelSpecs = new List<BarrelSpec>();
            MethodSpecs = new Dictionary<MethodInfo, MethodSpec>();
        }

        public virtual void OnBeforeGeneration(OnBeforeGenerationArgs args)
        {
            Options = args.GeneratorOptions;
        }

        public virtual void OnBeforeBarrelGeneration(OnBeforeBarrelGenerationArgs args)
        {
        }

        /// <summary>
        /// Adds a class
        /// </summary>
        /// <param name="type"></param>
        /// <param name="outputDir"></param>
        /// <returns></returns>
        protected ClassSpecBuilder AddClass(Type type, string outputDir = null)
        {
            var typeSpec = AddTypeSpec(type, TypeSpecFactory.CreateClassTypeSpec(type, outputDir));
            return new ClassSpecBuilder(typeSpec);
        }

        /// <summary>
        /// Adds a class
        /// </summary>
        /// <param name="outputDir"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        protected Generic.ClassSpecBuilder<T> AddClass<T>(string outputDir = null) where T : class
        {
            var typeSpec = AddTypeSpec(typeof(T), TypeSpecFactory.CreateClassTypeSpec(typeof(T), outputDir));
            return new Generic.ClassSpecBuilder<T>(typeSpec);
        }

        /// <summary>
        /// Adds an interface
        /// </summary>
        /// <param name="type"></param>
        /// <param name="outputDir"></param>
        /// <returns></returns>
        protected InterfaceSpecBuilder AddInterface(Type type, string outputDir = null)
        {
            var typeSpec = AddTypeSpec(type, TypeSpecFactory.CreateInterfaceTypeSpec(type, outputDir));
            return new InterfaceSpecBuilder(typeSpec);
        }

        /// <summary>
        /// Adds an interface
        /// </summary>
        /// <param name="outputDir"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        protected Generic.InterfaceSpecBuilder<T> AddInterface<T>(string outputDir = null) where T : class
        {
            var typeSpec = AddTypeSpec(typeof(T), TypeSpecFactory.CreateInterfaceTypeSpec(typeof(T), outputDir));
            return new Generic.InterfaceSpecBuilder<T>(typeSpec);
        }

        /// <summary>
        /// Adds an enum
        /// </summary>
        /// <param name="type"></param>
        /// <param name="outputDir"></param>
        /// <param name="isConst"></param>
        /// <returns></returns>
        protected EnumSpecBuilder AddEnum(Type type, string outputDir = null, bool isConst = false)
        {
            var typeSpec = AddTypeSpec(type, TypeSpecFactory.CreateEnumTypeSpec(type, outputDir, isConst));
            return new EnumSpecBuilder(typeSpec);
        }

        /// <summary>
        /// Adds an enum
        /// </summary>
        /// <param name="outputDir"></param>
        /// <param name="isConst"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        protected Generic.EnumSpecBuilder<T> AddEnum<T>(string outputDir = null, bool isConst = false) where T : Enum
        {
            var typeSpec = AddTypeSpec(typeof(T), TypeSpecFactory.CreateEnumTypeSpec(typeof(T), outputDir, isConst));
            return new Generic.EnumSpecBuilder<T>(typeSpec);
        }

        private TypeSpec AddTypeSpec(Type type, TypeSpec typeSpec)
        {
            TypeSpecs[type] = typeSpec;

            return typeSpec;
        }

        /// <summary>
        /// Adds a barrel file for a specified directory
        /// </summary>
        /// <param name="directory"></param>
        /// <param name="barrelScope"></param>
        protected void AddBarrel(string directory, BarrelScope barrelScope = BarrelScope.Files | BarrelScope.Directories) => BarrelSpecs.Add(new BarrelSpec(directory, barrelScope));

        /// <summary>
        /// Adds an api method (and all its parameter / return type(s))
        /// </summary>
        /// <param name="methodInfo"></param>
        /// <param name="methodSpec"></param>
        protected internal void AddMethod(MethodInfo methodInfo, MethodSpec methodSpec)
        {
            MethodSpecs[methodInfo] = methodSpec;
        }

        /// <summary>
        /// Adds a type 
        /// </summary>
        /// <param name="t"></param>
        protected internal void AddType(Type t)
        {
            AddTypeSpec(t, TypeSpecFactory.CreateTypeSpec(t));
        }
    }
}