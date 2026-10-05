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
/// Counts and times execution rows into buckets of fire time, for a store that holds the rows in memory
/// and for the default <c>QueryExecutionStatistics</c>, which reads them through <c>QueryExecutions</c>.
/// </summary>
/// <remarks>
/// <para>
/// The percentile arithmetic is the database's too: the ADO store reads the runs either side of a
/// percentile's rank and interpolates with <see cref="Interpolate" />, so every store answers the same
/// fixture with the same ticks.
/// </para>
/// <para>
/// A rank is held in hundredths, as an integer, so the position <c>(n - 1) * p</c> is exact. A
/// <see langword="double" /> would put 0.95 a hair below itself and occasionally pick the rank below.
/// </para>
/// </remarks>
internal sealed class ExecutionStatisticsBuilder
{
    /// <summary>The median's rank, in hundredths.</summary>
    internal const int Median = 50;

    /// <summary>The 95th percentile's rank, in hundredths.</summary>
    internal const int NinetyFifth = 95;

    private readonly TimeSpan bucketSize;
    private readonly Dictionary<long, Bucket> buckets = [];

    /// <param name="bucketSize">How much fire time one bucket covers.</param>
    public ExecutionStatisticsBuilder(TimeSpan bucketSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bucketSize, ExecutionStatisticsQuery.MinimumBucketSize);
        this.bucketSize = bucketSize;
    }

    /// <summary>Counts one run into the bucket its fire time falls in.</summary>
    public void Add(ExecutionHistoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        long index = entry.FiredAtUtc.UtcTicks / bucketSize.Ticks;
        if (!buckets.TryGetValue(index, out Bucket? bucket))
        {
            bucket = new Bucket();
            buckets.Add(index, bucket);
        }

        bucket.Add(ResultOf(entry), entry.Duration.Ticks);
    }

    /// <summary>The buckets counted so far, oldest first.</summary>
    /// <param name="truncated">Whether the rows counted were not every row the query matched.</param>
    public ExecutionStatistics Build(bool truncated)
    {
        List<ExecutionStatisticsBucket> result = new(buckets.Count);
        foreach (KeyValuePair<long, Bucket> bucket in buckets.OrderBy(static pair => pair.Key))
        {
            result.Add(bucket.Value.ToBucket(StartOf(bucket.Key, bucketSize)));
        }

        return new ExecutionStatistics
        {
            BucketSize = bucketSize,
            Buckets = result,
            Truncated = truncated
        };
    }

    /// <summary>When the bucket numbered <paramref name="index" /> starts: that many bucket sizes from the start of time.</summary>
    internal static DateTimeOffset StartOf(long index, TimeSpan bucketSize) => new(index * bucketSize.Ticks, TimeSpan.Zero);

    /// <summary>
    /// The duration at a percentile's rank among <paramref name="sortedTicks" />, which holds at least one.
    /// </summary>
    /// <param name="sortedTicks">The runs' durations in ticks, shortest first.</param>
    /// <param name="hundredths">The percentile, in hundredths: <see cref="Median" /> or <see cref="NinetyFifth" />.</param>
    internal static TimeSpan Percentile(List<long> sortedTicks, int hundredths)
    {
        (int lower, int remainder) = Rank(sortedTicks.Count, hundredths);
        return Interpolate(sortedTicks[lower], remainder == 0 ? sortedTicks[lower] : sortedTicks[lower + 1], remainder);
    }

    /// <summary>
    /// Where a percentile falls among <paramref name="count" /> sorted runs: the zero-based index of the run
    /// at or below it, and how far towards the next one it is, in hundredths.
    /// </summary>
    internal static (int Lower, int Remainder) Rank(long count, int hundredths)
    {
        long position = (count - 1) * hundredths;
        return ((int) (position / 100), (int) (position % 100));
    }

    /// <summary>
    /// The duration <paramref name="remainder" /> hundredths of the way from <paramref name="lower" /> to
    /// <paramref name="upper" />, to the nearest tick.
    /// </summary>
    internal static TimeSpan Interpolate(long lower, long upper, int remainder)
    {
        Int128 offset = ((Int128) (upper - lower) * remainder + 50) / 100;
        return TimeSpan.FromTicks(lower + (long) offset);
    }

    /// <summary>
    /// A row's effective result, or what its success flag implies for a value this version does not know.
    /// </summary>
    private static JobRunResult ResultOf(ExecutionHistoryEntry entry) => entry.EffectiveResult switch
    {
        JobRunResult.Succeeded => JobRunResult.Succeeded,
        JobRunResult.Failed => JobRunResult.Failed,
        JobRunResult.Cancelled => JobRunResult.Cancelled,
        JobRunResult.Skipped => JobRunResult.Skipped,
        _ => entry.Succeeded ? JobRunResult.Succeeded : JobRunResult.Failed
    };

    private sealed class Bucket
    {
        private readonly List<long> durations = [];
        private long succeeded;
        private long failed;
        private long cancelled;
        private long skipped;

        public void Add(JobRunResult result, long durationTicks)
        {
            switch (result)
            {
                case JobRunResult.Succeeded:
                    succeeded++;
                    break;
                case JobRunResult.Failed:
                    failed++;
                    break;
                case JobRunResult.Cancelled:
                    cancelled++;
                    break;
                default:
                    skipped++;
                    break;
            }

            durations.Add(durationTicks);
        }

        public ExecutionStatisticsBucket ToBucket(DateTimeOffset start)
        {
            durations.Sort();

            return new ExecutionStatisticsBucket(start)
            {
                SucceededCount = succeeded,
                FailedCount = failed,
                CancelledCount = cancelled,
                SkippedCount = skipped,
                P50Duration = Percentile(durations, Median),
                P95Duration = Percentile(durations, NinetyFifth),
                MaxDuration = TimeSpan.FromTicks(durations[^1])
            };
        }
    }
}
