using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Quartz.Util
{
    /// <summary>
    /// Compatibility shim to support HashSet&lt;T&gt; as IReadOnlyCollection pre .NET 4.6.1.
    /// </summary>
    [Serializable]
    // ReSharper disable once RedundantExtendsListEntry
    internal class ReadOnlyCompatibleHashSet<T> : HashSet<T>, IReadOnlyCollection<T>
    {
        public ReadOnlyCompatibleHashSet()
        {
        }

        public ReadOnlyCompatibleHashSet(IEnumerable<T> collection) : base(collection)
        {
        }

        protected ReadOnlyCompatibleHashSet(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}