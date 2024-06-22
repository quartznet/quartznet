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

namespace Quartz.Util;

/// <summary>
/// An implementation of <see cref="IDictionary" /> that wraps another <see cref="IDictionary" />
/// and flags itself 'dirty' when it is modified, enforces that all keys are
/// strings.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
public class StringKeyDirtyFlagMap : DirtyFlagMap<string, object>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StringKeyDirtyFlagMap"/> class.
    /// </summary>
    public StringKeyDirtyFlagMap()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StringKeyDirtyFlagMap"/> class.
    /// </summary>
    /// <param name="initialCapacity">The initial capacity.</param>
    public StringKeyDirtyFlagMap(int initialCapacity) : base(initialCapacity)
    {
    }

    /// <summary>
    /// Serialization constructor.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="context"></param>
    protected StringKeyDirtyFlagMap(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }

    /// <summary>
    /// Retrieve the identified <see cref="int" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual int GetInt(string key)
    {
        if (!TryGetInt(key, out int value))
        {
            ThrowHelper.ThrowInvalidCastException("Identified object is not an Integer.");
        }

        return value;
    }

    /// <summary>
    /// Retrieve the identified <see cref="long" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual long GetLong(string key)
    {
        if (!TryGetLong(key, out long value))
        {
            ThrowHelper.ThrowInvalidCastException("Identified object is not a Long.");
        }

        return value;
    }

    /// <summary>
    /// Retrieve the identified <see cref="float" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual float GetFloat(string key)
    {
        if (!TryGetFloat(key, out float value))
        {
            ThrowHelper.ThrowInvalidCastException("Identified object is not a Float.");
        }

        return value;
    }

    /// <summary>
    /// Retrieve the identified <see cref="double" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual double GetDouble(string key)
    {
        if (!TryGetDouble(key, out double value))
        {
            ThrowHelper.ThrowInvalidCastException("Identified object is not a Double.");
        }

        return value;
    }

    /// <summary>
    /// Retrieve the identified <see cref="bool" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual bool GetBoolean(string key)
    {
        if (!TryGetBoolean(key, out bool value))
        {
            ThrowHelper.ThrowInvalidCastException("Identified object is not a Boolean.");
        }

        return value;
    }

    /// <summary>
    /// Retrieve the identified <see cref="char" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual char GetChar(string key)
    {
        if (!TryGetChar(key, out char value))
        {
            ThrowHelper.ThrowInvalidCastException("Identified object is not a Character.");
        }

        return value;
    }

    /// <summary>
    /// Retrieve the identified <see cref="string" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual string? GetString(string key)
    {
        TryGetString(key, out string? value);
        return value;
    }

    /// <summary>
    /// Retrieve the identified <see cref="DateTime" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual DateTime GetDateTime(string key)
    {
        if (!TryGetDateTime(key, out DateTime value))
        {
            ThrowHelper.ThrowInvalidCastException("Identified object is not a DateTime.");
        }

        return value;
    }

    /// <summary>
    /// Retrieve the identified <see cref="DateTimeOffset" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual DateTimeOffset GetDateTimeOffset(string key)
    {
        if (!TryGetDateTimeOffset(key, out DateTimeOffset value))
        {
            ThrowHelper.ThrowInvalidCastException("Identified object is not a DateTimeOffset.");
        }

        return value;
    }

    /// <summary>
    /// Retrieve the identified <see cref="TimeSpan" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual TimeSpan GetTimeSpan(string key)
    {
        if (!TryGetTimeSpan(key, out TimeSpan value))
        {
            ThrowHelper.ThrowInvalidCastException("Identified object is not a TimeSpan.");
        }

        return value;
    }

    /// <summary>
    /// Retrieve the identified <see cref="Guid" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual Guid GetGuid(string key)
    {
        if (!TryGetGuid(key, out Guid value))
        {
            ThrowHelper.ThrowInvalidCastException("Identified object is not a Guid");
        }

        return value;
    }

    /// <summary>
    /// Try to retrieve the identified <see cref="int" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual bool TryGetInt(string key, out int value)
    {
        if (!TryGetValue(key, out object? obj))
        {
            value = default;
            return false;
        }

        if (obj is string s)
        {
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        try
        {
            value = Convert.ToInt32(obj);
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Try to retrieve the identified <see cref="bool" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual bool TryGetBoolean(string key, out bool value)
    {
        if (!TryGetValue(key, out object? obj))
        {
            value = default;
            return false;
        }

        if (obj is string s)
        {
            value = string.Equals("true", s, StringComparison.OrdinalIgnoreCase);
            return true;
        }

        try
        {
            value = Convert.ToBoolean(obj);
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Try to retrieve the identified <see cref="double" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual bool TryGetDouble(string key, out double value)
    {
        if (!TryGetValue(key, out object? obj))
        {
            value = default;
            return false;
        }

        if (obj is string s)
        {
            return double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
        }

        try
        {
            value = Convert.ToDouble(obj);
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Try to retrieve the identified <see cref="float" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual bool TryGetFloat(string key, out float value)
    {
        if (!TryGetValue(key, out object? obj))
        {
            value = default;
            return false;
        }

        if (obj is string s)
        {
            return float.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value);
        }

        try
        {
            value = Convert.ToSingle(obj);
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Try to retrieve the identified <see cref="long" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual bool TryGetLong(string key, out long value)
    {
        if (!TryGetValue(key, out object? obj))
        {
            value = default;
            return false;
        }

        if (obj is string s)
        {
            return long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        try
        {
            value = Convert.ToInt64(obj);
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Try to retrieve the identified <see cref="DateTime" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual bool TryGetDateTime(string key, out DateTime value)
    {
        if (!TryGetValue(key, out object? obj))
        {
            value = default;
            return false;
        }

        if (obj is string s)
        {
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }

        try
        {
            value = obj is DateTimeOffset dto
                ? dto.DateTime
                : Convert.ToDateTime(obj, CultureInfo.InvariantCulture);

            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Try to retrieve the identified <see cref="DateTimeOffset" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual bool TryGetDateTimeOffset(string key, out DateTimeOffset value)
    {
        if (!TryGetValue(key, out object? obj))
        {
            value = default;
            return false;
        }

        if (obj is string s)
        {
            return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }

        try
        {
            value = (DateTimeOffset) obj;
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Try to retrieve the identified <see cref="TimeSpan" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual bool TryGetTimeSpan(string key, out TimeSpan value)
    {
        if (!TryGetValue(key, out object? obj))
        {
            value = default;
            return false;
        }

        if (obj is string s)
        {
            return TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out value);
        }

        try
        {
            value = (TimeSpan) obj;
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Try to retrieve the identified <see cref="Guid" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual bool TryGetGuid(string key, out Guid value)
    {
        if (!TryGetValue(key, out object? obj))
        {
            value = Guid.Empty;
            return false;
        }

        if (obj is string s)
        {
            return Guid.TryParse(s, out value);
        }

        try
        {
            value = (Guid) obj;
            return true;
        }
        catch
        {
            value = Guid.Empty;
            return false;
        }
    }

    /// <summary>
    /// Try to retrieve the identified <see cref="char" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual bool TryGetChar(string key, out char value)
    {
        if (!TryGetValue(key, out object? obj))
        {
            value = default;
            return false;
        }

        try
        {
            value = Convert.ToChar(obj);
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Try to retrieve the identified <see cref="string" /> value from the <see cref="JobDataMap" />.
    /// </summary>
    public virtual bool TryGetString(string key, out string? value)
    {
        if (!TryGetValue(key, out object? obj) || (obj is not string && obj is not null))
        {
            value = default;
            return false;
        }

        value = obj as string;
        return true;
    }

}