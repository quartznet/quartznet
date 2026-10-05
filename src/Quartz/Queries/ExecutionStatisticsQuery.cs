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
/// The runs of a scheduler, counted by result and timed, in buckets of fire time; optionally narrowed as
/// an <see cref="ExecutionHistoryQuery" /> narrows a page.
/// </summary>
/// <remarks>
/// <para>
/// Every filter but <see cref="JobGroup" /> is <see cref="ExecutionHistoryQuery" />'s and means what it means
/// there, so a chart beside a history page counts the rows that page lists.
/// </para>
/// <para>
/// A bucket starts at a whole multiple of <see cref="BucketSize" /> counted from
/// <see cref="DateTimeOffset.MinValue" />: an hour bucket starts on the hour and a day bucket at midnight, UTC.
/// </para>
/// </remarks>
/// <seealso cref="Extensibility.IExecutionHistoryStore.QueryExecutionStatistics" />
public sealed record ExecutionStatisticsQuery
{
    /// <summary>
    /// The narrowest <see cref="BucketSize" />: one minute.
    /// </summary>
    public static readonly TimeSpan MinimumBucketSize = TimeSpan.FromMinutes(1);

    /// <summary>
    /// The scheduler whose runs to count. Required: a store keeps every scheduler's rows together.
    /// </summary>
    public required string SchedulerName { get; init; }

    /// <summary>
    /// The node whose runs to count, or <see langword="null" /> for every node's.
    /// </summary>
    public string? SchedulerInstanceId { get; init; }

    /// <summary>
    /// Counts only the runs whose job key matches this, or every job's when null.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="ExecutionHistoryQuery" path="/remarks" />
    /// </remarks>
    public string? JobContains { get; init; }

    /// <summary>
    /// Counts only the runs whose trigger key matches this, or every trigger's when null.
    /// </summary>
    public string? TriggerContains { get; init; }

    /// <summary>
    /// Counts only the runs that failed for the last time (<see langword="true" />), or only the rest
    /// (<see langword="false" />). <see langword="null" />, the default, counts them all.
    /// </summary>
    /// <remarks>
    /// As <see cref="ExecutionHistoryQuery.FailedFinally" />.
    /// </remarks>
    public bool? FailedFinally { get; init; }

    /// <summary>
    /// Counts only the runs of this job, matched exactly, or every job's when null.
    /// </summary>
    public JobKey? Job { get; init; }

    /// <summary>
    /// Counts only the runs of the jobs in this group, matched exactly, or every group's when null.
    /// </summary>
    public string? JobGroup { get; init; }

    /// <summary>
    /// Counts only the runs that fired at or after this instant, or from the start of the history when null.
    /// </summary>
    public DateTimeOffset? FiredFrom { get; init; }

    /// <summary>
    /// Counts only the runs that fired before this instant, or up to now when null. Exclusive.
    /// </summary>
    public DateTimeOffset? FiredBefore { get; init; }

    /// <summary>
    /// Counts only the runs whose <see cref="ExecutionHistoryEntry.EffectiveResult" /> is one of these, or
    /// every result when null. An empty set counts nothing.
    /// </summary>
    public IReadOnlyCollection<JobRunResult>? Results { get; init; }

    /// <summary>
    /// How much fire time one bucket covers. Defaults to one hour; at least <see cref="MinimumBucketSize" />.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Shorter than <see cref="MinimumBucketSize" />.</exception>
    public TimeSpan BucketSize
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, MinimumBucketSize);
            field = value;
        }
    } = TimeSpan.FromHours(1);

    /// <summary>
    /// The page of history this query counts, without the page: every filter but <see cref="JobGroup" />,
    /// which <see cref="ExecutionHistoryQuery" /> has no spelling of.
    /// </summary>
    internal ExecutionHistoryQuery AsHistoryQuery(int skip, int take) => new()
    {
        SchedulerName = SchedulerName,
        SchedulerInstanceId = SchedulerInstanceId,
        JobContains = JobContains,
        TriggerContains = TriggerContains,
        FailedFinally = FailedFinally,
        Job = Job,
        FiredFrom = FiredFrom,
        FiredBefore = FiredBefore,
        Results = Results,
        Skip = skip,
        Take = take
    };
}
