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

#nullable enable

using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// The run statistics half of the history contract: buckets, counts per result and the duration
/// percentiles, against a fixture whose answer is worked out by hand.
/// </summary>
/// <remarks>
/// Run against the in-memory store, which counts its own rows, and the database one on SQLite, which ranks
/// the runs in SQL and interpolates here. <c>ExecutionOutcomeRoundTripTest</c> holds the other five dialects
/// to the same fixture.
/// </remarks>
public abstract partial class ExecutionHistoryStoreContractTest
{
    /// <summary>
    /// Three hours before the contract's clock. A property, because a static field here could be initialised
    /// before <see cref="Start" />, which another part of the class declares.
    /// </summary>
    private static DateTimeOffset NineOClock => Start.AddHours(-3);

    /// <summary>
    /// Three buckets and a gap: the counts are per result and the percentiles interpolate as
    /// <c>PERCENTILE_CONT</c> does, between the two runs either side of <c>(n - 1) * p</c>.
    /// </summary>
    /// <remarks>
    /// 09:00 holds 100, 200, 300, 400 and 1,000 ms: the median is the third, 300; the 95th percentile lies
    /// at 3.8, so 400 + 0.8 × 600 = 880. 10:00 holds 50 and 150: the median lies at 0.5, so 100, and the 95th
    /// percentile at 0.95, so 145. 11:00 holds nothing and has no bucket. 12:00 holds one run of 70.
    /// </remarks>
    [Test]
    public async Task RunsAreCountedByResultAndTimedPerBucket()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });
        await RecordStatisticsFixture(store);

        ExecutionStatistics statistics = await store.QueryExecutionStatistics(new ExecutionStatisticsQuery { SchedulerName = SchedulerName });

        statistics.BucketSize.Should().Be(TimeSpan.FromHours(1), "an hour is the default bucket");
        statistics.Truncated.Should().BeFalse("a shipped store counts every run it holds");
        statistics.Buckets.Select(bucket => bucket.StartUtc).Should().Equal(
            [NineOClock, NineOClock.AddHours(1), NineOClock.AddHours(3)],
            "a bucket starts on the hour, and the hour with no run has no bucket");

        ExecutionStatisticsBucket nine = statistics.Buckets[0];
        nine.RunCount.Should().Be(5);
        nine.SucceededCount.Should().Be(2);
        nine.SkippedCount.Should().Be(1);
        nine.FailedCount.Should().Be(1);
        nine.CancelledCount.Should().Be(1, "the run at 09:59:59 is still the nine o'clock bucket's");
        nine.P50Duration.Should().Be(TimeSpan.FromMilliseconds(300), "the median of five runs is the third");
        nine.P95Duration.Should().Be(TimeSpan.FromMilliseconds(880), "rank 3.8 lies 0.8 of the way from 400 to 1,000");
        nine.MaxDuration.Should().Be(TimeSpan.FromMilliseconds(1000));

        ExecutionStatisticsBucket ten = statistics.Buckets[1];
        ten.RunCount.Should().Be(2);
        ten.SucceededCount.Should().Be(1);
        ten.FailedCount.Should().Be(1);
        ten.P50Duration.Should().Be(TimeSpan.FromMilliseconds(100), "the median of two runs is halfway between them");
        ten.P95Duration.Should().Be(TimeSpan.FromMilliseconds(145), "rank 0.95 lies 0.95 of the way from 50 to 150");
        ten.MaxDuration.Should().Be(TimeSpan.FromMilliseconds(150));

        ExecutionStatisticsBucket noon = statistics.Buckets[2];
        noon.RunCount.Should().Be(1);
        noon.SkippedCount.Should().Be(1);
        noon.P50Duration.Should().Be(TimeSpan.FromMilliseconds(70), "one run is every percentile of itself");
        noon.P95Duration.Should().Be(TimeSpan.FromMilliseconds(70));
        noon.MaxDuration.Should().Be(TimeSpan.FromMilliseconds(70));
    }

    /// <summary>
    /// The statistics are narrowed as the listing is, so a chart beside a page counts the rows it lists.
    /// </summary>
    [Test]
    public async Task RunsAreCountedThroughTheListingsFilters()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });
        await RecordStatisticsFixture(store);

        (await Runs(store, query => query with { Job = new JobKey("hourly", JobGroup) })).Should().Be(5,
            "one job, matched exactly");
        (await Runs(store, query => query with { JobGroup = "other" })).Should().Be(1, "one group, matched exactly");
        (await Runs(store, query => query with { JobGroup = JobGroup })).Should().Be(7);
        (await Runs(store, query => query with { JobContains = "daily" })).Should().Be(2);
        (await Runs(store, query => query with { FiredFrom = NineOClock.AddHours(1), FiredBefore = Start })).Should().Be(2,
            "from is inclusive and before exclusive, so the run at noon is out");
        (await Runs(store, query => query with { Results = [JobRunResult.Failed, JobRunResult.Cancelled] })).Should().Be(3);
        (await Runs(store, query => query with { Results = [] })).Should().Be(0, "an empty set of results counts nothing");
        (await Runs(store, query => query with { SchedulerInstanceId = "node-b" })).Should().Be(1);
        (await Runs(store, query => query with { FailedFinally = true })).Should().Be(2,
            "the cancelled run and the failure nobody retried gave up; the retried one did not");
        (await Runs(store, query => query with { SchedulerName = "Nobody" })).Should().Be(0);
    }

    /// <summary>
    /// A day bucket starts at midnight UTC, and the rows past the age bound are not counted.
    /// </summary>
    [Test]
    public async Task ADayBucketStartsAtMidnightAndTheAgeBoundApplies()
    {
        IExecutionHistoryStore store = await CreateStore(options => options.Retention = TimeSpan.FromHours(14));

        await store.AddExecution(Execution(Start.AddHours(-15), "expired"));
        await store.AddExecution(Execution(Start.AddHours(-12.5), "late-yesterday"));
        await store.AddExecution(Execution(Start.AddHours(-11.5), "early-today"));
        await store.AddExecution(Execution(Start, "noon"));

        ExecutionStatistics statistics = await store.QueryExecutionStatistics(new ExecutionStatisticsQuery
        {
            SchedulerName = SchedulerName,
            BucketSize = TimeSpan.FromDays(1)
        });

        statistics.Buckets.Select(bucket => (bucket.StartUtc, bucket.RunCount)).Should().Equal(
            [(new DateTimeOffset(2026, 9, 18, 0, 0, 0, TimeSpan.Zero), 1L), (new DateTimeOffset(2026, 9, 19, 0, 0, 0, TimeSpan.Zero), 2L)],
            "a day runs from midnight UTC, and the run fifteen hours ago is past the fourteen the history keeps");
    }

    /// <summary>
    /// A row without a result, as a node before 4.4 wrote it, counts as its success flag says.
    /// </summary>
    [Test]
    public async Task ARowWithoutAResultCountsAsItsSucceededFlagSays()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddExecution(Execution(Start, "legacy-ok"));
        await store.AddExecution(Execution(Start, "legacy-failed") with { Succeeded = false });

        ExecutionStatisticsBucket bucket = (await store.QueryExecutionStatistics(new ExecutionStatisticsQuery { SchedulerName = SchedulerName }))
            .Buckets.Should().ContainSingle().Subject;

        bucket.SucceededCount.Should().Be(1);
        bucket.FailedCount.Should().Be(1, "EffectiveResult reads a row with no result from SUCCEEDED");
    }

    /// <summary>
    /// The fixture <see cref="RunsAreCountedByResultAndTimedPerBucket" /> works out by hand: seven runs of
    /// <c>reports</c> jobs and one of another group, on two nodes.
    /// </summary>
    private static async Task RecordStatisticsFixture(IExecutionHistoryStore store)
    {
        ExecutionHistoryEntry[] runs =
        [
            Timed(NineOClock.AddMinutes(5), "hourly", 100, JobRunResult.Succeeded),
            Timed(NineOClock.AddMinutes(10), "hourly", 200, JobRunResult.Succeeded),
            Timed(NineOClock.AddMinutes(20), "hourly", 300, JobRunResult.Skipped),
            Timed(NineOClock.AddMinutes(30), "hourly", 400, JobRunResult.Failed) with { ExceptionMessage = "down" },
            Timed(NineOClock.AddMinutes(59).AddSeconds(59), "hourly", 1000, JobRunResult.Cancelled),
            Timed(NineOClock.AddMinutes(75), "daily", 50, JobRunResult.Failed) with { RetryScheduled = true },
            Timed(NineOClock.AddMinutes(105), "daily", 150, JobRunResult.Succeeded) with { SchedulerInstanceId = "node-b" },
            Timed(Start, "elsewhere", 70, JobRunResult.Skipped) with { JobGroup = "other" }
        ];

        foreach (ExecutionHistoryEntry run in runs)
        {
            await store.AddExecution(run);
        }
    }

    private static ExecutionHistoryEntry Timed(DateTimeOffset firedAt, string jobName, int milliseconds, JobRunResult result)
    {
        return Execution(firedAt, jobName) with
        {
            Duration = TimeSpan.FromMilliseconds(milliseconds),
            Succeeded = result is JobRunResult.Succeeded or JobRunResult.Skipped,
            Result = result
        };
    }

    private static async Task<long> Runs(IExecutionHistoryStore store, Func<ExecutionStatisticsQuery, ExecutionStatisticsQuery> narrow)
    {
        ExecutionStatistics statistics = await store.QueryExecutionStatistics(narrow(new ExecutionStatisticsQuery { SchedulerName = SchedulerName }));
        return statistics.Buckets.Sum(bucket => bucket.RunCount);
    }
}
