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

namespace Quartz.Impl.Triggers;

/// <summary>
/// The English the schedule describers share: lists, ordinals, units, clock times, days and months.
/// </summary>
/// <remarks>
/// Invariant throughout. The text is English whatever the culture of the process, so a number never
/// picks up a culture's digits or separators.
/// </remarks>
internal static class ScheduleWords
{
    /// <summary>
    /// Every day of the week, Monday first: the order a list of days is read in.
    /// </summary>
    private static readonly DayOfWeek[] Week =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday
    ];

    private static readonly string[] OrdinalWords = ["first", "second", "third", "fourth", "fifth"];

    /// <summary>
    /// <c>a</c>, <c>a and b</c>, <c>a, b and c</c>.
    /// </summary>
    public static string JoinList(IReadOnlyList<string> items)
    {
        return items.Count switch
        {
            0 => string.Empty,
            1 => items[0],
            _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1]
        };
    }

    public static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>1st</c>, <c>2nd</c>, <c>11th</c>, <c>23rd</c>.
    /// </summary>
    public static string Ordinal(int value)
    {
        int lastTwo = value % 100;
        string suffix = lastTwo is >= 11 and <= 13
            ? "th"
            : (value % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            };

        return Number(value) + suffix;
    }

    /// <summary>
    /// <c>first</c> to <c>fifth</c> in words, and <see cref="Ordinal" /> past that.
    /// </summary>
    public static string OrdinalWord(int value)
    {
        return value is >= 1 and <= 5 ? OrdinalWords[value - 1] : Ordinal(value);
    }

    /// <summary>
    /// An ordinal that may count from the end: <c>third</c>, <c>last</c>, <c>second-to-last</c>.
    /// </summary>
    public static string SignedOrdinalWord(int value)
    {
        return value switch
        {
            -1 => "last",
            < 0 => OrdinalWord(-value) + "-to-last",
            _ => OrdinalWord(value)
        };
    }

    /// <summary>
    /// <c>1 minute</c>, <c>15 minutes</c>.
    /// </summary>
    public static string Count(long count, string unit)
    {
        return Number(count) + " " + unit + (count == 1 ? string.Empty : "s");
    }

    /// <summary>
    /// <c>every minute</c>, <c>every 15 minutes</c>.
    /// </summary>
    public static string Every(long count, string unit)
    {
        return count == 1 ? "every " + unit : "every " + Count(count, unit);
    }

    /// <summary>
    /// <c>3 times</c>.
    /// </summary>
    public static string Times(long count)
    {
        return count == 1 ? "once" : Number(count) + " times";
    }

    /// <summary>
    /// A duration as a count of the largest unit that divides it exactly: <c>(90, "minute")</c> for an hour
    /// and a half, <c>(1500, "millisecond")</c> for a second and a half.
    /// </summary>
    public static (long Count, string Unit) Duration(TimeSpan duration)
    {
        long ticks = duration.Ticks;
        if (ticks % TimeSpan.TicksPerDay == 0)
        {
            return (ticks / TimeSpan.TicksPerDay, "day");
        }

        if (ticks % TimeSpan.TicksPerHour == 0)
        {
            return (ticks / TimeSpan.TicksPerHour, "hour");
        }

        if (ticks % TimeSpan.TicksPerMinute == 0)
        {
            return (ticks / TimeSpan.TicksPerMinute, "minute");
        }

        if (ticks % TimeSpan.TicksPerSecond == 0)
        {
            return (ticks / TimeSpan.TicksPerSecond, "second");
        }

        return (Math.Max(1, ticks / TimeSpan.TicksPerMillisecond), "millisecond");
    }

    /// <summary>
    /// The noun an <see cref="IntervalUnit" /> counts in: <c>minute</c>, <c>month</c>.
    /// </summary>
    public static string UnitName(IntervalUnit unit)
    {
        return unit switch
        {
            IntervalUnit.Millisecond => "millisecond",
            IntervalUnit.Second => "second",
            IntervalUnit.Minute => "minute",
            IntervalUnit.Hour => "hour",
            IntervalUnit.Day => "day",
            IntervalUnit.Week => "week",
            IntervalUnit.Month => "month",
            IntervalUnit.Year => "year",
            _ => unit.ToString().ToLowerInvariant()
        };
    }

    /// <summary>
    /// <c>07:00</c>, or <c>07:00:30</c> when <paramref name="withSeconds" /> is set.
    /// </summary>
    public static string Clock(int hour, int minute, int second, bool withSeconds)
    {
        string text = hour.ToString("00", CultureInfo.InvariantCulture) + ":" + minute.ToString("00", CultureInfo.InvariantCulture);
        return withSeconds ? text + ":" + second.ToString("00", CultureInfo.InvariantCulture) : text;
    }

    public static string Clock(TimeOnly time, bool withSeconds)
    {
        return Clock(time.Hour, time.Minute, time.Second, withSeconds);
    }

    public static string MonthName(int month)
    {
        return CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);
    }

    /// <summary>
    /// <c> (America/Chicago)</c>: the zone a schedule is read in, by its id. Nothing for a trigger of an
    /// application's own that answers no zone at all.
    /// </summary>
    public static string Zone(TimeZoneInfo? timeZone)
    {
        return timeZone is null ? string.Empty : " (" + timeZone.Id + ")";
    }

    public static string Capitalize(string text)
    {
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }

    /// <summary>
    /// Whether <paramref name="days" /> is Monday to Friday and nothing else.
    /// </summary>
    public static bool IsWorkWeek(IReadOnlyCollection<DayOfWeek> days)
    {
        HashSet<DayOfWeek> set = [.. days];
        return set.Count == 5 && !set.Contains(DayOfWeek.Saturday) && !set.Contains(DayOfWeek.Sunday);
    }

    /// <summary>
    /// Some days of the week in words, or <see langword="null" /> when they are all seven.
    /// </summary>
    /// <remarks>
    /// Three or more days in a row, counting Sunday to Monday as a row, read as a span: <c>Monday to Friday</c>,
    /// <c>Friday to Monday</c>. Anything else is a list, Monday first unless the days are two in a row:
    /// <c>Monday, Wednesday and Friday</c>, <c>Saturday and Sunday</c>, <c>Sunday and Monday</c>.
    /// </remarks>
    public static string? Days(IReadOnlyCollection<DayOfWeek>? days)
    {
        if (days is null)
        {
            return null;
        }

        bool[] on = new bool[Week.Length];
        foreach (DayOfWeek day in days)
        {
            int index = Array.IndexOf(Week, day);
            if (index >= 0)
            {
                on[index] = true;
            }
        }

        int count = on.Count(static x => x);
        if (count is 0 or 7)
        {
            return null;
        }

        // Where the one run starts, if the days are one run: the day on whose left the run is off.
        int runs = 0;
        int start = 0;
        for (int i = 0; i < Week.Length; i++)
        {
            if (on[i] && !on[(i + Week.Length - 1) % Week.Length])
            {
                runs++;
                start = i;
            }
        }

        if (runs == 1 && count >= 3)
        {
            return Week[start] + " to " + Week[(start + count - 1) % Week.Length];
        }

        int from = runs == 1 ? start : 0;
        List<string> names = [];
        for (int i = 0; i < Week.Length; i++)
        {
            int index = (from + i) % Week.Length;
            if (on[index])
            {
                names.Add(Week[index].ToString());
            }
        }

        return JoinList(names);
    }
}
