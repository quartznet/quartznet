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

namespace Quartz.HttpApiContract;

/// <summary>
/// One finished execution on the wire.
/// </summary>
/// <remarks>
/// The scheduler's own name is not repeated: the route names the scheduler, and every row in the answer
/// belongs to it. The <em>node's</em> id is repeated on every row, because a cluster's history is
/// several nodes' and a reader has to be able to tell them apart.
/// </remarks>
internal sealed record ExecutionHistoryEntryDto(
    string SchedulerInstanceId,
    string JobGroup,
    string JobName,
    string TriggerGroup,
    string TriggerName,
    DateTimeOffset FiredAtUtc,
    TimeSpan Duration,
    bool Succeeded,
    string? ExceptionMessage)
{
    public static ExecutionHistoryEntryDto Create(ExecutionHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new ExecutionHistoryEntryDto(
            SchedulerInstanceId: entry.SchedulerInstanceId,
            JobGroup: entry.JobGroup,
            JobName: entry.JobName,
            TriggerGroup: entry.TriggerGroup,
            TriggerName: entry.TriggerName,
            FiredAtUtc: entry.FiredAtUtc,
            Duration: entry.Duration,
            Succeeded: entry.Succeeded,
            ExceptionMessage: entry.ExceptionMessage
        );
    }

    /// <param name="schedulerName">
    /// The scheduler the row belongs to, which the reader knows from the route it asked on.
    /// </param>
    public ExecutionHistoryEntry AsExecutionHistoryEntry(string schedulerName)
    {
        return new ExecutionHistoryEntry(
            schedulerName,
            SchedulerInstanceId,
            JobGroup,
            JobName,
            TriggerGroup,
            TriggerName,
            FiredAtUtc,
            Duration,
            Succeeded,
            ExceptionMessage
        );
    }
}

/// <summary>
/// One firing that was missed, on the wire.
/// </summary>
/// <remarks>
/// <inheritdoc cref="ExecutionHistoryEntryDto" path="/remarks" />
/// </remarks>
internal sealed record MisfireHistoryEntryDto(
    string SchedulerInstanceId,
    string TriggerGroup,
    string TriggerName,
    KeyDto? JobKey,
    DateTimeOffset MisfiredAtUtc,
    DateTimeOffset? ScheduledFireTimeUtc)
{
    public static MisfireHistoryEntryDto Create(MisfireHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new MisfireHistoryEntryDto(
            SchedulerInstanceId: entry.SchedulerInstanceId,
            TriggerGroup: entry.TriggerGroup,
            TriggerName: entry.TriggerName,
            JobKey: entry.JobKey is null ? null : KeyDto.Create(entry.JobKey),
            MisfiredAtUtc: entry.MisfiredAtUtc,
            ScheduledFireTimeUtc: entry.ScheduledFireTimeUtc
        );
    }

    /// <inheritdoc cref="ExecutionHistoryEntryDto.AsExecutionHistoryEntry" />
    public MisfireHistoryEntry AsMisfireHistoryEntry(string schedulerName)
    {
        return new MisfireHistoryEntry(
            schedulerName,
            SchedulerInstanceId,
            TriggerGroup,
            TriggerName,
            JobKey?.AsJobKey(),
            MisfiredAtUtc,
            ScheduledFireTimeUtc
        );
    }
}

/// <summary>
/// How many misfires a scheduler has recorded since an instant.
/// </summary>
/// <remarks>
/// A body rather than a bare number, so the answer can gain a member — a window, a breakdown by node —
/// without every reader of it breaking.
/// </remarks>
internal sealed record MisfireCountResponse(int Count);
