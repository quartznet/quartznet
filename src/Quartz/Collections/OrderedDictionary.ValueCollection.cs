// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections;
using System.Diagnostics;

namespace Quartz.Collections;

internal partial class OrderedDictionary<TKey, TValue>
{
    /// <summary>
    /// Represents the collection of values in a <see cref="OrderedDictionary{TKey, TValue}" />. This class cannot be inherited.
    /// </summary>
    [DebuggerTypeProxy(typeof(DictionaryValueCollectionDebugView<,>))]
    [DebuggerDisplay("Count = {Count}")]
    public sealed class ValueCollection : IList<TValue>, IReadOnlyList<TValue>
    {
        private readonly OrderedDictionary<TKey, TValue> _orderedDictionary;

        /// <summary>
        /// Gets the number of elements contained in the <see cref="OrderedDictionary{TKey, TValue}.ValueCollection" />.
        /// </summary>
        /// <returns>The number of elements contained in the <see cref="OrderedDictionary{TKey, TValue}.ValueCollection" />.</returns>
        public int Count => _orderedDictionary.Count;

        /// <summary>
        /// Gets the value at the specified index as an O(1) operation.
        /// </summary>
        /// <param name="index">The zero-based index of the value to get.</param>
        /// <returns>The value at the specified index.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> is less than 0.-or-<paramref name="index" /> is equal to or greater than <see cref="OrderedDictionary{TKey, TValue}.ValueCollection.Count" />.</exception>
        public TValue this[int index] => _orderedDictionary[index];

        TValue IList<TValue>.this[int index]
        {
            get => this[index];
            set => ThrowHelper.ThrowNotSupportedException();
        }

        bool ICollection<TValue>.IsReadOnly => true;

        internal ValueCollection(OrderedDictionary<TKey, TValue> orderedDictionary)
        {
            _orderedDictionary = orderedDictionary;
        }

        /// <summary>
        /// Returns an enumerator that iterates through the <see cref="OrderedDictionary{TKey, TValue}.ValueCollection" />.
        /// </summary>
        /// <returns>A <see cref="OrderedDictionary{TKey, TValue}.ValueCollection.Enumerator" /> for the <see cref="OrderedDictionary{TKey, TValue}.ValueCollection" />.</returns>
        public Enumerator GetEnumerator() => new Enumerator(_orderedDictionary);

        IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        int IList<TValue>.IndexOf(TValue item)
        {
            EqualityComparer<TValue> comparer = EqualityComparer<TValue>.Default;
            Entry[] entries = _orderedDictionary._entries;
            int count = Count;
            for (int i = 0; i < count; ++i)
            {
                if (comparer.Equals(entries[i].Value, item))
                {
                    return i;
                }
            }
            return -1;
        }

        void IList<TValue>.Insert(int index, TValue item) => ThrowHelper.ThrowNotSupportedException();

        void IList<TValue>.RemoveAt(int index) => ThrowHelper.ThrowNotSupportedException();

        void ICollection<TValue>.Add(TValue item) => ThrowHelper.ThrowNotSupportedException();

        void ICollection<TValue>.Clear() => ThrowHelper.ThrowNotSupportedException();

        bool ICollection<TValue>.Contains(TValue item) => ((IList<TValue>) this).IndexOf(item) >= 0;

        void ICollection<TValue>.CopyTo(TValue[] array, int arrayIndex)
        {
            if (array is null)
            {
                ThrowHelper.ThrowArgumentNullException(nameof(array));
            }
            if ((uint) arrayIndex > (uint) array.Length)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(nameof(arrayIndex));
            }
            int count = Count;
            if (array.Length - arrayIndex < count)
            {
                ThrowHelper.ThrowArgumentException("Invalid range");
            }

            Entry[] entries = _orderedDictionary._entries;
            for (int i = 0; i < count; ++i)
            {
                array[i + arrayIndex] = entries[i].Value;
            }
        }

        public TValue[] ToArray()
        {
            var count = Count;

            if (count == 0)
            {
                return Array.Empty<TValue>();
            }

            var entries = _orderedDictionary._entries;
            var array = new TValue[count];

            for (int i = 0; i < count; ++i)
            {
                array[i] = entries[i].Value;
            }

            return array;
        }

        bool ICollection<TValue>.Remove(TValue item)
        {
            ThrowHelper.ThrowNotSupportedException();
            return false;
        }

        /// <summary>
        /// Enumerates the elements of a <see cref="OrderedDictionary{TKey, TValue}.ValueCollection" />.
        /// </summary>
        public struct Enumerator : IEnumerator<TValue>
        {
            private readonly OrderedDictionary<TKey, TValue> _orderedDictionary;
            private readonly int _version;
            private int _index;
            private TValue _current;

            /// <summary>
            /// Gets the element at the current position of the enumerator.
            /// </summary>
            /// <returns>The element in the <see cref="OrderedDictionary{TKey, TValue}.ValueCollection" /> at the current position of the enumerator.</returns>
            public TValue Current => _current;

            object IEnumerator.Current => _current!;

            internal Enumerator(OrderedDictionary<TKey, TValue> orderedDictionary)
            {
                _orderedDictionary = orderedDictionary;
                _version = orderedDictionary._version;
                _index = 0;
                _current = default!;
            }

            /// <summary>
            /// Releases all resources used by the <see cref="OrderedDictionary{TKey, TValue}.ValueCollection.Enumerator" />.
            /// </summary>
            public void Dispose()
            {
            }

            /// <summary>
            /// Advances the enumerator to the next element of the <see cref="OrderedDictionary{TKey, TValue}.ValueCollection" />.
            /// </summary>
            /// <returns>true if the enumerator was successfully advanced to the next element; false if the enumerator has passed the end of the collection.</returns>
            /// <exception cref="InvalidOperationException">The collection was modified after the enumerator was created.</exception>
            public bool MoveNext()
            {
                if (_version != _orderedDictionary._version)
                {
                    ThrowHelper.ThrowInvalidOperationException();
                }

                if (_index < _orderedDictionary.Count)
                {
                    _current = _orderedDictionary._entries[_index].Value;
                    ++_index;
                    return true;
                }
                _current = default!;
                return false;
            }

            void IEnumerator.Reset()
            {
                if (_version != _orderedDictionary._version)
                {
                    ThrowHelper.ThrowInvalidOperationException();
                }

                _index = 0;
                _current = default!;
            }
        }
    }
}