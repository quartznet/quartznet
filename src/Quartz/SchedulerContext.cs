#region License

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

#endregion

using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Quartz;

/// <summary>
/// Holds context/environment data that can be made available to Jobs as they
/// are executed.
/// </summary>
/// <remarks>
/// <para>
/// The context lives for the whole scheduler: plugins write to it during initialization while jobs
/// and listeners read from it on their own threads, so it is backed by a
/// <see cref="ConcurrentDictionary{TKey,TValue}" /> and is safe to read and write concurrently.
/// It is never persisted.
/// </para>
/// <para>
/// The typed read accessors (<c>GetString</c>, <c>TryGetInt</c> and friends) are extension members
/// declared in <see cref="DataMapExtensions" />.
/// </para>
/// </remarks>
/// <seealso cref="IScheduler.Context" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
#pragma warning disable CA1710
public sealed class SchedulerContext : IDictionary<string, object?>, IReadOnlyDictionary<string, object?>
#pragma warning restore CA1710
{
    private readonly ConcurrentDictionary<string, object?> store;

    /// <summary>
    /// Create an empty <see cref="SchedulerContext" />.
    /// </summary>
    public SchedulerContext()
    {
        store = new ConcurrentDictionary<string, object?>();
    }

    /// <summary>
    /// Create a <see cref="SchedulerContext" /> with the given data.
    /// </summary>
    public SchedulerContext(IDictionary<string, object?> map) : this()
    {
        foreach (KeyValuePair<string, object?> pair in map)
        {
            store[pair.Key] = pair.Value;
        }
    }

    /// <summary>
    /// Gets a value indicating whether this instance is empty.
    /// </summary>
    /// <value><c>true</c> if this instance is empty; otherwise, <c>false</c>.</value>
    public bool IsEmpty => store.IsEmpty;

    /// <summary>
    /// Gets the number of entries contained in the context.
    /// </summary>
    public int Count => store.Count;

    /// <summary>
    /// Gets a snapshot of the keys in the context.
    /// </summary>
    public ICollection<string> Keys => store.Keys;

    /// <summary>
    /// Gets a snapshot of the values in the context.
    /// </summary>
    public ICollection<object?> Values => store.Values;

    /// <inheritdoc/>
    IEnumerable<string> IReadOnlyDictionary<string, object?>.Keys => store.Keys;

    /// <inheritdoc/>
    IEnumerable<object?> IReadOnlyDictionary<string, object?>.Values => store.Values;

    /// <summary>
    /// Gets or sets the <see cref="object"/> with the specified key.
    /// </summary>
    public object? this[string key]
    {
        get => store[key];
        set => store[key] = value;
    }

    /// <summary>
    /// Gets the value associated with the specified key.
    /// </summary>
    /// <param name="key">The key whose value to get.</param>
    /// <param name="value">When this method returns, contains the value associated with the specified key, if the key is found; otherwise, <see langword="null" />.</param>
    /// <returns>
    /// <see langword="true"/> if the <see cref="SchedulerContext"/> contains an element with the specified key;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out object? value)
    {
        return store.TryGetValue(key, out value);
    }

    /// <summary>
    /// Determines whether the context contains an entry with the specified key.
    /// </summary>
    /// <param name="key">The key to locate.</param>
    /// <returns>
    /// 	<see langword="true"/> if the context contains an entry with the key; otherwise, <see langword="false"/>.
    /// </returns>
    public bool ContainsKey(string key)
    {
        return store.ContainsKey(key);
    }

    /// <summary>
    /// Adds an entry with the provided key and value to the context.
    /// </summary>
    /// <param name="key">The key of the entry to add.</param>
    /// <param name="value">The value of the entry to add.</param>
    /// <exception cref="System.ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="System.ArgumentException">
    /// An entry with the same key already exists in the context.
    /// </exception>
    public void Add(string key, object? value)
    {
        if (!store.TryAdd(key, value))
        {
            Throw.ArgumentException("An entry with the same key already exists.", nameof(key));
        }
    }

    /// <summary>
    /// Removes the entry with the specified key from the context.
    /// </summary>
    /// <param name="key">The key of the entry to remove.</param>
    public bool Remove(string key)
    {
        return store.TryRemove(key, out _);
    }

    /// <summary>
    /// Removes all entries from the context.
    /// </summary>
    public void Clear()
    {
        store.Clear();
    }

    public void CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex)
    {
        ((ICollection<KeyValuePair<string, object?>>) store).CopyTo(array, arrayIndex);
    }

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        return store.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    void ICollection<KeyValuePair<string, object?>>.Add(KeyValuePair<string, object?> item)
    {
        Add(item.Key, item.Value);
    }

    bool ICollection<KeyValuePair<string, object?>>.Contains(KeyValuePair<string, object?> item)
    {
        return ((ICollection<KeyValuePair<string, object?>>) store).Contains(item);
    }

    bool ICollection<KeyValuePair<string, object?>>.Remove(KeyValuePair<string, object?> item)
    {
        return store.TryRemove(item);
    }

    bool ICollection<KeyValuePair<string, object?>>.IsReadOnly => false;
}
