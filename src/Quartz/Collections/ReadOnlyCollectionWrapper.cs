using System.Collections;
using System.Collections.Generic;

namespace Quartz.Collections
{
    internal sealed class ReadOnlyCollectionWrapper<T> : IReadOnlyCollection<T>
    {
        private readonly ICollection<T> values;

        public ReadOnlyCollectionWrapper(ICollection<T> values)
        {
            this.values = values;
        }

        public IEnumerator<T> GetEnumerator() => values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public int Count => values.Count;
    }
}