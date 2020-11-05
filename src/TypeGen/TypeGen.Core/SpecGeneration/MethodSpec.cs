﻿using System.Collections.Generic;
using System.Reflection;

namespace TypeGen.Core.SpecGeneration
{
    public class MethodSpec
    {
        public string Method { get; set; }
        public string Path { get; set; }
        public bool IsFormData { get; set; }
    }

    public class ControllerSpec
    {
        public string OutputDir { get; set; }
        public IDictionary<MethodInfo, MethodSpec> Methods { get; set; } = new Dictionary<MethodInfo, MethodSpec>();
    }

    public class HubSpec
    {
        public string OutputDir { get; set; }
        public HashSet<MethodInfo> Events { get; set; } = new HashSet<MethodInfo>();
        public HashSet<MethodInfo> Functions { get; set; } = new HashSet<MethodInfo>();
    }
}