using TypeGen.Core.Storage;

namespace TypeGen.Core.Generator.Services
{
    /// <summary>
    /// Fills templates with data
    /// </summary>
    internal class ScribanTemplateService : TemplateService
    {
        // dependencies

        private readonly IInternalStorage _internalStorage;
        private readonly IGeneratorOptionsProvider _generatorOptionsProvider;

        private GeneratorOptions GeneratorOptions => _generatorOptionsProvider.GeneratorOptions;

        public ScribanTemplateService(IInternalStorage internalStorage, IGeneratorOptionsProvider generatorOptionsProvider)
            : base(internalStorage, generatorOptionsProvider)
        {
            _internalStorage = internalStorage;
            _generatorOptionsProvider = generatorOptionsProvider;
        }
    }
}