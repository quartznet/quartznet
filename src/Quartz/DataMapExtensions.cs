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
/// There is one accessor per type only for the handful of types job data is usually made of — the
/// set twenty years of Quartz tutorials teach: <c>int</c>, <c>long</c>, <c>float</c>,
/// <c>double</c>, <c>bool</c>, <c>string</c> and the <see cref="DateTimeOffset" /> Quartz's own
/// times are. Everything else is <c>Get&lt;T&gt;</c> / <c>TryGet&lt;T&gt;</c> /
/// <c>GetValueOrDefault&lt;T&gt;</c>, which coerce exactly as a named accessor would:
/// <c>Get&lt;Guid&gt;</c> reads what a <c>GetGuid</c> would have read, and so do
/// <c>Get&lt;TimeSpan&gt;</c>, <c>Get&lt;decimal&gt;</c>, <c>Get&lt;DateOnly&gt;</c> and
/// <c>Get&lt;SomeEnum&gt;</c>. A named accessor per readable type is a set that only ever grows, and
/// the generic one is not a weaker substitute for it.
/// </para>
/// <para>
/// The accessors are declared for the two concrete types rather than for
/// <c>IReadOnlyDictionary&lt;string, object?&gt;</c> on purpose: an interface receiver would graft
/// them onto every string-keyed dictionary in any file with <c>using Quartz;</c>. Both blocks are
/// one-line bridges into a shared coercion core taking the looked-up value.
/// </para>
/// <para>
/// <see cref="SchedulerContext" /> gets the read accessors only, and that asymmetry with
/// <see cref="JobDataMap" /> is deliberate. The <c>PutAsString</c> writers are instance members of
/// <see cref="JobDataMap" /> because writing to a job's map is not just storing a value: the map
/// records that it was changed, which is what tells the scheduler to persist it after a
/// <c>[PersistJobDataAfterExecution]</c> job runs, and its equality is part of deciding whether
/// anything moved. An extension cannot participate in that, and a context has nothing for it to
/// participate in — the context is process state that no store writes back.
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
        /// Retrieve the identified <see cref="bool" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool GetBoolean(string key) => CoerceBooleanOrThrow(map.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="string" /> value from the <see cref="JobDataMap" />,
        /// or <see langword="null" /> when the entry is missing or is not a string.
        /// </summary>
        public string? GetString(string key) => CoerceStringOrNull(map.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="DateTimeOffset" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public DateTimeOffset GetDateTimeOffset(string key) => CoerceDateTimeOffsetOrThrow(map.TryGetValue(key, out object? obj), obj);

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
        /// Try to retrieve the identified <see cref="bool" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetBoolean(string key, out bool value) => TryCoerceBoolean(map.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="string" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetString(string key, out string? value) => TryCoerceString(map.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="DateTimeOffset" /> value from the <see cref="JobDataMap" />.
        /// </summary>
        public bool TryGetDateTimeOffset(string key, out DateTimeOffset value) => TryCoerceDateTimeOffset(map.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified value from the <see cref="JobDataMap" /> as a
        /// <typeparamref name="T" />.
        /// </summary>
        /// <remarks>
        /// Coerces exactly as <c>Get&lt;T&gt;</c> does: the stored type first, then the string form
        /// the store may have written, and a plain type test for a type Quartz cannot parse.
        /// </remarks>
        public bool TryGet<T>(string key, [MaybeNullWhen(false)] out T value) => TryCoerce(map.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Retrieve the identified value from the <see cref="JobDataMap" /> as a
        /// <typeparamref name="T" />, saying what went wrong instead of answering
        /// <see langword="false" /> as <c>TryGet&lt;T&gt;</c> does.
        /// </summary>
        /// <remarks>
        /// The accessor for every type this class does not name: <c>Get&lt;Guid&gt;</c>,
        /// <c>Get&lt;TimeSpan&gt;</c>, <c>Get&lt;DayOfWeek&gt;</c>, <c>Get&lt;decimal&gt;</c> read
        /// exactly what a named accessor for each of them would have. The stored type is matched
        /// first; a string is parsed with <see cref="CultureInfo.InvariantCulture" /> for every type
        /// <c>PutAsString</c> writes a string form of, and an enum is parsed by name
        /// case-insensitively; an exotic stored type falls back to <see cref="Convert" />. A type
        /// Quartz has no string form for — a payload class of your own — is a plain type test, which
        /// is all it could ever have been.
        /// </remarks>
        /// <exception cref="KeyNotFoundException">The map has no entry under <paramref name="key" />.</exception>
        /// <exception cref="InvalidCastException">The entry cannot be read as a <typeparamref name="T" />.</exception>
        public T Get<T>(string key) => CoerceOrThrow<T>(key, map.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified value from the <see cref="JobDataMap" /> as a
        /// <typeparamref name="T" />, or <paramref name="defaultValue" /> when there is no such entry
        /// or it cannot be read as one.
        /// </summary>
        /// <remarks>
        /// Coerces exactly as <c>Get&lt;T&gt;</c> does: the stored type first, then the string form
        /// the store may have written, and a plain type test for a type Quartz cannot parse.
        /// </remarks>
        public T GetValueOrDefault<T>(string key, T defaultValue) => CoerceOrDefault(map.TryGetValue(key, out object? obj), obj, defaultValue);
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
        /// Retrieve the identified <see cref="bool" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool GetBoolean(string key) => CoerceBooleanOrThrow(context.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="string" /> value from the <see cref="SchedulerContext" />,
        /// or <see langword="null" /> when the entry is missing or is not a string.
        /// </summary>
        public string? GetString(string key) => CoerceStringOrNull(context.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified <see cref="DateTimeOffset" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public DateTimeOffset GetDateTimeOffset(string key) => CoerceDateTimeOffsetOrThrow(context.TryGetValue(key, out object? obj), obj);

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
        /// Try to retrieve the identified <see cref="bool" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetBoolean(string key, out bool value) => TryCoerceBoolean(context.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="string" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetString(string key, out string? value) => TryCoerceString(context.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified <see cref="DateTimeOffset" /> value from the <see cref="SchedulerContext" />.
        /// </summary>
        public bool TryGetDateTimeOffset(string key, out DateTimeOffset value) => TryCoerceDateTimeOffset(context.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Try to retrieve the identified value from the <see cref="SchedulerContext" /> as a
        /// <typeparamref name="T" />.
        /// </summary>
        /// <remarks>
        /// Coerces exactly as <c>Get&lt;T&gt;</c> does: the stored type first, then the string form
        /// the store may have written, and a plain type test for a type Quartz cannot parse.
        /// </remarks>
        public bool TryGet<T>(string key, [MaybeNullWhen(false)] out T value) => TryCoerce(context.TryGetValue(key, out object? obj), obj, out value);

        /// <summary>
        /// Retrieve the identified value from the <see cref="SchedulerContext" /> as a
        /// <typeparamref name="T" />, saying what went wrong instead of answering
        /// <see langword="false" /> as <c>TryGet&lt;T&gt;</c> does.
        /// </summary>
        /// <remarks>
        /// The accessor for every type this class does not name: <c>Get&lt;Guid&gt;</c>,
        /// <c>Get&lt;TimeSpan&gt;</c>, <c>Get&lt;DayOfWeek&gt;</c>, <c>Get&lt;decimal&gt;</c> read
        /// exactly what a named accessor for each of them would have. The stored type is matched
        /// first; a string is parsed with <see cref="CultureInfo.InvariantCulture" />, and an enum is
        /// parsed by name case-insensitively; an exotic stored type falls back to
        /// <see cref="Convert" />. A type Quartz has no string form for — a value of your own — is a
        /// plain type test, which is all it could ever have been.
        /// </remarks>
        /// <exception cref="KeyNotFoundException">The context has no entry under <paramref name="key" />.</exception>
        /// <exception cref="InvalidCastException">The entry cannot be read as a <typeparamref name="T" />.</exception>
        public T Get<T>(string key) => CoerceOrThrow<T>(key, context.TryGetValue(key, out object? obj), obj);

        /// <summary>
        /// Retrieve the identified value from the <see cref="SchedulerContext" /> as a
        /// <typeparamref name="T" />, or <paramref name="defaultValue" /> when there is no such entry
        /// or it cannot be read as one.
        /// </summary>
        /// <remarks>
        /// Coerces exactly as <c>Get&lt;T&gt;</c> does: the stored type first, then the string form
        /// the store may have written, and a plain type test for a type Quartz cannot parse.
        /// </remarks>
        public T GetValueOrDefault<T>(string key, T defaultValue) => CoerceOrDefault(context.TryGetValue(key, out object? obj), obj, defaultValue);
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

    private static bool CoerceBooleanOrThrow(bool found, object? obj)
    {
        if (!TryCoerceBoolean(found, obj, out bool value))
        {
            Throw.InvalidCastException("Identified object is not a Boolean.");
        }

        return value;
    }

    private static string? CoerceStringOrNull(bool found, object? obj)
    {
        TryCoerceString(found, obj, out string? value);
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

    /// <summary>
    /// The generic accessors' coercion: the stored type first, then the same parsing the named
    /// accessors do, for every type Quartz has a string form of.
    /// </summary>
    /// <remarks>
    /// This is what lets <c>Get&lt;T&gt;</c> stand in for a named accessor per type. Under
    /// <c>StoreJobDataAsStrings = true</c> everything comes back as a string, so a generic accessor
    /// that only tested the type would answer <see langword="false" /> for values it had itself
    /// written — which is why the dispatch is here rather than a cast at the call site. The order is
    /// the one the named accessors use: the stored type wins, so nothing is parsed that need not be,
    /// and a type with no string form falls through to the type test alone.
    /// </remarks>
    private static bool TryCoerce<T>(bool found, object? obj, [MaybeNullWhen(false)] out T value)
    {
        if (found && obj is T typed)
        {
            value = typed;
            return true;
        }

        if (found && obj is not null && TryParseAs(obj, out T? parsed))
        {
            value = parsed!;
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// <see cref="TryCoerce{T}" />, with the two ways it can fail told apart: an absent entry and an
    /// entry that cannot be read as the type asked for are different mistakes, and a message that
    /// named neither the key nor the types would leave the caller guessing which one was made.
    /// </summary>
    private static T CoerceOrThrow<T>(string key, bool found, object? obj)
    {
        if (!found)
        {
            Throw.KeyNotFoundException($"No entry named '{key}'.");
        }

        if (TryCoerce(true, obj, out T? value))
        {
            return value!;
        }

        object storedType = obj is null ? "null" : obj.GetType();
        Throw.InvalidCastException($"Entry '{key}' holds {storedType}, which cannot be read as {typeof(T)}.");
        return default!;
    }

    private static T CoerceOrDefault<T>(bool found, object? obj, T defaultValue)
    {
        return TryCoerce(found, obj, out T? value) ? value! : defaultValue;
    }

    /// <summary>
    /// Reads a stored value as <typeparamref name="T" /> when Quartz knows how — which is every type
    /// <c>JobDataMap.PutAsString</c> writes a string form of, plus any enum.
    /// </summary>
    /// <remarks>
    /// A chain of <c>typeof(T) ==</c> tests rather than reflection: each comparison folds away when
    /// the generic is instantiated, so a <c>Get&lt;Guid&gt;</c> compiles to the Guid branch, and
    /// nothing here needs a type to be preserved for trimming or generated at run time.
    /// </remarks>
    private static bool TryParseAs<T>(object obj, [MaybeNullWhen(false)] out T value)
    {
        if (typeof(T) == typeof(int) && TryCoerceInt(true, obj, out int intValue))
        {
            value = (T) (object) intValue;
            return true;
        }

        if (typeof(T) == typeof(long) && TryCoerceLong(true, obj, out long longValue))
        {
            value = (T) (object) longValue;
            return true;
        }

        if (typeof(T) == typeof(float) && TryCoerceFloat(true, obj, out float floatValue))
        {
            value = (T) (object) floatValue;
            return true;
        }

        if (typeof(T) == typeof(double) && TryCoerceDouble(true, obj, out double doubleValue))
        {
            value = (T) (object) doubleValue;
            return true;
        }

        if (typeof(T) == typeof(decimal) && TryCoerceDecimal(true, obj, out decimal decimalValue))
        {
            value = (T) (object) decimalValue;
            return true;
        }

        if (typeof(T) == typeof(bool) && TryCoerceBoolean(true, obj, out bool boolValue))
        {
            value = (T) (object) boolValue;
            return true;
        }

        if (typeof(T) == typeof(char) && TryCoerceChar(true, obj, out char charValue))
        {
            value = (T) (object) charValue;
            return true;
        }

        if (typeof(T) == typeof(string) && TryCoerceString(true, obj, out string? stringValue) && stringValue is not null)
        {
            value = (T) (object) stringValue;
            return true;
        }

        if (typeof(T) == typeof(DateTime) && TryCoerceDateTime(true, obj, out DateTime dateTimeValue))
        {
            value = (T) (object) dateTimeValue;
            return true;
        }

        if (typeof(T) == typeof(DateTimeOffset) && TryCoerceDateTimeOffset(true, obj, out DateTimeOffset dateTimeOffsetValue))
        {
            value = (T) (object) dateTimeOffsetValue;
            return true;
        }

        if (typeof(T) == typeof(DateOnly) && TryCoerceDateOnly(true, obj, out DateOnly dateOnlyValue))
        {
            value = (T) (object) dateOnlyValue;
            return true;
        }

        if (typeof(T) == typeof(TimeOnly) && TryCoerceTimeOnly(true, obj, out TimeOnly timeOnlyValue))
        {
            value = (T) (object) timeOnlyValue;
            return true;
        }

        if (typeof(T) == typeof(TimeSpan) && TryCoerceTimeSpan(true, obj, out TimeSpan timeSpanValue))
        {
            value = (T) (object) timeSpanValue;
            return true;
        }

        if (typeof(T) == typeof(Guid) && TryCoerceGuid(true, obj, out Guid guidValue))
        {
            value = (T) (object) guidValue;
            return true;
        }

        if (typeof(T).IsEnum && TryCoerceEnum(typeof(T), obj, out object? enumValue))
        {
            value = (T) enumValue;
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
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
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
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
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
            return decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
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

    /// <summary>
    /// Reads a stored value as a value of the given enum type: the name <c>PutAsString</c> wrote,
    /// case-insensitively, or the underlying number a JSON round trip can hand back instead.
    /// </summary>
    /// <remarks>
    /// Takes the type rather than a generic parameter because its caller has only
    /// <c>typeof(T).IsEnum</c> to go on, and <c>T</c> cannot be constrained to
    /// <see cref="Enum" /> there. Neither call needs a type to be preserved for trimming: the enum
    /// type is the one the caller named, so it is statically reachable.
    /// </remarks>
    private static bool TryCoerceEnum(Type enumType, object obj, [NotNullWhen(true)] out object? value)
    {
        if (obj is string s)
        {
            return Enum.TryParse(enumType, s, ignoreCase: true, out value) && value is not null;
        }

        // A JSON round trip can hand the underlying number back instead of the enum.
        if (obj is int or long)
        {
            value = Enum.ToObject(enumType, obj);
            return true;
        }

        value = null;
        return false;
    }
}
