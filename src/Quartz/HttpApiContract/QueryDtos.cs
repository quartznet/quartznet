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

// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract - Can be null when received from Web API

namespace Quartz.HttpApiContract;

internal sealed record PagedResultDto<T>(T[] Items, bool HasMore, int? TotalCount);

/// <remarks>
/// <see cref="JobType" /> carries the same assembly-qualified name as
/// <see cref="JobDetailDto.JobType" />: one value, one name on the wire. Core's own listing record calls
/// it <see cref="JobHeader.JobTypeName" />, and keeps that name — a store's noun and the wire's need not
/// agree, but the wire may not disagree with itself.
/// </remarks>
internal sealed record JobHeaderDto(
    string Name,
    string Group,
    string? Description,
    string JobType,
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
            JobType: header.JobTypeName,
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
            JobType,
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

/// <remarks>
/// Replaces the old <c>CurrentlyExecutingJobDto</c>, which serialized a whole job detail, a whole trigger
/// and a calendar to say that something was running. This one is what the store can answer for the whole
/// cluster, and it carries the two things the old shape could not: the fire instance id — so a caller can
/// interrupt the one execution it is looking at — and the state, so a reservation is not mistaken for a
/// running job.
/// </remarks>
internal sealed record FireInstanceDto(
    string FireInstanceId,
    string TriggerName,
    string TriggerGroup,
    string? JobName,
    string? JobGroup,
    string SchedulerInstanceId,
    FireInstanceState State,
    DateTimeOffset FireTimeUtc,
    DateTimeOffset? ScheduledFireTimeUtc,
    string? ExecutionGroup)
{
    public static FireInstanceDto Create(FireInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        return new FireInstanceDto(
            FireInstanceId: instance.FireInstanceId,
            TriggerName: instance.TriggerKey.Name,
            TriggerGroup: instance.TriggerKey.Group,
            JobName: instance.JobKey?.Name,
            JobGroup: instance.JobKey?.Group,
            SchedulerInstanceId: instance.SchedulerInstanceId,
            State: instance.State,
            FireTimeUtc: instance.FireTimeUtc,
            ScheduledFireTimeUtc: instance.ScheduledFireTimeUtc,
            ExecutionGroup: instance.ExecutionGroup
        );
    }

    public FireInstance AsFireInstance()
    {
        return new FireInstance(
            FireInstanceId,
            new TriggerKey(TriggerName, TriggerGroup),
            JobName is not null && JobGroup is not null ? new JobKey(JobName, JobGroup) : null,
            SchedulerInstanceId,
            State,
            FireTimeUtc,
            ScheduledFireTimeUtc,
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
