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
    public DateTimeOffset? StartTime { get; set; }
    public int? StartTimeSecondsInFuture { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public Dictionary<string, string>? JobDataMap { get; set; }

    public JsonSimpleSchedule? Simple { get; set; }
    public JsonCronSchedule? Cron { get; set; }
    public JsonCalendarIntervalSchedule? CalendarInterval { get; set; }
    public JsonDailyTimeIntervalSchedule? DailyTimeInterval { get; set; }
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
