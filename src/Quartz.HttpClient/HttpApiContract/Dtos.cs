// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract - Can be null when received from Web API

namespace Quartz.HttpApiContract;

internal record KeyDto(string Name, string Group) : IValidatable
{
    public static KeyDto Create(JobKey jobKey)
    {
        ArgumentNullException.ThrowIfNull(jobKey);

        return new KeyDto(jobKey.Name, jobKey.Group);
    }

    public static KeyDto Create(TriggerKey triggerKey)
    {
        ArgumentNullException.ThrowIfNull(triggerKey);

        return new KeyDto(triggerKey.Name, triggerKey.Group);
    }

    public JobKey AsJobKey() => new(Name, Group);

    public TriggerKey AsTriggerKey() => new(Name, Group);

    public IEnumerable<string> Validate()
    {
        if (Name is null)
        {
            yield return "Key is missing name";
        }

        if (Group is null)
        {
            yield return "Key is missing group";
        }
    }

    public override string ToString() => Group + '.' + Name;
}

internal record NamesDto(List<string> Names);

internal record SchedulerContextDto(Dictionary<string, string?> Context)
{
    public static SchedulerContextDto Create(SchedulerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Values.Any(x => x is not string))
        {
            throw new NotSupportedException("Only string values are supported in SchedulerContext");
        }

        var data = context.ToDictionary(x => x.Key, x => (string?) x.Value);
        return new SchedulerContextDto(data);
    }

    public SchedulerContext AsContext()
    {
        return new SchedulerContext(Context.ToDictionary(x => x.Key, x => (object?) x.Value));
    }
}

internal record TriggerStateDto(TriggerState State);