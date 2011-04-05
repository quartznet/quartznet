using System;
using System.Collections.Generic;

namespace Quartz.Collection
{
    /// <summary>
    /// A wrapper for generic HashSet that brings a common interface.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    [Serializable]
    public class HashSet<T> : System.Collections.Generic.HashSet<T>, ISet<T>
    {
        public HashSet()
        {
        }

        public HashSet(IEnumerable<T> collection) : base(collection)
        {
        }
    }
}
