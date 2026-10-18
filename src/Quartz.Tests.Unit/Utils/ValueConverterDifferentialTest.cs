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

#nullable enable

using System.ComponentModel;
using System.Globalization;

using Quartz.Util;

namespace Quartz.Tests.Unit.Utils;

/// <summary>
/// Holds <see cref="ValueConverter" />'s fast paths to what <see cref="TypeDescriptor" /> gave before
/// they existed, over a corpus of every target type they cover and the inputs whose handling differs
/// between converters.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Legacy" /> is the routine as it stood before the fast paths, copied here so the comparison
/// does not depend on the code under test. Every target is crossed with every value, under four current
/// cultures — the converters parse invariantly, and a fast path that consulted the current culture would
/// show up as a mismatch under <c>fi-FI</c>, whose decimal separator is a comma, or <c>tr-TR</c>, whose
/// upper-case <c>i</c> is dotted. An outcome is the value with its runtime type, or the exception's type
/// and message.
/// </para>
/// <para>
/// With the switch on the two must agree everywhere, because a pair no fast path covers still reaches
/// <see cref="TypeConverter" />. With it off they must agree wherever a fast path covers the pair, and
/// the pairs that are not covered are listed in <see cref="IsDeclined" /> with the reason; a pair the old
/// routine converted that is neither covered nor listed fails the test, so a fast path cannot quietly
/// stop answering.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class ValueConverterDifferentialTest
{
    private static readonly string[] Cultures = ["", "en-US", "fi-FI", "tr-TR"];

    private static readonly Type[] ScalarTargets =
    [
        typeof(bool), typeof(char), typeof(sbyte), typeof(byte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
        typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal), typeof(IntPtr),
        typeof(DateTime), typeof(DateTimeOffset), typeof(TimeSpan), typeof(Guid),
        typeof(DateOnly), typeof(TimeOnly), typeof(Half), typeof(Int128), typeof(UInt128),
        typeof(DayOfWeek), typeof(ByteEnum), typeof(Permissions), typeof(WideFlags), typeof(LongEnum), typeof(TurkishEnum), typeof(ConvertedEnum),
    ];

    private static readonly Type[] ReferenceTargets = [typeof(string), typeof(Uri), typeof(Version), typeof(CultureInfo)];

    private static readonly string[] Strings =
    [
        // Whitespace, signs and separators, which the number converters trim, allow or refuse.
        "", " ", "   ", "\t", "0", "1", "-1", "+1", "42", " 42 ", "\t42\n", "4 2", "042", "1,000", "1.5", "1,5", "-0",
        "1e3", "1E+3", "1.5e-3", "0.1", ".5", "5.", "$5", "(5)", "5-",
        // Every integral boundary, either side.
        "127", "128", "-128", "-129", "255", "256", "32767", "32768", "-32768", "-32769", "65535", "65536",
        "2147483647", "2147483648", "-2147483648", "-2147483649", "4294967295", "4294967296",
        "9223372036854775807", "9223372036854775808", "-9223372036854775808", "18446744073709551615", "18446744073709551616",
        "79228162514264337593543950335", "1e400", "-1e400", "3.4028235E+38", "3.5E+38",
        // Int128's and UInt128's boundaries, either side, and Half's: its largest finite value, what rounds to
        // it, what overflows to infinity, its smallest subnormal, and the round-trip forms its ToString writes.
        "170141183460469231731687303715884105727", "170141183460469231731687303715884105728",
        "-170141183460469231731687303715884105728", "-170141183460469231731687303715884105729",
        "340282366920938463463374607431768211455", "340282366920938463463374607431768211456",
        "65504", "65519", "65520", "-65504", "6E-08", "5.9604645E-08", "0.000061035156", "0.3333", "1,000.5",
        "0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", "0x7FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", "0x1FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF", "# 1F",
        // Hexadecimal, which only the integral converters read, and the prefixes' corner cases.
        "0x1F", "0X1f", "0x", "0xFF", "0xFFFF", "0xFFFFFFFF", "0x7FFFFFFF", "0x80000000", "0xFFFFFFFFFFFFFFFF", "0x0x1F", "0xG",
        " 0x1F ", "#1F", "#", "#-1", "#FFFFFFFF", "&h1F", "&H1f", "&h", "&hFF",
        // Non-finite and non-ASCII digits.
        "NaN", "nan", "Infinity", "-Infinity", "\u221E", "-\u221E", "\uFF11\uFF12", "\u00A042", "\u0664\u0662",
        // Booleans and characters.
        "true", "True", "TRUE", " false ", "False", "yes", "no", "t", "1 ", "a", "A", "ab", " a ", "  x  ", "\0",
        // Enum members, by name, case and number, alone and combined.
        "Wednesday", "wednesday", "WEDNESDAY", " Wednesday ", "3", "7", "99", "Monday, Friday", "Monday,Friday", "Monday,",
        ",Monday", "Monday, 7", "Funday", "Sunday|Monday",
        "None", "Read", "read, WRITE", "Read, Write", "ReadWrite", "Read, 4", "64", "Read,,Write", "Execute, ReadWrite",
        "Low, High", "High", "Max", "Min", "Zero", "One", "Item", "ITEM", "item", "\u0130TEM", "\u0131tem", "Info",
        "Alpha", "alpha", "BETA",
        // Guids in every format Guid reads, and some it does not.
        "d3b07384-d9a0-4c9f-8d6b-1b2b3c4d5e6f", "D3B07384-D9A0-4C9F-8D6B-1B2B3C4D5E6F", "{d3b07384-d9a0-4c9f-8d6b-1b2b3c4d5e6f}",
        "(d3b07384-d9a0-4c9f-8d6b-1b2b3c4d5e6f)", "d3b07384d9a04c9f8d6b1b2b3c4d5e6f", " d3b07384-d9a0-4c9f-8d6b-1b2b3c4d5e6f ",
        "{0xd3b07384,0xd9a0,0x4c9f,{0x8d,0x6b,0x1b,0x2b,0x3c,0x4d,0x5e,0x6f}}", "d3b07384-d9a0-4c9f-8d6b", "00000000-0000-0000-0000-000000000000",
        // Durations, including one that overflows, which TimeSpanConverter does not rewrap.
        "00:00:01", "1.02:03:04.5", "  00:05  ", "-00:01", "1:2:3", "24:00", "23:59:59.9999999", "10675199.02:48:05.4775807",
        "10675199.02:48:05.4775808", "1:60", "P1D", "00:00:01,5",
        // Dates in the invariant culture's formats and in others.
        "2026-10-16", "2026-10-16T12:34:56", "2026-10-16T12:34:56Z", "2026-10-16T12:34:56.1234567+03:00", "2026-10-16 12:34",
        "10/16/2026", "16/10/2026", "16.10.2026", "Oct 16, 2026", "16 October 2026", "Fri, 16 Oct 2026 12:00:00 GMT", "0001-01-01",
        "9999-12-31T23:59:59.9999999", "9999-12-31T23:59:59.9999999-01:00", "2026-02-30", "12:00", "2026-10-16T25:00",
        // DateOnly's and TimeOnly's own round trips (ToString() and "O" under the invariant culture), and
        // what their parsers refuse: a date with a time of day, a time with a date, an offset.
        "Friday, 16 October 2026", "00:00", "12:34", "12:34:56", "12:34:56.1234567", "23:59:59.9999999",
        "12:34 PM", "1:02 AM", "00:00:00.0000000", "2026-10-16T00:00:00", "12:34:56Z", "12:34:56+03:00", "24:00:00",
        // Absolute, relative and malformed addresses.
        "https://example.com/a?b=c#d", "https://example.com/a b", "relative/path", "/rooted", "http://", "::", @"\\server\share",
        "file:///c:/x", "https://\u4F8B\u3048.jp", "mailto:a@b.c",
        // What only a converter the fast paths do not reproduce reads.
        "System.Int32", "1.2.3", "1.2.3.4", "en-US",
    ];

    private static readonly object[] NonStrings =
    [
        (sbyte) -5, (byte) 200, (short) -300, (ushort) 60000, 0, 1, 3, 42, -1, 256, int.MaxValue, int.MinValue,
        7u, uint.MaxValue, 5L, long.MaxValue, long.MinValue, 9UL, ulong.MaxValue,
        1.5f, -2.5f, float.NaN, float.PositiveInfinity, 3.0, 1234.5, -0.5, 2.5, double.NaN, double.NegativeInfinity, 1e20,
        1.5m, 2.50m, decimal.MaxValue,
        true, false, 'A', '\0', ' ', '7',
        new Guid("d3b07384-d9a0-4c9f-8d6b-1b2b3c4d5e6f"), Guid.Empty, TimeSpan.FromMinutes(90), TimeSpan.MinValue,
        new DateTime(2026, 10, 16), new DateTime(2026, 10, 16, 12, 34, 56, DateTimeKind.Utc), DateTime.MinValue,
        new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.FromHours(3)),
        new DateOnly(2026, 10, 16), DateOnly.MinValue, new TimeOnly(12, 34, 56), (Half) 1.5, Half.PositiveInfinity,
        (Int128) 42, Int128.MaxValue, (UInt128) 42, UInt128.MaxValue,
        new Uri("https://example.com/a?b=c"), new Uri("relative/path", UriKind.Relative),
        DayOfWeek.Wednesday, (DayOfWeek) 99, Permissions.Read | Permissions.Write, (Permissions) 64, WideFlags.High | WideFlags.Low,
        ByteEnum.Max, LongEnum.Min, TurkishEnum.Info, ConvertedEnum.Beta,
        new Version(1, 2, 3), new Unconvertible(),
    ];

    private static IEnumerable<Type> Targets()
    {
        foreach (Type scalar in ScalarTargets)
        {
            yield return scalar;
            yield return typeof(Nullable<>).MakeGenericType(scalar);
        }

        foreach (Type reference in ReferenceTargets)
        {
            yield return reference;
        }
    }

    private static IEnumerable<object> Values() => Strings.Cast<object>().Concat(NonStrings);

    [Test]
    public void WithTheSwitchOnEveryConversionIsWhatTypeDescriptorGave()
    {
        List<string> mismatches = [];
        int compared = 0;

        foreach (string culture in Cultures)
        {
            using CultureScope scope = new(culture);
            foreach (Type target in Targets())
            {
                foreach (object value in Values())
                {
                    string expected = Outcome(() => Legacy(target, value));
                    string actual = Outcome(() => ValueConverter.ConvertValueIfNecessary(target, value));
                    compared++;

                    if (!string.Equals(expected, actual, StringComparison.Ordinal))
                    {
                        mismatches.Add($"[{culture}] {Describe(value)} -> {target}: TypeDescriptor gave {expected}, ValueConverter gave {actual}");
                    }
                }
            }
        }

        TestContext.Out.WriteLine($"{Targets().Count()} targets x {Values().Count()} values x {Cultures.Length} cultures = {compared} conversions compared");
        compared.Should().BeGreaterThan(40_000, "the corpus is what makes this a differential test rather than a handful of examples");
        mismatches.Should().BeEmpty("with the switch on, a fast path answers exactly as the TypeConverter it stands in front of, and anything it declines still reaches that TypeConverter{0}", Summary(mismatches));
    }

    [Test]
    public void WithTheSwitchOffTheFastPathsAnswerAsTypeDescriptorDidOrRefuse()
    {
        List<string> mismatches = [];
        Dictionary<Type, int> answered = [];

        using FeatureSwitchScope off = new(FeatureSwitches.TypeConverterCoercionSwitch, enabled: false);
        foreach (string culture in Cultures)
        {
            using CultureScope scope = new(culture);
            foreach (Type target in Targets())
            {
                foreach (object value in Values())
                {
                    string expected = Outcome(() => Legacy(target, value));
                    string actual = Outcome(() => ValueConverter.ConvertValueIfNecessary(target, value));

                    if (string.Equals(expected, actual, StringComparison.Ordinal))
                    {
                        if (!expected.StartsWith("!", StringComparison.Ordinal))
                        {
                            answered[target] = answered.GetValueOrDefault(target) + 1;
                        }

                        continue;
                    }

                    bool refused = actual.StartsWith($"!{typeof(CoercionNotSupportedException).FullName}:", StringComparison.Ordinal)
                                   && actual.Contains(FeatureSwitches.TypeConverterCoercionSwitch, StringComparison.Ordinal);

                    // A refusal stands in for TypeDescriptor's own refusal, or for a pair the fast paths
                    // do not cover. Anything else is a fast path answering differently.
                    if (refused && (expected.StartsWith("!System.NotSupportedException:", StringComparison.Ordinal) || IsDeclined(target, value)))
                    {
                        continue;
                    }

                    mismatches.Add($"[{culture}] {Describe(value)} -> {target}: TypeDescriptor gave {expected}, ValueConverter gave {actual}");
                }
            }
        }

        TestContext.Out.WriteLine($"{answered.Values.Sum()} conversions succeeded with no TypeConverter, identically to TypeDescriptor");
        mismatches.Should().BeEmpty("with the switch off, a covered pair is answered exactly as before and an uncovered one is refused, never answered differently{0}", Summary(mismatches));

        foreach (Type target in Targets())
        {
            Type underlying = Nullable.GetUnderlyingType(target) ?? target;
            if (underlying == typeof(Version) || underlying == typeof(CultureInfo) || underlying == typeof(IntPtr) || underlying == typeof(ConvertedEnum))
            {
                continue;
            }

            answered.GetValueOrDefault(target).Should().BeGreaterThan(0, $"{target} is a fast-path target, so some value in the corpus must convert to it with no TypeConverter at all");
        }
    }

    /// <summary>
    /// The pairs TypeDescriptor converted and the fast paths leave to it, each for a stated reason.
    /// </summary>
    private static bool IsDeclined(Type target, object value)
    {
        Type underlying = Nullable.GetUnderlyingType(target) ?? target;

        // No fast path reads these at all: Version and CultureInfo have converters of their own, and
        // IntPtr has none, so a string never reached it anyway.
        if (underlying == typeof(Version) || underlying == typeof(CultureInfo) || underlying == typeof(IntPtr))
        {
            return true;
        }

        // An enum that declares its own converter is that converter's to answer, in both directions.
        if (underlying == typeof(ConvertedEnum) || value is ConvertedEnum)
        {
            return true;
        }

        // DateTimeConverter and DateTimeOffsetConverter format a date their own way, which nothing in
        // Quartz asks for.
        if (target == typeof(string) && value is DateTime or DateTimeOffset)
        {
            return true;
        }

        // The converters of DateOnly, TimeOnly, Half, Int128 and UInt128 read strings, and the fast paths
        // reproduce only that direction: none of the five is IConvertible, so their way out to a number is
        // an InvalidCastException, and their way out to a string is formatting nothing in Quartz asks for.
        if (value is DateOnly or TimeOnly or Half or Int128 or UInt128)
        {
            return true;
        }

        // Enum.ToObject accepts a bool or a char as readily as a number, and nothing stores a flag as one.
        if (underlying.IsEnum && value is bool or char)
        {
            return true;
        }

        // Types with no fast path as a source.
        return value is Version or Unconvertible;
    }

    /// <summary>
    /// <c>ValueConverter.ConvertValueIfNecessary</c> as it was before the fast paths and the switch.
    /// </summary>
    private static object? Legacy(Type requiredType, object? newValue)
    {
        if (newValue is null)
        {
            return requiredType.IsValueType ? Activator.CreateInstance(requiredType) : null;
        }

        if (requiredType.IsInstanceOfType(newValue))
        {
            return newValue;
        }

        TypeConverter typeConverter = TypeDescriptor.GetConverter(requiredType);
        if (typeConverter.CanConvertFrom(newValue.GetType()))
        {
            return typeConverter.ConvertFrom(null, CultureInfo.InvariantCulture, newValue);
        }

        typeConverter = TypeDescriptor.GetConverter(newValue.GetType());
        if (typeConverter.CanConvertTo(requiredType))
        {
            return typeConverter.ConvertTo(null, CultureInfo.InvariantCulture, newValue, requiredType);
        }

        if (requiredType == typeof(Type))
        {
            return Type.GetType(newValue.ToString()!, throwOnError: true);
        }

        if (newValue.GetType().IsEnum)
        {
            return Legacy(requiredType, Convert.ChangeType(newValue, Convert.GetTypeCode(newValue), null));
        }

        if (requiredType.IsEnum)
        {
            try
            {
                return Enum.ToObject(requiredType, newValue);
            }
            catch
            {
                // As before: fall through to the refusal.
            }
        }

        throw new NotSupportedException($"{newValue} is not a supported value for a target of type {requiredType}");
    }

    /// <summary>
    /// How many pairs disagreed, and the first of them, since a fast path that goes wrong usually goes
    /// wrong for a whole family of inputs at once.
    /// </summary>
    private static string Summary(List<string> mismatches) =>
        mismatches.Count == 0
            ? string.Empty
            : $"; {mismatches.Count} pair(s) disagree, the first {Math.Min(mismatches.Count, 40)}:{Environment.NewLine}{string.Join(Environment.NewLine, mismatches.Take(40))}";

    private static string Outcome(Func<object?> conversion)
    {
        try
        {
            return Describe(conversion());
        }
        catch (Exception e)
        {
            return $"!{e.GetType().FullName}: {e.Message}";
        }
    }

    /// <summary>
    /// A value with its runtime type, written so that two values describe alike only when they are
    /// alike: a date keeps its kind and offset, a binary float its exact bits, a decimal its scale.
    /// </summary>
    private static string Describe(object? value) => value switch
    {
        null => "null",
        DateTime date => $"DateTime:{date.ToString("O", CultureInfo.InvariantCulture)}",
        DateTimeOffset date => $"DateTimeOffset:{date.ToString("O", CultureInfo.InvariantCulture)}",
        DateOnly date => $"DateOnly:{date.ToString("O", CultureInfo.InvariantCulture)}",
        TimeOnly time => $"TimeOnly:{time.ToString("O", CultureInfo.InvariantCulture)}",
        float number => $"Single:{number.ToString("R", CultureInfo.InvariantCulture)}",
        double number => $"Double:{number.ToString("R", CultureInfo.InvariantCulture)}",
        Uri uri => $"Uri:{uri.OriginalString}|{uri.IsAbsoluteUri}",
        string text => $"String:\"{text}\"",
        IFormattable formattable => $"{value.GetType().FullName}:{formattable.ToString(null, CultureInfo.InvariantCulture)}",
        _ => $"{value.GetType().FullName}:{value}",
    };

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo previous = CultureInfo.CurrentCulture;

        public CultureScope(string name)
        {
            CultureInfo.CurrentCulture = name.Length == 0 ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(name);
        }

        public void Dispose()
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private enum ByteEnum : byte
    {
        Zero = 0,
        One = 1,
        Max = 255,
    }

    [Flags]
    private enum Permissions
    {
        None = 0,
        Read = 1,
        Write = 2,
        Execute = 4,
        ReadWrite = Read | Write,
    }

    [Flags]
    private enum WideFlags : ulong
    {
        None = 0,
        Low = 1,
        High = 0x8000_0000_0000_0000,
    }

    private enum LongEnum : long
    {
        Min = long.MinValue,
        Zero = 0,
        Max = long.MaxValue,
    }

    private enum TurkishEnum
    {
        Item,
        Info,
    }

    [TypeConverter(typeof(UpperCaseOnlyConverter))]
    private enum ConvertedEnum
    {
        Alpha,
        Beta,
    }

    /// <summary>
    /// A converter of an enum's own, which reads only upper case and writes a marker, so an answer that
    /// went round it is visible.
    /// </summary>
    private sealed class UpperCaseOnlyConverter : EnumConverter
    {
        public UpperCaseOnlyConverter(Type type) : base(type)
        {
        }

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string text && !string.Equals(text, text.ToUpperInvariant(), StringComparison.Ordinal))
            {
                throw new FormatException($"'{text}' is not upper case.");
            }

            return base.ConvertFrom(context, culture, value);
        }

        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            return destinationType == typeof(string) ? $"converted:{value}" : base.ConvertTo(context, culture, value, destinationType);
        }
    }

    private sealed class Unconvertible
    {
        public override string ToString() => "unconvertible";
    }
}
