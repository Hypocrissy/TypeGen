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
                dict1.Merge(item.Key, item.Value);
            }
        }

        /// <summary>
        /// Inserts Key / Value if not present
        /// </summary>
        /// <typeparam name="T1"></typeparam>
        /// <typeparam name="T2"></typeparam>
        /// <param name="dict1"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public static void Merge<T1, T2>(this IDictionary<T1, T2> dict1, T1 key, T2 value)
        {
            if (!dict1.ContainsKey(key))
                dict1[key] = value;
        }
    }
}