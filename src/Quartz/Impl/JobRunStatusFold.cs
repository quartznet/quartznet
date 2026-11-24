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

namespace Quartz.Impl;

/// <summary>
/// Folds one recorded execution into its job's <see cref="JobRunStatus" />, by <see cref="RunTally" />'s rule.
/// </summary>
/// <remarks>
/// <inheritdoc cref="RunTally" path="/remarks" />
/// </remarks>
internal static class JobRunStatusFold
{
    internal static JobRunStatus Apply(JobRunStatus? current, ExecutionHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        RunTally tally = current is null ? default : Tally(current);
        tally.Fold(entry, first: current is null);

        return Status(
            current?.SchedulerName ?? entry.SchedulerName,
            current?.Job ?? new JobKey(entry.JobName, entry.JobGroup),
            tally);
    }

    /// <summary>The run fields of a job's status.</summary>
    internal static RunTally Tally(JobRunStatus status) => new()
    {
        LastFiredAtUtc = status.LastFiredAtUtc,
        LastResult = status.LastResult,
        LastDuration = status.LastDuration,
        LastSchedulerInstanceId = status.LastSchedulerInstanceId,
        LastEntryId = status.LastEntryId,
        LastSummary = status.LastSummary,
        LastSucceededAtUtc = status.LastSucceededAtUtc,
        LastFailedAtUtc = status.LastFailedAtUtc,
        LastFailureMessage = status.LastFailureMessage,
        ConsecutiveFailures = status.ConsecutiveFailures,
        RunCount = status.RunCount,
        FailureCount = status.FailureCount,
        FirstFiredAtUtc = status.FirstFiredAtUtc
    };

    /// <summary>A job's status, from its key and its run fields.</summary>
    internal static JobRunStatus Status(string schedulerName, JobKey job, in RunTally tally) => new(
        schedulerName,
        job,
        tally.LastFiredAtUtc,
        tally.LastResult)
    {
        LastDuration = tally.LastDuration,
        LastSchedulerInstanceId = tally.LastSchedulerInstanceId,
        LastEntryId = tally.LastEntryId,
        LastSummary = tally.LastSummary,
        LastSucceededAtUtc = tally.LastSucceededAtUtc,
        LastFailedAtUtc = tally.LastFailedAtUtc,
        LastFailureMessage = tally.LastFailureMessage,
        ConsecutiveFailures = tally.ConsecutiveFailures,
        RunCount = tally.RunCount,
        FailureCount = tally.FailureCount,
        FirstFiredAtUtc = tally.FirstFiredAtUtc
    };
}
