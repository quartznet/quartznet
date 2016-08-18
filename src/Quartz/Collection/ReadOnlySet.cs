using System;
using System.Collections;
using System.Collections.Generic;

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
                throw new ArgumentNullException(nameof(internalSet));
            }

            this.internalSet = internalSet;
        }

        public void Add(T item)
        {
            throw new InvalidOperationException();
        }

        public void UnionWith(IEnumerable<T> other)
        {
            internalSet.UnionWith(other);
        }

        public void IntersectWith(IEnumerable<T> other)
        {
            internalSet.IntersectWith(other);
        }

        public void ExceptWith(IEnumerable<T> other)
        {
            internalSet.ExceptWith(other);
        }

        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            internalSet.SymmetricExceptWith(other);
        }

        public bool IsSubsetOf(IEnumerable<T> other)
        {
            return internalSet.IsSubsetOf(other);
        }

        public bool IsSupersetOf(IEnumerable<T> other)
        {
            return internalSet.IsSupersetOf(other);
        }

        public bool IsProperSupersetOf(IEnumerable<T> other)
        {
            return internalSet.IsProperSupersetOf(other);
        }

        public bool IsProperSubsetOf(IEnumerable<T> other)
        {
            return internalSet.IsProperSubsetOf(other);
        }

        public bool Overlaps(IEnumerable<T> other)
        {
            return internalSet.Overlaps(other);
        }

        public bool SetEquals(IEnumerable<T> other)
        {
            return internalSet.SetEquals(other);
        }

        bool ISet<T>.Add(T item)
        {
            return internalSet.Add(item);
        }

        public void Clear()
        {
            throw new InvalidOperationException();
        }

        public bool Contains(T item)
        {
            return internalSet.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            internalSet.CopyTo(array, arrayIndex);
        }

        public int Count => internalSet.Count;

        public bool IsReadOnly => true;

        public bool Remove(T item)
        {
            throw new InvalidOperationException();
        }

        public IEnumerator<T> GetEnumerator()
        {
            return internalSet.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return internalSet.GetEnumerator();
        }
    }
}