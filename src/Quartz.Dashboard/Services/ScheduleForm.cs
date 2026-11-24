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

using Quartz.Impl.Triggers;
using Quartz.Extensibility;

namespace Quartz.Dashboard.Services;

/// <summary>
/// The five kinds of schedule the dashboard edits and creates.
/// </summary>
internal enum ScheduleKind
{
    Cron,
    Simple,
    CalendarInterval,
    DailyTimeInterval,
    Recurrence,
}

/// <summary>
/// A field of <see cref="ScheduleForm" />, which an error is said beside.
/// </summary>
internal enum ScheduleField
{
    /// <summary>The schedule as a whole: one that never fires again, or one the trigger refused.</summary>
    Schedule,
    CronExpression,
    TimeZone,
    SimpleInterval,
    RepeatCount,
    CalendarInterval,
    DailyStart,
    DailyEnd,
    DailyDays,
    DailyInterval,
    DailyRepeatCount,
    RecurrenceRule,
}

/// <summary>
/// What is wrong with one field of a schedule.
/// </summary>
internal sealed record ScheduleFieldError(ScheduleField Field, string Message);

/// <summary>
/// The trigger a schedule form builds, or what is wrong with the form.
/// </summary>
/// <param name="Trigger">
/// The trigger with the form's schedule, its run state reset and its first fire computed; <see langword="null" />
/// while anything is wrong.
/// </param>
/// <param name="Errors">What is wrong, by field.</param>
internal sealed record ScheduleBuild(IOperableTrigger? Trigger, List<ScheduleFieldError> Errors);

/// <summary>
/// One schedule of any of the five kinds, as the text its inputs hold, and the one place it becomes the trigger
/// the dashboard posts (#4412).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Apply" /> clones the trigger it is given and changes only its schedule, as
/// <c>TriggerPayloadBuilder.TryWithCronExpression</c> did for cron alone: the calendar, the node pin, the data map
/// and the runtime type are kept, which is what #3294 lost when the page assembled triggers from what it showed.
/// A trigger of a type the form cannot rebuild faithfully is not offered one at all: <see cref="From" /> answers
/// <see langword="null" />.
/// </para>
/// <para>
/// A reschedule replaces the trigger, so what the old one had done is reset on purpose, as the confirm dialog says:
/// the previous fire time, the count of fires and a pending retry. The first fire is the schedule's first at or after
/// now, less what the calendar excludes, rather than its first from a start long gone, which every kind but cron would
/// otherwise store as a misfire. A simple trigger is the one measured from its start, so its start moves to that first
/// fire, and its repeat count is counted from there.
/// </para>
/// </remarks>
internal sealed class ScheduleForm
{
    /// <summary>Every kind, in the order the form offers them.</summary>
    public static IReadOnlyList<ScheduleKind> Kinds { get; } =
        [ScheduleKind.Cron, ScheduleKind.Simple, ScheduleKind.CalendarInterval, ScheduleKind.DailyTimeInterval, ScheduleKind.Recurrence];

    /// <summary>The week, from Monday, as the day boxes list it.</summary>
    public static IReadOnlyList<DayOfWeek> WeekDays { get; } =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    /// <summary>The units a simple trigger's interval is written in.</summary>
    public static IReadOnlyList<IntervalUnit> SimpleUnits { get; } = [IntervalUnit.Second, IntervalUnit.Minute, IntervalUnit.Hour, IntervalUnit.Day];

    /// <summary>The units a calendar interval trigger repeats in.</summary>
    public static IReadOnlyList<IntervalUnit> CalendarUnits { get; } =
        [IntervalUnit.Second, IntervalUnit.Minute, IntervalUnit.Hour, IntervalUnit.Day, IntervalUnit.Week, IntervalUnit.Month, IntervalUnit.Year];

    /// <summary>The units a daily time interval trigger repeats in within its window.</summary>
    public static IReadOnlyList<IntervalUnit> DailyUnits { get; } = [IntervalUnit.Second, IntervalUnit.Minute, IntervalUnit.Hour];

    private ScheduleForm(ScheduleKind kind)
    {
        Kind = kind;
    }

    public ScheduleKind Kind { get; set; }

    public string CronExpression { get; set; } = "0 0 * * * ?";

    /// <summary>The zone the schedule is read in, by its id: every kind but simple has one.</summary>
    public string TimeZoneId { get; set; } = TimeZoneInfo.Utc.Id;

    public string SimpleInterval { get; set; } = "15";

    public IntervalUnit SimpleUnit { get; set; } = IntervalUnit.Minute;

    public bool RepeatForever { get; set; } = true;

    /// <summary>How many times a simple trigger repeats after its first fire, while it does not repeat forever.</summary>
    public string RepeatCount { get; set; } = "0";

    public bool MeasureIntervalFromCompletion { get; set; }

    public string CalendarInterval { get; set; } = "1";

    public IntervalUnit CalendarUnit { get; set; } = IntervalUnit.Day;

    public bool PreserveHourOfDayAcrossDaylightSavings { get; set; }

    public bool SkipDayIfHourDoesNotExist { get; set; }

    /// <summary>The window's start as a time input holds it: <c>HH:mm</c> or <c>HH:mm:ss</c>.</summary>
    public string DailyStart { get; set; } = "09:00:00";

    public string DailyEnd { get; set; } = "17:00:00";

    public HashSet<DayOfWeek> DailyDays { get; } = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    public string DailyInterval { get; set; } = "1";

    public IntervalUnit DailyUnit { get; set; } = IntervalUnit.Hour;

    /// <summary>How many times the trigger repeats each day after its first fire; blank is until the window ends.</summary>
    public string DailyRepeatCount { get; set; } = string.Empty;

    public string RecurrenceRule { get; set; } = "FREQ=WEEKLY;BYDAY=MO;BYHOUR=9;BYMINUTE=0;BYSECOND=0";

    /// <summary>
    /// The kind of schedule <paramref name="trigger" /> has, when the form can rebuild it faithfully: its type is
    /// one of the five built-in ones, or derives from one, so its clone is itself.
    /// </summary>
    public static ScheduleKind? KindOf(ITrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        return trigger switch
        {
            CronTriggerImpl => ScheduleKind.Cron,
            SimpleTriggerImpl => ScheduleKind.Simple,
            CalendarIntervalTriggerImpl => ScheduleKind.CalendarInterval,
            DailyTimeIntervalTriggerImpl => ScheduleKind.DailyTimeInterval,
            RecurrenceTriggerImpl => ScheduleKind.Recurrence,
            _ => null,
        };
    }

    /// <summary>The kind in words, as the kind select and the editor's heading say it.</summary>
    public static string Describe(ScheduleKind kind) => kind switch
    {
        ScheduleKind.Cron => "Cron",
        ScheduleKind.Simple => "Simple",
        ScheduleKind.CalendarInterval => "Calendar interval",
        ScheduleKind.DailyTimeInterval => "Daily time interval",
        _ => "Recurrence (RRULE)",
    };

    /// <summary>
    /// A form holding <paramref name="trigger" />'s schedule, or <see langword="null" /> for a trigger the form cannot
    /// rebuild faithfully.
    /// </summary>
    public static ScheduleForm? From(ITrigger trigger)
    {
        if (KindOf(trigger) is not { } kind)
        {
            return null;
        }

        ScheduleForm form = new(kind);
        switch (trigger)
        {
            case CronTriggerImpl cron:
                form.CronExpression = cron.CronExpressionString ?? string.Empty;
                form.TimeZoneId = cron.TimeZone.Id;
                break;

            case SimpleTriggerImpl simple:
                (form.SimpleInterval, form.SimpleUnit) = Split(simple.RepeatInterval);
                form.RepeatForever = simple.RepeatCount == SimpleTriggerImpl.RepeatIndefinitely;
                form.RepeatCount = form.RepeatForever ? "0" : Number(simple.RepeatCount);
                form.MeasureIntervalFromCompletion = simple.MeasureIntervalFromCompletion;
                break;

            case CalendarIntervalTriggerImpl calendar:
                form.CalendarInterval = Number(calendar.RepeatInterval);
                form.CalendarUnit = calendar.RepeatIntervalUnit;
                form.TimeZoneId = calendar.TimeZone.Id;
                form.PreserveHourOfDayAcrossDaylightSavings = calendar.PreserveHourOfDayAcrossDaylightSavings;
                form.SkipDayIfHourDoesNotExist = calendar.SkipDayIfHourDoesNotExist;
                break;

            case DailyTimeIntervalTriggerImpl daily:
                form.DailyStart = daily.StartTimeOfDay.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                form.DailyEnd = daily.EndTimeOfDay.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                form.DailyDays.Clear();
                form.DailyDays.UnionWith(daily.DaysOfWeek);
                form.DailyInterval = Number(daily.RepeatInterval);
                form.DailyUnit = daily.RepeatIntervalUnit;
                form.DailyRepeatCount = daily.RepeatCount == DailyTimeIntervalTriggerImpl.RepeatIndefinitely ? string.Empty : Number(daily.RepeatCount);
                form.TimeZoneId = daily.TimeZone.Id;
                break;

            case RecurrenceTriggerImpl recurrence:
                form.RecurrenceRule = recurrence.RecurrenceRule;
                form.TimeZoneId = recurrence.TimeZone.Id;
                break;
        }

        return form;
    }

    /// <summary>
    /// A form for a new trigger of <paramref name="kind" />, read in <paramref name="timeZoneId" />.
    /// </summary>
    public static ScheduleForm ForNew(ScheduleKind kind, string timeZoneId)
    {
        return new ScheduleForm(kind) { TimeZoneId = timeZoneId };
    }

    /// <summary>
    /// A new trigger of <paramref name="kind" /> with nothing set but its defaults, for <see cref="Apply" /> to give
    /// a schedule to.
    /// </summary>
    public static IOperableTrigger NewTrigger(ScheduleKind kind) => kind switch
    {
        ScheduleKind.Cron => new CronTriggerImpl(),
        ScheduleKind.Simple => new SimpleTriggerImpl(),
        ScheduleKind.CalendarInterval => new CalendarIntervalTriggerImpl(),
        ScheduleKind.DailyTimeInterval => new DailyTimeIntervalTriggerImpl(),
        _ => new RecurrenceTriggerImpl(),
    };

    /// <summary>Whether the kind is read in a time zone: every kind but simple.</summary>
    public bool HasTimeZone => Kind != ScheduleKind.Simple;

    /// <summary>
    /// Whether <paramref name="other" /> holds the same schedule: the fields of this form's kind, compared as typed.
    /// </summary>
    public bool SameScheduleAs(ScheduleForm other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (Kind != other.Kind || (HasTimeZone && !Same(TimeZoneId, other.TimeZoneId)))
        {
            return false;
        }

        return Kind switch
        {
            ScheduleKind.Cron => Same(CronExpression, other.CronExpression),
            ScheduleKind.Simple => Same(SimpleInterval, other.SimpleInterval)
                                   && SimpleUnit == other.SimpleUnit
                                   && RepeatForever == other.RepeatForever
                                   && (RepeatForever || Same(RepeatCount, other.RepeatCount))
                                   && MeasureIntervalFromCompletion == other.MeasureIntervalFromCompletion,
            ScheduleKind.CalendarInterval => Same(CalendarInterval, other.CalendarInterval)
                                             && CalendarUnit == other.CalendarUnit
                                             && PreserveHourOfDayAcrossDaylightSavings == other.PreserveHourOfDayAcrossDaylightSavings
                                             && SkipDayIfHourDoesNotExist == other.SkipDayIfHourDoesNotExist,
            ScheduleKind.DailyTimeInterval => Same(Seconds(DailyStart), Seconds(other.DailyStart))
                                              && Same(Seconds(DailyEnd), Seconds(other.DailyEnd))
                                              && DailyDays.SetEquals(other.DailyDays)
                                              && Same(DailyInterval, other.DailyInterval)
                                              && DailyUnit == other.DailyUnit
                                              && Same(DailyRepeatCount, other.DailyRepeatCount),
            _ => Same(RecurrenceRule, other.RecurrenceRule),
        };
    }

    /// <summary>
    /// The zone <see cref="TimeZoneId" /> names, or <see langword="null" /> while it names none this process knows.
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
    /// <paramref name="template" /> with this form's schedule: a clone, its run state reset and its first fire the
    /// schedule's first at or after now, less what <paramref name="calendar" /> excludes.
    /// </summary>
    /// <param name="template">
    /// The trigger being rescheduled, or a new one of the form's kind (<see cref="NewTrigger" />) carrying the rest of
    /// what is to be stored.
    /// </param>
    /// <param name="calendar">The calendar the trigger names, as read, or <see langword="null" />.</param>
    /// <param name="clock">What now is.</param>
    /// <param name="startAtFirstFire">
    /// Whether the trigger starts at its first fire whatever its kind, as a new one must: <c>ScheduleJob</c> counts
    /// the first fire from the start, so a start behind now would be stored as a misfire. A reschedule keeps the
    /// start, which a calendar interval or a recurrence counts its phase from; <c>RescheduleJob</c> keeps the first fire
    /// it is given.
    /// </param>
    public ScheduleBuild Apply(ITrigger template, ICalendar? calendar, TimeProvider clock, bool startAtFirstFire = false)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(clock);

        List<ScheduleFieldError> errors = [];
        if (KindOf(template) != Kind || template is not IOperableTrigger operable)
        {
            errors.Add(new ScheduleFieldError(ScheduleField.Schedule, "A " + TriggerDisplay.TypeName(template) + " trigger cannot be given a " + Describe(Kind).ToLowerInvariant() + " schedule here."));
            return new ScheduleBuild(null, errors);
        }

        TimeZoneInfo? timeZone = null;
        if (HasTimeZone)
        {
            timeZone = TryResolveTimeZone();
            if (timeZone is null)
            {
                errors.Add(new ScheduleFieldError(ScheduleField.TimeZone, TimeZoneId.Trim().Length == 0
                    ? "A time zone is needed: the schedule's times are read in it."
                    : "Time zone '" + TimeZoneId.Trim() + "' is not one this dashboard knows."));
            }
        }

        IOperableTrigger copy = (IOperableTrigger) operable.Clone();
        switch (copy)
        {
            case CronTriggerImpl cron:
                ApplyCron(cron, errors);
                break;
            case SimpleTriggerImpl simple:
                ApplySimple(simple, errors);
                break;
            case CalendarIntervalTriggerImpl calendarInterval:
                ApplyCalendarInterval(calendarInterval, errors);
                break;
            case DailyTimeIntervalTriggerImpl daily:
                ApplyDaily(daily, errors);
                break;
            case RecurrenceTriggerImpl recurrence:
                ApplyRecurrence(recurrence, errors);
                break;
        }

        if (errors.Count > 0)
        {
            return new ScheduleBuild(null, errors);
        }

        if (timeZone is not null)
        {
            SetTimeZone(copy, timeZone);
        }

        // A reschedule is a replacement, so what the old trigger had done goes with it: the confirm dialog says so.
        copy.PreviousFireTimeUtc = null;
        copy.NextFireTimeUtc = null;
        ResetTimesTriggered(copy);
        if (copy is TriggerBase triggerBase)
        {
            triggerBase.RetryAttempt = 0;
        }

        try
        {
            copy.Validate();
        }
        catch (SchedulerException ex)
        {
            errors.Add(new ScheduleFieldError(copy is RecurrenceTriggerImpl ? ScheduleField.RecurrenceRule : ScheduleField.Schedule, ex.Message));
            return new ScheduleBuild(null, errors);
        }

        DateTimeOffset? first;
        try
        {
            first = FirstFireFrom(copy, calendar, clock);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        {
            errors.Add(new ScheduleFieldError(ScheduleField.Schedule, ex.Message));
            return new ScheduleBuild(null, errors);
        }

        if (first is not { } firstFire)
        {
            errors.Add(new ScheduleFieldError(ScheduleField.Schedule, calendar is null
                ? "This schedule does not fire again after now."
                : "This schedule does not fire again after now, less what its calendar excludes."));
            return new ScheduleBuild(null, errors);
        }

        // Measured from its start, so it starts at its first fire: otherwise the scheduler moves a start that is
        // behind it up to the moment it stores the trigger, and every fire after the first moves with it.
        if (copy is SimpleTriggerImpl || startAtFirstFire)
        {
            copy.StartTimeUtc = firstFire;
        }

        copy.NextFireTimeUtc = firstFire;
        return new ScheduleBuild(copy, errors);
    }

    /// <summary>
    /// The first fire of <paramref name="trigger" />'s schedule that is not behind now: its first
    /// from its start when that is still ahead, else its first after now, less what the calendar excludes.
    /// </summary>
    /// <remarks>
    /// Cron already counts from now when its start is behind it. The other kinds count from their start, and a start
    /// long gone would be stored as a fire due long ago: a misfire the moment the trigger is saved.
    /// </remarks>
    internal static DateTimeOffset? FirstFireFrom(IOperableTrigger trigger, ICalendar? calendar, TimeProvider clock)
    {
        // A clone is on the system clock whatever the trigger was on, and cron reads now off it.
        IOperableTrigger probe = (IOperableTrigger) trigger.Clone();
        if (probe is TriggerBase triggerBase)
        {
            triggerBase.TimeProvider = clock;
        }

        DateTimeOffset now = clock.GetUtcNow();
        DateTimeOffset? first = probe.ComputeFirstFireTimeUtc(calendar);
        if (first is not { } fire || fire >= now)
        {
            return first;
        }

        DateTimeOffset? next = probe.GetFireTimeAfter(now);

        // Bounded, as the store's own walk past a calendar is by the trigger's end: a calendar that excludes every
        // second leaves the schedule without a fire rather than the page without an answer.
        for (int skipped = 0; next is { } candidate && calendar is not null && !calendar.IsTimeIncluded(candidate); skipped++)
        {
            if (skipped >= 10_000)
            {
                return null;
            }

            next = probe.GetFireTimeAfter(candidate);
        }

        return next;
    }

    private void ApplyCron(CronTriggerImpl cron, List<ScheduleFieldError> errors)
    {
        string expression = CronExpression.Trim();
        if (expression.Length == 0)
        {
            errors.Add(new ScheduleFieldError(ScheduleField.CronExpression, "A cron expression is needed."));
            return;
        }

        try
        {
            cron.CronExpressionString = expression;
        }
        catch (FormatException ex)
        {
            errors.Add(new ScheduleFieldError(ScheduleField.CronExpression, "Invalid cron expression: " + ex.Message));
        }
    }

    private void ApplySimple(SimpleTriggerImpl simple, List<ScheduleFieldError> errors)
    {
        int repeatCount = SimpleTriggerImpl.RepeatIndefinitely;
        if (!RepeatForever)
        {
            if (!int.TryParse(RepeatCount.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out repeatCount) || repeatCount < 0)
            {
                errors.Add(new ScheduleFieldError(ScheduleField.RepeatCount, "The repeat count is a whole number, 0 or more: how many times it fires after the first."));
                repeatCount = 0;
            }
        }

        TimeSpan? interval = ParseInterval(SimpleInterval, SimpleUnit);
        if (interval is null)
        {
            errors.Add(new ScheduleFieldError(ScheduleField.SimpleInterval, "The interval is a number, 0 or more."));
        }
        else if (interval <= TimeSpan.Zero && repeatCount != 0)
        {
            errors.Add(new ScheduleFieldError(ScheduleField.SimpleInterval, "A trigger that repeats needs an interval of more than 0."));
        }

        if (errors.Count > 0 || interval is not { } repeatInterval)
        {
            return;
        }

        simple.RepeatInterval = repeatInterval;
        simple.RepeatCount = repeatCount;
        simple.MeasureIntervalFromCompletion = MeasureIntervalFromCompletion;
    }

    private void ApplyCalendarInterval(CalendarIntervalTriggerImpl trigger, List<ScheduleFieldError> errors)
    {
        if (!int.TryParse(CalendarInterval.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int interval) || interval < 1)
        {
            errors.Add(new ScheduleFieldError(ScheduleField.CalendarInterval, "The interval is a whole number, 1 or more."));
            return;
        }

        trigger.RepeatInterval = interval;
        trigger.RepeatIntervalUnit = CalendarUnit;
        trigger.PreserveHourOfDayAcrossDaylightSavings = PreserveHourOfDayAcrossDaylightSavings;
        trigger.SkipDayIfHourDoesNotExist = PreserveHourOfDayAcrossDaylightSavings && SkipDayIfHourDoesNotExist;
    }

    private void ApplyDaily(DailyTimeIntervalTriggerImpl trigger, List<ScheduleFieldError> errors)
    {
        TimeOnly? start = ParseTime(DailyStart);
        TimeOnly? end = ParseTime(DailyEnd);
        if (start is null)
        {
            errors.Add(new ScheduleFieldError(ScheduleField.DailyStart, "The window's start is a time of day, as 09:00."));
        }

        if (end is null)
        {
            errors.Add(new ScheduleFieldError(ScheduleField.DailyEnd, "The window's end is a time of day, as 17:00."));
        }
        else if (start is { } from && end < from)
        {
            errors.Add(new ScheduleFieldError(ScheduleField.DailyEnd, "The window ends before it starts."));
        }

        if (DailyDays.Count == 0)
        {
            errors.Add(new ScheduleFieldError(ScheduleField.DailyDays, "Tick at least one day: a trigger with none never fires."));
        }

        if (!int.TryParse(DailyInterval.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int interval) || interval < 1)
        {
            errors.Add(new ScheduleFieldError(ScheduleField.DailyInterval, "The interval is a whole number, 1 or more."));
        }
        else if (TimeSpanOf(interval, DailyUnit) > TimeSpan.FromHours(24))
        {
            errors.Add(new ScheduleFieldError(ScheduleField.DailyInterval, "The interval cannot be more than 24 hours."));
        }

        int repeatCount = DailyTimeIntervalTriggerImpl.RepeatIndefinitely;
        if (DailyRepeatCount.Trim().Length > 0
            && (!int.TryParse(DailyRepeatCount.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out repeatCount) || repeatCount < 0))
        {
            errors.Add(new ScheduleFieldError(ScheduleField.DailyRepeatCount, "The repeat count is a whole number, 0 or more, or blank to repeat until the window ends."));
        }

        if (errors.Count > 0 || start is not { } startOfDay || end is not { } endOfDay)
        {
            return;
        }

        // The end first when it moves later, the start first when it moves earlier: each setter refuses a window that
        // would end before it starts on the way.
        if (endOfDay >= trigger.StartTimeOfDay)
        {
            trigger.EndTimeOfDay = endOfDay;
            trigger.StartTimeOfDay = startOfDay;
        }
        else
        {
            trigger.StartTimeOfDay = startOfDay;
            trigger.EndTimeOfDay = endOfDay;
        }

        trigger.DaysOfWeek = DailyDays.ToList();
        trigger.RepeatIntervalUnit = DailyUnit;
        trigger.RepeatInterval = interval;
        trigger.RepeatCount = repeatCount;
    }

    private void ApplyRecurrence(RecurrenceTriggerImpl trigger, List<ScheduleFieldError> errors)
    {
        string rule = RecurrenceRule.Trim();
        if (rule.Length == 0)
        {
            errors.Add(new ScheduleFieldError(ScheduleField.RecurrenceRule, "A recurrence rule is needed, as FREQ=WEEKLY;BYDAY=MO."));
            return;
        }

        trigger.RecurrenceRule = rule;
    }

    private static void SetTimeZone(IOperableTrigger trigger, TimeZoneInfo timeZone)
    {
        switch (trigger)
        {
            case CronTriggerImpl cron:
                cron.TimeZone = timeZone;
                break;
            case CalendarIntervalTriggerImpl calendar:
                calendar.TimeZone = timeZone;
                break;
            case DailyTimeIntervalTriggerImpl daily:
                daily.TimeZone = timeZone;
                break;
            case RecurrenceTriggerImpl recurrence:
                recurrence.TimeZone = timeZone;
                break;
        }
    }

    private static void ResetTimesTriggered(IOperableTrigger trigger)
    {
        switch (trigger)
        {
            case SimpleTriggerImpl simple:
                simple.TimesTriggered = 0;
                break;
            case CalendarIntervalTriggerImpl calendar:
                calendar.TimesTriggered = 0;
                break;
            case DailyTimeIntervalTriggerImpl daily:
                daily.TimesTriggered = 0;
                break;
            case RecurrenceTriggerImpl recurrence:
                recurrence.TimesTriggered = 0;
                break;
        }
    }

    /// <summary>
    /// An interval as the largest unit it is a whole number of, so 90 minutes is <c>90</c> minutes and two hours
    /// is <c>2</c> hours.
    /// </summary>
    private static (string Value, IntervalUnit Unit) Split(TimeSpan interval)
    {
        if (interval > TimeSpan.Zero)
        {
            if (interval.Ticks % TimeSpan.TicksPerDay == 0)
            {
                return (Number(interval.Ticks / TimeSpan.TicksPerDay), IntervalUnit.Day);
            }

            if (interval.Ticks % TimeSpan.TicksPerHour == 0)
            {
                return (Number(interval.Ticks / TimeSpan.TicksPerHour), IntervalUnit.Hour);
            }

            if (interval.Ticks % TimeSpan.TicksPerMinute == 0)
            {
                return (Number(interval.Ticks / TimeSpan.TicksPerMinute), IntervalUnit.Minute);
            }
        }

        return (interval.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture), IntervalUnit.Second);
    }

    private static TimeSpan? ParseInterval(string text, IntervalUnit unit)
    {
        if (!decimal.TryParse(text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal value) || value < 0)
        {
            return null;
        }

        try
        {
            decimal seconds = value * unit switch
            {
                IntervalUnit.Minute => 60m,
                IntervalUnit.Hour => 3600m,
                IntervalUnit.Day => 86400m,
                _ => 1m,
            };
            return TimeSpan.FromTicks((long) decimal.Round(seconds * TimeSpan.TicksPerSecond));
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static TimeSpan TimeSpanOf(int interval, IntervalUnit unit) => unit switch
    {
        IntervalUnit.Hour => TimeSpan.FromHours(interval),
        IntervalUnit.Minute => TimeSpan.FromMinutes(interval),
        _ => TimeSpan.FromSeconds(interval),
    };

    /// <summary>A time input's value, <c>HH:mm</c> or <c>HH:mm:ss</c>, to the whole second the trigger keeps.</summary>
    private static TimeOnly? ParseTime(string text)
    {
        string[] formats = ["HH:mm", "HH:mm:ss"];
        return TimeOnly.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time) ? time : null;
    }

    private static string Seconds(string time) => ParseTime(time)?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? time.Trim();

    private static bool Same(string left, string right) => string.Equals(left.Trim(), right.Trim(), StringComparison.Ordinal);

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
}
