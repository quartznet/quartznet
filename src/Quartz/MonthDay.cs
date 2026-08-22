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
using System.Runtime.InteropServices;

namespace Quartz;

/// <summary>
/// A day of the year with no year attached — "December 25th", every year.
/// </summary>
/// <remarks>
/// <para>
/// This is the value <see cref="Quartz.Impl.Calendar.AnnualCalendar" /> excludes: the same date
/// every year. A <see cref="DateOnly" /> always carries a year, so a set of them either lies about
/// the year or fails its own membership test; this type says exactly what is stored.
/// </para>
/// <para>
/// February 29th is a valid value — in years without one, no day matches it.
/// </para>
/// <para>
/// Its text form is the ISO 8601 spelling of a recurring month-day, <c>--MM-DD</c>: always seven
/// characters, and the only form <see cref="Parse(string, IFormatProvider?)" /> reads. There is
/// nothing to vary, so the format string and the format provider every formatting and parsing member
/// takes are ignored — they are there because the BCL interfaces ask for them.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly record struct MonthDay :
    IComparable<MonthDay>,
    IParsable<MonthDay>,
    ISpanParsable<MonthDay>,
    ISpanFormattable,
    IUtf8SpanFormattable
{
    // Validation and ordering use a leap year so February 29th is representable; the value itself
    // carries no year.
    private const int LeapYear = 2000;

    /// <summary>
    /// The length of the <c>--MM-DD</c> form, in characters and — being ASCII — in UTF-8 bytes.
    /// </summary>
    private const int FormatLength = 7;

    /// <summary>
    /// Initializes a new <see cref="MonthDay" />.
    /// </summary>
    /// <param name="month">The month (1-12).</param>
    /// <param name="day">The day of the month (1 through the month's length; February counts 29).</param>
    /// <exception cref="ArgumentOutOfRangeException">The pair is not a valid day of any year.</exception>
    public MonthDay(int month, int day)
    {
        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be between 1 and 12.");
        }

        if (day < 1 || day > DateTime.DaysInMonth(LeapYear, month))
        {
            throw new ArgumentOutOfRangeException(nameof(day), day, $"Day must be between 1 and {DateTime.DaysInMonth(LeapYear, month)} for month {month}.");
        }

        Month = month;
        Day = day;
    }

    /// <summary>
    /// The month (1-12).
    /// </summary>
    public int Month { get; }

    /// <summary>
    /// The day of the month (1-31).
    /// </summary>
    public int Day { get; }

    /// <summary>
    /// The month and day of the given date, with its year dropped.
    /// </summary>
    public static MonthDay From(DateOnly date)
    {
        return new MonthDay(date.Month, date.Day);
    }

    /// <summary>
    /// The value as a <see cref="DateOnly" /> pinned to a fixed leap year, which is the date-shaped
    /// form serialized payloads carry.
    /// </summary>
    internal DateOnly ToDateOnly()
    {
        return new DateOnly(LeapYear, Month, Day);
    }

    /// <inheritdoc />
    public int CompareTo(MonthDay other)
    {
        int byMonth = Month.CompareTo(other.Month);
        return byMonth != 0 ? byMonth : Day.CompareTo(other.Day);
    }

    /// <summary>Compares two values in calendar order.</summary>
    public static bool operator <(MonthDay left, MonthDay right) => left.CompareTo(right) < 0;

    /// <summary>Compares two values in calendar order.</summary>
    public static bool operator <=(MonthDay left, MonthDay right) => left.CompareTo(right) <= 0;

    /// <summary>Compares two values in calendar order.</summary>
    public static bool operator >(MonthDay left, MonthDay right) => left.CompareTo(right) > 0;

    /// <summary>Compares two values in calendar order.</summary>
    public static bool operator >=(MonthDay left, MonthDay right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// The value in the <c>--MM-DD</c> form.
    /// </summary>
    public override string ToString()
    {
        return string.Create(FormatLength, this, static (destination, value) => value.Write(destination));
    }

    /// <summary>
    /// The value in the <c>--MM-DD</c> form. <paramref name="format" /> and
    /// <paramref name="formatProvider" /> are ignored: the form is fixed.
    /// </summary>
    public string ToString(string? format, IFormatProvider? formatProvider) => ToString();

    /// <summary>
    /// Writes the <c>--MM-DD</c> form. <paramref name="format" /> and <paramref name="provider" /> are
    /// ignored: the form is fixed, and always seven characters.
    /// </summary>
    public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        if (destination.Length < FormatLength)
        {
            charsWritten = 0;
            return false;
        }

        Write(destination);
        charsWritten = FormatLength;
        return true;
    }

    /// <summary>
    /// Writes the <c>--MM-DD</c> form as UTF-8. <paramref name="format" /> and
    /// <paramref name="provider" /> are ignored: the form is fixed, ASCII, and always seven bytes.
    /// </summary>
    public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
    {
        if (utf8Destination.Length < FormatLength)
        {
            bytesWritten = 0;
            return false;
        }

        utf8Destination[0] = (byte) '-';
        utf8Destination[1] = (byte) '-';
        utf8Destination[2] = (byte) ('0' + Month / 10);
        utf8Destination[3] = (byte) ('0' + Month % 10);
        utf8Destination[4] = (byte) '-';
        utf8Destination[5] = (byte) ('0' + Day / 10);
        utf8Destination[6] = (byte) ('0' + Day % 10);
        bytesWritten = FormatLength;
        return true;
    }

    /// <summary>
    /// Reads the <c>--MM-DD</c> form. <paramref name="provider" /> is ignored: the form is fixed.
    /// </summary>
    /// <exception cref="FormatException"><paramref name="s" /> is not a valid month and day.</exception>
    public static MonthDay Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        if (!TryParse(s, provider, out MonthDay result))
        {
            Throw.FormatException($"'{s.ToString()}' is not a month and day in the form --MM-DD.");
        }

        return result;
    }

    /// <summary>
    /// Reads the <c>--MM-DD</c> form.
    /// </summary>
    /// <exception cref="FormatException"><paramref name="s" /> is not a valid month and day.</exception>
    public static MonthDay Parse(ReadOnlySpan<char> s) => Parse(s, provider: null);

    /// <summary>
    /// Reads the <c>--MM-DD</c> form. <paramref name="provider" /> is ignored: the form is fixed.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="s" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException"><paramref name="s" /> is not a valid month and day.</exception>
    public static MonthDay Parse(string s, IFormatProvider? provider)
    {
        if (s is null)
        {
            Throw.ArgumentNullException(nameof(s));
        }

        return Parse(s.AsSpan(), provider);
    }

    /// <summary>
    /// Reads the <c>--MM-DD</c> form.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="s" /> is <see langword="null" />.</exception>
    /// <exception cref="FormatException"><paramref name="s" /> is not a valid month and day.</exception>
    public static MonthDay Parse(string s) => Parse(s, provider: null);

    /// <summary>
    /// Reads the <c>--MM-DD</c> form, without throwing. <paramref name="provider" /> is ignored: the
    /// form is fixed.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out MonthDay result)
    {
        result = default;

        if (s.Length != FormatLength || s[0] != '-' || s[1] != '-' || s[4] != '-')
        {
            return false;
        }

        if (!TryReadTwoDigits(s[2], s[3], out int month) || !TryReadTwoDigits(s[5], s[6], out int day))
        {
            return false;
        }

        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(LeapYear, month))
        {
            return false;
        }

        result = new MonthDay(month, day);
        return true;
    }

    /// <summary>
    /// Reads the <c>--MM-DD</c> form, without throwing. <paramref name="provider" /> is ignored: the
    /// form is fixed.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out MonthDay result)
    {
        if (s is null)
        {
            result = default;
            return false;
        }

        return TryParse(s.AsSpan(), provider, out result);
    }

    /// <summary>
    /// Reads the <c>--MM-DD</c> form, without throwing.
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? s, out MonthDay result) => TryParse(s, provider: null, out result);

    /// <summary>
    /// Reads the <c>--MM-DD</c> form, without throwing.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> s, out MonthDay result) => TryParse(s, provider: null, out result);

    private static bool TryReadTwoDigits(char tens, char units, out int value)
    {
        if (!char.IsAsciiDigit(tens) || !char.IsAsciiDigit(units))
        {
            value = 0;
            return false;
        }

        value = (tens - '0') * 10 + (units - '0');
        return true;
    }

    private void Write(Span<char> destination)
    {
        destination[0] = '-';
        destination[1] = '-';
        destination[2] = (char) ('0' + Month / 10);
        destination[3] = (char) ('0' + Month % 10);
        destination[4] = '-';
        destination[5] = (char) ('0' + Day / 10);
        destination[6] = (char) ('0' + Day % 10);
    }
}
