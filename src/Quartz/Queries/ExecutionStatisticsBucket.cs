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
/// The runs that fired in one bucket: how many there were of each result, and how long they took.
/// </summary>
/// <remarks>
/// <para>
/// A run is counted by its <see cref="ExecutionHistoryEntry.EffectiveResult" />, so a row written before 4.4
/// counts as succeeded or failed.
/// </para>
/// <para>
/// The durations are over every run in the bucket, whatever its result. A percentile interpolates between
/// the two runs nearest its rank, as SQL's <c>PERCENTILE_CONT</c> does: with the durations sorted, the
/// <c>p</c>-th percentile of <c>n</c> runs lies at position <c>(n - 1) * p</c>.
/// </para>
/// </remarks>
/// <param name="StartUtc">When the bucket starts, inclusive. It ends one bucket size later, exclusive.</param>
public sealed record ExecutionStatisticsBucket(DateTimeOffset StartUtc)
{
    /// <summary>
    /// How many runs fired in the bucket, every result included.
    /// </summary>
    public long RunCount => SucceededCount + FailedCount + CancelledCount + SkippedCount;

    /// <summary>How many runs were <see cref="JobRunResult.Succeeded" />.</summary>
    public long SucceededCount { get; init; }

    /// <summary>How many runs were <see cref="JobRunResult.Failed" />, failures the trigger retried included.</summary>
    public long FailedCount { get; init; }

    /// <summary>How many runs were <see cref="JobRunResult.Cancelled" />.</summary>
    public long CancelledCount { get; init; }

    /// <summary>How many runs were <see cref="JobRunResult.Skipped" />.</summary>
    public long SkippedCount { get; init; }

    /// <summary>The median duration.</summary>
    public TimeSpan P50Duration { get; init; }

    /// <summary>The 95th percentile duration.</summary>
    public TimeSpan P95Duration { get; init; }

    /// <summary>The longest duration.</summary>
    public TimeSpan MaxDuration { get; init; }

    /// <summary>
    /// How many runs had <paramref name="result" />; zero for a result this version does not know.
    /// </summary>
    /// <param name="result">The result to count.</param>
    public long CountOf(JobRunResult result) => result switch
    {
        JobRunResult.Succeeded => SucceededCount,
        JobRunResult.Failed => FailedCount,
        JobRunResult.Cancelled => CancelledCount,
        JobRunResult.Skipped => SkippedCount,
        _ => 0
    };
}
