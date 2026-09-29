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
/// Folds one recorded execution into its job's <see cref="JobRunStatus" />.
/// </summary>
/// <remarks>
/// <para>
/// The one definition of the rollup, so every store that keeps a status keeps the same one. Pure: the
/// caller serializes the updates to one job.
/// </para>
/// <list type="table">
/// <listheader><term>Result</term><description>What changes</description></listheader>
/// <item>
/// <term><see cref="JobRunResult.Succeeded" />, <see cref="JobRunResult.Skipped" /></term>
/// <description>Runs +1. Consecutive failures reset. Last success moves forward.</description>
/// </item>
/// <item>
/// <term><see cref="JobRunResult.Failed" />, not retried</term>
/// <description>Runs +1, failures +1, consecutive failures +1. Last failure moves forward.</description>
/// </item>
/// <item>
/// <term><see cref="JobRunResult.Failed" />, retry scheduled</term>
/// <description>Runs +1. Last failure moves forward.</description>
/// </item>
/// <item>
/// <term><see cref="JobRunResult.Cancelled" /></term>
/// <description>Runs +1.</description>
/// </item>
/// </list>
/// <para>
/// The last-run fields and the consecutive failures change only for an execution that fired at or after
/// the latest one folded so far; one that completes out of order is counted and leaves them alone. The
/// last success and the last failure keep the later fire time of the two they are offered.
/// </para>
/// </remarks>
internal static class JobRunStatusFold
{
    internal static JobRunStatus Apply(JobRunStatus? current, ExecutionHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        JobRunResult result = entry.EffectiveResult;
        DateTimeOffset firedAt = entry.FiredAtUtc;
        bool failed = result == JobRunResult.Failed;
        bool finalFailure = failed && !entry.RetryScheduled;

        JobRunStatus status = current ?? new JobRunStatus(
            entry.SchedulerName,
            new JobKey(entry.JobName, entry.JobGroup),
            firedAt,
            result)
        {
            FirstFiredAtUtc = firedAt
        };

        bool newest = current is null || firedAt >= current.LastFiredAtUtc;

        int consecutiveFailures = status.ConsecutiveFailures;
        if (newest)
        {
            if (JobRunClassifier.IsSuccess(result))
            {
                consecutiveFailures = 0;
            }
            else if (finalFailure)
            {
                consecutiveFailures++;
            }
        }

        status = status with
        {
            RunCount = status.RunCount + 1,
            FailureCount = finalFailure ? status.FailureCount + 1 : status.FailureCount,
            ConsecutiveFailures = consecutiveFailures,
            FirstFiredAtUtc = firedAt < status.FirstFiredAtUtc ? firedAt : status.FirstFiredAtUtc
        };

        if (newest)
        {
            status = status with
            {
                LastFiredAtUtc = firedAt,
                LastResult = result,
                LastDuration = entry.Duration,
                LastSchedulerInstanceId = entry.SchedulerInstanceId,
                LastEntryId = entry.EntryId,
                LastSummary = entry.Summary
            };
        }

        if (JobRunClassifier.IsSuccess(result) && !(firedAt <= status.LastSucceededAtUtc))
        {
            status = status with { LastSucceededAtUtc = firedAt };
        }

        if (failed && !(firedAt < status.LastFailedAtUtc))
        {
            status = status with
            {
                LastFailedAtUtc = firedAt,
                LastFailureMessage = entry.ExceptionMessage ?? entry.Summary
            };
        }

        return status;
    }
}
