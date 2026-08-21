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
using System.Globalization;
using System.Runtime.Serialization;
using System.Security;

using Quartz.Util;

namespace Quartz;

/// <summary>
/// Holds state information for <see cref="IJob" /> instances.
/// </summary>
/// <remarks>
/// <see cref="JobDataMap" /> instances are stored once when the <see cref="IJob" />
/// is added to a scheduler. They are also re-persisted after every execution of
/// instances that have <see cref="PersistJobDataAfterExecutionAttribute" /> present.
/// <para>
/// <see cref="JobDataMap" /> instances can also be stored with a
/// <see cref="ITrigger" />.  This can be useful in the case where you have a Job
/// that is stored in the scheduler for regular/repeated use by multiple
/// Triggers, yet with each independent triggering, you want to supply the
/// Job with different data inputs.
/// </para>
/// <para>
/// The <see cref="IJobExecutionContext" /> passed to a Job at execution time
/// also contains a convenience <see cref="JobDataMap" /> that is the result
/// of merging the contents of the trigger's JobDataMap (if any) over the
/// Job's JobDataMap (if any).
/// </para>
/// <para>
/// Update since 2.4.2 - We keep an dirty flag for this map so that whenever you modify(add/delete) any of the entries,
/// it will set to "true". However if you create new instance using an existing map with constructor, then
/// the dirty flag will NOT be set to "true" until you modify the instance.
/// </para>
/// <para>
/// The typed read accessors (<c>GetInt</c>, <c>TryGetDateTime</c> and friends) are extension members
/// declared in <see cref="DataMapExtensions" />; the <c>PutAsString</c> writers live here because they
/// participate in the map's change tracking.
/// </para>
/// <para>
/// The binary-serialized form of this type — the entries named <c>version</c>, <c>dirty</c> and
/// <c>map</c>, with a <see cref="Dictionary{TKey,TValue}" /> of <see cref="string" /> to
/// <see cref="object" /> as the payload — is the shape 3.x wrote into <c>JOB_DATA</c> and
/// <c>BLOB_TRIGGERS</c> blobs, and it is what the binary-to-JSON migration path reads. It must not
/// change.
/// </para>
/// </remarks>
/// <seealso cref="IJob" />
/// <seealso cref="PersistJobDataAfterExecutionAttribute" />
/// <seealso cref="ITrigger" />
/// <seealso cref="IJobExecutionContext" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
#pragma warning disable CA1710
public sealed class JobDataMap : IDictionary<string, object?>, IReadOnlyDictionary<string, object?>, IEquatable<JobDataMap>, ISerializable
#pragma warning restore CA1710
{
    private readonly DirtyFlagMap<string, object> map;

    /// <summary>
    /// Create an empty <see cref="JobDataMap" />.
    /// </summary>
    public JobDataMap() : this(0)
    {
    }

    /// <summary>
    /// Create <see cref="JobDataMap" /> with initial capacity.
    /// </summary>
    public JobDataMap(int initialCapacity)
    {
        map = new DirtyFlagMap<string, object>(initialCapacity);
    }

    /// <summary>
    /// Create a <see cref="JobDataMap" /> with the given data.
    /// </summary>
    /// <remarks>
    /// A <see cref="SchedulerConstants.ForceJobDataMapDirty" /> entry is not copied; it instead leaves the
    /// new map flagged dirty, which is how the job store asks for the data blob to be rewritten.
    /// </remarks>
    public JobDataMap(IDictionary<string, object?> map) : this(map.Count)
    {
        bool clearDirtyFlag = true;
        foreach (KeyValuePair<string, object?> pair in map)
        {
            if (SchedulerConstants.ForceJobDataMapDirty.Equals(pair.Key, StringComparison.Ordinal))
            {
                clearDirtyFlag = false;
            }
            else
            {
                this[pair.Key] = pair.Value;
            }
        }

        if (clearDirtyFlag)
        {
            // When constructing a new data map from another existing map, we should NOT mark dirty flag as true
            // Use case: loading JobDataMap from DB
            ClearDirtyFlag();
        }
    }

    // Make sure that future serialized-shape changes are done in a DCS-friendly way (with [OnSerializing] and [OnDeserialized] methods).
    /// <summary>
    /// Serialization constructor. Reads the shape every Quartz version since 1.x has written:
    /// a <c>version</c> probe, the <c>dirty</c> flag, and the entries under <c>map</c> —
    /// including the pre-2.x form whose entries were prefixed <c>DirtyFlagMap+</c> and whose
    /// payload was a <see cref="Hashtable" />.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="context"></param>
    private JobDataMap(SerializationInfo info, StreamingContext context)
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

        bool dirty = false;
        Dictionary<string, object?> entries;
        switch (version)
        {
            case 0:
                object o = info.GetValue(prefix + "map", typeof(object))!;
                if (o is Hashtable oldMap)
                {
                    // need to call ondeserialization to get hashtable
                    // initialized correctly
                    oldMap.OnDeserialization(this);

                    entries = new Dictionary<string, object?>();
#pragma warning disable 8605
                    foreach (DictionaryEntry entry in oldMap)
#pragma warning restore 8605
                    {
                        entries.Add((string) entry.Key, entry.Value);
                    }
                }
                else
                {
                    // new version
                    entries = (Dictionary<string, object?>) o;
                }

                break;
            case 1:
                dirty = (bool) info.GetValue("dirty", typeof(bool))!;
                entries = (Dictionary<string, object?>) info.GetValue("map", typeof(Dictionary<string, object?>))!;
                break;
            default:
                Throw.NotSupportedException("Unknown serialization version");
                entries = null!;
                break;
        }

        map = new DirtyFlagMap<string, object>(entries, dirty);
    }

    /// <summary>
    /// Populates a <see cref="SerializationInfo"/> with the data needed to serialize the target object.
    /// </summary>
    /// <param name="info">The <see cref="SerializationInfo"/> to populate with data.</param>
    /// <param name="context">The destination for this serialization.</param>
    [SecurityCritical]
    void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
    {
        info.AddValue("version", 1);
        info.AddValue("dirty", map.Dirty);
        info.AddValue("map", map.WrappedMap);
    }

    /// <summary>
    /// Determine whether the map is flagged dirty.
    /// </summary>
    internal bool Dirty => map.Dirty;

    /// <summary>
    /// Clear the 'dirty' flag (set dirty flag to <see langword="false" />).
    /// </summary>
    internal void ClearDirtyFlag()
    {
        map.ClearDirtyFlag();
    }

    /// <summary>
    /// Gets a value indicating whether this instance is empty.
    /// </summary>
    /// <value><c>true</c> if this instance is empty; otherwise, <c>false</c>.</value>
    public bool IsEmpty => map.IsEmpty;

    /// <summary>
    /// Gets the number of entries contained in the map.
    /// </summary>
    public int Count => map.Count;

    /// <summary>
    /// Gets a collection containing the keys of the map.
    /// </summary>
    public ICollection<string> Keys => map.Keys;

    /// <summary>
    /// Gets a collection containing the values in the map.
    /// </summary>
    public ICollection<object?> Values => map.Values;

    /// <inheritdoc/>
    IEnumerable<string> IReadOnlyDictionary<string, object?>.Keys => map.Keys;

    /// <inheritdoc/>
    IEnumerable<object?> IReadOnlyDictionary<string, object?>.Values => map.Values;

    /// <summary>
    /// Gets or sets the <see cref="object"/> with the specified key.
    /// </summary>
    public object? this[string key]
    {
        get => map[key];
        set => map[key] = value;
    }

    /// <summary>
    /// Gets the value associated with the specified key.
    /// </summary>
    /// <param name="key">Gets the value associated with the specified key.</param>
    /// <param name="value">When this method returns, contains the value associated with the specified key, if the key is found; otherwise, the default value for the type of the <paramref name="value"/> parameter. This parameter is passed uninitialized.</param>
    /// <returns>
    /// <see langword="true"/> if the <see cref="JobDataMap"/> contains an element with the specified key;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    public bool TryGetValue(string key, out object? value)
    {
        return map.TryGetValue(key, out value);
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
    public bool ContainsKey(string key)
    {
        return map.ContainsKey(key);
    }

    /// <summary>
    /// Determines whether the specified obj contains value.
    /// </summary>
    /// <param name="obj">The obj.</param>
    /// <returns>
    /// 	<c>true</c> if the specified obj contains value; otherwise, <c>false</c>.
    /// </returns>
    public bool ContainsValue(object? obj)
    {
        return map.ContainsValue(obj!);
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
    public void Add(string key, object? value)
    {
        map.Add(key, value);
    }

    /// <summary>
    /// Removes the entry with the specified key from the map.
    /// </summary>
    /// <param name="key">The key of the entry to remove.</param>
    /// <exception cref="System.ArgumentNullException">
    /// 	<paramref name="key "/> is <see langword="null"/>.</exception>
    public bool Remove(string key)
    {
        return map.Remove(key);
    }

    /// <summary>
    /// Removes all entries from the map.
    /// </summary>
    public void Clear()
    {
        map.Clear();
    }

    public void CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex)
    {
        map.CopyTo(array, arrayIndex);
    }

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        return map.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    void ICollection<KeyValuePair<string, object?>>.Add(KeyValuePair<string, object?> item)
    {
        ((ICollection<KeyValuePair<string, object?>>) map).Add(item);
    }

    bool ICollection<KeyValuePair<string, object?>>.Contains(KeyValuePair<string, object?> item)
    {
        return ((ICollection<KeyValuePair<string, object?>>) map).Contains(item);
    }

    bool ICollection<KeyValuePair<string, object?>>.Remove(KeyValuePair<string, object?> item)
    {
        return ((ICollection<KeyValuePair<string, object?>>) map).Remove(item);
    }

    bool ICollection<KeyValuePair<string, object?>>.IsReadOnly => false;

    /// <summary>
    /// Adds the given value as a string version using the default ToString operation.
    /// </summary>
    /// <remarks>
    /// An enum lands here and stores its name (<c>DayOfWeek.Monday</c> stores <c>"Monday"</c>),
    /// which <c>TryGetEnum</c> reads back.
    /// <para>
    /// The constraint is <see cref="IFormattable" /> rather than the legacy
    /// <see cref="IConvertible" />: formatting for a culture is what this does, and it lets through
    /// every modern numeric type that never implemented <see cref="IConvertible" />. The types that
    /// have a round-trip format worth pinning — dates, times, <see cref="Guid" /> — have their own
    /// overloads below, which win over this one.
    /// </para>
    /// </remarks>
    public void PutAsString<T>(string key, T value) where T : IFormattable
    {
        this[key] = value.ToString(format: null, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Adds the given <see cref="bool" /> value as <c>"True"</c> or <c>"False"</c> to the
    /// <see cref="IJob" />'s data map, which <c>GetBoolean</c> and <c>TryGetBoolean</c> read back.
    /// </summary>
    /// <remarks>
    /// <see cref="bool" /> is not <see cref="IFormattable" /> — it has nothing to format for a culture —
    /// so it needs an overload of its own to stay writable this way.
    /// </remarks>
    public void PutAsString(string key, bool value)
    {
        this[key] = value.ToString();
    }

    /// <summary>
    /// Adds the given <see cref="char" /> value as a one-character string to the
    /// <see cref="IJob" />'s data map.
    /// </summary>
    /// <remarks>
    /// <see cref="char" />, like <see cref="bool" />, is not <see cref="IFormattable" />.
    /// </remarks>
    public void PutAsString(string key, char value)
    {
        this[key] = value.ToString();
    }

    /// <summary>
    /// Adds the given <see cref="DateTime" /> value as a round-trip ("O") formatted string to the
    /// <see cref="IJob" />'s data map, preserving sub-second precision and <see cref="DateTime.Kind" />.
    /// </summary>
    public void PutAsString(string key, DateTime value)
    {
        string strValue = value.ToString("O", CultureInfo.InvariantCulture);
        this[key] = strValue;
    }

    /// <summary>
    /// Adds the given <see cref="DateTimeOffset" /> value as a round-trip ("O") formatted string to the
    /// <see cref="IJob" />'s data map, preserving sub-second precision and the offset.
    /// </summary>
    public void PutAsString(string key, DateTimeOffset value)
    {
        string strValue = value.ToString("O", CultureInfo.InvariantCulture);
        this[key] = strValue;
    }

    /// <summary>
    /// Adds the given <see cref="DateOnly" /> value as a round-trip ("O", <c>yyyy-MM-dd</c>)
    /// formatted string to the <see cref="IJob" />'s data map.
    /// </summary>
    public void PutAsString(string key, DateOnly value)
    {
        string strValue = value.ToString("O", CultureInfo.InvariantCulture);
        this[key] = strValue;
    }

    /// <summary>
    /// Adds the given <see cref="TimeOnly" /> value as a round-trip ("O") formatted string to the
    /// <see cref="IJob" />'s data map.
    /// </summary>
    public void PutAsString(string key, TimeOnly value)
    {
        string strValue = value.ToString("O", CultureInfo.InvariantCulture);
        this[key] = strValue;
    }

    /// <summary>
    /// Adds the given <see cref="TimeSpan" /> value as a string version to the
    /// <see cref="IJob" />'s data map.
    /// </summary>
    public void PutAsString(string key, TimeSpan value)
    {
        string strValue = value.ToString();
        this[key] = strValue;
    }

    /// <summary>
    /// Adds the given <see cref="Guid" /> value as a string version to the
    /// <see cref="IJob" />'s data map. The hyphens are omitted from the  <see cref="Guid" />.
    /// </summary>
    public void PutAsString(string key, Guid value)
    {
        string strValue = value.ToString("N");
        this[key] = strValue;
    }

    /// <summary>
    /// Two maps are equal when they hold the same keys with equal values. The dirty flag does not
    /// participate.
    /// </summary>
    /// <remarks>
    /// Until 4.0 this comparison looked at the key sets only, so two maps with the same keys but
    /// different values compared equal — and assigning such a map as a nested value did not mark the
    /// outer map dirty, silently skipping the job store rewrite.
    /// </remarks>
    public bool Equals(JobDataMap? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || Count != other.Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, object?> pair in map)
        {
            if (!other.map.TryGetValue(pair.Key, out object? otherValue) || !Equals(pair.Value, otherValue))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc cref="Equals(JobDataMap?)" />
    public override bool Equals(object? obj)
    {
        return Equals(obj as JobDataMap);
    }

    /// <summary>
    /// A constant, consistent with <see cref="Equals(JobDataMap?)" />: equal maps trivially hash
    /// equally.
    /// </summary>
    /// <remarks>
    /// The map is mutable, so no hash derived from its content can honor the contract that an
    /// object's hash never changes while it sits in a hash-keyed collection — a map mutated after
    /// insertion would move out of reach of its own bucket and the entry would be silently lost.
    /// With a constant, hash-keyed use of maps degrades to an Equals scan instead of corrupting.
    /// </remarks>
    public override int GetHashCode()
    {
        return typeof(JobDataMap).GetHashCode();
    }

    internal JobDataMap Clone()
    {
        return new JobDataMap(map.WrappedMap);
    }
}
