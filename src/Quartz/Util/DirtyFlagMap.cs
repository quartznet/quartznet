/* 
* Copyright 2004-2005 OpenSymphony 
* 
* Licensed under the Apache License, Version 2.0 (the "License"); you may not 
* use this file except in compliance with the License. You may obtain a copy 
* of the License at 
* 
*   http://www.apache.org/licenses/LICENSE-2.0 
*   
* Unless required by applicable law or agreed to in writing, software 
* distributed under the License is distributed on an "AS IS" BASIS, WITHOUT 
* WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the 
* License for the specific language governing permissions and limitations 
* under the License.
* 
*/

/*
* Previously Copyright (c) 2001-2004 James House
*/

using System;
using System.Collections;

using Quartz.Collection;

namespace Quartz.Util
{
    /// <summary>
    /// An implementation of <see cref="IDictionary" /> that wraps another <see cref="IDictionary" />
    /// and flags itself 'dirty' when it is modified.
    /// </summary>
    /// <author>James House</author>
    [Serializable]
    public class DirtyFlagMap : IDictionary, ICloneable
    {
        private bool dirty = false;
        [NonSerialized] private bool locked = false;
        private Hashtable map;
        private readonly object syncRoot = new object();

        /// <summary>
        /// Create a DirtyFlagMap that 'wraps' a <see cref="Hashtable" />.
        /// </summary>
        public DirtyFlagMap()
        {
            map = new Hashtable();
        }

        /// <summary>
        /// Create a DirtyFlagMap that 'wraps' a <see cref="Hashtable" /> that has the
        /// given initial capacity.
        /// </summary>
        public DirtyFlagMap(int initialCapacity)
        {
            map = new Hashtable(initialCapacity);
        }

        /// <summary>
        /// Create a DirtyFlagMap that 'wraps' a <see cref="Hashtable" /> that has the
        /// given initial capacity and load factor.
        /// </summary>
        public DirtyFlagMap(int initialCapacity, float loadFactor)
        {
            map = new Hashtable(initialCapacity, loadFactor);
        }

        /// <summary>
        /// Determine whether the <see cref="IDictionary" /> is flagged dirty.
        /// </summary>
        public virtual bool Dirty
        {
            get { return dirty; }
        }

        /// <summary>
        /// Get a direct handle to the underlying Map.
        /// </summary>
        public virtual IDictionary WrappedMap
        {
            get { return map; }
        }

        /// <summary>
        /// Gets a value indicating whether this instance is empty.
        /// </summary>
        /// <value><c>true</c> if this instance is empty; otherwise, <c>false</c>.</value>
        public virtual bool IsEmpty
        {
            get { return (map.Count == 0); }
        }

        #region ICloneable Members

        /// <summary>
        /// Creates a new object that is a copy of the current instance.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public virtual object Clone()
        {
            DirtyFlagMap copy;
            try
            {
                copy = (DirtyFlagMap) MemberwiseClone();
                copy.map = (Hashtable) map.Clone();
            }
            catch (Exception)
            {
                throw new Exception("Not Cloneable.");
            }

            return copy;
        }

        #endregion

        #region IDictionary Members

        /// <summary>
        /// Gets or sets the <see cref="Object"/> with the specified key.
        /// </summary>
        /// <value></value>
        public virtual object this[object key]
        {
            get { return map[key]; }
            set
            {
                map[key] = value;
                dirty = true;
            }
        }

        /// <summary>
        /// When implemented by a class, gets the number of
        /// elements contained in the <see cref="T:System.Collections.ICollection"/>.
        /// </summary>
        /// <value></value>
        public virtual int Count
        {
            get { return map.Count; }
        }

        /// <summary>
        /// When implemented by a class, gets an <see cref="T:System.Collections.ICollection"/> containing the values in the <see cref="T:System.Collections.IDictionary"/>.
        /// </summary>
        /// <value></value>
        public virtual ICollection Values
        {
            get { return map.Values; }
        }

        /// <summary>
        /// When implemented by a class, removes all elements from the <see cref="T:System.Collections.IDictionary"/>.
        /// </summary>
        /// <exception cref="T:System.NotSupportedException">
        /// The <see cref="T:System.Collections.IDictionary"/> is read-only.
        /// </exception>
        public virtual void Clear()
        {
            if (map.Count != 0)
            {
                dirty = true;
            }

            map.Clear();
        }

        /// <summary>
        /// When implemented by a class, determines whether the <see cref="T:System.Collections.IDictionary"/> contains an element with the specified key.
        /// </summary>
        /// <param name="key">The key to locate in the <see cref="T:System.Collections.IDictionary"/>.</param>
        /// <returns>
        /// 	<see langword="true"/> if the <see cref="T:System.Collections.IDictionary"/> contains an element with the key; otherwise, <see langword="false"/>.
        /// </returns>
        /// <exception cref="T:System.ArgumentNullException">
        /// 	<paramref name="key "/>is <see langword="null"/>.</exception>
        public virtual bool Contains(object key)
        {
            return map.Contains(key);
        }

        /// <summary>
        /// When implemented by a class, removes the element with the
        /// specified key from the <see cref="T:System.Collections.IDictionary"/>.
        /// </summary>
        /// <param name="key">The key of the element to remove.</param>
        /// <exception cref="T:System.ArgumentNullException">
        /// 	<paramref name="key "/> is <see langword="null"/>.</exception>
        /// <exception cref="T:System.NotSupportedException">
        /// 	<para>The <see cref="T:System.Collections.IDictionary"/> is read-only.</para>
        /// 	<para>-or-</para>
        /// 	<para>The <see cref="T:System.Collections.IDictionary"/> has a fixed size.</para>
        /// </exception>
        public virtual void Remove(object key)
        {
            object tempObject;
            tempObject = map[key];
            map.Remove(key);
            object obj = tempObject;

            if (obj != null)
            {
                dirty = true;
            }
        }

        /// <summary>
        /// When implemented by a class, returns an
        /// <see cref="T:System.Collections.IDictionaryEnumerator"/> for the <see cref="T:System.Collections.IDictionary"/>.
        /// </summary>
        /// <returns>
        /// An <see cref="T:System.Collections.IDictionaryEnumerator"/> for the <see cref="T:System.Collections.IDictionary"/>.
        /// </returns>
        IDictionaryEnumerator IDictionary.GetEnumerator()
        {
            return map.GetEnumerator();
        }

        /// <summary>
        /// When implemented by a class, adds an element with the provided key and value to the <see cref="T:System.Collections.IDictionary"/>.
        /// </summary>
        /// <param name="key">The <see cref="T:System.Object"/> to use as the key of the element to add.</param>
        /// <param name="value">The <see cref="T:System.Object"/> to use as the value of the element to add.</param>
        /// <exception cref="T:System.ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
        /// <exception cref="T:System.ArgumentException">
        /// An element with the same key already exists in the <see cref="T:System.Collections.IDictionary"/>.
        /// </exception>
        /// <exception cref="T:System.NotSupportedException">
        /// 	<para>The <see cref="T:System.Collections.IDictionary"/> is read-only.</para>
        /// 	<para>-or-</para>
        /// 	<para>The <see cref="T:System.Collections.IDictionary"/> has a fixed size.</para>
        /// </exception>
        public virtual void Add(object key, object value)
        {
            map.Add(key, value);
            dirty = true;
        }

        /// <summary>
        /// When implemented by a class, copies the elements of
        /// the <see cref="T:System.Collections.ICollection"/> to an <see cref="T:System.Array"/>, starting at a particular <see cref="T:System.Array"/> index.
        /// </summary>
        /// <param name="array">The one-dimensional <see cref="T:System.Array"/> that is the destination of the elements copied from <see cref="T:System.Collections.ICollection"/>. The <see cref="T:System.Array"/> must have zero-based indexing.</param>
        /// <param name="index">The zero-based index in <paramref name="array"/> at which copying begins.</param>
        /// <exception cref="T:System.ArgumentNullException">
        /// 	<paramref name="array"/> is <see langword="null"/>.</exception>
        /// <exception cref="T:System.ArgumentOutOfRangeException">
        /// 	<paramref name="index"/> is less than zero.</exception>
        /// <exception cref="T:System.ArgumentException">
        /// 	<para>
        /// 		<paramref name="array"/> is multidimensional.</para>
        /// 	<para>-or-</para>
        /// 	<para>
        /// 		<paramref name="index"/> is equal to or greater than the length of <paramref name="array"/>.</para>
        /// 	<para>-or-</para>
        /// 	<para>The number of elements in the source <see cref="T:System.Collections.ICollection"/> is greater than the available space from <paramref name="index"/> to the end of the destination <paramref name="array"/>.</para>
        /// </exception>
        /// <exception cref="T:System.InvalidCastException">The type of the source <see cref="T:System.Collections.ICollection"/> cannot be cast automatically to the type of the destination <paramref name="array"/>.</exception>
        public virtual void CopyTo(Array array, int index)
        {
            object[] keys = new object[Count];
            object[] values = new object[Count];
            if (Keys != null)
            {
                Keys.CopyTo(keys, index);
            }
            if (Values != null)
            {
                Values.CopyTo(values, index);
            }
            for (int i = index; i < Count; i++)
            {
                if (keys[i] != null || values[i] != null)
                {
                    array.SetValue(new DictionaryEntry(keys[i], values[i]), i);
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return map.GetEnumerator();
        }

        /// <summary>
        /// When implemented by a class, gets an <see cref="T:System.Collections.ICollection"/> containing the keys of the <see cref="T:System.Collections.IDictionary"/>.
        /// </summary>
        /// <value></value>
        public virtual ICollection Keys
        {
            get { return map.Keys; }
        }

        /// <summary>
        /// When implemented by a class, gets a value indicating whether the <see cref="T:System.Collections.IDictionary"/>
        /// is read-only.
        /// </summary>
        /// <value></value>
        public virtual bool IsReadOnly
        {
            get { return locked; }
        }

        /// <summary>
        /// When implemented by a class, gets a value indicating whether the <see cref="T:System.Collections.IDictionary"/>
        /// has a fixed size.
        /// </summary>
        /// <value></value>
        public virtual Boolean IsFixedSize
        {
            get { return false; }
        }

        /// <summary>
        /// When implemented by a class, gets an object that
        /// can be used to synchronize access to the <see cref="T:System.Collections.ICollection"/>.
        /// </summary>
        /// <value></value>
        public virtual object SyncRoot
        {
            get { return syncRoot; }
        }

        /// <summary>
        /// When implemented by a class, gets a value
        /// indicating whether access to the <see cref="T:System.Collections.ICollection"/> is synchronized
        /// (thread-safe).
        /// </summary>
        /// <value></value>
        public virtual bool IsSynchronized
        {
            get { return false; }
        }

        #endregion

        /// <summary>
        /// Clear the 'dirty' flag (set dirty flag to <see langword="false" />).
        /// </summary>
        public virtual void ClearDirtyFlag()
        {
            dirty = false;
        }

        /// <summary>
        /// Determines whether the specified obj contains value.
        /// </summary>
        /// <param name="obj">The obj.</param>
        /// <returns>
        /// 	<c>true</c> if the specified obj contains value; otherwise, <c>false</c>.
        /// </returns>
        public virtual bool ContainsValue(object obj)
        {
            return map.ContainsValue(obj);
        }

        /// <summary>
        /// Gets the entries as a set.
        /// </summary>
        /// <returns></returns>
        public virtual ISet EntrySet()
        {
            return new HashSet(map);
        }

        /// <summary>
        /// Determines whether the specified <see cref="T:System.Object"/> is equal to the current <see cref="T:System.Object"/>.
        /// </summary>
        /// <param name="obj">The <see cref="T:System.Object"/> to compare with the current <see cref="T:System.Object"/>.</param>
        /// <returns>
        /// 	<see langword="true"/> if the specified <see cref="T:System.Object"/> is equal to the
        /// current <see cref="T:System.Object"/>; otherwise, <see langword="false"/>.
        /// </returns>
        public override bool Equals(object obj)
        {
            if (obj == null || !(obj is DirtyFlagMap))
            {
                return false;
            }

            IDictionary targetAux = new Hashtable((IDictionary) obj);

            if (Count == targetAux.Count)
            {
                IEnumerator sourceEnum = Keys.GetEnumerator();
                while (sourceEnum.MoveNext())
                {
                    if (targetAux.Contains(sourceEnum.Current))
                    {
                        targetAux.Remove(sourceEnum.Current);
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            else
            {
                return false;
            }
            if (targetAux.Count == 0)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Serves as a hash function for a particular type, suitable
        /// for use in hashing algorithms and data structures like a hash table.
        /// </summary>
        /// <returns>
        /// A hash code for the current <see cref="T:System.Object"/>.
        /// </returns>
        public override int GetHashCode()
        {
            return map.GetHashCode() ^ locked.GetHashCode() ^ dirty.GetHashCode();
        }

        /// <summary>
        /// Gets keyset for this map.
        /// </summary>
        /// <returns></returns>
        public virtual ISet KeySet()
        {
            return new HashSet(map.Keys);
        }

        /// <summary>
        /// Puts the value behind a specified key.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="val">The val.</param>
        /// <returns></returns>
        public virtual object Put(object key, object val)
        {
            dirty = true;
            object tempObject;
            tempObject = map[key];
            map[key] = val;
            return tempObject;
        }

        /// <summary>
        /// Puts all.
        /// </summary>
        /// <param name="t">The t.</param>
        public virtual void PutAll(IDictionary t)
        {
            if (t != null && t.Count > 0)
            {
                dirty = true;

                ArrayList keys = new ArrayList(t.Keys);
                ArrayList values = new ArrayList(t.Values);

                for (int i = 0; i < keys.Count; i++)
                {
                    this[keys[i]] = values[i];
                }
            }
        }
    }
}