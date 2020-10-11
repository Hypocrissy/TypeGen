using System.Collections.Generic;

namespace TypeGen.Core.Extensions
{
    internal static class DictionaryExtension
    {
        /// <summary>
        /// Merges dict2 into dict1
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <param name="dict1"></param>
        /// <param name="dict2"></param>
        public static void Merge<T1, T2>(this IDictionary<T1, T2> dict1, IDictionary<T1, T2> dict2)
        {
            foreach (var item in dict2)
            {
                if (!dict1.ContainsKey(item.Key))
                    dict1[item.Key] = item.Value;
            }
        }
    }
}