using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;

namespace Quartz.Collection
{
    public class ReadOnlySet<T> : ISet<T>
    {
        private ISet<T> _internalSet;

        /// <summary>
        /// Initializes a new instance of the <see cref="ReadOnlySet{T}" /> class.
        /// </summary>
        /// <param name="internalSet">The internal set to wrap.</param>
        /// <exception cref="System.ArgumentNullException">internalSet</exception>
        public ReadOnlySet(ISet<T> internalSet)
        {
            if (internalSet == null)
                throw new ArgumentNullException("internalSet");

            _internalSet = internalSet;
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
            return _internalSet.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _internalSet.CopyTo(array, arrayIndex);
        }

        public int Count
        {
            get { return _internalSet.Count; }
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
            return _internalSet.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return _internalSet.GetEnumerator();
        }
    }
}
