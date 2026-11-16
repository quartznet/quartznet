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

namespace Quartz.Impl.Triggers;

/// <summary>
/// Says a Quartz cron expression in words: <c>Every weekday at 07:00 (America/Chicago)</c>.
/// </summary>
/// <remarks>
/// <para>
/// It reads the expression as written rather than as the parser stored it, so a step reads as a step and a
/// range as a range. The expression has already been through the parser, so it is valid; a
/// <see cref="CronFormat.Unix" /> one arrives in the Quartz form the parser rewrote it into.
/// </para>
/// <para>
/// A shape it has no sentence for answers <see langword="null" />, and the caller shows the expression
/// itself. Saying nothing new is better than saying something the schedule does not do, so every rule
/// below either reads the whole field or gives up.
/// </para>
/// </remarks>
internal static class CronScheduleDescriber
{
    private static readonly char[] FieldSeparators = [' ', '\t'];

    private static readonly string[] MonthNames = ["JAN", "FEB", "MAR", "APR", "MAY", "JUN", "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"];

    private static readonly string[] DayNames = ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];

    private const int MinYear = 1970;
    private const int MaxYear = 2999;

    /// <summary>
    /// The expression in words with its zone, or the expression itself with its zone.
    /// </summary>
    public static string Describe(string expression, TimeZoneInfo timeZone)
    {
        return (Phrase(expression) ?? expression.Trim()) + ScheduleWords.Zone(timeZone);
    }

    /// <summary>
    /// The expression in words, without a zone, or <see langword="null" /> when this has no sentence for it.
    /// </summary>
    internal static string? Phrase(string expression)
    {
        string[] fields = expression.ToUpperInvariant().Split(FieldSeparators, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length is not (6 or 7))
        {
            return null;
        }

        TimeField? seconds = TimeField.Read(fields[0], 59);
        TimeField? minutes = TimeField.Read(fields[1], 59);
        TimeField? hours = TimeField.Read(fields[2], 23);
        if (seconds is null || minutes is null || hours is null)
        {
            return null;
        }

        TimePhrase? time = DescribeTime(seconds, minutes, hours);
        DayPhrase? days = DescribeDays(fields[3], fields[5]);
        MonthPhrase? months = DescribeMonths(fields[4]);
        string? years = fields.Length == 7 ? DescribeYears(fields[6]) : string.Empty;
        if (time is null || days is null || months is null || years is null)
        {
            return null;
        }

        // 22-2 under a day filter fires the morning and the evening of each day it names, never the night from
        // one into the next, so an overnight window would claim hours it skips.
        if (time.CrossesMidnight && days.Kind != DayKind.EveryDay)
        {
            return null;
        }

        string text;
        switch (days.Kind)
        {
            case DayKind.EveryDay:
                text = (time.IsClockTimes ? "every day " + time.Text : time.Text) + months.Suffix;
                break;
            case DayKind.Weekly:
                text = (time.IsClockTimes ? "every " + days.Weekdays + " " + time.Text : time.Text + ", every " + days.Weekdays) + months.Suffix;
                break;
            case DayKind.Monthly:
                text = time.Text + " on " + days.MonthDays + " of " + months.Of;
                break;
            default:
                // Both day fields name days, and the expression fires on either: crontab's rule, and Quartz's.
                text = months.IsEvery
                    ? time.Text + " on " + days.MonthDays + " of every month and every " + days.Weekdays
                    : time.Text + " on " + days.MonthDays + " and every " + days.Weekdays + months.Suffix;
                break;
        }

        return ScheduleWords.Capitalize(text + years);
    }

    private static TimePhrase? DescribeTime(TimeField seconds, TimeField minutes, TimeField hours)
    {
        TimePhrase? time = DescribeTimeOfDay(seconds, minutes, hours);
        return time is not null && hours.Wraps ? time with { CrossesMidnight = true } : time;
    }

    private static TimePhrase? DescribeTimeOfDay(TimeField seconds, TimeField minutes, TimeField hours)
    {
        if (seconds.Kind == FieldKind.Values && minutes.Kind == FieldKind.Values && hours.Kind == FieldKind.Values)
        {
            return ClockTimes(seconds, minutes, hours);
        }

        if (seconds.IsSingle)
        {
            int second = seconds.Values[0];
            if (minutes.IsSingle)
            {
                return EveryHour(minutes.Values[0], second, hours);
            }

            if (minutes.Kind == FieldKind.Values)
            {
                // At some minutes of every hour; under fewer hours than every one, ClockTimes has it or nothing does.
                return hours.Kind == FieldKind.Any && second == 0
                    ? new TimePhrase("every hour at minutes " + ScheduleWords.JoinList(minutes.Values.Select(x => ScheduleWords.Number(x)).ToList()), IsClockTimes: false)
                    : null;
            }

            return MinuteInterval(minutes, second, hours);
        }

        return seconds.Kind == FieldKind.Values ? null : SecondInterval(seconds, minutes, hours);
    }

    /// <summary>
    /// <c>at 07:00</c>, <c>at 08:00, 12:00 and 18:00</c>: a handful of times of day, each written out.
    /// </summary>
    private static TimePhrase? ClockTimes(TimeField seconds, TimeField minutes, TimeField hours)
    {
        int count = seconds.Values.Length * minutes.Values.Length * hours.Values.Length;
        bool onlyTheHourVaries = seconds.IsSingle && minutes.IsSingle;
        if (count > 6 && !onlyTheHourVaries)
        {
            return null;
        }

        bool withSeconds = seconds.Values.Any(static x => x != 0);
        List<string> times = [];
        foreach (int hour in hours.Values)
        {
            foreach (int minute in minutes.Values)
            {
                foreach (int second in seconds.Values)
                {
                    times.Add(ScheduleWords.Clock(hour, minute, second, withSeconds));
                }
            }
        }

        return new TimePhrase("at " + ScheduleWords.JoinList(times), IsClockTimes: true);
    }

    /// <summary>
    /// One minute and second of more hours than a list names: <c>every hour</c>, <c>every hour from 09:30 to
    /// 17:30</c>, <c>every 2 hours</c>.
    /// </summary>
    private static TimePhrase EveryHour(int minute, int second, TimeField hours)
    {
        bool withSeconds = second != 0;
        string text;
        switch (hours.Kind)
        {
            case FieldKind.Any:
                text = "every hour";
                if (minute != 0 || second != 0)
                {
                    text += " at minute " + ScheduleWords.Number(minute) + (second != 0 ? ", second " + ScheduleWords.Number(second) : string.Empty);
                }

                break;
            case FieldKind.Range:
                text = "every hour from " + ScheduleWords.Clock(hours.First, minute, second, withSeconds)
                       + " to " + ScheduleWords.Clock(hours.Last, minute, second, withSeconds);
                break;
            default:
                text = ScheduleWords.Every(hours.Step, "hour");
                if (hours.IsEven(24))
                {
                    if (hours.First != 0 || minute != 0 || second != 0)
                    {
                        text += " from " + ScheduleWords.Clock(hours.First, minute, second, withSeconds);
                    }
                }
                else
                {
                    text += " from " + ScheduleWords.Clock(hours.First, minute, second, withSeconds)
                            + " to " + ScheduleWords.Clock(hours.Last, minute, second, withSeconds);
                }

                break;
        }

        return new TimePhrase(text, IsClockTimes: false);
    }

    /// <summary>
    /// One second of moving minutes: <c>every 5 minutes</c>, <c>every minute between 14:00 and 14:05</c>,
    /// <c>every 15 minutes between 09:00 and 17:45</c>.
    /// </summary>
    private static TimePhrase? MinuteInterval(TimeField minutes, int second, TimeField hours)
    {
        if (minutes.Wraps)
        {
            return null;
        }

        bool withSeconds = second != 0;
        string text = minutes.Kind == FieldKind.Step ? ScheduleWords.Every(minutes.Step, "minute") : "every minute";
        bool even = minutes.IsEven(60);

        if (hours.Kind == FieldKind.Any)
        {
            if (!even)
            {
                text += " from minute " + ScheduleWords.Number(minutes.First) + " to minute " + ScheduleWords.Number(minutes.Last) + " of every hour";
            }
            else if (minutes.First != 0)
            {
                text += ", starting at minute " + ScheduleWords.Number(minutes.First);
            }

            return new TimePhrase(withSeconds ? text + ", at second " + ScheduleWords.Number(second) : text, IsClockTimes: false);
        }

        if (hours.IsSingle)
        {
            int hour = hours.Values[0];
            return new TimePhrase(
                text + " between " + ScheduleWords.Clock(hour, minutes.First, second, withSeconds)
                + " and " + ScheduleWords.Clock(hour, minutes.Last, second, withSeconds),
                IsClockTimes: false);
        }

        // Across several hours the minutes must run on evenly through each hour boundary, or the span between
        // the first and the last firing would claim times the schedule skips.
        if (!even || hours.Runs() is not { } runs)
        {
            return null;
        }

        List<string> windows = runs
            .Select(run => "between " + ScheduleWords.Clock(run.From, minutes.First, second, withSeconds)
                           + " and " + ScheduleWords.Clock(run.To, minutes.Last, second, withSeconds))
            .ToList();
        return new TimePhrase(text + " " + string.Join(" and ", windows), IsClockTimes: false);
    }

    /// <summary>
    /// Moving seconds: <c>every 30 seconds</c>, <c>every 10 seconds between 14:00:00 and 14:59:50</c>.
    /// </summary>
    private static TimePhrase? SecondInterval(TimeField seconds, TimeField minutes, TimeField hours)
    {
        if (seconds.Wraps)
        {
            return null;
        }

        string text = seconds.Kind == FieldKind.Step ? ScheduleWords.Every(seconds.Step, "second") : "every second";
        bool even = seconds.IsEven(60);

        if (minutes.Kind == FieldKind.Any && hours.Kind == FieldKind.Any)
        {
            if (!even)
            {
                text += " from second " + ScheduleWords.Number(seconds.First) + " to second " + ScheduleWords.Number(seconds.Last) + " of every minute";
            }
            else if (seconds.First != 0)
            {
                text += ", starting at second " + ScheduleWords.Number(seconds.First);
            }

            return new TimePhrase(text, IsClockTimes: false);
        }

        if (minutes.IsSingle && hours.IsSingle)
        {
            int hour = hours.Values[0];
            int minute = minutes.Values[0];
            return new TimePhrase(
                text + " between " + ScheduleWords.Clock(hour, minute, seconds.First, withSeconds: true)
                + " and " + ScheduleWords.Clock(hour, minute, seconds.Last, withSeconds: true),
                IsClockTimes: false);
        }

        if (minutes.Kind != FieldKind.Any || !even || hours.Runs() is not { } runs)
        {
            return null;
        }

        List<string> windows = runs
            .Select(run => "between " + ScheduleWords.Clock(run.From, 0, seconds.First, withSeconds: true)
                           + " and " + ScheduleWords.Clock(run.To, 59, seconds.Last, withSeconds: true))
            .ToList();
        return new TimePhrase(text + " " + string.Join(" and ", windows), IsClockTimes: false);
    }

    private static DayPhrase? DescribeDays(string dayOfMonth, string dayOfWeek)
    {
        MonthDays? monthDays = MonthDays.Read(dayOfMonth);
        WeekDays? weekDays = WeekDays.Read(dayOfWeek);
        if (monthDays is null || weekDays is null)
        {
            return null;
        }

        if (weekDays.Monthly is { } monthlyWeekday)
        {
            // The nth or last of a weekday, beside a day of the month, is a union this does not phrase.
            return monthDays.IsEvery ? new DayPhrase(DayKind.Monthly, monthlyWeekday, Weekdays: null) : null;
        }

        // A field that names every day matches every day, whatever the other one says.
        if (weekDays.IsEveryNamed || monthDays.IsEveryNamed || (weekDays.IsEvery && monthDays.IsEvery))
        {
            return new DayPhrase(DayKind.EveryDay, MonthDays: null, Weekdays: null);
        }

        if (monthDays.IsEvery)
        {
            return new DayPhrase(DayKind.Weekly, MonthDays: null, weekDays.Phrase);
        }

        return weekDays.IsEvery
            ? new DayPhrase(DayKind.Monthly, monthDays.Phrase, Weekdays: null)
            : new DayPhrase(DayKind.Union, monthDays.Phrase, weekDays.Phrase);
    }

    private static MonthPhrase? DescribeMonths(string field)
    {
        List<Item>? items = Item.ReadAll(field, 1, 12, MonthNames);
        if (items is null)
        {
            return null;
        }

        SortedSet<int> months = Item.Expand(items, 1, 12);
        if (months.Count == 12)
        {
            return new MonthPhrase(Suffix: string.Empty, Of: "every month", IsEvery: true);
        }

        if (CyclicRun(months, 1, 12) is { } run && months.Count >= 3)
        {
            string span = "from " + ScheduleWords.MonthName(run.From) + " to " + ScheduleWords.MonthName(run.To);
            return new MonthPhrase(Suffix: " " + span, Of: "every month " + span, IsEvery: false);
        }

        string names = ScheduleWords.JoinList(months.Select(ScheduleWords.MonthName).ToList());
        return new MonthPhrase(Suffix: " in " + names, Of: names, IsEvery: false);
    }

    /// <summary>
    /// The year field as a suffix: empty for every year, <c> in 2026</c>, <c> from 2026 to 2030</c>,
    /// <c> every 2 years from 2026</c>; <see langword="null" /> for a shape this does not phrase.
    /// </summary>
    private static string? DescribeYears(string field)
    {
        List<Item>? items = Item.ReadAll(field, MinYear, MaxYear, names: null);
        if (items is null)
        {
            return null;
        }

        if (items is [{ Kind: ItemKind.Star }])
        {
            return string.Empty;
        }

        // A step or range that reaches one year is that year: 2026-2027/2 is 2026 only.
        SortedSet<int> years = Item.Expand(items, MinYear, MaxYear);
        if (years.Count == 1 || items.TrueForAll(static x => x.Kind == ItemKind.Value))
        {
            return " in " + ScheduleWords.JoinList(years.Select(x => ScheduleWords.Number(x)).ToList());
        }

        return items switch
        {
            [{ Kind: ItemKind.Range } range] when range.From < range.To
                => " from " + ScheduleWords.Number(range.From) + " to " + ScheduleWords.Number(range.To),
            [{ Kind: ItemKind.Step, To: -1 } step]
                => " " + ScheduleWords.Every(step.Increment, "year") + " from " + ScheduleWords.Number(step.From),
            [{ Kind: ItemKind.Step } step] when step.From < step.To
                => " " + ScheduleWords.Every(step.Increment, "year") + " from " + ScheduleWords.Number(step.From)
                   + " to " + ScheduleWords.Number(step.From + (step.To - step.From) / step.Increment * step.Increment),
            _ => null
        };
    }

    /// <summary>
    /// The values as one run, counting the last value to the first as adjacent, or <see langword="null" /> when
    /// they are several runs or every value.
    /// </summary>
    private static (int From, int To)? CyclicRun(SortedSet<int> values, int min, int max)
    {
        int size = max - min + 1;
        if (values.Count == 0 || values.Count == size)
        {
            return null;
        }

        int? start = null;
        foreach (int value in values)
        {
            int previous = value == min ? max : value - 1;
            if (!values.Contains(previous))
            {
                if (start is not null)
                {
                    return null;
                }

                start = value;
            }
        }

        if (start is not { } from)
        {
            return null;
        }

        int to = from;
        while (true)
        {
            int next = to == max ? min : to + 1;
            if (!values.Contains(next))
            {
                return (from, to);
            }

            to = next;
        }
    }

    private static bool TryNumber(string text, int min, int max, string[]? names, out int value)
    {
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value))
        {
            return value >= min && value <= max;
        }

        if (names is not null)
        {
            int index = Array.IndexOf(names, text);
            if (index >= 0)
            {
                value = index + 1;
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static DayOfWeek CronDay(int day) => (DayOfWeek) (day - 1);

    /// <summary>
    /// The time of day in words. <see cref="CrossesMidnight" /> is set on a window whose hours wrap past midnight,
    /// <c>from 22:00 to 02:00</c>, which is one window only on a schedule that fires every day.
    /// </summary>
    private sealed record TimePhrase(string Text, bool IsClockTimes)
    {
        public bool CrossesMidnight { get; init; }
    }

    private enum DayKind
    {
        EveryDay,
        Weekly,
        Monthly,
        Union
    }

    /// <summary>
    /// What the two day fields say together. <see cref="MonthDays" /> is a noun phrase that reads after "on" and
    /// before "of": <c>the last day</c>, <c>the third Friday</c>; <see cref="Weekdays" /> reads after "every":
    /// <c>weekday</c>, <c>Saturday and Sunday</c>.
    /// </summary>
    private sealed record DayPhrase(DayKind Kind, string? MonthDays, string? Weekdays);

    /// <summary>
    /// The month field, as a suffix after a time (<c> in March</c>) and after "of" (<c>every month</c>, <c>March</c>).
    /// </summary>
    private sealed record MonthPhrase(string Suffix, string Of, bool IsEvery);

    private enum ItemKind
    {
        Star,
        Value,
        Range,
        Step
    }

    /// <summary>
    /// One comma-separated piece of a field: <c>*</c>, <c>5</c>, <c>MON-FRI</c>, <c>0/15</c>, <c>9-17/2</c>.
    /// <see cref="To" /> is <c>-1</c> on a step that runs to the end of the field.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Item(ItemKind Kind, int From, int To, int Increment)
    {
        public static List<Item>? ReadAll(string field, int min, int max, string[]? names)
        {
            List<Item> items = [];
            foreach (string token in field.Split(','))
            {
                if (Read(token, min, max, names) is not { } item)
                {
                    return null;
                }

                items.Add(item);
            }

            // A wildcard beside anything else is no shape a person writes.
            return items.Count > 1 && items.Exists(static x => x.Kind == ItemKind.Star) ? null : items;
        }

        private static Item? Read(string token, int min, int max, string[]? names)
        {
            if (token is "*" or "?")
            {
                return new Item(ItemKind.Star, min, max, 1);
            }

            int slash = token.IndexOf('/');
            if (slash >= 0)
            {
                if (!int.TryParse(token.AsSpan(slash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int increment) || increment < 1)
                {
                    return null;
                }

                string start = token.Substring(0, slash);
                if (start is "" or "*")
                {
                    return new Item(ItemKind.Step, min, -1, increment);
                }

                int startDash = start.IndexOf('-');
                if (startDash > 0)
                {
                    return TryNumber(start.Substring(0, startDash), min, max, names, out int rangeFrom)
                           && TryNumber(start.Substring(startDash + 1), min, max, names, out int rangeTo)
                        ? new Item(ItemKind.Step, rangeFrom, rangeTo, increment)
                        : null;
                }

                return TryNumber(start, min, max, names, out int from) ? new Item(ItemKind.Step, from, -1, increment) : null;
            }

            int dash = token.IndexOf('-');
            if (dash > 0)
            {
                return TryNumber(token.Substring(0, dash), min, max, names, out int from)
                       && TryNumber(token.Substring(dash + 1), min, max, names, out int to)
                    ? new Item(ItemKind.Range, from, to, 1)
                    : null;
            }

            return TryNumber(token, min, max, names, out int value) ? new Item(ItemKind.Value, value, value, 1) : null;
        }

        /// <summary>
        /// Every value the items name. A range or a step whose end is below its start wraps past the field's
        /// end, as the parser reads it.
        /// </summary>
        public static SortedSet<int> Expand(List<Item> items, int min, int max)
        {
            SortedSet<int> values = [];
            foreach (Item item in items)
            {
                int to = item.To < 0 ? max : item.To;
                int length = to >= item.From ? to - item.From + 1 : max - item.From + 1 + (to - min + 1);
                for (int offset = 0; offset < length; offset += item.Kind == ItemKind.Step ? item.Increment : 1)
                {
                    int value = item.From + offset;
                    values.Add(value > max ? value - (max - min + 1) : value);
                }
            }

            return values;
        }
    }

    private enum FieldKind
    {
        Any,
        Values,
        Range,
        Step
    }

    /// <summary>
    /// A second, minute or hour field: every value, a list of values, one range or one step.
    /// </summary>
    /// <remarks>
    /// <see cref="First" /> and <see cref="Last" /> are the first and last values it fires on, in the order it
    /// fires them; for a wrapping range the first is the larger.
    /// </remarks>
    private sealed class TimeField
    {
        private TimeField(FieldKind kind, int[] values, int first, int last, int step, bool open, bool wraps)
        {
            Kind = kind;
            Values = values;
            First = first;
            Last = last;
            Step = step;
            Open = open;
            Wraps = wraps;
        }

        public FieldKind Kind { get; }

        public int[] Values { get; }

        public int First { get; }

        public int Last { get; }

        public int Step { get; }

        /// <summary>
        /// A step with no end of its own, which runs to the end of the field.
        /// </summary>
        public bool Open { get; }

        public bool Wraps { get; }

        public bool IsSingle => Kind == FieldKind.Values && Values.Length == 1;

        /// <summary>
        /// Whether the field fires at one interval without a break, through the turn of the unit above it:
        /// <c>*</c>, and a step from below its own size that divides <paramref name="size" />, like <c>5/15</c>
        /// in minutes. <c>0/25</c> is not: 50 to the next hour's 0 is ten minutes.
        /// </summary>
        public bool IsEven(int size)
        {
            return Kind == FieldKind.Any || (Kind == FieldKind.Step && Open && First < Step && size % Step == 0);
        }

        /// <summary>
        /// The hours as at most three spans of consecutive hours, or <see langword="null" />.
        /// </summary>
        public List<(int From, int To)>? Runs()
        {
            if (Kind == FieldKind.Range)
            {
                return [(First, Last)];
            }

            if (Kind != FieldKind.Values)
            {
                return null;
            }

            List<(int From, int To)> runs = [];
            foreach (int value in Values)
            {
                if (runs.Count > 0 && runs[^1].To == value - 1)
                {
                    runs[^1] = (runs[^1].From, value);
                }
                else
                {
                    runs.Add((value, value));
                }
            }

            return runs.Count <= 3 ? runs : null;
        }

        public static TimeField? Read(string field, int max)
        {
            List<Item>? items = Item.ReadAll(field, 0, max, names: null);
            if (items is null)
            {
                return null;
            }

            int[] all = Item.Expand(items, 0, max).ToArray();
            if (all.Length == max + 1)
            {
                return new TimeField(FieldKind.Any, all, 0, max, 1, open: true, wraps: false);
            }

            // A step or range that reaches one value is that value: 20/6 in hours fires at 20 only, and reads as
            // an interval from 20:00 to 20:00 otherwise.
            if (all.Length == 1 || items.TrueForAll(static x => x.Kind == ItemKind.Value))
            {
                return new TimeField(FieldKind.Values, all, all[0], all[^1], 1, open: false, wraps: false);
            }

            if (items.Count != 1)
            {
                return null;
            }

            Item item = items[0];
            if (item.Kind == ItemKind.Range || (item.Kind == ItemKind.Step && item.Increment == 1))
            {
                int to = item.To < 0 ? max : item.To;
                return new TimeField(FieldKind.Range, all, item.From, to, 1, open: false, wraps: to < item.From);
            }

            if (item.To >= 0 && item.To < item.From)
            {
                // A step through a wrapping range: rare enough to show as written.
                return null;
            }

            return new TimeField(FieldKind.Step, all, item.From, all[^1], item.Increment, open: item.To < 0, wraps: false);
        }
    }

    /// <summary>
    /// The day-of-month field, as a noun phrase: <c>the 1st and the 15th</c>, <c>the last day</c>,
    /// <c>the weekday nearest the 15th</c>.
    /// </summary>
    private sealed class MonthDays
    {
        private MonthDays(string? phrase, bool isEvery, bool isEveryNamed)
        {
            Phrase = phrase;
            IsEvery = isEvery;
            IsEveryNamed = isEveryNamed;
        }

        public string? Phrase { get; }

        /// <summary>
        /// <c>*</c> or <c>?</c>: no opinion, which leaves the days to the other field.
        /// </summary>
        public bool IsEvery { get; }

        /// <summary>
        /// Every day of the month named one by one, which matches every day.
        /// </summary>
        public bool IsEveryNamed { get; }

        public static MonthDays? Read(string field)
        {
            if (field is "*" or "?")
            {
                return new MonthDays(phrase: null, isEvery: true, isEveryNamed: false);
            }

            List<string> phrases = [];
            List<Item> numbers = [];
            foreach (string token in field.Split(','))
            {
                if (token.StartsWith('L'))
                {
                    if (LastDay(token) is not { } last)
                    {
                        return null;
                    }

                    phrases.Add(last);
                }
                else if (token.EndsWith('W'))
                {
                    if (!TryNumber(token.Substring(0, token.Length - 1), 1, 31, names: null, out int day))
                    {
                        return null;
                    }

                    phrases.Add("the weekday nearest the " + ScheduleWords.Ordinal(day));
                }
                else
                {
                    if (Item.ReadAll(token, 1, 31, names: null) is not [{ Kind: not ItemKind.Star } item])
                    {
                        return null;
                    }

                    numbers.Add(item);

                    // A step or range that reaches one day is that day: 25/10 is the 25th only.
                    SortedSet<int> reached = Item.Expand([item], 1, 31);
                    phrases.Add(item.Kind switch
                    {
                        _ when reached.Count == 1 => "the " + ScheduleWords.Ordinal(reached.Min),
                        ItemKind.Value => "the " + ScheduleWords.Ordinal(item.From),
                        ItemKind.Range => "the " + ScheduleWords.Ordinal(item.From) + " to the " + ScheduleWords.Ordinal(item.To),
                        _ => "every " + (item.Increment == 1 ? string.Empty : ScheduleWords.Ordinal(item.Increment) + " ") + "day from the "
                             + ScheduleWords.Ordinal(item.From) + (item.To < 0 ? string.Empty : " to the " + ScheduleWords.Ordinal(item.To))
                    });
                }
            }

            bool everyNamed = numbers.Count > 0 && Item.Expand(numbers, 1, 31).Count == 31;
            return new MonthDays(ScheduleWords.JoinList(phrases), isEvery: false, everyNamed);
        }

        /// <summary>
        /// <c>L</c>, <c>L-3</c>, <c>LW</c> and <c>L-3W</c>; <see langword="null" /> for an offset from the last
        /// weekday (<c>LW-2</c>), which is rare enough to show as written.
        /// </summary>
        private static string? LastDay(string token)
        {
            string rest = token.Substring(1);
            bool weekday = rest.EndsWith('W');
            if (weekday)
            {
                rest = rest.Substring(0, rest.Length - 1);
            }

            int offset = 0;
            if (rest.Length > 0 && (rest[0] != '-' || !TryNumber(rest.Substring(1), 0, 30, names: null, out offset)))
            {
                return null;
            }

            string day = offset switch
            {
                0 => "the last day",
                1 => "the day before the last day",
                _ => "the day " + ScheduleWords.Count(offset, "day") + " before the last day"
            };

            return weekday ? (offset == 0 ? "the last weekday" : "the weekday nearest " + day) : day;
        }
    }

    /// <summary>
    /// The day-of-week field: a set of days, or one day's nth or last occurrence in the month.
    /// </summary>
    private sealed class WeekDays
    {
        private WeekDays(string? phrase, string? monthly, bool isEvery, bool isEveryNamed)
        {
            Phrase = phrase;
            Monthly = monthly;
            IsEvery = isEvery;
            IsEveryNamed = isEveryNamed;
        }

        /// <summary>
        /// The set of days, read after "every": <c>weekday</c>, <c>Monday and Friday</c>.
        /// </summary>
        public string? Phrase { get; }

        /// <summary>
        /// <c>the last Friday</c>, <c>the third Friday</c>: a day of the month rather than of every week.
        /// </summary>
        public string? Monthly { get; }

        public bool IsEvery { get; }

        public bool IsEveryNamed { get; }

        public static WeekDays? Read(string field)
        {
            if (field is "*" or "?")
            {
                return new WeekDays(phrase: null, monthly: null, isEvery: true, isEveryNamed: false);
            }

            int hash = field.IndexOf('#');
            if (hash > 0)
            {
                return TryNumber(field.Substring(0, hash), 1, 7, DayNames, out int day)
                       && TryNumber(field.Substring(hash + 1), 1, 5, names: null, out int nth)
                    ? new WeekDays(phrase: null, "the " + ScheduleWords.OrdinalWord(nth) + " " + CronDay(day), isEvery: false, isEveryNamed: false)
                    : null;
            }

            if (field.Length > 1 && field.EndsWith('L'))
            {
                return TryNumber(field.Substring(0, field.Length - 1), 1, 7, DayNames, out int day)
                    ? new WeekDays(phrase: null, "the last " + CronDay(day), isEvery: false, isEveryNamed: false)
                    : null;
            }

            // On its own, 'L' is the last day of the week: Saturday.
            List<Item>? items = Item.ReadAll(field == "L" ? "7" : field, 1, 7, DayNames);
            if (items is null)
            {
                return null;
            }

            List<DayOfWeek> days = Item.Expand(items, 1, 7).Select(CronDay).ToList();
            if (days.Count == 7)
            {
                return new WeekDays(phrase: null, monthly: null, isEvery: false, isEveryNamed: true);
            }

            string? phrase = ScheduleWords.IsWorkWeek(days) ? "weekday" : ScheduleWords.Days(days);
            return phrase is null ? null : new WeekDays(phrase, monthly: null, isEvery: false, isEveryNamed: false);
        }
    }
}
