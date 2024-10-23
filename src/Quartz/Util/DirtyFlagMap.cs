/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
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

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Security;

namespace Quartz.Util;

/// <summary>
/// An implementation of <see cref="IDictionary" /> that wraps another <see cref="IDictionary" />
/// and flags itself 'dirty' when it is modified.
/// </summary>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
#pragma warning disable CA1710
public class DirtyFlagMap<TKey, TValue> : IDictionary<TKey, TValue?>, IDictionary, IReadOnlyDictionary<TKey, TValue?>, ISerializable where TKey : notnull
#pragma warning restore CA1710
{
    private bool dirty;
    private readonly Dictionary<TKey, TValue?> map;

    /// <summary>
    /// Create a DirtyFlagMap that 'wraps' a <see cref="Hashtable" />.
    /// </summary>
    public DirtyFlagMap()
    {
        map = new Dictionary<TKey, TValue?>();
    }

    /// <summary>
    /// Create a DirtyFlagMap that 'wraps' a <see cref="Hashtable" /> that has the
    /// given initial capacity.
    /// </summary>
    public DirtyFlagMap(int initialCapacity)
    {
        map = new Dictionary<TKey, TValue?>(initialCapacity);
    }

    private DirtyFlagMap(DirtyFlagMap<TKey, TValue> other)
    {
        map = new Dictionary<TKey, TValue?>(other.map);
        dirty = other.dirty;
    }

    // Make sure that future DirtyFlagMap version changes are done in a DCS-friendly way (with [OnSerializing] and [OnDeserialized] methods).
    /// <summary>
    /// Serialization constructor.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="context"></param>
    protected DirtyFlagMap(SerializationInfo info, StreamingContext context)
    {
        int version;
        try
        {
            version = info.GetInt32("version");
        }
        catch
        {
            version = 0;
        }

        string prefix = "";
        if (version < 1)
        {
            try
            {
                info.GetValue("dirty", typeof(bool));
            }
            catch
            {
                // base class qualified format
                prefix = "DirtyFlagMap+";
            }
        }

        switch (version)
        {
            case 0:
                object o = info.GetValue(prefix + "map", typeof(object))!;
                if (o is Hashtable oldMap)
                {
                    // need to call ondeserialization to get hashtable
                    // initialized correctly
                    oldMap.OnDeserialization(this);

                    map = new Dictionary<TKey, TValue?>();
#pragma warning disable 8605
                    foreach (DictionaryEntry entry in oldMap)
#pragma warning restore 8605
                    {
                        map.Add((TKey) entry.Key, (TValue) entry.Value!);
                    }
                }
                else
                {
                    // new version
                    map = (Dictionary<TKey, TValue?>) o;
                }

                break;
            case 1:
                dirty = (bool) info.GetValue("dirty", typeof(bool))!;
                map = (Dictionary<TKey, TValue?>) info.GetValue("map", typeof(Dictionary<TKey, TValue?>))!;
                break;
            default:
                ThrowHelper.ThrowNotSupportedException("Unknown serialization version");
                break;
        }
    }

    /// <summary>
    /// Populates a <see cref="SerializationInfo"/> with the data needed to serialize the target object.
    /// </summary>
    /// <param name="info">The <see cref="SerializationInfo"/> to populate with data.</param>
    /// <param name="context">The destination for this serialization.</param>
    void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
    {
        GetObjectData(info, context);
    }

    /// <summary>
    /// Populates a <see cref="SerializationInfo"/> with the data needed to serialize the target object.
    /// </summary>
    /// <param name="info">The <see cref="SerializationInfo"/> to populate with data.</param>
    /// <param name="context">The destination for this serialization.</param>
    [SecurityCritical]
    protected void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        info.AddValue("version", 1);
        info.AddValue("dirty", dirty);
        info.AddValue("map", map);
    }

    /// <summary>
    /// Determine whether the <see cref="IDictionary" /> is flagged dirty.
    /// </summary>
    public bool Dirty => dirty;

    /// <summary>
    /// Get a direct handle to the underlying Map.
    /// </summary>
    public IDictionary<TKey, TValue?> WrappedMap => map;

    /// <summary>
    /// Gets a value indicating whether this instance is empty.
    /// </summary>
    /// <value><c>true</c> if this instance is empty; otherwise, <c>false</c>.</value>
    public bool IsEmpty => map.Count == 0;

    /// <summary>
    /// Creates a new object that is a copy of the current instance.
    /// </summary>
    /// <returns>
    /// A new object that is a copy of this instance.
    /// </returns>
    internal virtual DirtyFlagMap<TKey, TValue> Clone()
    {
        return new DirtyFlagMap<TKey, TValue>(this);
    }

    /// <summary>
    /// Gets the value associated with the specified key.
    /// </summary>
    /// <param name="key">Gets the value associated with the specified key.</param>
    /// <param name="value">When this method returns, contains the value associated with the specified key, if the key is found; otherwise, the default value for the type of the <paramref name="value"/> parameter. This parameter is passed uninitialized.</param>
    /// <returns>
    /// <see langword="true"/> if the <see cref="DirtyFlagMap{TKey, TValue}"/>contains an element with the specified key;
    /// otherwise, <see langword="false"/>.
    /// </returns>
#pragma warning disable CS8767 // Nullability of reference types in type of parameter doesn't match implicitly implemented member (possibly because of nullability attributes).
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
#pragma warning restore CS8767 // Nullability of reference types in type of parameter doesn't match implicitly implemented member (possibly because of nullability attributes).
    {
        return map.TryGetValue(key, out value);
    }

    /// <summary>
    /// Gets or sets the <see cref="object"/> with the specified key.
    /// </summary>
    public TValue? this[TKey key]
    {
        get
        {
            map.TryGetValue(key, out TValue? temp);
            return temp!;
        }
        set
        {
            map[key] = value;
            dirty = true;
        }
    }

    public bool Remove(KeyValuePair<TKey, TValue?> item)
    {
        return Remove(item.Key);
    }

    /// <summary>
    /// When implemented by a class, gets the number of
    /// elements contained in the <see cref="System.Collections.ICollection"/>.
    /// </summary>
    /// <value></value>
    public int Count => map.Count;

    /// <inheritdoc/>
    ICollection IDictionary.Keys => map.Keys;

    /// <inheritdoc/>
    ICollection IDictionary.Values => map.Values;

    /// <inheritdoc/>
    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue?>.Keys => map.Keys.AsEnumerable<TKey>();

    /// <inheritdoc/>
    IEnumerable<TValue?> IReadOnlyDictionary<TKey, TValue?>.Values => map.Values.AsEnumerable<TValue?>();

    /// <summary>
    /// When implemented by a class, gets an <see cref="System.Collections.ICollection"/> containing the values in the <see cref="System.Collections.IDictionary"/>.
    /// </summary>
    /// <value></value>
    public ICollection<TValue?> Values => map.Values;

    public void Add(KeyValuePair<TKey, TValue?> item)
    {
        Put(item.Key, item.Value);
    }

    public void Add(object key, object? value)
    {
        Put((TKey) key, (TValue) value!);
    }

    public bool Contains(object key)
    {
        return ((IDictionary) map).Contains(key);
    }

    /// <summary>
    /// When implemented by a class, removes all elements from the <see cref="System.Collections.IDictionary"/>.
    /// </summary>
    /// <exception cref="System.NotSupportedException">
    /// The <see cref="System.Collections.IDictionary"/> is read-only.
    /// </exception>
    public void Clear()
    {
        if (map.Count != 0)
        {
            dirty = true;
        }

        map.Clear();
    }

    public void Remove(object key)
    {
        Remove((TKey) key);
    }

    object? IDictionary.this[object key]
    {
        get => this[(TKey) key];
        set => this[(TKey) key] = (TValue) value!;
    }

    /// <summary>
    /// Determines whether the <see cref="DirtyFlagMap{TKey, TValue}"/> contains the specified key.
    /// Essentially this is a wrapper around <see cref="ContainsKey(TKey)"/>.
    /// </summary>
    /// <param name="item">The key to locate in the <see cref="DirtyFlagMap{TKey, TValue}"/>.</param>
    /// <returns>
    /// <see langword="true"/> if the <see cref="DirtyFlagMap{TKey, TValue}"/> contains the specified key; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Contains(KeyValuePair<TKey, TValue?> item)
    {
        return Contains(item.Key);
    }

    public void CopyTo(KeyValuePair<TKey, TValue?>[] array, int arrayIndex)
    {
        ((ICollection<KeyValuePair<TKey, TValue?>>) map).CopyTo(array, arrayIndex);
    }

    /// <summary>
    /// When implemented by a class, determines whether the <see cref="System.Collections.IDictionary"/> contains an element with the specified key.
    /// </summary>
    /// <param name="key">The key to locate in the <see cref="System.Collections.IDictionary"/>.</param>
    /// <returns>
    /// 	<see langword="true"/> if the <see cref="System.Collections.IDictionary"/> contains an element with the key; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="System.ArgumentNullException">
    /// 	<paramref name="key "/>is <see langword="null"/>.</exception>
    public bool ContainsKey(TKey key)
    {
        return map.ContainsKey(key);
    }

    /// <summary>
    /// When implemented by a class, removes the element with the
    /// specified key from the <see cref="System.Collections.IDictionary"/>.
    /// </summary>
    /// <param name="key">The key of the element to remove.</param>
    /// <exception cref="System.ArgumentNullException">
    /// 	<paramref name="key "/> is <see langword="null"/>.</exception>
    /// <exception cref="System.NotSupportedException">
    /// 	<para>The <see cref="System.Collections.IDictionary"/> is read-only.</para>
    /// 	<para>-or-</para>
    /// 	<para>The <see cref="System.Collections.IDictionary"/> has a fixed size.</para>
    /// </exception>
    public bool Remove(TKey key)
    {
        bool remove = map.Remove(key);
        dirty |= remove;
        return remove;
    }

    public Dictionary<TKey, TValue?>.Enumerator GetEnumerator()
    {
        return map.GetEnumerator();
    }

    IDictionaryEnumerator IDictionary.GetEnumerator()
    {
        return ((IDictionary) map).GetEnumerator();
    }

    IEnumerator<KeyValuePair<TKey, TValue?>> IEnumerable<KeyValuePair<TKey, TValue?>>.GetEnumerator()
    {
        return ((IEnumerable<KeyValuePair<TKey, TValue?>>) map).GetEnumerator();
    }

    /// <summary>
    /// When implemented by a class, adds an element with the provided key and value to the <see cref="System.Collections.IDictionary"/>.
    /// </summary>
    /// <param name="key">The <see cref="System.Object"/> to use as the key of the element to add.</param>
    /// <param name="value">The <see cref="System.Object"/> to use as the value of the element to add.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.ArgumentException">
    /// An element with the same key already exists in the <see cref="System.Collections.IDictionary"/>.
    /// </exception>
    /// <exception cref="System.NotSupportedException">
    /// 	<para>The <see cref="System.Collections.IDictionary"/> is read-only.</para>
    /// 	<para>-or-</para>
    /// 	<para>The <see cref="System.Collections.IDictionary"/> has a fixed size.</para>
    /// </exception>
    public void Add(TKey key, TValue? value)
    {
        map.Add(key, value);
        dirty = true;
    }

    /// <summary>
    /// When implemented by a class, copies the elements of
    /// the <see cref="System.Collections.ICollection"/> to an <see cref="System.Array"/>, starting at a particular <see cref="System.Array"/> index.
    /// </summary>
    /// <param name="array">The one-dimensional <see cref="System.Array"/> that is the destination of the elements copied from <see cref="System.Collections.ICollection"/>. The <see cref="System.Array"/> must have zero-based indexing.</param>
    /// <param name="index">The zero-based index in <paramref name="array"/> at which copying begins.</param>
    /// <exception cref="System.ArgumentNullException">
    /// 	<paramref name="array"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.ArgumentOutOfRangeException">
    /// 	<paramref name="index"/> is less than zero.</exception>
    /// <exception cref="System.ArgumentException">
    /// 	<para>
    /// 		<paramref name="array"/> is multidimensional.</para>
    /// 	<para>-or-</para>
    /// 	<para>
    /// 		<paramref name="index"/> is equal to or greater than the length of <paramref name="array"/>.</para>
    /// 	<para>-or-</para>
    /// 	<para>The number of elements in the source <see cref="System.Collections.ICollection"/> is greater than the available space from <paramref name="index"/> to the end of the destination <paramref name="array"/>.</para>
    /// </exception>
    /// <exception cref="System.InvalidCastException">The type of the source <see cref="System.Collections.ICollection"/> cannot be cast automatically to the type of the destination <paramref name="array"/>.</exception>
    public void CopyTo(Array array, int index)
    {
        TKey[] keys = new TKey[Count];
        TValue[] values = new TValue[Count];

        Keys.CopyTo(keys, index);
        Values.CopyTo(values, index);

        for (int i = index; i < Count; i++)
        {
            if (!Equals(keys[i], default(TKey)) || !Equals(values[i], default(TValue)))
            {
                array.SetValue(new DictionaryEntry(keys[i], values[i]), i);
            }
        }
    }

    /// <summary>
    /// When implemented by a class, gets an <see cref="System.Collections.ICollection"/> containing the keys of the <see cref="System.Collections.IDictionary"/>.
    /// </summary>
    /// <value></value>
    public ICollection<TKey> Keys => map.Keys;

    /// <summary>
    /// Gets a value indicating whether the <see cref="DirtyFlagMap{TKey,TValue}"/> is read-only.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if the <see cref="DirtyFlagMap{TKey,TValue}"/> is read-only; otherwise, <see langword="false"/>.
    /// In the default implementation of <see cref="DirtyFlagMap{TKey,TValue}"/>, this property always returns
    /// <see langword="false"/>.
    /// </value>
    bool IDictionary.IsReadOnly => false;

    /// <summary>
    /// Gets a value indicating whether the <see cref="DirtyFlagMap{TKey,TValue}"/> is read-only.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if the <see cref="DirtyFlagMap{TKey,TValue}"/> is read-only; otherwise, <see langword="false"/>.
    /// In the default implementation of <see cref="DirtyFlagMap{TKey,TValue}"/>, this property always returns
    /// <see langword="false"/>.
    /// </value>
    bool ICollection<KeyValuePair<TKey, TValue?>>.IsReadOnly => false;

    /// <summary>
    /// Gets a value indicating whether the <see cref="DirtyFlagMap{TKey,TValue}"/> has a fixed size.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if the <see cref="DirtyFlagMap{TKey,TValue}"/> has a fixed size;
    /// otherwise, <see langword="false"/>. In the default implementation of <see cref="DirtyFlagMap{TKey,TValue}"/>,
    /// this property always returns <see langword="false"/>.
    /// </value>
    bool IDictionary.IsFixedSize => false;

    /// <summary>
    /// Gets an object that can be used to synchronize access to the <see cref="DirtyFlagMap{TKey,TValue}"/>.
    /// </summary>
    /// <value>
    /// An object that can be used to synchronize access to the <see cref="DirtyFlagMap{TKey,TValue}"/>.
    /// </value>
    object ICollection.SyncRoot { get; } = new object();

    /// <summary>
    /// Gets a value indicating whether access to the <see cref="DirtyFlagMap{TKey,TValue}"/> is synchronized
    /// (thread-safe).
    /// </summary>
    /// <value>
    /// <see langword="true"/> if access to the <see cref="DirtyFlagMap{TKey,TValue}"/> is synchronized (thread safe);
    /// otherwise, <see langword="false"/>. In the default implementation of <see cref="DirtyFlagMap{TKey,TValue}"/>,
    /// this property always returns <see langword="false"/>.
    /// </value>
    bool ICollection.IsSynchronized => false;

    /// <summary>
    /// Clear the 'dirty' flag (set dirty flag to <see langword="false" />).
    /// </summary>
    public void ClearDirtyFlag()
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
    public bool ContainsValue(TValue obj)
    {
        return map.ContainsValue(obj);
    }

    /// <summary>
    /// Determines whether the specified <see cref="System.Object"/> is equal to the current <see cref="System.Object"/>.
    /// </summary>
    /// <param name="obj">The <see cref="System.Object"/> to compare with the current <see cref="System.Object"/>.</param>
    /// <returns>
    /// 	<see langword="true"/> if the specified <see cref="System.Object"/> is equal to the
    /// current <see cref="System.Object"/>; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj)
    {
        if (obj is not DirtyFlagMap<TKey, TValue> values)
        {
            return false;
        }

        Hashtable targetAux = new Hashtable(values);

        if (Count == targetAux.Count)
        {
            IEnumerator sourceEnum = Keys.GetEnumerator();
            while (sourceEnum.MoveNext())
            {
                if (sourceEnum.Current is not null && targetAux.Contains(sourceEnum.Current))
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

        return false;
    }

    /// <summary>
    /// Serves as a hash function for a particular type, suitable
    /// for use in hashing algorithms and data structures like a hash table.
    /// </summary>
    /// <returns>
    /// A hash code for the current <see cref="System.Object"/>.
    /// </returns>
    public override int GetHashCode()
    {
        return map.GetHashCode();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>
    /// Puts the value behind a specified key.
    /// </summary>
    /// <param name="key">The key to use.</param>
    /// <param name="val">The value to put.</param>
    /// <returns>The existing value, if any</returns>
    public object? Put(TKey key, TValue? val)
    {
        map.TryGetValue(key, out TValue? tempObject);
        map[key] = val;
        dirty = true;
        return tempObject;
    }

    /// <summary>
    /// Puts all values from source dictionary into this map.
    /// </summary>
    /// <param name="source">The source dictionary.</param>
    public void PutAll(IDictionary<TKey, TValue?> source)
    {
        if (source is null)
        {
            return;
        }

        foreach (KeyValuePair<TKey, TValue?> pair in source)
        {
            this[pair.Key] = pair.Value;
        }
    }
}