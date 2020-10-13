using System;
using System.Collections.Generic;
using System.Reflection;

namespace TypeGen.Core.SpecGeneration
{
    public class MethodSpec
    {
        public string Method { get; set; }
        public string Path { get; set; }
    }

    public class ControllerSpec
    {
        public string OutputDir { get; set; }
        public IDictionary<MethodInfo, MethodSpec> Methods { get; set; } = new Dictionary<MethodInfo, MethodSpec>();
    }
}