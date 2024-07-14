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

namespace Quartz.Util;

/// <summary>
/// Object representing a job or trigger key.
/// </summary>
/// <author>  <a href="mailto:jeff@binaryfeed.org">Jeffrey Wescott</a></author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
public class Key<T> : IComparable<Key<T>>
{
    /// <summary>
    /// The default group for scheduling entities, with the value "DEFAULT".
    /// </summary>
    public const string DefaultGroup = "DEFAULT";

    private string name = null!;
    private string group = null!;

    protected Key()
    {
    }

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
    public Key(string name, string group)
    {
        if (name is null)
            ThrowHelper.ThrowArgumentNullException(nameof(name));
        if (group is null)
            ThrowHelper.ThrowArgumentNullException(nameof(group));

        this.name = name;
        this.group = group;
    }

    /// <summary>
    /// Get the name portion of the key.
    /// </summary>
    /// <returns> the name
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public virtual string Name
    {
        get { return name; }
        set
        {
            if (value is null)
                ThrowHelper.ThrowArgumentNullException(nameof(value));

            name = value;
        }
    }

    /// <summary> <para>
    /// Get the group portion of the key.
    /// </para>
    /// </summary>
    /// <returns> the group
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    public virtual string Group
    {
        get { return group; }
        set
        {
            if (value is null)
                ThrowHelper.ThrowArgumentNullException(nameof(value));

            group = value;
        }
    }

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


    public override int GetHashCode()
    {
        const int Prime = 31;
        int result = 1;
        result = Prime * result + (@group is null ? 0 : group.GetHashCode());
        result = Prime * result + (name is null ? 0 : name.GetHashCode());
        return result;
    }

    public override bool Equals(object? obj)
    {
        if (ReferenceEquals(this, obj))
        {
            return true;
        }
        if (obj is null)
        {
            return false;
        }
        if (GetType() != obj.GetType())
        {
            return false;
        }

        Key<T> other = (Key<T>) obj;
        return group == other.group && name == other.name;
    }

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

    public static bool operator ==(Key<T>? left, Key<T>? right)
    {
        if (left is null)
        {
            return right is null;
        }

        return left.Equals(right);
    }

    public static bool operator !=(Key<T>? left, Key<T>? right)
    {
        return !(left == right);
    }

    public static bool operator <(Key<T> left, Key<T> right)
    {
        return left is null ? right is not null : left.CompareTo(right) < 0;
    }

    public static bool operator <=(Key<T> left, Key<T> right)
    {
        return left is null || left.CompareTo(right) <= 0;
    }

    public static bool operator >(Key<T> left, Key<T> right)
    {
        return left is not null && left.CompareTo(right) > 0;
    }

    public static bool operator >=(Key<T> left, Key<T> right)
    {
        return left is null ? right is null : left.CompareTo(right) >= 0;
    }
}