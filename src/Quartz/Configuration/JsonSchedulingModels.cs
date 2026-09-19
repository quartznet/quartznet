namespace Quartz.Configuration;

internal sealed class JsonJobDefinition
{
    public string Name { get; set; } = "";
    public string? Group { get; set; }
    public string JobType { get; set; } = "";
    public string? Description { get; set; }
    public bool Durable { get; set; }
    public bool Recover { get; set; }
    public Dictionary<string, string>? JobDataMap { get; set; }
}

internal sealed class JsonTriggerDefinition
{
    public string Name { get; set; } = "";
    public string? Group { get; set; }
    public string JobName { get; set; } = "";
    public string? JobGroup { get; set; }
    public string? Description { get; set; }
    public int? Priority { get; set; }
    public string? CalendarName { get; set; }
    public string? ExecutionGroup { get; set; }

    /// <summary>
    /// The trigger's retry policy in its stored form, for example <c>fixed;3;00:00:30</c>. Spelled as
    /// the stored string rather than as an object, so that the configuration, the column and a
    /// serialized trigger all say the same thing.
    /// </summary>
    public string? RetryPolicy { get; set; }

    /// <summary>
    /// The cluster node the trigger prefers: a scheduler instance id, <c>*</c> for an automatic pin, or
    /// absent for no preference. Spelled as the stored string for the same reason
    /// <see cref="RetryPolicy" /> is.
    /// </summary>
    public string? PreferredNode { get; set; }

    /// <summary>
    /// The trigger whose firing this one waits for, or absent when it waits for nothing. Named rather
    /// than resolved, so the parent may be declared later in the same document or be in the store
    /// already.
    /// </summary>
    public JsonTriggerKey? ContinuesAfter { get; set; }

    /// <summary>
    /// Which outcomes of that firing release the wait: <c>OnSuccess</c>, <c>OnFailure</c>,
    /// <c>OnCancellation</c>, <c>OnVeto</c> or <c>OnAnyOutcome</c>, joined with <c>|</c> for more than
    /// one. Absent means <c>OnSuccess</c>.
    /// </summary>
    public string? ContinuationCondition { get; set; }

    public DateTimeOffset? StartTime { get; set; }
    public int? StartTimeSecondsInFuture { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public Dictionary<string, string>? JobDataMap { get; set; }

    public JsonSimpleSchedule? Simple { get; set; }
    public JsonCronSchedule? Cron { get; set; }
    public JsonCalendarIntervalSchedule? CalendarInterval { get; set; }
    public JsonDailyTimeIntervalSchedule? DailyTimeInterval { get; set; }
    public JsonRecurrenceSchedule? Recurrence { get; set; }
}

/// <summary>
/// Another trigger, named by key. The same two members a delete command names one by, because that is
/// how this format has always referred to something it is not defining.
/// </summary>
internal sealed class JsonTriggerKey
{
    public string Name { get; set; } = "";
    public string? Group { get; set; }
}

internal sealed class JsonSimpleSchedule
{
    public int RepeatCount { get; set; }
    public string Interval { get; set; } = "00:00:00";
    public string? MisfireInstruction { get; set; }
}

internal sealed class JsonCronSchedule
{
    public string Expression { get; set; } = "";
    public string? TimeZone { get; set; }
    public string? MisfireInstruction { get; set; }
}

internal sealed class JsonCalendarIntervalSchedule
{
    public int RepeatInterval { get; set; }
    public string RepeatIntervalUnit { get; set; } = "Day";
    public string? MisfireInstruction { get; set; }
}

/// <summary>
/// A schedule stated as an RFC 5545 recurrence rule.
/// </summary>
/// <remarks>
/// The rule says how the firings repeat and the trigger's own <c>StartTime</c> says what they repeat
/// from — a rule is anchored to a moment, exactly as <c>DTSTART</c> anchors an iCalendar one, so
/// <c>FREQ=WEEKLY;INTERVAL=2</c> means "every second week counted from the start time" and a trigger
/// given no start time is anchored to the moment its scheduler read the file.
/// </remarks>
internal sealed class JsonRecurrenceSchedule
{
    /// <summary>
    /// The RFC 5545 rule, for example <c>FREQ=WEEKLY;INTERVAL=2;BYDAY=MO</c>.
    /// </summary>
    public string Rule { get; set; } = "";

    public string? TimeZone { get; set; }
    public string? MisfireInstruction { get; set; }
}

internal sealed class JsonDailyTimeIntervalSchedule
{
    public int RepeatInterval { get; set; } = 1;
    public string RepeatIntervalUnit { get; set; } = "Minute";
    public int RepeatCount { get; set; } = -1;
    public string? StartTimeOfDay { get; set; }
    public string? EndTimeOfDay { get; set; }
    public List<string>? DaysOfWeek { get; set; }
    public string? TimeZone { get; set; }
    public string? MisfireInstruction { get; set; }
}
