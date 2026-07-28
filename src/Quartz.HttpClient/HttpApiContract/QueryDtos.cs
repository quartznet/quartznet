// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract - Can be null when received from Web API

namespace Quartz.HttpApiContract;

internal sealed record PagedResultDto<T>(T[] Items, bool HasMore, int? TotalCount);

internal sealed record JobHeaderDto(
    string Name,
    string Group,
    string? Description,
    string JobTypeName,
    bool Durable,
    bool ConcurrentExecutionDisallowed,
    bool PersistJobDataAfterExecution,
    bool RequestsRecovery)
{
    public static JobHeaderDto Create(JobHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);

        return new JobHeaderDto(
            Name: header.Key.Name,
            Group: header.Key.Group,
            Description: header.Description,
            JobTypeName: header.JobTypeName,
            Durable: header.Durable,
            ConcurrentExecutionDisallowed: header.ConcurrentExecutionDisallowed,
            PersistJobDataAfterExecution: header.PersistJobDataAfterExecution,
            RequestsRecovery: header.RequestsRecovery
        );
    }

    public JobHeader AsJobHeader()
    {
        return new JobHeader(
            new JobKey(Name, Group),
            Description,
            JobTypeName,
            Durable,
            ConcurrentExecutionDisallowed,
            PersistJobDataAfterExecution,
            RequestsRecovery
        );
    }
}

internal sealed record TriggerHeaderDto(
    string Name,
    string Group,
    string JobName,
    string JobGroup,
    string? Description,
    string TriggerType,
    TriggerState State,
    DateTimeOffset StartTimeUtc,
    DateTimeOffset? EndTimeUtc,
    DateTimeOffset? NextFireTimeUtc,
    DateTimeOffset? PreviousFireTimeUtc,
    string? CalendarName,
    int Priority,
    string? ExecutionGroup)
{
    public static TriggerHeaderDto Create(TriggerHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);

        return new TriggerHeaderDto(
            Name: header.Key.Name,
            Group: header.Key.Group,
            JobName: header.JobKey.Name,
            JobGroup: header.JobKey.Group,
            Description: header.Description,
            TriggerType: header.TriggerType,
            State: header.State,
            StartTimeUtc: header.StartTimeUtc,
            EndTimeUtc: header.EndTimeUtc,
            NextFireTimeUtc: header.NextFireTimeUtc,
            PreviousFireTimeUtc: header.PreviousFireTimeUtc,
            CalendarName: header.CalendarName,
            Priority: header.Priority,
            ExecutionGroup: header.ExecutionGroup
        );
    }

    public TriggerHeader AsTriggerHeader()
    {
        return new TriggerHeader(
            new TriggerKey(Name, Group),
            new JobKey(JobName, JobGroup),
            Description,
            TriggerType,
            State,
            StartTimeUtc,
            EndTimeUtc,
            NextFireTimeUtc,
            PreviousFireTimeUtc,
            CalendarName,
            Priority,
            ExecutionGroup
        );
    }
}

internal sealed record JobGroupDto(string Name, bool Paused)
{
    public static JobGroupDto Create(JobGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        return new JobGroupDto(group.Name, group.Paused);
    }

    public JobGroup AsJobGroup() => new(Name, Paused);
}

internal sealed record TriggerGroupDto(string Name, bool Paused)
{
    public static TriggerGroupDto Create(TriggerGroup group)
    {
        ArgumentNullException.ThrowIfNull(group);

        return new TriggerGroupDto(group.Name, group.Paused);
    }

    public TriggerGroup AsTriggerGroup() => new(Name, Paused);
}
