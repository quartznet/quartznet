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

using System.ComponentModel;
using System.Globalization;

namespace Quartz.Util;

/// <summary>
/// The conversions <see cref="ValueConverter" /> makes without a <see cref="TypeConverter" />.
/// </summary>
/// <remarks>
/// <para>
/// Each one is the converter <see cref="TypeDescriptor" /> picks for the pair, written out: the same
/// parse, the same culture (invariant), the same exception type and message, and the same quirks — a
/// hexadecimal integer behind <c>#</c>, <c>0x</c> or <c>&amp;h</c>, a trimmed string, a case-insensitive
/// and comma-combining enum, the empty string as <see cref="DateTime.MinValue" /> or as
/// <see langword="null" /> for a <see cref="Nullable{T}" />. Where a converter would have taken a route
/// this file does not reproduce, it declines and the caller falls through to the converter or the
/// refusal; it never answers differently. <c>ValueConverterDifferentialTest</c> runs a corpus through
/// both and holds them equal.
/// </para>
/// <para>
/// What is covered: a string to a primitive, <see cref="decimal" />, an enum, <see cref="Guid" />,
/// <see cref="TimeSpan" />, <see cref="DateTime" />, <see cref="DateTimeOffset" />, <see cref="Uri" />,
/// or the <see cref="Nullable{T}" /> of one; a number to a primitive, to an enum, or to a string; an
/// enum to a string, or to any of those targets through its underlying number; and
/// <see cref="bool" />, <see cref="char" />, <see cref="Guid" />, <see cref="TimeSpan" /> and
/// <see cref="Uri" /> to a string. An enum declaring a <see cref="TypeConverterAttribute" /> of its own is
/// left to that converter. A converter registered at run time through <see cref="TypeDescriptor" /> for
/// one of these framework types is not consulted.
/// </para>
/// </remarks>
internal static partial class ValueConverter
{
    private static readonly NumberFormatInfo InvariantNumbers = CultureInfo.InvariantCulture.NumberFormat;

    /// <summary>
    /// Answers the conversion when one of the fast paths covers the pair.
    /// </summary>
    /// <returns>
    /// <see langword="false" /> when no fast path covers it, and the caller has to ask the converter.
    /// A covered pair whose value does not convert throws what the converter would have thrown.
    /// </returns>
    private static bool TryConvertWithoutTypeConverter(Type requiredType, object value, out object? converted)
    {
        if (value is string text)
        {
            return TryConvertString(requiredType, text, out converted);
        }

        if (value is Enum)
        {
            return TryConvertEnum(requiredType, value, out converted);
        }

        Type valueType = value.GetType();
        if (IsNumber(valueType))
        {
            return TryConvertNumber(requiredType, value, out converted);
        }

        if (requiredType == typeof(string))
        {
            // The base TypeConverter's ToString, under the invariant culture; CharConverter adds that NUL is
            // the empty string, and UriTypeConverter that a Uri is the string it was made from.
            if (value is char c)
            {
                converted = c == '\0' ? string.Empty : c.ToString();
                return true;
            }

            if (value is bool or Guid or TimeSpan)
            {
                converted = value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString();
                return true;
            }

            if (valueType == typeof(Uri))
            {
                converted = ((Uri) value).OriginalString;
                return true;
            }
        }

        converted = null;
        return false;
    }

    /// <summary>
    /// A string onto a type whose intrinsic converter reads strings, or onto its
    /// <see cref="Nullable{T}" />, which <c>NullableConverter</c> reads as <see langword="null" /> when
    /// the string is empty and hands to the underlying converter otherwise.
    /// </summary>
    private static bool TryConvertString(Type requiredType, string text, out object? converted)
    {
        Type target = Nullable.GetUnderlyingType(requiredType) ?? requiredType;
        if (!HasBuiltInConversion(target))
        {
            converted = null;
            return false;
        }

        if (text.Length == 0 && target != requiredType)
        {
            converted = null;
            return true;
        }

        if (target.IsEnum)
        {
            converted = ParseEnum(target, text);
            return true;
        }

        if (target == typeof(Guid))
        {
            // GuidConverter trims nothing, and lets the constructor's FormatException out as it is.
            converted = new Guid(text);
            return true;
        }

        if (target == typeof(TimeSpan))
        {
            converted = ParseTrimmed<TimeSpan>(text);
            return true;
        }

        if (target == typeof(DateTimeOffset))
        {
            converted = ParseDate(text, DateTimeOffset.MinValue);
            return true;
        }

        if (target == typeof(Uri))
        {
            converted = text.Length == 0 ? null : new Uri(text, UriKind.RelativeOrAbsolute);
            return true;
        }

        converted = Type.GetTypeCode(target) switch
        {
            TypeCode.Boolean => (object) ParseTrimmed<bool>(text),
            TypeCode.Char => (object) ParseChar(text),
            TypeCode.Single or TypeCode.Double or TypeCode.Decimal => ParseFloatingPoint(text, target),
            TypeCode.DateTime => (object) ParseDate(text, DateTime.MinValue),
            _ => ParseInteger(text, target),
        };
        return true;
    }

    /// <summary>
    /// A number onto a primitive (<c>BaseNumberConverter</c> calls <see cref="Convert.ChangeType(object, Type, IFormatProvider)" />),
    /// onto a string (its own format: <c>R</c> for binary floating point, <c>G</c> otherwise), or onto an
    /// enum (<see cref="Enum.ToObject(Type, object)" />, for an integral number only).
    /// </summary>
    private static bool TryConvertNumber(Type requiredType, object number, out object? converted)
    {
        if (requiredType == typeof(string))
        {
            string format = number is float or double ? "R" : "G";
            converted = ((IFormattable) number).ToString(format, InvariantNumbers);
            return true;
        }

        if (requiredType.IsPrimitive)
        {
            converted = Convert.ChangeType(number, requiredType, CultureInfo.InvariantCulture);
            return true;
        }

        if (requiredType.IsEnum && IsIntegral(number.GetType()) && !DeclaresConverter(requiredType))
        {
            converted = Enum.ToObject(requiredType, number);
            return true;
        }

        converted = null;
        return false;
    }

    /// <summary>
    /// An enum onto a string (<c>EnumConverter</c>: its name, refusing an undefined value unless the enum
    /// is <see cref="FlagsAttribute" />), or onto a built-in target through its underlying number, which
    /// is where <see cref="ConvertUsingTypeConverter" /> sends an enum no converter takes.
    /// </summary>
    private static bool TryConvertEnum(Type requiredType, object member, out object? converted)
    {
        Type enumType = member.GetType();
        if (DeclaresConverter(enumType))
        {
            converted = null;
            return false;
        }

        if (requiredType == typeof(string))
        {
            if (!enumType.IsDefined(typeof(FlagsAttribute), false) && !Enum.IsDefined(enumType, member))
            {
                // EnumConverter's exception, which names no parameter.
                Throw.ArgumentException($"The value '{member}' is not a valid value for the enum '{enumType.Name}'.");
            }

            converted = Enum.Format(enumType, member, "G");
            return true;
        }

        if (HasBuiltInConversion(Nullable.GetUnderlyingType(requiredType) ?? requiredType))
        {
            converted = ConvertValueIfNecessary(requiredType, Convert.ChangeType(member, Convert.GetTypeCode(member), provider: null));
            return true;
        }

        converted = null;
        return false;
    }

    /// <summary>
    /// Whether the type is one whose intrinsic converter this file reproduces: every primitive but the
    /// two pointer-sized integers, <see cref="decimal" />, <see cref="DateTime" />,
    /// <see cref="DateTimeOffset" />, <see cref="TimeSpan" />, <see cref="Guid" />, <see cref="Uri" />
    /// itself, and an enum that declares no converter of its own.
    /// </summary>
    private static bool HasBuiltInConversion(Type type)
    {
        if (type.IsEnum)
        {
            return !DeclaresConverter(type);
        }

        if (type == typeof(Guid) || type == typeof(TimeSpan) || type == typeof(DateTimeOffset) || type == typeof(Uri))
        {
            return true;
        }

        return Type.GetTypeCode(type) is >= TypeCode.Boolean and <= TypeCode.DateTime;
    }

    /// <summary>
    /// Whether the type names a converter of its own, which <see cref="TypeDescriptor" /> would pick
    /// over the intrinsic one this file reproduces.
    /// </summary>
    private static bool DeclaresConverter(Type type) => type.IsDefined(typeof(TypeConverterAttribute), inherit: true);

    private static bool IsNumber(Type type) => !type.IsEnum && Type.GetTypeCode(type) is >= TypeCode.SByte and <= TypeCode.Decimal;

    private static bool IsIntegral(Type type) => Type.GetTypeCode(type) is >= TypeCode.SByte and <= TypeCode.UInt64;

    /// <summary>
    /// <c>BaseNumberConverter.ConvertFrom</c> for an integral type: the trimmed string, hexadecimal after
    /// <c>#</c>, <c>0x</c> or <c>&amp;h</c>, and every failure — the empty string's
    /// <see cref="IndexOutOfRangeException" /> among them — wrapped in an <see cref="ArgumentException" />.
    /// </summary>
    private static object ParseInteger(string value, Type type)
    {
        string text = value.Trim();
        try
        {
            if (text[0] == '#')
            {
                return FromHexadecimal(text.Substring(1), type);
            }

            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || text.StartsWith("&h", StringComparison.OrdinalIgnoreCase))
            {
                return FromHexadecimal(text.Substring(2), type);
            }

            return Type.GetTypeCode(type) switch
            {
                TypeCode.SByte => (object) sbyte.Parse(text, NumberStyles.Integer, InvariantNumbers),
                TypeCode.Byte => (object) byte.Parse(text, NumberStyles.Integer, InvariantNumbers),
                TypeCode.Int16 => (object) short.Parse(text, NumberStyles.Integer, InvariantNumbers),
                TypeCode.UInt16 => (object) ushort.Parse(text, NumberStyles.Integer, InvariantNumbers),
                TypeCode.Int32 => (object) int.Parse(text, NumberStyles.Integer, InvariantNumbers),
                TypeCode.UInt32 => (object) uint.Parse(text, NumberStyles.Integer, InvariantNumbers),
                TypeCode.Int64 => (object) long.Parse(text, NumberStyles.Integer, InvariantNumbers),
                _ => (object) ulong.Parse(text, NumberStyles.Integer, InvariantNumbers),
            };
        }
        catch (Exception e)
        {
            throw new ArgumentException(NotAValidValue(text, type), nameof(value), e);
        }
    }

    private static object FromHexadecimal(string digits, Type type) => Type.GetTypeCode(type) switch
    {
        TypeCode.SByte => (object) Convert.ToSByte(digits, 16),
        TypeCode.Byte => (object) Convert.ToByte(digits, 16),
        TypeCode.Int16 => (object) Convert.ToInt16(digits, 16),
        TypeCode.UInt16 => (object) Convert.ToUInt16(digits, 16),
        TypeCode.Int32 => (object) Convert.ToInt32(digits, 16),
        TypeCode.UInt32 => (object) Convert.ToUInt32(digits, 16),
        TypeCode.Int64 => (object) Convert.ToInt64(digits, 16),
        _ => (object) Convert.ToUInt64(digits, 16),
    };

    /// <summary>
    /// <c>BaseNumberConverter.ConvertFrom</c> for <see cref="float" />, <see cref="double" /> and
    /// <see cref="decimal" />, whose converters read no hexadecimal: the trimmed string under
    /// <see cref="NumberStyles.Float" />, every failure wrapped in an <see cref="ArgumentException" />.
    /// </summary>
    private static object ParseFloatingPoint(string value, Type type)
    {
        string text = value.Trim();
        try
        {
            return Type.GetTypeCode(type) switch
            {
                TypeCode.Single => (object) float.Parse(text, NumberStyles.Float, InvariantNumbers),
                TypeCode.Double => (object) double.Parse(text, NumberStyles.Float, InvariantNumbers),
                _ => (object) decimal.Parse(text, NumberStyles.Float, InvariantNumbers),
            };
        }
        catch (Exception e)
        {
            throw new ArgumentException(NotAValidValue(text, type), nameof(value), e);
        }
    }

    /// <summary>
    /// <c>CharConverter.ConvertFrom</c>: a single character is itself, whitespace included; a longer
    /// string is trimmed and must then be one character; the empty string is NUL.
    /// </summary>
    private static char ParseChar(string value)
    {
        string text = value.Length > 1 ? value.Trim() : value;
        if (text.Length == 0)
        {
            return '\0';
        }

        if (text.Length != 1)
        {
            throw new FormatException(NotAValidValue(text, typeof(char)));
        }

        return text[0];
    }

    /// <summary>
    /// <c>EnumConverter.ConvertFrom</c>: case-insensitive whatever the enum, and a comma-separated list
    /// read member by member and combined, every failure a <see cref="FormatException" />.
    /// </summary>
    private static object ParseEnum(Type enumType, string value)
    {
        try
        {
            if (!value.Contains(','))
            {
                return Enum.Parse(enumType, value, ignoreCase: true);
            }

            bool isUnderlyingTypeUInt64 = Enum.GetUnderlyingType(enumType) == typeof(ulong);
            long combined = 0;
            foreach (Range member in value.AsSpan().Split(','))
            {
                object parsed = Enum.Parse(enumType, value.AsSpan(member), ignoreCase: true);
                combined |= isUnderlyingTypeUInt64
                    ? unchecked((long) Convert.ToUInt64(parsed, CultureInfo.InvariantCulture))
                    : Convert.ToInt64(parsed, CultureInfo.InvariantCulture);
            }

            return Enum.ToObject(enumType, combined);
        }
        catch (Exception e)
        {
            throw new FormatException(NotAValidValue(value, enumType), e);
        }
    }

    /// <summary>
    /// <c>BooleanConverter.ConvertFrom</c> and <c>TimeSpanConverter.ConvertFrom</c>: the trimmed string
    /// under the invariant culture, and only a <see cref="FormatException" /> rewrapped, naming the string
    /// as given — so a duration that overflows comes out as the <see cref="OverflowException" /> it is.
    /// </summary>
    private static T ParseTrimmed<T>(string value) where T : IParsable<T>
    {
        try
        {
            return T.Parse(value.Trim(), CultureInfo.InvariantCulture);
        }
        catch (FormatException e)
        {
            throw new FormatException(NotAValidValue(value, typeof(T)), e);
        }
    }

    /// <summary>
    /// <c>DateTimeConverter.ConvertFrom</c> and <c>DateTimeOffsetConverter.ConvertFrom</c>: the trimmed
    /// string under the invariant culture's date format, a blank one meaning the type's minimum.
    /// </summary>
    private static T ParseDate<T>(string value, T blank) where T : IParsable<T>
    {
        string text = value.Trim();
        if (text.Length == 0)
        {
            return blank;
        }

        try
        {
            return T.Parse(text, CultureInfo.InvariantCulture.DateTimeFormat);
        }
        catch (FormatException e)
        {
            throw new FormatException(NotAValidValue(value, typeof(T)), e);
        }
    }

    /// <summary>
    /// The converters' own message for a string they cannot read.
    /// </summary>
    private static string NotAValidValue(string text, Type type) => $"{text} is not a valid value for {type.Name}.";
}
