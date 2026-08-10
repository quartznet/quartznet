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
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly record struct MonthDay : IComparable<MonthDay>
{
    // Validation and ordering use a leap year so February 29th is representable; the value itself
    // carries no year.
    private const int LeapYear = 2000;

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

    /// <inheritdoc />
    public override string ToString()
    {
        // ISO 8601 spells a recurring month-day "--MM-DD"
        return $"--{Month:00}-{Day:00}";
    }
}
