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

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Quartz;

/// <summary>
/// The typed read accessors for <see cref="JobDataMap" /> and <see cref="SchedulerContext" />.
/// </summary>
/// <remarks>
/// <para>
/// A value can be stored either as its own type or — under <c>StoreJobDataAsStrings = true</c>, where the
/// job store keeps everything as strings — as an invariant-culture string. Each accessor accepts
/// both: the stored type is matched first, a string is parsed with
/// <see cref="CultureInfo.InvariantCulture" />, and only an exotic stored type falls back to
/// <see cref="Convert" /> semantics.
/// </para>
/// <para>
/// The accessors are declared for the two concrete types rather than for
/// <c>IReadOnlyDictionary&lt;string, object?&gt;</c> on purpose: an interface receiver would graft
/// them onto every string-keyed dictionary in any file with <c>using Quartz;</c>. Both blocks are
/// one-line bridges into a shared coercion core taking the looked-up value.
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
        public int GetInt(string key) => CoerceIntOrThrow(map.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="long" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public long GetLong(string key) => CoerceLongOrThrow(map.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="float" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public float GetFloat(string key) => CoerceFloatOrThrow(map.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="double" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public double GetDouble(string key) => CoerceDoubleOrThrow(map.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="decimal" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public decimal GetDecimal(string key) => CoerceDecimalOrThrow(map.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="bool" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool GetBoolean(string key) => CoerceBooleanOrThrow(map.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="char" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public char GetChar(string key) => CoerceCharOrThrow(map.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="string" /> value from the <see cref="JobDataMap" />,
        /// or <see langword="null" /> when the entry is missing or is not a string.
        /// </summary>
        public string? GetString(string key) => CoerceStringOrNull(map.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="DateTime" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public DateTime GetDateTime(string key) => CoerceDateTimeOrThrow(map.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="DateTimeOffset" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public DateTimeOffset GetDateTimeOffset(string key) => CoerceDateTimeOffsetOrThrow(map.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="TimeSpan" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public TimeSpan GetTimeSpan(string key) => CoerceTimeSpanOrThrow(map.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="Guid" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public Guid GetGuid(string key) => CoerceGuidOrThrow(map.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Try to retrieve the identified <see cref="int" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetInt(string key, out int value) => TryCoerceInt(map.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="long" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetLong(string key, out long value) => TryCoerceLong(map.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="float" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetFloat(string key, out float value) => TryCoerceFloat(map.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="double" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetDouble(string key, out double value) => TryCoerceDouble(map.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="decimal" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetDecimal(string key, out decimal value) => TryCoerceDecimal(map.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="bool" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetBoolean(string key, out bool value) => TryCoerceBoolean(map.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="char" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetChar(string key, out char value) => TryCoerceChar(map.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="string" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetString(string key, out string? value) => TryCoerceString(map.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="DateTime" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetDateTime(string key, out DateTime value) => TryCoerceDateTime(map.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="DateTimeOffset" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetDateTimeOffset(string key, out DateTimeOffset value) => TryCoerceDateTimeOffset(map.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="TimeSpan" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetTimeSpan(string key, out TimeSpan value) => TryCoerceTimeSpan(map.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="Guid" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetGuid(string key, out Guid value) => TryCoerceGuid(map.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Retrieve the identified <see cref="DateOnly" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public DateOnly GetDateOnly(string key) => CoerceDateOnlyOrThrow(map.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Try to retrieve the identified <see cref="DateOnly" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetDateOnly(string key, out DateOnly value) => TryCoerceDateOnly(map.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Retrieve the identified <see cref="TimeOnly" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public TimeOnly GetTimeOnly(string key) => CoerceTimeOnlyOrThrow(map.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Try to retrieve the identified <see cref="TimeOnly" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetTimeOnly(string key, out TimeOnly value) => TryCoerceTimeOnly(map.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Retrieve the identified enum value from the <see cref="JobDataMap" />; a string is parsed
        /// by name (case-insensitively), which is what <c>PutAsString</c> writes for an enum.
        /// </summary>
        public TEnum GetEnum<TEnum>(string key) where TEnum : struct, Enum => CoerceEnumOrThrow<TEnum>(map.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Try to retrieve the identified enum value from the <see cref="JobDataMap" />; a string is
        /// parsed by name (case-insensitively), which is what <c>PutAsString</c> writes for an enum.
        /// </summary>
        public bool TryGetEnum<TEnum>(string key, out TEnum value) where TEnum : struct, Enum => TryCoerceEnum(map.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified value from the <see cref="JobDataMap" /> when it is stored
        /// as a <typeparamref name="T" />. A pure type test — no string parsing or conversion.
        /// </summary>
        public bool TryGet<T>(string key, [MaybeNullWhen(false)] out T value) => TryCoerceExact(map.TryGetValue(key, out object? obj), obj, out value);
    }

    /// <summary>Typed read accessors for <see cref="SchedulerContext" />.</summary>
    extension(SchedulerContext context)
    {
        /// <summary>
        /// Retrieve the identified <see cref="int" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public int GetInt(string key) => CoerceIntOrThrow(context.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="long" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public long GetLong(string key) => CoerceLongOrThrow(context.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="float" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public float GetFloat(string key) => CoerceFloatOrThrow(context.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="double" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public double GetDouble(string key) => CoerceDoubleOrThrow(context.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="decimal" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public decimal GetDecimal(string key) => CoerceDecimalOrThrow(context.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="bool" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool GetBoolean(string key) => CoerceBooleanOrThrow(context.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="char" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public char GetChar(string key) => CoerceCharOrThrow(context.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="string" /> value from the <see cref="SchedulerContext" />,
        /// or <see langword="null" /> when the entry is missing or is not a string.
        /// </summary>
        public string? GetString(string key) => CoerceStringOrNull(context.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="DateTime" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public DateTime GetDateTime(string key) => CoerceDateTimeOrThrow(context.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="DateTimeOffset" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public DateTimeOffset GetDateTimeOffset(string key) => CoerceDateTimeOffsetOrThrow(context.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="TimeSpan" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public TimeSpan GetTimeSpan(string key) => CoerceTimeSpanOrThrow(context.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="Guid" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public Guid GetGuid(string key) => CoerceGuidOrThrow(context.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Try to retrieve the identified <see cref="int" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetInt(string key, out int value) => TryCoerceInt(context.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="long" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetLong(string key, out long value) => TryCoerceLong(context.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="float" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetFloat(string key, out float value) => TryCoerceFloat(context.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="double" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetDouble(string key, out double value) => TryCoerceDouble(context.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="decimal" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetDecimal(string key, out decimal value) => TryCoerceDecimal(context.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="bool" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetBoolean(string key, out bool value) => TryCoerceBoolean(context.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="char" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetChar(string key, out char value) => TryCoerceChar(context.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="string" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetString(string key, out string? value) => TryCoerceString(context.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="DateTime" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetDateTime(string key, out DateTime value) => TryCoerceDateTime(context.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="DateTimeOffset" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetDateTimeOffset(string key, out DateTimeOffset value) => TryCoerceDateTimeOffset(context.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="TimeSpan" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetTimeSpan(string key, out TimeSpan value) => TryCoerceTimeSpan(context.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="Guid" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetGuid(string key, out Guid value) => TryCoerceGuid(context.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Retrieve the identified <see cref="DateOnly" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public DateOnly GetDateOnly(string key) => CoerceDateOnlyOrThrow(context.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Try to retrieve the identified <see cref="DateOnly" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetDateOnly(string key, out DateOnly value) => TryCoerceDateOnly(context.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Retrieve the identified <see cref="TimeOnly" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public TimeOnly GetTimeOnly(string key) => CoerceTimeOnlyOrThrow(context.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Try to retrieve the identified <see cref="TimeOnly" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetTimeOnly(string key, out TimeOnly value) => TryCoerceTimeOnly(context.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Retrieve the identified enum value from the <see cref="SchedulerContext" />; a string is
        /// parsed by name (case-insensitively).
        /// </summary>
        public TEnum GetEnum<TEnum>(string key) where TEnum : struct, Enum => CoerceEnumOrThrow<TEnum>(context.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Try to retrieve the identified enum value from the <see cref="SchedulerContext" />; a
        /// string is parsed by name (case-insensitively).
        /// </summary>
        public bool TryGetEnum<TEnum>(string key, out TEnum value) where TEnum : struct, Enum => TryCoerceEnum(context.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified value from the <see cref="SchedulerContext" /> when it is
        /// stored as a <typeparamref name="T" />. A pure type test — no string parsing or conversion.
        /// </summary>
        public bool TryGet<T>(string key, [MaybeNullWhen(false)] out T value) => TryCoerceExact(context.TryGetValue(key, out object? obj), obj, out value);
    }

    // The coercion core. Each method takes the result of the receiver's TryGetValue so the two
    // extension blocks above stay one-line bridges over one implementation. The stored type and
    // the string form are matched without exceptions; only an exotic stored type reaches the
    // Convert-based cold path, whose semantics (including a stored null coercing to a type's
    // default) are kept from 3.x.

    private static int CoerceIntOrThrow(bool found, object? obj)
    {
        if (!TryCoerceInt(found, obj, out int value))
        {
            Throw.InvalidCastException("Identified object is not an Integer.");
        }

        return value;
    }

    private static long CoerceLongOrThrow(bool found, object? obj)
    {
        if (!TryCoerceLong(found, obj, out long value))
        {
            Throw.InvalidCastException("Identified object is not a Long.");
        }

        return value;
    }

    private static float CoerceFloatOrThrow(bool found, object? obj)
    {
        if (!TryCoerceFloat(found, obj, out float value))
        {
            Throw.InvalidCastException("Identified object is not a Float.");
        }

        return value;
    }

    private static double CoerceDoubleOrThrow(bool found, object? obj)
    {
        if (!TryCoerceDouble(found, obj, out double value))
        {
            Throw.InvalidCastException("Identified object is not a Double.");
        }

        return value;
    }

    private static decimal CoerceDecimalOrThrow(bool found, object? obj)
    {
        if (!TryCoerceDecimal(found, obj, out decimal value))
        {
            Throw.InvalidCastException("Identified object is not a Decimal.");
        }

        return value;
    }

    private static bool CoerceBooleanOrThrow(bool found, object? obj)
    {
        if (!TryCoerceBoolean(found, obj, out bool value))
        {
            Throw.InvalidCastException("Identified object is not a Boolean.");
        }

        return value;
    }

    private static char CoerceCharOrThrow(bool found, object? obj)
    {
        if (!TryCoerceChar(found, obj, out char value))
        {
            Throw.InvalidCastException("Identified object is not a Character.");
        }

        return value;
    }

    private static string? CoerceStringOrNull(bool found, object? obj)
    {
        TryCoerceString(found, obj, out string? value);
        return value;
    }

    private static DateTime CoerceDateTimeOrThrow(bool found, object? obj)
    {
        if (!TryCoerceDateTime(found, obj, out DateTime value))
        {
            Throw.InvalidCastException("Identified object is not a DateTime.");
        }

        return value;
    }

    private static DateTimeOffset CoerceDateTimeOffsetOrThrow(bool found, object? obj)
    {
        if (!TryCoerceDateTimeOffset(found, obj, out DateTimeOffset value))
        {
            Throw.InvalidCastException("Identified object is not a DateTimeOffset.");
        }

        return value;
    }

    private static TimeSpan CoerceTimeSpanOrThrow(bool found, object? obj)
    {
        if (!TryCoerceTimeSpan(found, obj, out TimeSpan value))
        {
            Throw.InvalidCastException("Identified object is not a TimeSpan.");
        }

        return value;
    }

    private static Guid CoerceGuidOrThrow(bool found, object? obj)
    {
        if (!TryCoerceGuid(found, obj, out Guid value))
        {
            Throw.InvalidCastException("Identified object is not a Guid");
        }

        return value;
    }

    private static DateOnly CoerceDateOnlyOrThrow(bool found, object? obj)
    {
        if (!TryCoerceDateOnly(found, obj, out DateOnly value))
        {
            Throw.InvalidCastException("Identified object is not a DateOnly.");
        }

        return value;
    }

    private static TimeOnly CoerceTimeOnlyOrThrow(bool found, object? obj)
    {
        if (!TryCoerceTimeOnly(found, obj, out TimeOnly value))
        {
            Throw.InvalidCastException("Identified object is not a TimeOnly.");
        }

        return value;
    }

    private static TEnum CoerceEnumOrThrow<TEnum>(bool found, object? obj) where TEnum : struct, Enum
    {
        if (!TryCoerceEnum(found, obj, out TEnum value))
        {
            Throw.InvalidCastException($"Identified object is not a {typeof(TEnum).Name}.");
        }

        return value;
    }

    private static bool TryCoerceExact<T>(bool found, object? obj, [MaybeNullWhen(false)] out T value)
    {
        if (found && obj is T typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

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
            // RoundtripKind restores what PutAsString's "O" format wrote: a string carrying an
            // offset or 'Z' comes back with its own clock reading and Kind, not shifted to local.
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
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

    private static bool TryCoerceDateOnly(bool found, object? obj, out DateOnly value)
    {
        if (!found)
        {
            value = default;
            return false;
        }

        if (obj is DateOnly d)
        {
            value = d;
            return true;
        }

        if (obj is string s)
        {
            return DateOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }

        value = default;
        return false;
    }

    private static bool TryCoerceTimeOnly(bool found, object? obj, out TimeOnly value)
    {
        if (!found)
        {
            value = default;
            return false;
        }

        if (obj is TimeOnly t)
        {
            value = t;
            return true;
        }

        if (obj is string s)
        {
            return TimeOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
        }

        value = default;
        return false;
    }

    private static bool TryCoerceEnum<TEnum>(bool found, object? obj, out TEnum value) where TEnum : struct, Enum
    {
        if (!found)
        {
            value = default;
            return false;
        }

        if (obj is TEnum e)
        {
            value = e;
            return true;
        }

        if (obj is string s)
        {
            return Enum.TryParse(s, ignoreCase: true, out value);
        }

        // A JSON round trip can hand the underlying number back instead of the enum.
        if (obj is int i)
        {
            value = (TEnum) Enum.ToObject(typeof(TEnum), i);
            return true;
        }

        if (obj is long l)
        {
            value = (TEnum) Enum.ToObject(typeof(TEnum), l);
            return true;
        }

        value = default;
        return false;
    }
}
