using System;
using System.Collections.Generic;
using System.Data;

namespace Quartz.Collection
{
    public class ReadOnlySet<T> : ISet<T>
    {
        private readonly ISet<T> internalSet;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReadOnlySet{T}" /> class.
        /// </summary>
        /// <param name="internalSet">The internal set to wrap.</param>
        /// <exception cref="System.ArgumentNullException">internalSet</exception>
        public ReadOnlySet(ISet<T> internalSet)
        {
            if (internalSet == null)
            {
                throw new ArgumentNullException("internalSet");
            }

            this.internalSet = internalSet;
        }

        public void Add(T item)
        {
            throw new ReadOnlyException();
        }

        public void Clear()
        {
            throw new ReadOnlyException();
        }

        public bool Contains(T item)
        {
            return internalSet.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            internalSet.CopyTo(array, arrayIndex);
        }

        public int Count
        {
            get { return internalSet.Count; }
        }

        public bool IsReadOnly
        {
            get { return true; }
        }

        public bool Remove(T item)
        {
            throw new ReadOnlyException();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return internalSet.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return internalSet.GetEnumerator();
        }
    }
}