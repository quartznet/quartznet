using System.Collections.Generic;

namespace Quartz.Util
{
    /// <summary>
    /// Compatibility shim to support HashSet&lt;T&gt; as IReadOnlyCollection pre .NET 4.6.1.
    /// </summary>
    // ReSharper disable once RedundantExtendsListEntry
    internal class ReadOnlyCompatibleHashSet<T> : HashSet<T>, IReadOnlyCollection<T>
    {
        public ReadOnlyCompatibleHashSet()
        {
        }

        public ReadOnlyCompatibleHashSet(IEnumerable<T> collection) : base(collection)
        {
        }
    }
}