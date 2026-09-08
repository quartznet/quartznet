// ReSharper disable InconsistentNaming

namespace Quartz.AspNetCore.HttpApi.OpenApi;

// We use ICalendar & ITrigger when handling calendars and triggers in the Web API. Because of this, OpenAPI document for calendars and
// triggers is lacking. Here we have Calendar & Trigger interfaces which are only used with OpenAPI attributes (and should be!). Because
// some requests and DTOs also use ICalendar and ITrigger, we have copy of those also here using the OpenAPI specific calendar & trigger types.
internal interface Calendar
{
    /// <summary>
    /// Type of the calendar. Quartz.NET has built in calendar types BaseCalendar, AnnualCalendar, CronCalendar, DailyCalendar, HolidayCalendar, MonthlyCalendar and WeeklyCalendar
    /// </summary>
    string Type { get; }
    string? Description { get; }

    /// <summary>
    /// Should be present when Type is BaseCalendar, AnnualCalendar, CronCalendar, DailyCalendar, HolidayCalendar, MonthlyCalendar or WeeklyCalendar
    /// </summary>
    string? TimeZoneId { get; }

    /// <summary>
    /// The calendar this one is layered on top of, or null when there is none. Nested to any depth,
    /// each level having the same shape as this one.
    /// </summary>
    Calendar? BaseCalendar { get; }

    /// <summary>
    /// Should be present when Type is AnnualCalendar (dates as yyyy-MM-dd, only the month
    /// and day are significant), MonthlyCalendar (days of the month, 1 through 31) or
    /// WeeklyCalendar (day names)
    /// </summary>
    object[]? ExcludedDays { get; }

    /// <summary>
    /// Should be present when Type is CronCalendar
    /// </summary>
    string? CronExpressionString { get; }

    /// <summary>
    /// Should be present when Type is DailyCalendar, as HH:mm:ss.fff
    /// </summary>
    string? RangeStart { get; }

    /// <summary>
    /// Should be present when Type is DailyCalendar, as HH:mm:ss.fff
    /// </summary>
    string? RangeEnd { get; }

    /// <summary>
    /// Should be present when Type is DailyCalendar
    /// </summary>
    bool? InvertTimeRange { get; }

    /// <summary>
    /// Should be present when Type is HolidayCalendar
    /// </summary>
    DateOnly[]? ExcludedDates { get; }
}

/// <summary>
/// A time of day, carried as its separate parts rather than as a string.
/// </summary>
internal interface TimeOfDay
{
    int Hour { get; }
    int Minute { get; }
    int Second { get; }
}

internal interface Trigger
{
    /// <summary>
    /// Type of the trigger. Quartz.NET has built in trigger types CalendarIntervalTrigger, CronTrigger, DailyTimeIntervalTrigger, RecurrenceTrigger and SimpleTrigger
    /// </summary>
    string TriggerType { get; }

    HttpApiContract.KeyDto Key { get; }
    HttpApiContract.KeyDto? JobKey { get; }
    string? Description { get; }
    string? CalendarName { get; }
    JobDataMap JobDataMap { get; }
    int MisfireInstruction { get; }
    DateTimeOffset StartTimeUtc { get; }
    DateTimeOffset? EndTimeUtc { get; }
    int Priority { get; }

    /// <summary>
    /// When the trigger is next due to fire, or null when it will not fire again. Read-only: a value
    /// sent when scheduling or rescheduling is ignored, because the scheduler computes the fire times.
    /// </summary>
    DateTimeOffset? NextFireTimeUtc { get; }

    /// <summary>
    /// When the trigger last fired, or null when it has not fired yet. Read-only, like NextFireTimeUtc.
    /// </summary>
    DateTimeOffset? PreviousFireTimeUtc { get; }

    /// <summary>
    /// The execution group whose per-node thread limit this trigger's job counts against, or null when
    /// it belongs to none
    /// </summary>
    string? ExecutionGroup { get; }

    /// <summary>
    /// The node this trigger is pinned to, null when it is not pinned, or the auto sentinel when it is
    /// pinned automatically and has not yet been claimed. Read together with PreferredNodeAuto: a claimed
    /// automatic pin carries a node name too, and only an automatic pin is released when its node stops
    /// checking in.
    /// </summary>
    string? PreferredNode { get; }

    /// <summary>
    /// Whether this trigger's pin was requested automatically rather than naming a node
    /// </summary>
    bool PreferredNodeAuto { get; }

    /// <summary>
    /// How the scheduler re-fires this trigger when its job fails, in the stored form the RETRY_POLICY
    /// column carries — for example "fixed;3;00:00:30", "exp;5;00:00:10;2;00:10:00" or
    /// "list;00:00:01;00:00:05". Null when the trigger does not retry
    /// </summary>
    string? RetryPolicy { get; }

    /// <summary>
    /// How many times the occurrence currently being executed has already been retried; 0 when it has
    /// not. Read-only: the scheduler advances and clears it, and a value sent when scheduling is ignored
    /// </summary>
    int RetryAttempt { get; }

    /// <summary>
    /// Should be present when TriggerType is CalendarIntervalTrigger, CronTrigger, DailyTimeIntervalTrigger or RecurrenceTrigger
    /// </summary>
    string? TimeZone { get; }

    /// <summary>
    /// Should be present when TriggerType is CalendarIntervalTrigger or DailyTimeIntervalTrigger
    /// </summary>
    int? RepeatInterval { get; }

    /// <summary>
    /// Should be present when TriggerType is CalendarIntervalTrigger or DailyTimeIntervalTrigger
    /// </summary>
    IntervalUnit? RepeatIntervalUnit { get; }

    /// <summary>
    /// Should be present when TriggerType is CalendarIntervalTrigger
    /// </summary>
    bool? PreserveHourOfDayAcrossDaylightSavings { get; }

    /// <summary>
    /// Should be present when TriggerType is CalendarIntervalTrigger
    /// </summary>
    bool? SkipDayIfHourDoesNotExist { get; }

    /// <summary>
    /// Should be present when TriggerType is CronTrigger
    /// </summary>
    string? CronExpressionString { get; }

    /// <summary>
    /// Should be present when TriggerType is DailyTimeIntervalTrigger or SimpleTrigger
    /// </summary>
    int? RepeatCount { get; }

    /// <summary>
    /// Should be present when TriggerType is DailyTimeIntervalTrigger
    /// </summary>
    TimeOfDay? StartTimeOfDay { get; }

    /// <summary>
    /// Should be present when TriggerType is DailyTimeIntervalTrigger
    /// </summary>
    TimeOfDay? EndTimeOfDay { get; }

    /// <summary>
    /// Should be present when TriggerType is DailyTimeIntervalTrigger
    /// </summary>
    DayOfWeek[]? DaysOfWeek { get; }

    /// <summary>
    /// Should be present when TriggerType is RecurrenceTrigger, as an RFC 5545 RRULE
    /// </summary>
    string? RecurrenceRule { get; }

    /// <summary>
    /// Should be present when TriggerType is SimpleTrigger
    /// </summary>
    TimeSpan? RepeatIntervalTimeSpan { get; }

    /// <summary>
    /// How many times the trigger has already fired. Should be present when TriggerType is
    /// CalendarIntervalTrigger, DailyTimeIntervalTrigger, RecurrenceTrigger or SimpleTrigger — CronTrigger
    /// does not count its fires. Defaults to 0 when omitted from a trigger being scheduled.
    /// </summary>
    int? TimesTriggered { get; }
}

internal interface AddCalendarRequest
{
    string CalendarName { get; }
    Calendar Calendar { get; }
    bool Replace { get; }
    bool UpdateTriggers { get; }
}

internal interface ScheduleJobRequest
{
    Trigger Trigger { get; }
    HttpApiContract.JobDetailDto? Job { get; }
    bool Replace { get; }
}

internal interface ScheduleJobsRequest
{
    ScheduleJobsRequestItem[] JobsAndTriggers { get; }
    bool Replace { get; }
}

internal interface ScheduleJobsRequestItem
{
    HttpApiContract.JobDetailDto Job { get; }
    Trigger[] Triggers { get; }
}

internal interface RescheduleJobRequest
{
    Trigger NewTrigger { get; }
}

/// <summary>
/// The trigger-details patch. Every member is optional, and the two ways of leaving one out mean
/// different things: a member that is <b>absent</b> leaves the trigger's value alone, and one present as
/// <c>null</c> <b>clears</b> it. The schema cannot say that — an OpenAPI optional-and-nullable property
/// is one thing — so it is said here and in the endpoint's own description.
/// </summary>
internal interface UpdateTriggerDetailsRequest
{
    /// <summary>
    /// The description to set, or null to clear it
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// The priority to set
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// The job data map to set, or null to empty it
    /// </summary>
    JobDataMap? JobDataMap { get; }

    /// <summary>
    /// The calendar to associate the trigger with, or null to disassociate it
    /// </summary>
    string? CalendarName { get; }

    /// <summary>
    /// The misfire instruction code to set, as the trigger body's misfireInstruction carries it
    /// </summary>
    int MisfireInstruction { get; }

    /// <summary>
    /// The schedule family misfireInstruction is stated in — Simple, Cron, CalendarInterval,
    /// DailyTimeInterval or Recurrence. The same number means a different policy in each, so naming one
    /// is what lets the scheduler refuse an update aimed at a trigger of another family. Omit it to set
    /// the code without that check, which is the only way to set one on a trigger type of your own
    /// </summary>
    string? MisfireInstructionFamily { get; }

    /// <summary>
    /// The node pin as the triggers table holds it: null to clear it, "*" to ask for an automatic pin,
    /// or a scheduler instance id. Sent together with preferredNodeAuto
    /// </summary>
    string? PreferredNode { get; }

    /// <summary>
    /// Whether the pin was handed out automatically rather than named, as the triggers table holds it
    /// </summary>
    bool PreferredNodeAuto { get; }

    /// <summary>
    /// The execution group whose thread limit this trigger's job counts against, or null to leave every
    /// group
    /// </summary>
    string? ExecutionGroup { get; }

    /// <summary>
    /// How the scheduler re-fires the trigger when its job fails, in the stored form the RETRY_POLICY
    /// column carries — for example "fixed;3;00:00:30" — or null to stop retrying
    /// </summary>
    string? RetryPolicy { get; }
}