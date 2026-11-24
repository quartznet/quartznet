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
using System.Runtime.InteropServices;

using Quartz.Impl.Calendar;

namespace Quartz.Dashboard.Components.Shared;

/// <summary>
/// The six calendar types Quartz ships, the base type they derive from, and a type of an application's own.
/// </summary>
internal enum CalendarKind
{
    Holiday,
    Annual,
    Weekly,
    Monthly,
    Daily,
    Cron,
    Base,
    Custom,
}

/// <summary>
/// What the calendar pages read off a calendar: its kind, its time zone, the calendars it is layered over,
/// and the times a cron calendar excludes next.
/// </summary>
/// <remarks>
/// Calendar Detail used to reflect over every public property and print its <c>ToString()</c>, which showed a
/// holiday calendar's dates as <c>System.Collections.Generic.SortedSet`1[System.DateOnly]</c> and printed
/// whatever a custom calendar happened to expose. Each built-in type is read through its own members now, and
/// a type of the application's own is named and not read.
/// </remarks>
internal static class CalendarReading
{
    /// <summary>
    /// The longest base-calendar chain the pages walk: far past any real one, and a stop for a custom calendar
    /// whose base leads back to itself.
    /// </summary>
    public const int MaxChainLength = 32;

    /// <summary>The format a holiday's date is written and read in.</summary>
    public const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// The days of the week, Monday first. <see cref="DayOfWeek" /> starts on Sunday.
    /// </summary>
    public static readonly IReadOnlyList<DayOfWeek> WeekDays =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday,
    ];

    /// <summary>
    /// The kinds the dashboard creates and edits: the six built-in types with rules of their own.
    /// </summary>
    public static readonly IReadOnlyList<CalendarKind> EditableKinds =
    [
        CalendarKind.Holiday,
        CalendarKind.Annual,
        CalendarKind.Weekly,
        CalendarKind.Monthly,
        CalendarKind.Daily,
        CalendarKind.Cron,
    ];

    /// <summary>
    /// The kind of <paramref name="calendar" />. A type derived from <see cref="BaseCalendar" /> by an
    /// application is <see cref="CalendarKind.Custom" />: its rules are its own.
    /// </summary>
    public static CalendarKind KindOf(ICalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        return calendar switch
        {
            HolidayCalendar => CalendarKind.Holiday,
            AnnualCalendar => CalendarKind.Annual,
            WeeklyCalendar => CalendarKind.Weekly,
            MonthlyCalendar => CalendarKind.Monthly,
            DailyCalendar => CalendarKind.Daily,
            CronCalendar => CalendarKind.Cron,
            _ when calendar.GetType() == typeof(BaseCalendar) => CalendarKind.Base,
            _ => CalendarKind.Custom,
        };
    }

    /// <summary>
    /// The calendar's type in a word, as the Calendars listing shows it: <c>Holiday</c>, <c>Annual</c>, <c>Weekly</c>,
    /// <c>Monthly</c>, <c>Daily</c>, <c>Cron</c> or <c>Base</c>, and a custom type's own name.
    /// </summary>
    public static string ShortName(ICalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        CalendarKind kind = KindOf(calendar);
        return kind == CalendarKind.Custom ? calendar.GetType().Name : kind.ToString();
    }

    /// <summary>Whether the dashboard edits a calendar of this kind.</summary>
    public static bool IsEditable(CalendarKind kind) => kind is not (CalendarKind.Base or CalendarKind.Custom);

    /// <summary>The kind in words, as the pages name it.</summary>
    public static string Describe(CalendarKind kind) => kind switch
    {
        CalendarKind.Holiday => "Holiday calendar",
        CalendarKind.Annual => "Annual calendar",
        CalendarKind.Weekly => "Weekly calendar",
        CalendarKind.Monthly => "Monthly calendar",
        CalendarKind.Daily => "Daily calendar",
        CalendarKind.Cron => "Cron calendar",
        CalendarKind.Base => "Base calendar",
        _ => "Custom calendar",
    };

    /// <summary>
    /// The zone a calendar reads its days and times in, or <see langword="null" /> for a type that does not
    /// derive from <see cref="BaseCalendar" /> and so has none to report.
    /// </summary>
    /// <remarks>
    /// A calendar given no zone uses the local zone of the process it runs in. That is the zone the wire
    /// carries, because the serializer writes the zone the calendar reports; a calendar read in this process
    /// reports this process's, which is then the scheduler's too.
    /// </remarks>
    public static TimeZoneInfo? TimeZoneOf(ICalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        return calendar is BaseCalendar baseCalendar ? baseCalendar.TimeZone : null;
    }

    /// <summary>
    /// The calendars <paramref name="calendar" /> is layered over, nearest first.
    /// </summary>
    public static List<ICalendar> BaseChainOf(ICalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        List<ICalendar> chain = [];
        ICalendar? next = calendar.CalendarBase;
        while (next is not null && chain.Count < MaxChainLength && !ReferenceEquals(next, calendar) && !ContainsInstance(chain, next))
        {
            chain.Add(next);
            next = next.CalendarBase;
        }

        return chain;
    }

    /// <summary>
    /// The next spans of time <paramref name="expression" /> excludes, from <paramref name="from" /> on. Each
    /// runs from a second the expression matches to the first second it does not, which is when the calendar
    /// lets a trigger fire again.
    /// </summary>
    /// <remarks>
    /// A span already under way at <paramref name="from" /> is the first one, starting at it. A span with no
    /// <see cref="ExcludedSpan.End" /> never ends: the expression matches every second after its start.
    /// </remarks>
    public static List<ExcludedSpan> NextExcludedSpans(CronExpression expression, DateTimeOffset from, int count)
    {
        ArgumentNullException.ThrowIfNull(expression);

        List<ExcludedSpan> spans = [];

        // One second back, so a second that matches at "from" itself is found: the search answers the next
        // match strictly after the time it is given.
        DateTimeOffset cursor = from.AddSeconds(-1);
        while (spans.Count < count)
        {
            DateTimeOffset? start = expression.GetNextValidTimeAfter(cursor);
            if (start is null)
            {
                break;
            }

            DateTimeOffset? end = expression.GetNextInvalidTimeAfter(start.Value);
            spans.Add(new ExcludedSpan(start.Value, end));
            if (end is null)
            {
                break;
            }

            cursor = end.Value;
        }

        return spans;
    }

    /// <summary>
    /// An instant as a wall clock in <paramref name="timeZone" />, with its offset: <c>2026-12-24 09:00:00 +02:00</c>.
    /// </summary>
    public static string FormatInZone(DateTimeOffset instant, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        return TimeZoneInfo.ConvertTime(instant, timeZone).ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
    }

    /// <summary>A time of day, with its milliseconds only when it has some: <c>09:30:00</c>, <c>23:59:59.999</c>.</summary>
    public static string FormatTime(TimeOnly time)
    {
        return time.ToString(time.Millisecond == 0 ? "HH:mm:ss" : "HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }

    /// <summary>A holiday as <see cref="DateFormat" />.</summary>
    public static string FormatDate(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);

    /// <summary>The English name of a month, which is the language the dashboard's pages are written in.</summary>
    public static string MonthName(int month) => CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(month);

    /// <summary>A day of the year in words: <c>December 25</c>.</summary>
    public static string FormatMonthDay(MonthDay day) => MonthName(day.Month) + " " + day.Day.ToString(CultureInfo.InvariantCulture);

    /// <summary>The days a month can have, February counting 29.</summary>
    public static int DaysIn(int month) => DateTime.DaysInMonth(2000, month);

    /// <summary>Items joined as a sentence would list them: <c>a</c>, <c>a and b</c>, <c>a, b and c</c>.</summary>
    public static string JoinWords(IReadOnlyList<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return items.Count switch
        {
            0 => string.Empty,
            1 => items[0],
            _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
        };
    }

    /// <summary>A count with its noun, made plural when it is not one: <c>1 date</c>, <c>3 dates</c>.</summary>
    public static string Count(int count, string noun)
    {
        return count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? noun : noun + "s");
    }

    private static bool ContainsInstance(List<ICalendar> chain, ICalendar calendar)
    {
        foreach (ICalendar item in chain)
        {
            if (ReferenceEquals(item, calendar))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// A span of time a cron calendar excludes: from <see cref="Start" /> up to <see cref="End" />, which is the
/// first second it includes again, or with no end when it never does.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ExcludedSpan(DateTimeOffset Start, DateTimeOffset? End);
