namespace Quartz.HttpApiContract;

internal record KeyDto(string Name, string Group)
{
    public static KeyDto Create(JobKey jobKey) => new(jobKey.Name, jobKey.Group);

    public static KeyDto Create(TriggerKey triggerKey) => new(triggerKey.Name, triggerKey.Group);

    public JobKey AsJobKey() => new(Name, Group);

    public TriggerKey AsTriggerKey() => new(Name, Group);

    public override string ToString() => Group + '.' + Name;
}

internal record NamesDto(IReadOnlyCollection<string> Names);

internal record SchedulerContextDto(Dictionary<string, string> Context)
{
    public static SchedulerContextDto Create(SchedulerContext context)
    {
        if (context.Values.Any(x => x is not string))
        {
            throw new NotSupportedException("Only string values are supported in SchedulerContext");
        }

        var data = context.ToDictionary(x => x.Key, x => (string)x.Value);
        return new SchedulerContextDto(data);
    }

    public SchedulerContext AsContext()
    {
        return new SchedulerContext(Context.ToDictionary(x => x.Key, x => (object)x.Value));
    }
}

internal record TriggerStateDto(TriggerState State);