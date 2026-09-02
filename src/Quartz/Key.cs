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

namespace Quartz;

/// <summary>
/// Object representing a job or trigger key.
/// </summary>
/// <author>  <a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
// S4035 (seal classes implementing IEquatable<T>): the base cannot seal — JobKey and TriggerKey
// derive from it — and cannot drop IEquatable without breaking the public surface. The hazard the
// rule guards against does not arise: Equals demands exact runtime-type equality, so no derived
// key ever compares equal across types, and the sealed leaves define consistent typed overloads.
#pragma warning disable S4035
public class Key<T> : IComparable<Key<T>>, IComparable, IEquatable<Key<T>>
#pragma warning restore S4035
{
    /// <summary>
    /// The default group for scheduling entities, with the value "DEFAULT".
    /// </summary>
    public const string DefaultGroup = "DEFAULT";

    private readonly string name;
    private readonly string group;

    // Computed lazily rather than in the constructor so that a 3.x binary blob, which lacks the
    // field and deserializes it as 0, hashes correctly. [NonSerialized] keeps the serialized shape
    // unchanged. 0 doubles as the not-yet-computed sentinel; the rare key hashing to exactly 0 is
    // recomputed on every call, which is only the price the cache exists to avoid.
    [NonSerialized]
    private int hash;

    /// <summary>
    /// Construct a new key with the given name and <see cref="DefaultGroup"/> as group.
    /// </summary>
    /// <param name="name">the name</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public Key(string name) : this(name, DefaultGroup)
    {
    }

    /// <summary>
    /// Construct a new key with the given name and group.
    /// </summary>
    /// <param name="name">the name</param>
    /// <param name="group">the group</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="group"/> are <see langword="null"/>.</exception>
    // S5766 (validate data during deserialization) asks a [Serializable] type to repeat its
    // constructor's validation in a deserialization callback, because a formatter that rebuilds an
    // object field by field never runs the constructor. There is no such formatter left: BinaryFormatter
    // is gone from the shared framework, this type implements no ISerializable and declares no
    // serialization callback, and every serializer Quartz ships reaches a key through this constructor.
    // The attribute is metadata a 3.x-era blob's type identity is matched on and nothing more.
#pragma warning disable S5766
    public Key(string name, string group)
    {
        if (name is null)
            Throw.ArgumentNullException(nameof(name));
        if (group is null)
            Throw.ArgumentNullException(nameof(group));

        this.name = name;
        this.group = group;
    }
#pragma warning restore S5766

    /// <summary>
    /// Get the name portion of the key.
    /// </summary>
    /// <returns> the name
    /// </returns>
    public string Name => name;

    /// <summary> <para>
    /// Get the group portion of the key.
    /// </para>
    /// </summary>
    /// <returns> the group
    /// </returns>
    public string Group => group;

    /// <summary> <para>
    /// Return the string representation of the key. The format will be:
    /// &lt;group&gt;.&lt;name&gt;.
    /// </para>
    /// </summary>
    /// <returns> the string representation of the key
    /// </returns>
    public override string ToString()
    {
        return $"{Group}.{Name}";
    }


    // S2328 (no mutable fields in GetHashCode): the field is a memo over the readonly name and
    // group, so the value a hash-keyed collection observes can never change; it cannot be an
    // eagerly-computed readonly field because binary deserialization bypasses constructors.
#pragma warning disable S2328
    /// <inheritdoc />
    public override int GetHashCode()
    {
        int result = hash;
        if (result == 0)
        {
            const int Prime = 31;
            result = (Prime + group.GetHashCode()) * Prime + name.GetHashCode();
            hash = result;
        }

        return result;
    }
#pragma warning restore S2328

    /// <summary>
    /// Whether this key and <paramref name="other" /> name the same thing.
    /// </summary>
    /// <param name="other">The key to compare with.</param>
    /// <returns>
    /// <see langword="true" /> when the two are the same runtime type and both the name and the group
    /// are equal, ordinally.
    /// </returns>
    public bool Equals(Key<T>? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }
        if (other is null)
        {
            return false;
        }
        if (GetType() != other.GetType())
        {
            return false;
        }

        return group == other.group && name == other.name;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return Equals(obj as Key<T>);
    }

    /// <summary>
    /// Splits the <c>&lt;group&gt;.&lt;name&gt;</c> form <see cref="ToString" /> composes back into
    /// its parts, at the first '.' — the exact inverse of the composition. A group containing '.'
    /// is the ambiguous case: it parses at the first dot, which is not the key it printed from.
    /// </summary>
    private protected static bool TryParseParts(string? s, out string name, out string group)
    {
        int separator = s?.IndexOf('.') ?? -1;
        if (s is null || separator < 0)
        {
            name = null!;
            group = null!;
            return false;
        }

        group = s.Substring(0, separator);
        name = s.Substring(separator + 1);
        return true;
    }

    /// <summary>
    /// Orders keys by group and then name, ordinally, with <see cref="DefaultGroup" /> sorting before
    /// every other group.
    /// </summary>
    public int CompareTo(Key<T>? other)
    {
        if (other is null)
        {
            return 1;
        }

        if (ReferenceEquals(group, other.group))
        {
            return ReferenceEquals(name, other.name) ? 0 : StringComparer.Ordinal.Compare(name, other.name);
        }

        if (group == DefaultGroup && other.group != DefaultGroup)
        {
            return -1;
        }
        if (group != DefaultGroup && other.group == DefaultGroup)
        {
            return 1;
        }

        int r = StringComparer.Ordinal.Compare(group, other.group);
        if (r != 0)
        {
            return r;
        }

        return ReferenceEquals(name, other.name) ? 0 : StringComparer.Ordinal.Compare(name, other.name);
    }

    /// <summary>
    /// The non-generic comparison, so that a key reaches the sorting machinery that asks for
    /// <see cref="IComparable" /> rather than <see cref="IComparable{T}" />.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="obj" /> is not a key of this kind.</exception>
    int IComparable.CompareTo(object? obj)
    {
        if (obj is null)
        {
            return 1;
        }

        if (obj is not Key<T> other)
        {
            return Throw.ArgumentException<int>($"Object must be of type {typeof(Key<T>)}.", nameof(obj));
        }

        return CompareTo(other);
    }

    /// <summary>
    /// Whether two keys name the same thing.
    /// </summary>
    public static bool operator ==(Key<T>? left, Key<T>? right)
    {
        if (left is null)
        {
            return right is null;
        }

        return left.Equals(right);
    }

    /// <summary>
    /// Whether two keys name different things.
    /// </summary>
    public static bool operator !=(Key<T>? left, Key<T>? right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Whether <paramref name="left" /> sorts before <paramref name="right" />: by group, then by
    /// name, ordinally.
    /// </summary>
    public static bool operator <(Key<T> left, Key<T> right)
    {
        return left is null ? right is not null : left.CompareTo(right) < 0;
    }

    /// <summary>
    /// Whether <paramref name="left" /> sorts before <paramref name="right" /> or equals it.
    /// </summary>
    public static bool operator <=(Key<T> left, Key<T> right)
    {
        return left is null || left.CompareTo(right) <= 0;
    }

    /// <summary>
    /// Whether <paramref name="left" /> sorts after <paramref name="right" />: by group, then by
    /// name, ordinally.
    /// </summary>
    public static bool operator >(Key<T> left, Key<T> right)
    {
        return left is not null && left.CompareTo(right) > 0;
    }

    /// <summary>
    /// Whether <paramref name="left" /> sorts after <paramref name="right" /> or equals it.
    /// </summary>
    public static bool operator >=(Key<T> left, Key<T> right)
    {
        return left is null ? right is null : left.CompareTo(right) >= 0;
    }
}