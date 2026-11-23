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

using Quartz.Dashboard.Components.Shared;
using Quartz.Impl.Calendar;

namespace Quartz.Dashboard.Services;

/// <summary>
/// The calendar editor's fields, as the text and ticks the inputs hold, and the one place they become a
/// calendar of one of the six built-in types.
/// </summary>
/// <remarks>
/// <para>
/// An edit builds a new calendar of the same type and replaces the stored one with it, which is what
/// <see cref="IScheduler.AddCalendar" /> offers. The calendars it is layered over are kept as they were read:
/// the editor changes a calendar's own rules, its description and its time zone, not its chain.
/// </para>
/// <para>
/// The time zone is a field of its own and always filled: a new calendar starts in UTC rather than in the
/// zone of whichever process the browser or the scheduler runs in, and an edited one starts in the zone it
/// reports.
/// </para>
/// </remarks>
internal sealed class CalendarForm
{
    /// <summary>The forms a time of day is read in: to the minute, the second or the millisecond.</summary>
    private static readonly string[] TimeFormats = ["H:mm", "H:mm:ss", "H:mm:ss.fff"];

    private readonly ICalendar? original;

    private CalendarForm(CalendarKind kind, ICalendar? original)
    {
        Kind = kind;
        this.original = original;
    }

    /// <summary>A form for a calendar not stored yet, of <paramref name="kind" />.</summary>
    public static CalendarForm ForNew(CalendarKind kind = CalendarKind.Holiday)
    {
        if (!CalendarReading.IsEditable(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "The dashboard creates the six built-in calendar types only.");
        }

        CalendarForm form = new(kind, original: null)
        {
            TimeZoneId = TimeZoneInfo.Utc.Id,
        };

        // A weekly calendar excludes the weekend until told otherwise, and the form starts where the type does.
        form.WeeklyDays.Add(DayOfWeek.Saturday);
        form.WeeklyDays.Add(DayOfWeek.Sunday);
        return form;
    }

    /// <summary>A form holding what <paramref name="calendar" /> holds now.</summary>
    /// <exception cref="ArgumentException">The calendar is not one of the six types the dashboard edits.</exception>
    public static CalendarForm From(ICalendar calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        CalendarKind kind = CalendarReading.KindOf(calendar);
        if (!CalendarReading.IsEditable(kind))
        {
            throw new ArgumentException("A " + calendar.GetType().Name + " is not edited by the dashboard.", nameof(calendar));
        }

        CalendarForm form = new(kind, calendar)
        {
            Description = calendar.Description ?? string.Empty,
            TimeZoneId = CalendarReading.TimeZoneOf(calendar)?.Id ?? TimeZoneInfo.Utc.Id,
        };

        switch (calendar)
        {
            case HolidayCalendar holiday:
                form.HolidayDates = string.Join("\n", holiday.DaysExcluded.Order().Select(CalendarReading.FormatDate));
                break;
            case AnnualCalendar annual:
                form.AnnualDays.UnionWith(annual.DaysExcluded);
                break;
            case WeeklyCalendar weekly:
                form.WeeklyDays.UnionWith(weekly.DaysExcluded);
                break;
            case MonthlyCalendar monthly:
                form.MonthlyDays.UnionWith(monthly.DaysExcluded);
                break;
            case DailyCalendar daily:
                form.DailyStart = CalendarReading.FormatTime(daily.TimeRange.Start);
                form.DailyEnd = CalendarReading.FormatTime(daily.TimeRange.End);
                form.DailyInvert = daily.InvertTimeRange;
                break;
            case CronCalendar cron:
                form.CronExpression = cron.CronExpression.CronExpressionString;
                break;
        }

        return form;
    }

    /// <summary>Whether the form is for a calendar not stored yet, whose type can still be chosen.</summary>
    public bool IsNew => original is null;

    /// <summary>The type the form builds. Only a new calendar's can be changed.</summary>
    public CalendarKind Kind { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>The zone the calendar's days and times are read in, by its system id.</summary>
    public string TimeZoneId { get; set; } = string.Empty;

    /// <summary>A holiday calendar's dates, one a line, as <c>yyyy-MM-dd</c>.</summary>
    public string HolidayDates { get; set; } = string.Empty;

    /// <summary>An annual calendar's days of the year, which the grid's ticks change in place.</summary>
    public HashSet<MonthDay> AnnualDays { get; } = [];

    /// <summary>A weekly calendar's days.</summary>
    public HashSet<DayOfWeek> WeeklyDays { get; } = [];

    /// <summary>A monthly calendar's days of the month, 1 through 31.</summary>
    public HashSet<int> MonthlyDays { get; } = [];

    /// <summary>A daily calendar's range start, as a time of day.</summary>
    public string DailyStart { get; set; } = string.Empty;

    /// <summary>A daily calendar's range end, as a time of day.</summary>
    public string DailyEnd { get; set; } = string.Empty;

    /// <summary>Whether a daily calendar's range is the part of the day it includes rather than excludes.</summary>
    public bool DailyInvert { get; set; }

    /// <summary>A cron calendar's expression, whose matching seconds it excludes.</summary>
    public string CronExpression { get; set; } = string.Empty;

    /// <summary>
    /// The zone <see cref="TimeZoneId" /> names, or <see langword="null" /> while it names none this process
    /// knows.
    /// </summary>
    public TimeZoneInfo? TryResolveTimeZone()
    {
        string id = TimeZoneId.Trim();
        if (id.Length == 0)
        {
            return null;
        }

        try
        {
            return TimeZones.FindById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }

    /// <summary>
    /// The calendar the fields describe, or the reasons they describe none. Each reason names the field it is
    /// about, so the reader corrects that one rather than starting again.
    /// </summary>
    public CalendarFormResult Build()
    {
        List<string> errors = [];

        TimeZoneInfo? timeZone = TryResolveTimeZone();
        if (timeZone is null)
        {
            errors.Add(TimeZoneId.Trim().Length == 0
                ? "A calendar needs a time zone: its days and times are read in it."
                : "Time zone '" + TimeZoneId.Trim() + "' is not one this dashboard knows.");
        }

        // The editor changes the calendar's own rules; the calendars under it go back as they came.
        ICalendar? calendarBase = original?.CalendarBase?.Clone();
        string? description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();

        ICalendar? calendar = Kind switch
        {
            CalendarKind.Holiday => BuildHoliday(errors, calendarBase),
            CalendarKind.Annual => BuildAnnual(errors, calendarBase),
            CalendarKind.Weekly => BuildWeekly(errors, calendarBase),
            CalendarKind.Monthly => BuildMonthly(errors, calendarBase),
            CalendarKind.Daily => BuildDaily(errors, calendarBase),
            CalendarKind.Cron => BuildCron(errors, calendarBase, timeZone),
            _ => throw new InvalidOperationException("The dashboard does not build a " + CalendarReading.Describe(Kind).ToLowerInvariant() + "."),
        };

        if (errors.Count > 0 || calendar is null || timeZone is null)
        {
            return new CalendarFormResult(null, errors);
        }

        calendar.Description = description;
        ((BaseCalendar) calendar).TimeZone = timeZone;
        return new CalendarFormResult(calendar, errors);
    }

    private HolidayCalendar? BuildHoliday(List<string> errors, ICalendar? calendarBase)
    {
        HolidayCalendar calendar = new() { CalendarBase = calendarBase };
        foreach (string line in HolidayDates.Split('\n'))
        {
            string text = line.Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (DateOnly.TryParseExact(text, CalendarReading.DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
            {
                calendar.AddExcludedDay(date);
            }
            else
            {
                errors.Add("'" + text + "' is not a date written as " + CalendarReading.DateFormat + ".");
            }
        }

        return calendar;
    }

    private AnnualCalendar BuildAnnual(List<string> errors, ICalendar? calendarBase)
    {
        if (AnnualDays.Count == 366)
        {
            errors.Add("An annual calendar that excludes every day of the year leaves a trigger nothing to fire at.");
        }

        AnnualCalendar calendar = new() { CalendarBase = calendarBase };
        foreach (MonthDay day in AnnualDays)
        {
            calendar.AddExcludedDay(day);
        }

        return calendar;
    }

    private WeeklyCalendar BuildWeekly(List<string> errors, ICalendar? calendarBase)
    {
        if (WeeklyDays.Count == 7)
        {
            errors.Add("A weekly calendar that excludes every day of the week leaves a trigger nothing to fire at.");
        }

        // Each day is said, because the type starts out excluding the weekend.
        WeeklyCalendar calendar = new() { CalendarBase = calendarBase };
        foreach (DayOfWeek day in CalendarReading.WeekDays)
        {
            if (WeeklyDays.Contains(day))
            {
                calendar.AddExcludedDay(day);
            }
            else
            {
                calendar.RemoveExcludedDay(day);
            }
        }

        return calendar;
    }

    private MonthlyCalendar BuildMonthly(List<string> errors, ICalendar? calendarBase)
    {
        if (MonthlyDays.Count == 31)
        {
            errors.Add("A monthly calendar that excludes every day of the month leaves a trigger nothing to fire at.");
        }

        MonthlyCalendar calendar = new() { CalendarBase = calendarBase };
        foreach (int day in MonthlyDays)
        {
            calendar.AddExcludedDay(day);
        }

        return calendar;
    }

    private DailyCalendar? BuildDaily(List<string> errors, ICalendar? calendarBase)
    {
        TimeOnly? start = ParseTime(DailyStart, "range start", errors);
        TimeOnly? end = ParseTime(DailyEnd, "range end", errors);
        if (start is null || end is null)
        {
            return null;
        }

        if (start.Value >= end.Value)
        {
            errors.Add("The range must start before it ends, and may not cross midnight.");
            return null;
        }

        return new DailyCalendar(start.Value, end.Value, calendarBase) { InvertTimeRange = DailyInvert };
    }

    private CronCalendar? BuildCron(List<string> errors, ICalendar? calendarBase, TimeZoneInfo? timeZone)
    {
        string expression = CronExpression.Trim();
        if (expression.Length == 0)
        {
            errors.Add("A cron calendar needs an expression: it excludes every second the expression matches.");
            return null;
        }

        try
        {
            return new CronCalendar(calendarBase, expression, timeZone ?? TimeZoneInfo.Utc);
        }
        catch (FormatException ex)
        {
            errors.Add("Invalid cron expression: " + ex.Message);
            return null;
        }
    }

    private static TimeOnly? ParseTime(string text, string field, List<string> errors)
    {
        string trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            errors.Add("A daily calendar needs a " + field + ".");
            return null;
        }

        if (TimeOnly.TryParseExact(trimmed, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time))
        {
            return time;
        }

        errors.Add("The " + field + " '" + trimmed + "' is not a time of day written as HH:mm, HH:mm:ss or HH:mm:ss.fff.");
        return null;
    }
}

/// <summary>
/// What <see cref="CalendarForm.Build" /> made of the fields: the calendar, or the reasons there is none.
/// </summary>
internal sealed record CalendarFormResult(ICalendar? Calendar, List<string> Errors);
