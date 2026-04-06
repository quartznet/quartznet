using System;
using System.Collections.Generic;

namespace Quartz;

/// <summary>
/// Represents a job definition from JSON configuration.
/// </summary>
internal sealed class JsonJobDefinition
{
    /// <summary>
    /// The job name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The job group. Defaults to the default group.
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// The assembly-qualified job type name.
    /// </summary>
    public string JobType { get; set; } = "";

    /// <summary>
    /// Optional job description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether the job should remain stored after it is orphaned (no triggers point to it). Defaults to <c>false</c>.
    /// </summary>
    public bool Durable { get; set; }

    /// <summary>
    /// Whether the job should be re-executed if a 'recovery' or 'fail-over' situation is encountered. Defaults to <c>false</c>.
    /// </summary>
    public bool Recover { get; set; }

    /// <summary>
    /// Optional key/value pairs to place in the job's <see cref="JobDataMap"/>.
    /// </summary>
    public Dictionary<string, string>? JobDataMap { get; set; }
}

/// <summary>
/// Represents a trigger definition from JSON configuration.
/// Exactly one schedule type (<see cref="Simple"/>, <see cref="Cron"/>, <see cref="CalendarInterval"/>, or <see cref="DailyTimeInterval"/>)
/// must be specified.
/// </summary>
internal sealed class JsonTriggerDefinition
{
    /// <summary>
    /// The trigger name.
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The trigger group. Defaults to the default group.
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// The name of the job this trigger is associated with.
    /// </summary>
    public string JobName { get; set; } = "";

    /// <summary>
    /// The group of the job this trigger is associated with.
    /// </summary>
    public string? JobGroup { get; set; }

    /// <summary>
    /// Optional trigger description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional trigger priority.
    /// </summary>
    public int? Priority { get; set; }

    /// <summary>
    /// Optional calendar name for this trigger.
    /// </summary>
    public string? CalendarName { get; set; }

    /// <summary>
    /// Optional execution group for this trigger.
    /// </summary>
    public string? ExecutionGroup { get; set; }

    /// <summary>
    /// Optional absolute start time (ISO 8601). Mutually exclusive with <see cref="StartTimeSecondsInFuture"/>.
    /// </summary>
    public DateTimeOffset? StartTime { get; set; }

    /// <summary>
    /// Optional start time expressed as seconds in the future from now. Mutually exclusive with <see cref="StartTime"/>.
    /// </summary>
    public int? StartTimeSecondsInFuture { get; set; }

    /// <summary>
    /// Optional end time (ISO 8601).
    /// </summary>
    public DateTimeOffset? EndTime { get; set; }

    /// <summary>
    /// Optional key/value pairs to place in the trigger's <see cref="JobDataMap"/>.
    /// </summary>
    public Dictionary<string, string>? JobDataMap { get; set; }

    /// <summary>
    /// Simple schedule configuration. Set this for a simple (repeat-based) trigger.
    /// </summary>
    public JsonSimpleSchedule? Simple { get; set; }

    /// <summary>
    /// Cron schedule configuration. Set this for a cron-based trigger.
    /// </summary>
    public JsonCronSchedule? Cron { get; set; }

    /// <summary>
    /// Calendar interval schedule configuration. Set this for a calendar-interval trigger.
    /// </summary>
    public JsonCalendarIntervalSchedule? CalendarInterval { get; set; }

    /// <summary>
    /// Daily time interval schedule configuration. Set this for a daily-time-interval trigger.
    /// </summary>
    public JsonDailyTimeIntervalSchedule? DailyTimeInterval { get; set; }
}

/// <summary>
/// Simple schedule configuration for a trigger.
/// </summary>
internal sealed class JsonSimpleSchedule
{
    /// <summary>
    /// Number of times to repeat. Use <c>-1</c> for indefinite. Defaults to <c>0</c> (fire once).
    /// </summary>
    public int RepeatCount { get; set; }

    /// <summary>
    /// Interval between firings as a TimeSpan string (e.g., "00:00:10" for 10 seconds).
    /// </summary>
    public string Interval { get; set; } = "00:00:00";

    /// <summary>
    /// Misfire instruction name (e.g., "SmartPolicy", "FireNow", "RescheduleNowWithExistingRepeatCount").
    /// </summary>
    public string? MisfireInstruction { get; set; }
}

/// <summary>
/// Cron schedule configuration for a trigger.
/// </summary>
internal sealed class JsonCronSchedule
{
    /// <summary>
    /// The cron expression (e.g., "0/10 * * * * ?").
    /// </summary>
    public string Expression { get; set; } = "";

    /// <summary>
    /// Optional time zone identifier (IANA or Windows).
    /// </summary>
    public string? TimeZone { get; set; }

    /// <summary>
    /// Misfire instruction name (e.g., "SmartPolicy", "DoNothing", "FireOnceNow").
    /// </summary>
    public string? MisfireInstruction { get; set; }
}

/// <summary>
/// Calendar interval schedule configuration for a trigger.
/// </summary>
internal sealed class JsonCalendarIntervalSchedule
{
    /// <summary>
    /// The repeat interval.
    /// </summary>
    public int RepeatInterval { get; set; }

    /// <summary>
    /// The interval unit (e.g., "Second", "Minute", "Hour", "Day", "Week", "Month", "Year").
    /// </summary>
    public string RepeatIntervalUnit { get; set; } = "Day";

    /// <summary>
    /// Misfire instruction name.
    /// </summary>
    public string? MisfireInstruction { get; set; }
}

/// <summary>
/// Daily time interval schedule configuration for a trigger.
/// </summary>
internal sealed class JsonDailyTimeIntervalSchedule
{
    /// <summary>
    /// The repeat interval.
    /// </summary>
    public int RepeatInterval { get; set; } = 1;

    /// <summary>
    /// The interval unit ("Second", "Minute", or "Hour").
    /// </summary>
    public string RepeatIntervalUnit { get; set; } = "Minute";

    /// <summary>
    /// Number of times to repeat. Use <c>-1</c> for indefinite.
    /// </summary>
    public int RepeatCount { get; set; } = -1;

    /// <summary>
    /// Start time of day in "HH:mm:ss" format.
    /// </summary>
    public string? StartTimeOfDay { get; set; }

    /// <summary>
    /// End time of day in "HH:mm:ss" format.
    /// </summary>
    public string? EndTimeOfDay { get; set; }

    /// <summary>
    /// Days of week (e.g., ["Monday", "Tuesday"]). If empty, all days are included.
    /// </summary>
    public List<string>? DaysOfWeek { get; set; }

    /// <summary>
    /// Optional time zone identifier.
    /// </summary>
    public string? TimeZone { get; set; }

    /// <summary>
    /// Misfire instruction name.
    /// </summary>
    public string? MisfireInstruction { get; set; }
}
