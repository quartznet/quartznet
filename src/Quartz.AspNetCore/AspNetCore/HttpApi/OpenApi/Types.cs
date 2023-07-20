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
    string CalendarType { get; }
    string? Description { get; }
    Calendar? CalendarBase { get; }

    /// <summary>
    /// Should be present when CalendarType is BaseCalendar, AnnualCalendar, CronCalendar, DailyCalendar, HolidayCalendar, MonthlyCalendar or WeeklyCalendar
    /// </summary>
    string? TimeZoneId { get; }

    /// <summary>
    /// Should be present when CalendarType is AnnualCalendar
    /// </summary>
    DateTime[]? DaysExcluded { get; }

    /// <summary>
    /// Should be present when CalendarType is CronCalendar
    /// </summary>
    string? CronExpressionString { get; }

    /// <summary>
    /// Should be present when CalendarType is DailyCalendar
    /// </summary>
    string? RangeStartingTime { get; }

    /// <summary>
    /// Should be present when CalendarType is DailyCalendar
    /// </summary>
    string? RangeEndingTime { get; }

    /// <summary>
    /// Should be present when CalendarType is DailyCalendar
    /// </summary>
    bool? InvertTimeRange { get; }

    /// <summary>
    /// Should be present when CalendarType is HolidayCalendar
    /// </summary>
    DateTime[]? ExcludedDates { get; }

    /// <summary>
    /// Should be present when CalendarType is MonthlyCalendar or WeeklyCalendar
    /// </summary>
    bool[]? ExcludedDays { get; }
}

internal interface Trigger
{
    /// <summary>
    /// Type of the trigger. Quartz.NET has built in trigger types CalendarIntervalTrigger, CronTrigger, DailyTimeIntervalTrigger and SimpleTrigger
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
    /// Should be present when TriggerType is CalendarIntervalTrigger, CronTrigger or DailyTimeIntervalTrigger
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
    /// Should be present when TriggerType is SimpleTrigger
    /// </summary>
    TimeSpan? RepeatIntervalTimeSpan { get; }
}

internal interface AddCalendarRequest
{
    string CalendarName { get; }
    Calendar Calendar { get; }
    bool Replace { get; }
    bool UpdateTriggers { get; }
}

internal interface CurrentlyExecutingJobDto
{
    HttpApiContract.JobDetailDto JobDetail { get; }
    Trigger Trigger { get; }
    Calendar? Calendar { get; }
    bool Recovering { get; }
    DateTimeOffset FireTime { get; }
    DateTimeOffset? ScheduledFireTime { get; }
    DateTimeOffset? PreviousFireTime { get; }
    DateTimeOffset? NextFireTime { get; }
}

internal interface ScheduleJobRequest
{
    Trigger Trigger { get; }
    HttpApiContract.JobDetailDto? Job { get; }
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