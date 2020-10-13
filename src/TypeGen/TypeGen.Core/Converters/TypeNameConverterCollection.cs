using System;
using System.Collections.Generic;
using System.Linq;

namespace TypeGen.Core.Converters
{
    /// <summary>
    /// Represents a collection of type name converters
    /// </summary>
    public class TypeNameConverterCollection : List<ITypeNameConverter>
    {
        public TypeNameConverterCollection()
        {
        }

        public TypeNameConverterCollection(params ITypeNameConverter[] converters)
            : this((IEnumerable<ITypeNameConverter>) converters)
        {
        }

        public TypeNameConverterCollection(IEnumerable<ITypeNameConverter> converters)
        {
            Clear();
            AddRange(converters);
        }

        /// <summary>
        /// Converts a type name using the chain of converters
        /// </summary>
        /// <param name="name"></param>
        /// <param name="type"></param>
        /// <returns></returns>
        public string Convert(string name, Type type)
        {
            return this.Aggregate(name, (current, converter) => converter.Convert(current, type));
        }
    }
}
