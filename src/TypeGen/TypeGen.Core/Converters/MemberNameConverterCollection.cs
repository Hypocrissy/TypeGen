using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace TypeGen.Core.Converters
{
    /// <summary>
    /// Represents a collection of member name converters
    /// </summary>
    public class MemberNameConverterCollection : List<IMemberNameConverter>, IMemberNameConverter
    {
        public MemberNameConverterCollection()
        {
        }

        public MemberNameConverterCollection(params IMemberNameConverter[] converters)
            : this((IEnumerable<IMemberNameConverter>)converters)
        {
        }

        public MemberNameConverterCollection(IEnumerable<IMemberNameConverter> converters)
        {
            Clear();
            AddRange(converters);
        }

        /// <summary>
        /// Converts a name using the chain of converters
        /// </summary>
        /// <param name="name"></param>
        /// <param name="memberInfo"></param>
        /// <returns></returns>
        public string Convert(string name, MemberInfo memberInfo)
        {
            return this.Aggregate(name, (current, converter) => converter.Convert(current, memberInfo));
        }
    }
}
