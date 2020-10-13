using TypeGen.Core.Generator;

namespace TypeGen.Core.SpecGeneration
{
    public class OnBeforeBarrelGenerationArgs
    {
        public GeneratorOptions GeneratorOptions { get; }
        public GenerationResult GenerationResult { get; }

        public OnBeforeBarrelGenerationArgs(GeneratorOptions generatorOptions, GenerationResult generationResult)
        {
            GeneratorOptions = generatorOptions;
            GenerationResult = generationResult;
        }
    }
}