using System;
using System.Collections.Generic;
using TypeGen.Core.SpecGeneration;

namespace TypeGen.Core.Generator
{
    public class AfterDependencyResolveArgs
    {
        public Dictionary<Type, TypeSpec> Specs { get; }

        public AfterDependencyResolveArgs(Dictionary<Type, TypeSpec> specs)
        {
            this.Specs = specs;
        }
    }
}