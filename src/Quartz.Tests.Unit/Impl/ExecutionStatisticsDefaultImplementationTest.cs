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

using FakeItEasy;

using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// What <see cref="IExecutionHistoryStore.QueryExecutionStatistics" /> answers for a store written before
/// 4.4, which has only <see cref="IExecutionHistoryStore.QueryExecutions" /> to count from.
/// </summary>
/// <remarks>
/// Through a fake that answers the listing from a list and leaves the statistics to the interface's own
/// body, as a store of an application's own does.
/// </remarks>
public sealed class ExecutionStatisticsDefaultImplementationTest
{
    private const string SchedulerName = "Reporting";

    private static readonly DateTimeOffset Noon = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly List<ExecutionHistoryQuery> asked = [];

    [SetUp]
    public void ForgetWhatWasAsked()
    {
        asked.Clear();
    }

    [Test]
    public async Task TheDefaultCountsWhatTheListingHoldsAsAStoreThatCountsItselfDoes()
    {
        List<ExecutionHistoryEntry> rows = Rows(2_500, Noon.AddHours(-3));
        IExecutionHistoryStore store = StoreOver(rows);

        ExecutionStatistics counted = await store.QueryExecutionStatistics(new ExecutionStatisticsQuery { SchedulerName = SchedulerName });

        InMemoryExecutionHistoryStore inMemory = new(
            Options.Create(new ExecutionHistoryOptions { MaxEntriesPerScheduler = 10_000 }), new FakeTimeProvider(Noon));
        foreach (ExecutionHistoryEntry row in rows)
        {
            await inMemory.AddExecution(row);
        }

        ExecutionStatistics expected = await inMemory.QueryExecutionStatistics(new ExecutionStatisticsQuery { SchedulerName = SchedulerName });

        counted.Truncated.Should().BeFalse("every row was read");
        counted.Buckets.Should().BeEquivalentTo(expected.Buckets, options => options.WithStrictOrdering(),
            "the default and a store that counts its own rows apply the same arithmetic to the same rows");
        counted.Buckets.Sum(bucket => bucket.RunCount).Should().Be(2_500);

        asked.Select(query => (query.Skip, query.Take)).Should().Equal(
            [(0, 1_000), (1_000, 1_000), (2_000, 1_000)],
            "the listing is read a thousand rows at a time, so no one answer is the whole history");
    }

    [Test]
    public async Task TheDefaultStopsAtItsRowLimitAndSaysSo()
    {
        List<ExecutionHistoryEntry> rows = Rows(ExecutionStatistics.DefaultRowLimit + 500, Noon.AddHours(-6));
        IExecutionHistoryStore store = StoreOver(rows);

        ExecutionStatistics counted = await store.QueryExecutionStatistics(new ExecutionStatisticsQuery { SchedulerName = SchedulerName });

        counted.Truncated.Should().BeTrue("500 rows were left unread");
        counted.Buckets.Sum(bucket => bucket.RunCount).Should().Be(ExecutionStatistics.DefaultRowLimit,
            "the newest ten thousand are counted and the oldest five hundred are not");
        asked.Should().HaveCount(10);
    }

    [Test]
    public async Task TheListingsFiltersAreHandedToTheListing()
    {
        IExecutionHistoryStore store = StoreOver([]);

        await store.QueryExecutionStatistics(new ExecutionStatisticsQuery
        {
            SchedulerName = SchedulerName,
            SchedulerInstanceId = "node-b",
            JobContains = "night",
            TriggerContains = "midnight",
            FailedFinally = true,
            Job = new JobKey("nightly", "reports"),
            FiredFrom = Noon.AddDays(-1),
            FiredBefore = Noon,
            Results = [JobRunResult.Failed]
        });

        ExecutionHistoryQuery listing = asked.Should().ContainSingle().Subject;
        listing.SchedulerName.Should().Be(SchedulerName);
        listing.SchedulerInstanceId.Should().Be("node-b");
        listing.JobContains.Should().Be("night");
        listing.TriggerContains.Should().Be("midnight");
        listing.FailedFinally.Should().BeTrue();
        listing.Job.Should().Be(new JobKey("nightly", "reports"));
        listing.FiredFrom.Should().Be(Noon.AddDays(-1));
        listing.FiredBefore.Should().Be(Noon);
        listing.Results.Should().Equal([JobRunResult.Failed]);
    }

    [Test]
    public async Task AGroupIsNarrowedByTheKeyFilterAndMatchedExactly()
    {
        List<ExecutionHistoryEntry> rows =
        [
            Row(Noon.AddMinutes(-1), "a", "reports", "a"),
            Row(Noon.AddMinutes(-2), "b", "reports-archive", "b"),
            Row(Noon.AddMinutes(-3), "c", "billing", "c")
        ];
        IExecutionHistoryStore store = StoreOver(rows);

        ExecutionStatistics counted = await store.QueryExecutionStatistics(new ExecutionStatisticsQuery
        {
            SchedulerName = SchedulerName,
            JobGroup = "reports"
        });

        asked.Should().ContainSingle().Which.JobContains.Should().Be("reports",
            "a listing has no group filter, and the key filter finds every row of the group");
        counted.Buckets.Sum(bucket => bucket.RunCount).Should().Be(1, "reports-archive contains the name but is another group");
    }

    [Test]
    public async Task ARowReadTwiceAcrossPagesIsCountedOnce()
    {
        List<ExecutionHistoryEntry> rows = Rows(1_000, Noon.AddHours(-1));
        IExecutionHistoryStore store = A.Fake<IExecutionHistoryStore>();
        A.CallTo(() => store.QueryExecutionStatistics(A<ExecutionStatisticsQuery>._, A<CancellationToken>._)).CallsBaseMethod();

        // A row recorded between the two reads pushes the page down by one, so the second page starts with
        // the first page's last row.
        A.CallTo(() => store.QueryExecutions(A<ExecutionHistoryQuery>._, A<CancellationToken>._))
            .ReturnsLazily((ExecutionHistoryQuery query, CancellationToken _) => query.Skip == 0
                ? new ValueTask<PagedResult<ExecutionHistoryEntry>>(new PagedResult<ExecutionHistoryEntry>(rows, HasMore: true))
                : new ValueTask<PagedResult<ExecutionHistoryEntry>>(new PagedResult<ExecutionHistoryEntry>([rows[^1]], HasMore: false)));

        ExecutionStatistics counted = await store.QueryExecutionStatistics(new ExecutionStatisticsQuery { SchedulerName = SchedulerName });

        counted.Buckets.Sum(bucket => bucket.RunCount).Should().Be(1_000, "a row named by its entry id is counted once");
    }

    [Test]
    public void ABucketShorterThanAMinuteIsRefused()
    {
        Action act = () => _ = new ExecutionStatisticsQuery { SchedulerName = SchedulerName, BucketSize = TimeSpan.FromSeconds(30) };

        act.Should().Throw<ArgumentOutOfRangeException>("a minute is the narrowest bucket the stores count in");
    }

    /// <summary>A store with nothing but a listing, answered from <paramref name="rows" /> newest first.</summary>
    private IExecutionHistoryStore StoreOver(List<ExecutionHistoryEntry> rows)
    {
        List<ExecutionHistoryEntry> newestFirst = rows.OrderByDescending(row => row.FiredAtUtc).ToList();

        IExecutionHistoryStore store = A.Fake<IExecutionHistoryStore>();
        A.CallTo(() => store.QueryExecutionStatistics(A<ExecutionStatisticsQuery>._, A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => store.QueryExecutions(A<ExecutionHistoryQuery>._, A<CancellationToken>._))
            .ReturnsLazily((ExecutionHistoryQuery query, CancellationToken _) =>
            {
                asked.Add(query);
                List<ExecutionHistoryEntry> page = newestFirst.Skip(query.Skip).Take(query.Take).ToList();
                return new ValueTask<PagedResult<ExecutionHistoryEntry>>(
                    new PagedResult<ExecutionHistoryEntry>(page, query.Skip + page.Count < newestFirst.Count));
            });

        return store;
    }

    /// <summary>Rows a minute apart from <paramref name="start" />, with every result and a spread of durations.</summary>
    private static List<ExecutionHistoryEntry> Rows(int count, DateTimeOffset start)
    {
        JobRunResult[] results = [JobRunResult.Succeeded, JobRunResult.Succeeded, JobRunResult.Failed, JobRunResult.Skipped, JobRunResult.Cancelled];

        List<ExecutionHistoryEntry> rows = new(count);
        for (int i = 0; i < count; i++)
        {
            JobRunResult result = results[i % results.Length];
            rows.Add(Row(start.AddSeconds(i * 7), "job-" + (i % 3), "reports", "entry-" + i) with
            {
                Duration = TimeSpan.FromMilliseconds(10 + i * 37 % 1_000),
                Result = result,
                Succeeded = result is JobRunResult.Succeeded or JobRunResult.Skipped
            });
        }

        return rows;
    }

    private static ExecutionHistoryEntry Row(DateTimeOffset firedAt, string jobName, string jobGroup, string entryId) => new(
        SchedulerName: SchedulerName,
        SchedulerInstanceId: "node-a",
        JobGroup: jobGroup,
        JobName: jobName,
        TriggerGroup: "nightly",
        TriggerName: "at-midnight",
        FiredAtUtc: firedAt,
        Duration: TimeSpan.FromMilliseconds(5),
        Succeeded: true,
        ExceptionMessage: null)
    {
        EntryId = entryId
    };
}
