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
/// The run fields a <see cref="JobRunStatus" /> and a <see cref="TriggerRunStatus" /> share, and the one rule
/// that folds a recorded execution into them.
/// </summary>
/// <remarks>
/// <para>
/// The one definition of the rollup, so every store that keeps a status keeps the same one, and a job's
/// status and a trigger's cannot drift apart. Pure: the caller serializes the updates to one status.
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
internal struct RunTally
{
    public DateTimeOffset LastFiredAtUtc { get; set; }

    public JobRunResult LastResult { get; set; }

    public TimeSpan LastDuration { get; set; }

    public string? LastSchedulerInstanceId { get; set; }

    public string? LastEntryId { get; set; }

    public string? LastSummary { get; set; }

    public DateTimeOffset? LastSucceededAtUtc { get; set; }

    public DateTimeOffset? LastFailedAtUtc { get; set; }

    public string? LastFailureMessage { get; set; }

    public int ConsecutiveFailures { get; set; }

    public long RunCount { get; set; }

    public long FailureCount { get; set; }

    public DateTimeOffset FirstFiredAtUtc { get; set; }

    /// <summary>
    /// Folds one execution into the tally.
    /// </summary>
    /// <param name="entry">The recorded execution.</param>
    /// <param name="first">
    /// Whether nothing has been folded yet, so the tally starts from <paramref name="entry" /> whatever it holds.
    /// </param>
    public void Fold(ExecutionHistoryEntry entry, bool first)
    {
        ArgumentNullException.ThrowIfNull(entry);

        JobRunResult result = entry.EffectiveResult;
        DateTimeOffset firedAt = entry.FiredAtUtc;
        bool succeeded = JobRunClassifier.IsSuccess(result);
        bool failed = result == JobRunResult.Failed;
        bool finalFailure = failed && !entry.RetryScheduled;

        if (first)
        {
            this = default;
            FirstFiredAtUtc = firedAt;
        }

        bool newest = first || firedAt >= LastFiredAtUtc;
        if (newest)
        {
            if (succeeded)
            {
                ConsecutiveFailures = 0;
            }
            else if (finalFailure)
            {
                ConsecutiveFailures++;
            }

            LastFiredAtUtc = firedAt;
            LastResult = result;
            LastDuration = entry.Duration;
            LastSchedulerInstanceId = entry.SchedulerInstanceId;
            LastEntryId = entry.EntryId;
            LastSummary = entry.Summary;
        }

        RunCount++;
        if (finalFailure)
        {
            FailureCount++;
        }

        if (firedAt < FirstFiredAtUtc)
        {
            FirstFiredAtUtc = firedAt;
        }

        if (succeeded && !(firedAt <= LastSucceededAtUtc))
        {
            LastSucceededAtUtc = firedAt;
        }

        if (failed && !(firedAt < LastFailedAtUtc))
        {
            LastFailedAtUtc = firedAt;
            LastFailureMessage = entry.ExceptionMessage ?? entry.Summary;
        }
    }
}
