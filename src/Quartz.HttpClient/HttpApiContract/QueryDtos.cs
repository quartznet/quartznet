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
