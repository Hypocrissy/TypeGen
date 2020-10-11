using System;
using System.Collections.Generic;
using TypeGen.Core.SpecGeneration;
using TypeGen.Core.Validation;

namespace TypeGen.Core.Metadata
{
    internal class MetadataReaderFactory : IMetadataReaderFactory
    {
        private IMetadataReader _instance;
        private IDictionary<Type, TypeSpec> _previousSpecs;

        private IDictionary<Type, TypeSpec> _generationSpec;
        public IDictionary<Type, TypeSpec> Specs
        {
            get => _generationSpec;
            set
            {
                _previousSpecs = _generationSpec;
                _generationSpec = value;
            }
        }

        public IMetadataReader GetInstance()
        {
            Requires.NotNull(Specs, nameof(Specs));

            if (_previousSpecs == Specs) return _instance;

            _instance = new GenerationSpecMetadataReader(Specs);
            _previousSpecs = Specs;
            
            return _instance;
        }
    }
}