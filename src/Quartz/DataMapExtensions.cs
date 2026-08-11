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

using System.Globalization;

namespace Quartz;

/// <summary>
/// The typed read accessors for <see cref="JobDataMap" /> and <see cref="SchedulerContext" />.
/// </summary>
/// <remarks>
/// <para>
/// A value can be stored either as its own type or — under <c>UseProperties = true</c>, where the
/// job store keeps everything as strings — as an invariant-culture string. Each accessor accepts
/// both: the stored type is matched first, a string is parsed with
/// <see cref="CultureInfo.InvariantCulture" />, and only an exotic stored type falls back to
/// <see cref="Convert" /> semantics.
/// </para>
/// <para>
/// The accessors are declared for the two concrete types rather than for
/// <c>IReadOnlyDictionary&lt;string, object?&gt;</c> on purpose: an interface receiver would graft
/// them onto every string-keyed dictionary in any file with <c>using Quartz;</c>.
/// <see cref="SchedulerContext" /> gets the read accessors only; the <c>PutAsString</c> writers are
/// instance members of <see cref="JobDataMap" /> because they participate in its change tracking.
/// </para>
/// </remarks>
#pragma warning disable CA1708 // the analyzer trips over the compiler-generated extension-block markers; no user-visible names differ by case
public static class DataMapExtensions
#pragma warning restore CA1708
{
    /// <summary>Typed read accessors for <see cref="JobDataMap" />.</summary>
    extension(JobDataMap map)
    {
        /// <summary>
        /// Retrieve the identified <see cref="int" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public int GetInt(string key)
        {
            if (!TryCoerceInt(map.TryGetValue(key, out object? obj), obj, out int value))
            {
                Throw.InvalidCastException("Identified object is not an Integer.");
            }

            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="long" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public long GetLong(string key)
        {
            if (!TryCoerceLong(map.TryGetValue(key, out object? obj), obj, out long value))
            {
                Throw.InvalidCastException("Identified object is not a Long.");
            }

            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="float" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public float GetFloat(string key)
        {
            if (!TryCoerceFloat(map.TryGetValue(key, out object? obj), obj, out float value))
            {
                Throw.InvalidCastException("Identified object is not a Float.");
            }

            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="double" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public double GetDouble(string key)
        {
            if (!TryCoerceDouble(map.TryGetValue(key, out object? obj), obj, out double value))
            {
                Throw.InvalidCastException("Identified object is not a Double.");
            }

            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="decimal" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public decimal GetDecimal(string key)
        {
            if (!TryCoerceDecimal(map.TryGetValue(key, out object? obj), obj, out decimal value))
            {
                Throw.InvalidCastException("Identified object is not a Decimal.");
            }

            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="bool" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool GetBoolean(string key)
        {
            if (!TryCoerceBoolean(map.TryGetValue(key, out object? obj), obj, out bool value))
            {
                Throw.InvalidCastException("Identified object is not a Boolean.");
            }

            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="char" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public char GetChar(string key)
        {
            if (!TryCoerceChar(map.TryGetValue(key, out object? obj), obj, out char value))
            {
                Throw.InvalidCastException("Identified object is not a Character.");
            }

            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="string" /> value from the <see cref="JobDataMap" />,
        /// or <see langword="null" /> when the entry is missing or is not a string.
        /// </summary>
        public string? GetString(string key)
        {
            TryCoerceString(map.TryGetValue(key, out object? obj), obj, out string? value);
            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="DateTime" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public DateTime GetDateTime(string key)
        {
            if (!TryCoerceDateTime(map.TryGetValue(key, out object? obj), obj, out DateTime value))
            {
                Throw.InvalidCastException("Identified object is not a DateTime.");
            }

            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="DateTimeOffset" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public DateTimeOffset GetDateTimeOffset(string key)
        {
            if (!TryCoerceDateTimeOffset(map.TryGetValue(key, out object? obj), obj, out DateTimeOffset value))
            {
                Throw.InvalidCastException("Identified object is not a DateTimeOffset.");
            }

            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="TimeSpan" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public TimeSpan GetTimeSpan(string key)
        {
            if (!TryCoerceTimeSpan(map.TryGetValue(key, out object? obj), obj, out TimeSpan value))
            {
                Throw.InvalidCastException("Identified object is not a TimeSpan.");
            }

            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="Guid" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public Guid GetGuid(string key)
        {
            if (!TryCoerceGuid(map.TryGetValue(key, out object? obj), obj, out Guid value))
            {
                Throw.InvalidCastException("Identified object is not a Guid");
            }

            return value;
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="int" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetInt(string key, out int value)
        {
            return TryCoerceInt(map.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="long" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetLong(string key, out long value)
        {
            return TryCoerceLong(map.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="float" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetFloat(string key, out float value)
        {
            return TryCoerceFloat(map.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="double" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetDouble(string key, out double value)
        {
            return TryCoerceDouble(map.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="decimal" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetDecimal(string key, out decimal value)
        {
            return TryCoerceDecimal(map.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="bool" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetBoolean(string key, out bool value)
        {
            return TryCoerceBoolean(map.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="char" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetChar(string key, out char value)
        {
            return TryCoerceChar(map.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="string" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetString(string key, out string? value)
        {
            return TryCoerceString(map.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="DateTime" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetDateTime(string key, out DateTime value)
        {
            return TryCoerceDateTime(map.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="DateTimeOffset" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetDateTimeOffset(string key, out DateTimeOffset value)
        {
            return TryCoerceDateTimeOffset(map.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="TimeSpan" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetTimeSpan(string key, out TimeSpan value)
        {
            return TryCoerceTimeSpan(map.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="Guid" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetGuid(string key, out Guid value)
        {
            return TryCoerceGuid(map.TryGetValue(key, out object? obj), obj, out value);
        }
    }

    /// <summary>Typed read accessors for <see cref="SchedulerContext" />.</summary>
    extension(SchedulerContext context)
    {
        /// <summary>
        /// Retrieve the identified <see cref="int" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public int GetInt(string key)
        {
            if (!TryCoerceInt(context.TryGetValue(key, out object? obj), obj, out int value))
            {
                Throw.InvalidCastException("Identified object is not an Integer.");
            }

            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="long" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public long GetLong(string key)
        {
            if (!TryCoerceLong(context.TryGetValue(key, out object? obj), obj, out long value))
            {
                Throw.InvalidCastException("Identified object is not a Long.");
            }

            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="float" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public float GetFloat(string key)
        {
            if (!TryCoerceFloat(context.TryGetValue(key, out object? obj), obj, out float value))
            {
                Throw.InvalidCastException("Identified object is not a Float.");
            }

            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="double" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public double GetDouble(string key)
        {
            if (!TryCoerceDouble(context.TryGetValue(key, out object? obj), obj, out double value))
            {
                Throw.InvalidCastException("Identified object is not a Double.");
            }

            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="decimal" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public decimal GetDecimal(string key)
        {
            if (!TryCoerceDecimal(context.TryGetValue(key, out object? obj), obj, out decimal value))
            {
                Throw.InvalidCastException("Identified object is not a Decimal.");
            }

            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="bool" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool GetBoolean(string key)
        {
            if (!TryCoerceBoolean(context.TryGetValue(key, out object? obj), obj, out bool value))
            {
                Throw.InvalidCastException("Identified object is not a Boolean.");
            }

            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="char" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public char GetChar(string key)
        {
            if (!TryCoerceChar(context.TryGetValue(key, out object? obj), obj, out char value))
            {
                Throw.InvalidCastException("Identified object is not a Character.");
            }

            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="string" /> value from the <see cref="SchedulerContext" />,
        /// or <see langword="null" /> when the entry is missing or is not a string.
        /// </summary>
        public string? GetString(string key)
        {
            TryCoerceString(context.TryGetValue(key, out object? obj), obj, out string? value);
            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="DateTime" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public DateTime GetDateTime(string key)
        {
            if (!TryCoerceDateTime(context.TryGetValue(key, out object? obj), obj, out DateTime value))
            {
                Throw.InvalidCastException("Identified object is not a DateTime.");
            }

            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="DateTimeOffset" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public DateTimeOffset GetDateTimeOffset(string key)
        {
            if (!TryCoerceDateTimeOffset(context.TryGetValue(key, out object? obj), obj, out DateTimeOffset value))
            {
                Throw.InvalidCastException("Identified object is not a DateTimeOffset.");
            }

            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="TimeSpan" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public TimeSpan GetTimeSpan(string key)
        {
            if (!TryCoerceTimeSpan(context.TryGetValue(key, out object? obj), obj, out TimeSpan value))
            {
                Throw.InvalidCastException("Identified object is not a TimeSpan.");
            }

            return value;
        }

        /// <summary>
        /// Retrieve the identified <see cref="Guid" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public Guid GetGuid(string key)
        {
            if (!TryCoerceGuid(context.TryGetValue(key, out object? obj), obj, out Guid value))
            {
                Throw.InvalidCastException("Identified object is not a Guid");
            }

            return value;
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="int" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetInt(string key, out int value)
        {
            return TryCoerceInt(context.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="long" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetLong(string key, out long value)
        {
            return TryCoerceLong(context.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="float" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetFloat(string key, out float value)
        {
            return TryCoerceFloat(context.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="double" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetDouble(string key, out double value)
        {
            return TryCoerceDouble(context.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="decimal" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetDecimal(string key, out decimal value)
        {
            return TryCoerceDecimal(context.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="bool" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetBoolean(string key, out bool value)
        {
            return TryCoerceBoolean(context.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="char" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetChar(string key, out char value)
        {
            return TryCoerceChar(context.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="string" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetString(string key, out string? value)
        {
            return TryCoerceString(context.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="DateTime" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetDateTime(string key, out DateTime value)
        {
            return TryCoerceDateTime(context.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="DateTimeOffset" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetDateTimeOffset(string key, out DateTimeOffset value)
        {
            return TryCoerceDateTimeOffset(context.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="TimeSpan" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetTimeSpan(string key, out TimeSpan value)
        {
            return TryCoerceTimeSpan(context.TryGetValue(key, out object? obj), obj, out value);
        }

        /// <summary>
        /// Try to retrieve the identified <see cref="Guid" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetGuid(string key, out Guid value)
        {
            return TryCoerceGuid(context.TryGetValue(key, out object? obj), obj, out value);
        }
    }

    // The coercion core. Each method takes the result of the receiver's TryGetValue so the two
    // extension blocks above share one implementation. The stored type and the string form are
    // matched without exceptions; only an exotic stored type reaches the Convert-based cold path,
    // whose semantics (including a stored null coercing to a type's default) are kept from 3.x.

    private static bool TryCoerceInt(bool found, object? obj, out int value)
    {
        if (!found)
        {
            value = default;
            return false;
        }

        if (obj is int i)
        {
            value = i;
            return true;
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

    private static bool TryCoerceLong(bool found, object? obj, out long value)
    {
        if (!found)
        {
            value = default;
            return false;
        }

        if (obj is long l)
        {
            value = l;
            return true;
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

    private static bool TryCoerceFloat(bool found, object? obj, out float value)
    {
        if (!found)
        {
            value = default;
            return false;
        }

        if (obj is float f)
        {
            value = f;
            return true;
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

    private static bool TryCoerceDouble(bool found, object? obj, out double value)
    {
        if (!found)
        {
            value = default;
            return false;
        }

        if (obj is double d)
        {
            value = d;
            return true;
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

    private static bool TryCoerceDecimal(bool found, object? obj, out decimal value)
    {
        if (!found)
        {
            value = default;
            return false;
        }

        if (obj is decimal m)
        {
            value = m;
            return true;
        }

        if (obj is string s)
        {
            return decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
        }

        try
        {
            value = Convert.ToDecimal(obj, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }

    private static bool TryCoerceBoolean(bool found, object? obj, out bool value)
    {
        if (!found)
        {
            value = default;
            return false;
        }

        if (obj is bool b)
        {
            value = b;
            return true;
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

    private static bool TryCoerceChar(bool found, object? obj, out char value)
    {
        if (!found)
        {
            value = default;
            return false;
        }

        if (obj is char c)
        {
            value = c;
            return true;
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

    private static bool TryCoerceString(bool found, object? obj, out string? value)
    {
        if (!found || (obj is not string && obj is not null))
        {
            value = default;
            return false;
        }

        value = obj as string;
        return true;
    }

    private static bool TryCoerceDateTime(bool found, object? obj, out DateTime value)
    {
        if (!found)
        {
            value = default;
            return false;
        }

        if (obj is DateTime dt)
        {
            value = dt;
            return true;
        }

        if (obj is string s)
        {
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }

        if (obj is DateTimeOffset dto)
        {
            value = dto.DateTime;
            return true;
        }

        try
        {
            value = Convert.ToDateTime(obj, CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            value = default;
            return false;
        }
    }

    private static bool TryCoerceDateTimeOffset(bool found, object? obj, out DateTimeOffset value)
    {
        if (!found)
        {
            value = default;
            return false;
        }

        if (obj is DateTimeOffset dto)
        {
            value = dto;
            return true;
        }

        if (obj is string s)
        {
            return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }

        value = default;
        return false;
    }

    private static bool TryCoerceTimeSpan(bool found, object? obj, out TimeSpan value)
    {
        if (!found)
        {
            value = default;
            return false;
        }

        if (obj is TimeSpan ts)
        {
            value = ts;
            return true;
        }

        if (obj is string s)
        {
            return TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out value);
        }

        value = default;
        return false;
    }

    private static bool TryCoerceGuid(bool found, object? obj, out Guid value)
    {
        if (!found)
        {
            value = Guid.Empty;
            return false;
        }

        if (obj is Guid g)
        {
            value = g;
            return true;
        }

        if (obj is string s)
        {
            return Guid.TryParse(s, out value);
        }

        value = Guid.Empty;
        return false;
    }
}
