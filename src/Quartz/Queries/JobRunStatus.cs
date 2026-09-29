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

namespace Quartz;

/// <summary>
/// How one job's runs have gone: its last run, its last success and failure, and its counts.
/// </summary>
/// <remarks>
/// <para>
/// Kept by the history store beside the rows, from every execution it records, so it survives the rows
/// being trimmed. The counts start when the store first recorded the job.
/// </para>
/// <para>
/// The <c>Last*</c> run fields describe the execution that fired latest. A run that completes after a
/// later-fired one is counted but does not replace them.
/// </para>
/// </remarks>
/// <param name="SchedulerName">The scheduler the job belongs to.</param>
/// <param name="Job">The job.</param>
/// <param name="LastFiredAtUtc">When the latest-fired recorded run fired.</param>
/// <param name="LastResult">What that run achieved.</param>
public sealed record JobRunStatus(
    string SchedulerName,
    JobKey Job,
    DateTimeOffset LastFiredAtUtc,
    JobRunResult LastResult)
{
    /// <summary>
    /// How long the latest-fired run took.
    /// </summary>
    public TimeSpan LastDuration { get; init; }

    /// <summary>
    /// The node that ran the latest-fired run.
    /// </summary>
    public string? LastSchedulerInstanceId { get; init; }

    /// <summary>
    /// The latest-fired run's <see cref="ExecutionHistoryEntry.EntryId" />, which
    /// <see cref="Extensibility.IExecutionHistoryStore.GetExecution" /> reads it back by while it is kept.
    /// </summary>
    public string? LastEntryId { get; init; }

    /// <summary>
    /// The latest-fired run's <see cref="ExecutionHistoryEntry.Summary" />.
    /// </summary>
    public string? LastSummary { get; init; }

    /// <summary>
    /// When the latest successful run fired: <see cref="JobRunResult.Succeeded" /> or
    /// <see cref="JobRunResult.Skipped" />. <see langword="null" /> until one is recorded.
    /// </summary>
    public DateTimeOffset? LastSucceededAtUtc { get; init; }

    /// <summary>
    /// When the latest failed run fired, a failure the trigger retries included.
    /// <see langword="null" /> until one is recorded.
    /// </summary>
    public DateTimeOffset? LastFailedAtUtc { get; init; }

    /// <summary>
    /// The <see cref="ExecutionHistoryEntry.ExceptionMessage" /> of the run at <see cref="LastFailedAtUtc" />:
    /// the message of what the job threw, or else the run's summary.
    /// </summary>
    public string? LastFailureMessage { get; init; }

    /// <summary>
    /// How many occurrences in a row have failed for the last time, counted from the latest-fired run
    /// back. A success resets it; a cancelled run and a failure the trigger retries leave it alone.
    /// </summary>
    /// <remarks>
    /// Above zero is what <see cref="JobRunStatusQuery.Failing" /> selects.
    /// </remarks>
    public int ConsecutiveFailures { get; init; }

    /// <summary>
    /// How many runs have been recorded, every result included.
    /// </summary>
    public long RunCount { get; init; }

    /// <summary>
    /// How many occurrences have failed for the last time. A failure the trigger retries is not counted.
    /// </summary>
    public long FailureCount { get; init; }

    /// <summary>
    /// When the earliest-fired recorded run fired.
    /// </summary>
    public DateTimeOffset FirstFiredAtUtc { get; init; }
}
