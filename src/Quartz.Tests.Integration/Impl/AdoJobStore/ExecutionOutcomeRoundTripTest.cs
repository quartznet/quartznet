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

using System.Data.Common;
using System.Text;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Tests.Integration.TestHelpers;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The 4.4 outcome of a run — its result, summary, metrics and manual flag, and its job's
/// <c>QRTZ_JOB_STATUS</c> row — written, read and swept on every dialect.
/// </summary>
/// <remarks>
/// <para>
/// A SQLite-backed unit test proves the store's logic; only a real database of each dialect proves its
/// statements: Firebird's refusal of a parameter compared with a literal, MySQL's left-to-right <c>SET</c>,
/// Oracle's byte-counted <c>VARCHAR2</c> and its <c>CLOB</c> binding, each dialect's boolean and paging, and
/// a correlated <c>NOT EXISTS</c> in a <c>DELETE</c>.
/// </para>
/// <para>
/// Each case runs under a scheduler name of its own, so rows another case or another run left in the
/// container's database are not what it counts. The store's clock is frozen and never starts the sweep
/// timer, so a case sweeps exactly when it says so.
/// </para>
/// </remarks>
public abstract class ExecutionOutcomeRoundTripTest
{
    private string schedulerName;
    private FakeTimeProvider clock;
    private IDbProvider dbProvider;
    private StdAdoDelegate driverDelegate;

    /// <summary>The Quartz provider name of the ADO.NET driver.</summary>
    protected abstract string DbProviderName { get; }

    /// <summary>The delegate that speaks this database's dialect.</summary>
    protected abstract StdAdoDelegate CreateDriverDelegate();

    /// <summary>Makes the database ready and answers the connection string to reach it with.</summary>
    protected abstract ValueTask<string> PrepareDatabase();

    /// <summary>
    /// How many bytes of UTF-8 <c>SUMMARY</c>, <c>LAST_SUMMARY</c> and <c>LAST_FAILURE_MESSAGE</c> hold in
    /// this database, where they count bytes; <see langword="null" /> where they count characters. Stated
    /// from the dialect's schema script rather than asked of the store.
    /// </summary>
    protected virtual int? TextBytes => null;

    private DateTimeOffset Now => clock.GetUtcNow();

    [SetUp]
    public void NameThisCase()
    {
        schedulerName = "Outcome_" + GetType().Name.Replace("ExecutionOutcomeRoundTripTest", "", StringComparison.Ordinal)
                        + "_" + Guid.NewGuid().ToString("N")[..12];

        // Whole milliseconds, so what a dialect's BIGINT gives back compares equal whatever it rounds.
        DateTimeOffset now = DateTimeOffset.UtcNow;
        clock = new FakeTimeProvider(new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, TimeSpan.Zero));
    }

    [Test]
    public async Task TheOutcomeTheManualFlagAndAVetoRoundTrip()
    {
        using AdoExecutionHistoryStore store = await CreateStore(_ => { });

        await store.AddExecution(Run("nightly", "manual", Now.AddMinutes(-2), JobRunResult.Skipped) with
        {
            Summary = "nothing to do",
            MetricsJson = "{\"released\":0}",
            Manual = true,
            FireInstanceId = "node-a-17"
        });
        await store.AddExecution(Run("nightly", "scheduled", Now.AddMinutes(-1), JobRunResult.Succeeded));
        await store.AddExecution(Run("other", "other", Now, JobRunResult.Cancelled));

        List<ExecutionHistoryEntry> rows = (await store.QueryExecutions(Query())).Items.ToList();

        ExecutionHistoryEntry manual = rows.Should().ContainSingle(row => row.EntryId == "manual").Subject;
        manual.Result.Should().Be(JobRunResult.Skipped);
        manual.Succeeded.Should().BeTrue();
        manual.Summary.Should().Be("nothing to do");
        manual.MetricsJson.Should().Be("{\"released\":0}", "the listing carries the metrics; only the log is left out");
        manual.Manual.Should().BeTrue("MANUAL is written in this dialect's spelling of true and read back from it");
        manual.FireInstanceId.Should().Be("node-a-17");

        rows.Single(row => row.EntryId == "scheduled").Manual.Should().BeFalse("and of false");
        rows.Single(row => row.EntryId == "other").Result.Should().Be(JobRunResult.Cancelled);

        (await store.QueryExecutions(Query() with { Results = [JobRunResult.Skipped, JobRunResult.Cancelled] })).Items
            .Select(row => row.EntryId).Should().BeEquivalentTo(["manual", "other"]);
        (await store.QueryExecutions(Query() with { Job = new JobKey("nightly", "outcome"), FiredFrom = Now.AddMinutes(-1) })).Items
            .Should().ContainSingle().Which.EntryId.Should().Be("scheduled");
        (await store.QueryExecutions(Query() with { FiredBefore = Now.AddMinutes(-1), IncludeTotalCount = true })).TotalCount
            .Should().Be(1, "the end of the window is exclusive");

        await store.AddMisfire(new MisfireHistoryEntry(
            SchedulerName: schedulerName,
            SchedulerInstanceId: "node-a",
            TriggerGroup: "outcome",
            TriggerName: "vetoed",
            JobKey: new JobKey("nightly", "outcome"),
            MisfiredAtUtc: Now,
            ScheduledFireTimeUtc: Now)
        {
            Reason = MisfireReason.Vetoed
        });

        (await store.QueryMisfires(new MisfireHistoryQuery { SchedulerName = schedulerName, Job = new JobKey("nightly", "outcome") }))
            .Items.Should().ContainSingle().Which.Reason.Should().Be(MisfireReason.Vetoed, "REASON = 2 comes back as a veto");
        (await store.CountMisfires(schedulerName, Now.AddHours(-1))).Should().Be(0, "a veto is not a misfire");

        await store.AddMisfire(new MisfireHistoryEntry(schedulerName, "node-a", "outcome", "missed", null, Now.AddMinutes(-1), null));

        PagedResult<MisfireHistoryEntry> readable = await store.QueryMisfires(new MisfireHistoryQuery
        {
            SchedulerName = schedulerName,
            Reasons = [MisfireReason.Missed, MisfireReason.Overlap],
            IncludeTotalCount = true
        });

        readable.Items.Should().ContainSingle().Which.TriggerName.Should().Be("missed",
            "the reason filter binds each reason it names, and leaves the veto out of the page");
        readable.TotalCount.Should().Be(1, "and out of the count");
        (await store.QueryMisfires(new MisfireHistoryQuery { SchedulerName = schedulerName, Reasons = [MisfireReason.Vetoed] }))
            .Items.Should().ContainSingle().Which.TriggerName.Should().Be("vetoed");
    }

    [TestCaseSource(typeof(MultibyteTexts), nameof(MultibyteTexts.Kinds))]
    public async Task AMultibyteSummaryAndFailureAreKeptToWhatTheirColumnsHold(MultibyteText kind)
    {
        string text = MultibyteTexts.Of(kind, JobRunReport.MaxSummaryLength);
        string kept = MultibyteTexts.Kept(text, JobRunReport.MaxSummaryLength, TextBytes);

        using AdoExecutionHistoryStore store = await CreateStore(_ => { });

        // The first run inserts the job's status and the second updates it: both statements cut.
        foreach ((string entryId, int minutes) in new[] { ("first", -1), ("second", 0) })
        {
            await store.AddExecution(Run("failing", entryId, Now.AddMinutes(minutes), JobRunResult.Failed) with
            {
                ExceptionMessage = text,
                Summary = text
            });

            ExecutionHistoryEntry read = await store.GetExecution(schedulerName, entryId);
            read.Should().NotBeNull("the store drops a row whose unit fails, so a missing row is the database refusing a text");
            read.Summary.Should().Be(kept, "SUMMARY is cut to what the column holds, in the unit it counts");
            read.ExceptionMessage.Should().Be(kept);

            JobRunStatus status = await store.GetJobRunStatus(schedulerName, new JobKey("failing", "outcome"));
            status.Should().NotBeNull();
            status.LastSummary.Should().Be(kept, "LAST_SUMMARY is cut as SUMMARY is, after the {0} run", entryId);
            status.LastFailureMessage.Should().Be(kept, "LAST_FAILURE_MESSAGE is cut as ERROR_MESSAGE is, after the {0} run", entryId);
        }
    }

    /// <summary>
    /// Metrics far longer than any string column, in characters of more than one byte, go through the
    /// large-text binding whole: the managed Oracle driver's <c>Clob</c>, where a <c>Varchar2</c> fails
    /// past 4,000 bytes.
    /// </summary>
    [Test]
    public async Task MetricsLongerThanAnyStringColumnRoundTripWhole()
    {
        string metrics = "{\"text\":\"" + string.Concat(Enumerable.Repeat("é日本", 2_000)) + "\"}";
        Encoding.UTF8.GetByteCount(metrics).Should().BeGreaterThan(4_000 * 4);

        using AdoExecutionHistoryStore store = await CreateStore(_ => { });
        await store.AddExecution(Run("measured", "measured", Now, JobRunResult.Succeeded) with { MetricsJson = metrics });

        ExecutionHistoryEntry read = await store.GetExecution(schedulerName, "measured");

        read.Should().NotBeNull("on Oracle a Varchar2 parameter past 4,000 bytes into the CLOB fails the whole unit");
        read.MetricsJson.Should().Be(metrics);
    }

    /// <summary>
    /// A run's input goes into <c>JOB_INPUT</c> whole and comes back on the single read only, and the flag
    /// that says one was too large is written and read in this dialect's spelling of true and false.
    /// </summary>
    /// <remarks>
    /// The input is longer than any string column, in characters of more than one byte, so it goes through
    /// the large-text binding as the metrics do.
    /// </remarks>
    [Test]
    public async Task TheInputAndItsTooLargeFlagRoundTrip()
    {
        string input = "{\"note\":\"" + string.Concat(Enumerable.Repeat("é日本", 2_000)) + "\"}";
        Encoding.UTF8.GetByteCount(input).Should().BeGreaterThan(4_000 * 4);

        using AdoExecutionHistoryStore store = await CreateStore(_ => { });
        await store.AddExecution(Run("invoice", "with-input", Now.AddMinutes(-2), JobRunResult.Failed) with { Input = input });
        await store.AddExecution(Run("invoice", "too-large", Now.AddMinutes(-1), JobRunResult.Failed) with { InputTooLarge = true });
        await store.AddExecution(Run("invoice", "without-input", Now, JobRunResult.Succeeded));

        List<ExecutionHistoryEntry> rows = (await store.QueryExecutions(Query())).Items.ToList();
        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(row => row.Input == null, "the listing leaves JOB_INPUT out, as it leaves the log out");
        rows.Single(row => row.EntryId == "too-large").InputTooLarge.Should().BeTrue("JOB_INPUT_TOO_LARGE is read back from this dialect's true");
        rows.Single(row => row.EntryId == "with-input").InputTooLarge.Should().BeFalse("and from its false");

        ExecutionHistoryEntry withInput = await store.GetExecution(schedulerName, "with-input");
        withInput.Should().NotBeNull("on Oracle a Varchar2 parameter past 4,000 bytes into the CLOB fails the whole unit");
        withInput.Input.Should().Be(input, "the input is kept whole: a cut one would run the job with something else");

        ExecutionHistoryEntry tooLarge = await store.GetExecution(schedulerName, "too-large");
        tooLarge.Input.Should().BeNull();
        tooLarge.InputTooLarge.Should().BeTrue();

        (await store.GetExecution(schedulerName, "without-input")).Input.Should().BeNull("no input is NULL, not an empty string");
    }

    /// <summary>
    /// The status row is inserted by a job's first run and updated by every later one, each kind of run
    /// through its own statement, and ends where <see cref="JobRunStatusFold" /> ends.
    /// </summary>
    [Test]
    public async Task TheStatusIsUpsertedAndFoldsOutOfOrderRunsAsTheFoldDoes()
    {
        using AdoExecutionHistoryStore store = await CreateStore(_ => { });
        JobKey job = new("folded", "outcome");

        ExecutionHistoryEntry[] runs =
        [
            Run(job.Name, "r1", Now.AddMinutes(-10), JobRunResult.Succeeded) with { Summary = "first" },
            Run(job.Name, "r2", Now.AddMinutes(-9), JobRunResult.Failed) with { RetryScheduled = true, ExceptionMessage = "retrying" },
            Run(job.Name, "r3", Now.AddMinutes(-8), JobRunResult.Failed) with { ExceptionMessage = "gave up" },
            Run(job.Name, "r4", Now.AddMinutes(-2), JobRunResult.Skipped) with { SchedulerInstanceId = "node-b" },
            Run(job.Name, "r5", Now.AddMinutes(-5), JobRunResult.Failed) with { ExceptionMessage = "completed late" },
            Run(job.Name, "r6", Now.AddMinutes(-1), JobRunResult.Cancelled),
            Run(job.Name, "r7", Now.AddMinutes(-1), JobRunResult.Failed) with { Summary = "reported, same instant" },
            Run(job.Name, "r8", Now.AddMinutes(-20), JobRunResult.Succeeded) with { Result = null, Summary = "a 4.3 row" }
        ];

        JobRunStatus folded = null;
        foreach (ExecutionHistoryEntry run in runs)
        {
            folded = JobRunStatusFold.Apply(folded, run);
            await store.AddExecution(run);

            (await store.GetJobRunStatus(schedulerName, job)).Should().BeEquivalentTo(folded,
                "the row the database keeps is the fold's, after {0}", run.EntryId);
        }
    }

    /// <summary>
    /// Every sweep statement runs on this dialect and deletes what it should: the longest window, a
    /// shorter window for its results, the per-job cap, the misfire feed and the statuses of deleted jobs.
    /// </summary>
    [Test]
    public async Task TheSweepTrimsByResultByJobAndForgetsDeletedJobs()
    {
        using AdoExecutionHistoryStore store = await CreateStore(options =>
        {
            options.Retention = TimeSpan.FromHours(1);
            options.RetentionByResult[JobRunResult.Failed] = TimeSpan.FromDays(3);
            options.MisfireRetention = TimeSpan.FromHours(1);
            options.MaxEntriesPerJob = 2;
        });

        await store.AddExecution(Run("tiered", "tier-success", Now.AddDays(-2), JobRunResult.Succeeded));
        await store.AddExecution(Run("tiered", "tier-failure", Now.AddDays(-2), JobRunResult.Failed));
        await store.AddExecution(Run("tiered", "too-old-failure", Now.AddDays(-4), JobRunResult.Failed));
        await store.AddExecution(Run("capped", "cap-failure", Now.AddMinutes(-50), JobRunResult.Failed));

        foreach (int minutes in new[] { 40, 30, 20, 10 })
        {
            await store.AddExecution(Run("capped", "cap-" + minutes, Now.AddMinutes(-minutes), JobRunResult.Succeeded));
        }

        await store.AddExecution(Run("deleted", "deleted-job", Now.AddDays(-4), JobRunResult.Succeeded));
        await store.AddMisfire(new MisfireHistoryEntry(schedulerName, "node-a", "outcome", "missed", null, Now.AddHours(-2), null));

        await store.Sweep();

        (await store.QueryExecutions(Query() with { FiredFrom = DateTimeOffset.UnixEpoch })).Items.Select(row => row.EntryId).Should()
            .BeEquivalentTo(["tier-failure", "cap-failure", "cap-20", "cap-10"],
                "the success is past its hour, the old failure past the longest window, and the capped job keeps its two "
                + "newest successes and every failure");
        (await store.QueryMisfires(new MisfireHistoryQuery { SchedulerName = schedulerName })).Items.Should().BeEmpty(
            "the misfire feed is kept for MisfireRetention");

        (await StatusNames(store, new JobRunStatusQuery { SchedulerName = schedulerName })).Should().Equal(["capped", "tiered"],
            "the deleted job's status is past the longest window with no job behind it, and goes");
        (await StatusNames(store, new JobRunStatusQuery { SchedulerName = schedulerName, Failing = true })).Should().Equal(["tiered"]);
        (await StatusNames(store, new JobRunStatusQuery
        {
            SchedulerName = schedulerName,
            Jobs = [new JobKey("capped", "outcome"), new JobKey("deleted", "outcome")]
        })).Should().Equal(["capped"]);
    }

    /// <summary>
    /// The sweep's election reads this dialect's check-ins: a live node with a lower instance id sweeps,
    /// and one that stopped checking in does not.
    /// </summary>
    [Test]
    public async Task TheElectionReadsThisDialectsCheckIns()
    {
        using AdoExecutionHistoryStore store = await CreateStore(options => options.MaxEntriesPerScheduler = 1);

        await store.AddExecution(Run("counted", "older", Now.AddMinutes(-2), JobRunResult.Succeeded));
        await store.AddExecution(Run("counted", "newer", Now.AddMinutes(-1), JobRunResult.Succeeded));

        try
        {
            await CheckIn("node-0", Now);
            await store.Sweep();

            (await store.QueryExecutions(Query())).Items.Should().HaveCount(2,
                "node-0 checked in just now and sorts before node-a, so the sweep is node-0's");

            await CheckIn("node-0", Now.AddMinutes(-1));
            await store.Sweep();

            (await store.QueryExecutions(Query())).Items.Should().ContainSingle(
                    "node-0 has missed two check-ins, so node-a sweeps and the count bound keeps one row")
                .Which.EntryId.Should().Be("newer");
        }
        finally
        {
            await WithConnection(conn => driverDelegate.DeleteSchedulerState(conn, "node-0"));
        }
    }

    /// <summary>
    /// The run statistics on this dialect: its bucket expression, its effective result, and its percentiles
    /// — the database's own on PostgreSQL and Oracle, interpolated from ranked rows elsewhere — against the
    /// fixture <c>ExecutionHistoryStoreContractTest</c> works out by hand.
    /// </summary>
    /// <remarks>
    /// Nine o'clock holds 100, 200, 300, 400 and 1,000 ms: a median of 300 and a 95th percentile of
    /// 400 + 0.8 × 600 = 880. Ten holds 50 and 150: 100 and 145. Eleven holds nothing, and the hour now holds
    /// one run of another group.
    /// </remarks>
    [Test]
    public async Task RunsAreCountedByResultAndTimedPerBucket()
    {
        using AdoExecutionHistoryStore store = await CreateStore(_ => { });

        DateTimeOffset hour = new(Now.UtcTicks / TimeSpan.TicksPerHour * TimeSpan.TicksPerHour, TimeSpan.Zero);
        DateTimeOffset nine = hour.AddHours(-3);

        ExecutionHistoryEntry[] runs =
        [
            Timed("hourly", "s1", nine.AddMinutes(5), 100, JobRunResult.Succeeded),
            Timed("hourly", "s2", nine.AddMinutes(10), 200, JobRunResult.Succeeded),
            Timed("hourly", "k1", nine.AddMinutes(20), 300, JobRunResult.Skipped),
            Timed("hourly", "f1", nine.AddMinutes(30), 400, JobRunResult.Failed),
            Timed("hourly", "c1", nine.AddMinutes(59).AddSeconds(59), 1000, JobRunResult.Cancelled),
            Timed("daily", "f2", nine.AddMinutes(75), 50, JobRunResult.Failed) with { RetryScheduled = true },
            Timed("daily", "s3", nine.AddMinutes(105), 150, JobRunResult.Succeeded) with { SchedulerInstanceId = "node-b" },
            Timed("elsewhere", "k2", hour, 70, JobRunResult.Skipped) with { JobGroup = "other" },
            Timed("legacy", "legacy", nine.AddMinutes(150), 20, JobRunResult.Failed) with { JobGroup = "legacy", Result = null }
        ];

        foreach (ExecutionHistoryEntry run in runs)
        {
            await store.AddExecution(run);
        }

        ExecutionStatistics statistics = await store.QueryExecutionStatistics(Statistics() with { JobGroup = "outcome" });

        statistics.Buckets.Select(bucket => bucket.StartUtc).Should().Equal([nine, nine.AddHours(1)],
            "a bucket starts on the hour, in this dialect's integer division");

        ExecutionStatisticsBucket first = statistics.Buckets[0];
        (first.SucceededCount, first.SkippedCount, first.FailedCount, first.CancelledCount).Should().Be((2L, 1L, 1L, 1L));
        first.P50Duration.Should().Be(TimeSpan.FromMilliseconds(300));
        first.P95Duration.Should().Be(TimeSpan.FromMilliseconds(880), "rank 3.8 lies 0.8 of the way from 400 to 1,000");
        first.MaxDuration.Should().Be(TimeSpan.FromMilliseconds(1000));

        ExecutionStatisticsBucket second = statistics.Buckets[1];
        (second.SucceededCount, second.FailedCount).Should().Be((1L, 1L));
        second.P50Duration.Should().Be(TimeSpan.FromMilliseconds(100));
        second.P95Duration.Should().Be(TimeSpan.FromMilliseconds(145));
        second.MaxDuration.Should().Be(TimeSpan.FromMilliseconds(150));

        ExecutionStatisticsBucket legacy = (await store.QueryExecutionStatistics(Statistics() with { JobGroup = "legacy" }))
            .Buckets.Should().ContainSingle().Subject;
        legacy.FailedCount.Should().Be(1, "a row without a RESULT counts as its SUCCEEDED says, in this dialect's spelling of false");

        (await RunCount(store, Statistics())).Should().Be(9);
        (await RunCount(store, Statistics() with { Job = new JobKey("hourly", "outcome") })).Should().Be(5);
        (await RunCount(store, Statistics() with { JobGroup = "outcome", FiredFrom = nine.AddHours(1), FiredBefore = hour })).Should().Be(2);
        (await RunCount(store, Statistics() with { Results = [JobRunResult.Failed, JobRunResult.Cancelled] })).Should().Be(4);
        (await RunCount(store, Statistics() with { SchedulerInstanceId = "node-b" })).Should().Be(1);
        (await RunCount(store, Statistics() with { JobContains = "daily" })).Should().Be(2);

        ExecutionStatistics daily = await store.QueryExecutionStatistics(Statistics() with { BucketSize = TimeSpan.FromDays(1) });
        daily.Buckets.Sum(bucket => bucket.RunCount).Should().Be(9);
        daily.Buckets.Should().OnlyContain(bucket => bucket.StartUtc.TimeOfDay == TimeSpan.Zero, "a day bucket starts at midnight UTC");
    }

    // ---------------------------------------------------------------------------------------------

    private ExecutionStatisticsQuery Statistics() => new() { SchedulerName = schedulerName };

    private static async Task<long> RunCount(AdoExecutionHistoryStore store, ExecutionStatisticsQuery query)
    {
        return (await store.QueryExecutionStatistics(query)).Buckets.Sum(bucket => bucket.RunCount);
    }

    private ExecutionHistoryEntry Timed(string jobName, string entryId, DateTimeOffset firedAt, int milliseconds, JobRunResult result)
    {
        return Run(jobName, entryId, firedAt, result) with { Duration = TimeSpan.FromMilliseconds(milliseconds) };
    }

    private ExecutionHistoryQuery Query() => new() { SchedulerName = schedulerName };

    private ExecutionHistoryEntry Run(string jobName, string entryId, DateTimeOffset firedAt, JobRunResult result) => new(
        SchedulerName: schedulerName,
        SchedulerInstanceId: "node-a",
        JobGroup: "outcome",
        JobName: jobName,
        TriggerGroup: "outcome",
        TriggerName: jobName,
        FiredAtUtc: firedAt,
        Duration: TimeSpan.FromMilliseconds(1234),
        Succeeded: result is JobRunResult.Succeeded or JobRunResult.Skipped,
        ExceptionMessage: null)
    {
        EntryId = entryId,
        Result = result
    };

    private static async Task<List<string>> StatusNames(AdoExecutionHistoryStore store, JobRunStatusQuery query)
    {
        return (await store.QueryJobRunStatuses(query)).Items.Select(status => status.Job.Name).ToList();
    }

    /// <summary>A check-in row for this case's scheduler, replacing any earlier one of the node.</summary>
    private Task CheckIn(string instanceId, DateTimeOffset at)
    {
        return WithConnection(async conn =>
        {
            await driverDelegate.DeleteSchedulerState(conn, instanceId);
            return await driverDelegate.InsertSchedulerState(conn, instanceId, at, TimeSpan.FromSeconds(7.5));
        });
    }

    private async Task WithConnection(Func<ConnectionAndTransactionHolder, ValueTask<int>> action)
    {
        await using DbConnection connection = dbProvider.CreateConnection();
        await connection.OpenAsync();
        await action(new ConnectionAndTransactionHolder(connection, transaction: null, ownsResources: false));
    }

    private async ValueTask<AdoExecutionHistoryStore> CreateStore(Action<ExecutionHistoryOptions> configure)
    {
        string connectionString = await PrepareDatabase();
        dbProvider = new DbProvider(DbProviderName, connectionString);

        driverDelegate = CreateDriverDelegate();
        driverDelegate.Initialize(new DriverDelegateContext
        {
            TablePrefix = "QRTZ_",
            SchedulerName = schedulerName,
            InstanceId = "node-a",
            DbProvider = dbProvider,
            TypeLoader = new SimpleTypeLoader(),
        });

        ExecutionHistoryOptions options = new();
        configure(options);

        return new AdoExecutionHistoryStore(
            dbProvider,
            driverDelegate,
            Options.Create(options),
            Options.Create(new QuartzSchedulerOptions { InstanceName = schedulerName }),
            new FrozenClock(clock));
    }

    /// <summary>
    /// The case's frozen clock, with no timer that ever fires: the case sweeps where it says so.
    /// </summary>
    private sealed class FrozenClock(FakeTimeProvider inner) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => inner.GetUtcNow();

        public override ITimer CreateTimer(TimerCallback callback, object state, TimeSpan dueTime, TimeSpan period) => new NeverFiringTimer();

        private sealed class NeverFiringTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => default;
        }
    }

    /// <summary>
    /// The connection string the container this assembly started published, as the job store contract
    /// fixtures read it.
    /// </summary>
    protected static string ContainerConnectionString(string variableName)
    {
        string connectionString = Environment.GetEnvironmentVariable(variableName);

        connectionString.Should().NotBeNullOrWhiteSpace(
            "{0} is set by the container this assembly starts, so an empty one means the container for this leg "
            + "never started — run the fixture through its own QUARTZ_TEST_DATABASE leg",
            variableName);

        return connectionString;
    }
}

[TestFixture]
[NonParallelizable]
[Category("db-postgres")]
public sealed class PostgresExecutionOutcomeRoundTripTest : ExecutionOutcomeRoundTripTest
{
    protected override string DbProviderName => DataSourceOptions.Providers.Npgsql;

    protected override StdAdoDelegate CreateDriverDelegate() => new PostgreSQLDelegate();

    protected override ValueTask<string> PrepareDatabase() => new(ContainerConnectionString("PG_CONNECTION_STRING"));
}

[TestFixture]
[NonParallelizable]
[Category("db-sqlserver")]
public sealed class SqlServerExecutionOutcomeRoundTripTest : ExecutionOutcomeRoundTripTest
{
    protected override string DbProviderName => DataSourceOptions.Providers.SqlServer;

    protected override StdAdoDelegate CreateDriverDelegate() => new SqlServerDelegate();

    protected override ValueTask<string> PrepareDatabase() => new(ContainerConnectionString("MSSQL_CONNECTION_STRING"));
}

[TestFixture]
[NonParallelizable]
[Category("db-mysql")]
public sealed class MySqlExecutionOutcomeRoundTripTest : ExecutionOutcomeRoundTripTest
{
    protected override string DbProviderName => DataSourceOptions.Providers.MySqlConnector;

    protected override StdAdoDelegate CreateDriverDelegate() => new MySQLDelegate();

    protected override ValueTask<string> PrepareDatabase() => new(ContainerConnectionString("MYSQL_CONNECTION_STRING"));
}

[TestFixture]
[NonParallelizable]
[Category("db-oracle")]
public sealed class OracleExecutionOutcomeRoundTripTest : ExecutionOutcomeRoundTripTest
{
    protected override string DbProviderName => DataSourceOptions.Providers.Oracle;

    protected override StdAdoDelegate CreateDriverDelegate() => new OracleDelegate();

    /// <summary>The width <c>tables_oracle.sql</c> declares, which its <c>VARCHAR2</c> counts in bytes.</summary>
    protected override int? TextBytes => 4000;

    protected override ValueTask<string> PrepareDatabase() => new(ContainerConnectionString("ORACLE_CONNECTION_STRING"));
}

[TestFixture]
[NonParallelizable]
[Category("db-firebird")]
public sealed class FirebirdExecutionOutcomeRoundTripTest : ExecutionOutcomeRoundTripTest
{
    protected override string DbProviderName => DataSourceOptions.Providers.Firebird;

    protected override StdAdoDelegate CreateDriverDelegate() => new FirebirdDelegate();

    /// <summary>
    /// The width <c>tables_firebird.sql</c> declares. The fixture's database has no default character set,
    /// where a <c>VARCHAR</c> counts bytes.
    /// </summary>
    protected override int? TextBytes => 1000;

    protected override ValueTask<string> PrepareDatabase() => new(ContainerConnectionString("FIREBIRD_CONNECTION_STRING"));
}

/// <summary>
/// The SQLite leg, on a file built from the fresh-install script — the one dialect that needs no container.
/// </summary>
[TestFixture]
[NonParallelizable]
[Category("db-sqlite")]
public sealed class SqliteExecutionOutcomeRoundTripTest : ExecutionOutcomeRoundTripTest
{
    private SqliteTestDatabase database;

    protected override string DbProviderName => "SQLite-Microsoft";

    protected override StdAdoDelegate CreateDriverDelegate() => new SQLiteDelegate();

    protected override async ValueTask<string> PrepareDatabase()
    {
        if (database is not null)
        {
            return database.ConnectionString;
        }

        database = new SqliteTestDatabase("execution-outcome");

        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = new(await File.ReadAllTextAsync(path), connection);
        await command.ExecuteNonQueryAsync();

        return database.ConnectionString;
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database?.Dispose();
        database = null;
    }
}
