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

using System.Diagnostics.CodeAnalysis;

namespace Quartz.Util;

/// <summary>
/// A dictionary that flags itself 'dirty' when it is modified.
/// </summary>
/// <remarks>
/// This is the storage behind <see cref="JobDataMap" />, which owns the serialized shape;
/// this type carries no serialization behaviour of its own.
/// </remarks>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
#pragma warning disable CA1710
internal class DirtyFlagMap<TKey, TValue> : IDictionary<TKey, TValue?>, IReadOnlyDictionary<TKey, TValue?> where TKey : notnull
#pragma warning restore CA1710
{
    private bool dirty;
    private readonly Dictionary<TKey, TValue?> map;

    /// <summary>
    /// Create an empty <see cref="DirtyFlagMap{TKey,TValue}" />.
    /// </summary>
    public DirtyFlagMap()
    {
        map = new Dictionary<TKey, TValue?>();
    }

    /// <summary>
    /// Create a <see cref="DirtyFlagMap{TKey,TValue}" /> with the given initial capacity.
    /// </summary>
    public DirtyFlagMap(int initialCapacity)
    {
        map = new Dictionary<TKey, TValue?>(initialCapacity);
    }

    /// <summary>
    /// Create a <see cref="DirtyFlagMap{TKey,TValue}" /> adopting the given dictionary as its storage,
    /// with the given dirty state. Used when reconstructing a map from its persisted form.
    /// </summary>
    internal DirtyFlagMap(Dictionary<TKey, TValue?> map, bool dirty)
    {
        this.map = map;
        this.dirty = dirty;
    }

    private DirtyFlagMap(DirtyFlagMap<TKey, TValue> other)
    {
        map = new Dictionary<TKey, TValue?>(other.map);
        dirty = other.dirty;
    }

    /// <summary>
    /// Determine whether the map is flagged dirty.
    /// </summary>
    public bool Dirty => dirty;

    /// <summary>
    /// Get a direct handle to the underlying Map.
    /// </summary>
    internal Dictionary<TKey, TValue?> WrappedMap => map;

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
    internal DirtyFlagMap<TKey, TValue> Clone()
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
        get => map[key];
        set
        {
            if (map.TryGetValue(key, out TValue? existing)
                && EqualityComparer<TValue>.Default.Equals(existing, value))
            {
                return;
            }

            map[key] = value;
            dirty = true;
        }
    }

    bool ICollection<KeyValuePair<TKey, TValue?>>.Remove(KeyValuePair<TKey, TValue?> item)
    {
        if (map.TryGetValue(item.Key, out TValue? existing)
            && EqualityComparer<TValue>.Default.Equals(existing, item.Value))
        {
            map.Remove(item.Key);
            dirty = true;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the number of entries contained in the map.
    /// </summary>
    public int Count => map.Count;

    /// <inheritdoc/>
    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue?>.Keys => map.Keys.AsEnumerable<TKey>();

    /// <inheritdoc/>
    IEnumerable<TValue?> IReadOnlyDictionary<TKey, TValue?>.Values => map.Values.AsEnumerable<TValue?>();

    /// <summary>
    /// Gets a collection containing the values in the map.
    /// </summary>
    public ICollection<TValue?> Values => map.Values;

    void ICollection<KeyValuePair<TKey, TValue?>>.Add(KeyValuePair<TKey, TValue?> item)
    {
        Add(item.Key, item.Value);
    }

    /// <summary>
    /// Removes all entries from the map.
    /// </summary>
    public void Clear()
    {
        if (map.Count != 0)
        {
            dirty = true;
        }

        map.Clear();
    }

    bool ICollection<KeyValuePair<TKey, TValue?>>.Contains(KeyValuePair<TKey, TValue?> item)
    {
        return map.TryGetValue(item.Key, out TValue? existing)
            && EqualityComparer<TValue>.Default.Equals(existing, item.Value);
    }

    public void CopyTo(KeyValuePair<TKey, TValue?>[] array, int arrayIndex)
    {
        ((ICollection<KeyValuePair<TKey, TValue?>>) map).CopyTo(array, arrayIndex);
    }

    /// <summary>
    /// Determines whether the map contains an entry with the specified key.
    /// </summary>
    /// <param name="key">The key to locate.</param>
    /// <returns>
    /// 	<see langword="true"/> if the map contains an entry with the key; otherwise, <see langword="false"/>.
    /// </returns>
    /// <exception cref="System.ArgumentNullException">
    /// 	<paramref name="key "/>is <see langword="null"/>.</exception>
    public bool ContainsKey(TKey key)
    {
        return map.ContainsKey(key);
    }

    /// <summary>
    /// Removes the entry with the specified key from the map.
    /// </summary>
    /// <param name="key">The key of the entry to remove.</param>
    /// <exception cref="System.ArgumentNullException">
    /// 	<paramref name="key "/> is <see langword="null"/>.</exception>
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

    IEnumerator<KeyValuePair<TKey, TValue?>> IEnumerable<KeyValuePair<TKey, TValue?>>.GetEnumerator()
    {
        return ((IEnumerable<KeyValuePair<TKey, TValue?>>) map).GetEnumerator();
    }

    /// <summary>
    /// Adds an entry with the provided key and value to the map.
    /// </summary>
    /// <param name="key">The key of the entry to add.</param>
    /// <param name="value">The value of the entry to add.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.ArgumentException">
    /// An entry with the same key already exists in the map.
    /// </exception>
    public void Add(TKey key, TValue? value)
    {
        map.Add(key, value);
        dirty = true;
    }

    /// <summary>
    /// Gets a collection containing the keys of the map.
    /// </summary>
    public ICollection<TKey> Keys => map.Keys;

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

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
