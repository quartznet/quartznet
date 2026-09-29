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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// What only the database history has to get right about the 4.4 outcome: the status's transaction, two
/// nodes racing a job's first run, rows a 4.3 node wrote, the election of one sweeper, and the statuses of
/// deleted jobs.
/// </summary>
public sealed partial class AdoExecutionHistoryStoreContractTest
{
    private static readonly JobKey reports = new("reports-nightly", JobGroup);

    /// <summary>
    /// The row and the status commit together: a status write that fails takes the row with it.
    /// </summary>
    /// <remarks>
    /// The update is made to throw for a failed run, which is every way the second statement of the unit
    /// can fail. Written with autocommit, as every history write was before 4.4, the row would stay while
    /// the status never counted it.
    /// </remarks>
    [Test]
    public async Task AStatusWriteThatFailsTakesItsRowWithIt()
    {
        await CreateStore(_ => { });
        RecordingLoggerProvider logs = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(logging => logging.AddProvider(logs));
        using AdoExecutionHistoryStore history = StoreOver(new FailedRunStatusFailingDelegate(), loggerFactory: loggerFactory);

        await history.AddExecution(Run(Start, reports.Name, JobRunResult.Succeeded));

        (await RowCount()).Should().Be(1, "a successful run's unit commits whole");
        (await StatusRowCount()).Should().Be(1);

        Func<Task> failed = async () => await history.AddExecution(Run(Start.AddMinutes(1), reports.Name, JobRunResult.Failed));
        await failed.Should().NotThrowAsync("a history write that fails is logged and dropped, never the firing's failure");

        (await RowCount()).Should().Be(1, "the execution row was rolled back with the status update that failed");
        (await history.GetJobRunStatus(SchedulerName, reports))!.RunCount.Should().Be(1,
            "a status never counts a run whose row is not there");

        await history.AddExecution(Run(Start.AddMinutes(2), "never-recorded", JobRunResult.Failed));

        (await RowCount()).Should().Be(1, "a job's first run that fails to fold leaves no row");
        (await history.GetJobRunStatus(SchedulerName, new JobKey("never-recorded", JobGroup))).Should().BeNull(
            "and no status either: the two commit together or not at all");
        logs.Entries.Where(entry => entry.EventId.Id == 3160).Should().HaveCount(2, "each lost row is reported");
    }

    /// <summary>
    /// Two nodes recording a job's first run at once both count.
    /// </summary>
    /// <remarks>
    /// The interleaving is the one that loses a run: this node's update finds no row because the other
    /// node's insert had not committed yet, then its own insert fails on the key. The delegate answers the
    /// first update with no rows to stand for that moment. The unit is rolled back and run again, so the
    /// row is not written twice and the status counts both runs.
    /// </remarks>
    [Test]
    public async Task TwoNodesRecordingAJobsFirstRunAtOnceBothCount()
    {
        IExecutionHistoryStore other = await CreateStore(_ => { });
        using AdoExecutionHistoryStore late = StoreOver(new LateToTheFirstRunDelegate());

        await other.AddExecution(Run(Start, reports.Name, JobRunResult.Succeeded) with { SchedulerInstanceId = "node-b" });
        await late.AddExecution(Run(Start.AddSeconds(1), reports.Name, JobRunResult.Failed));

        JobRunStatus status = (await late.GetJobRunStatus(SchedulerName, reports))!;

        status.RunCount.Should().Be(2, "the insert that lost the race is rolled back and the unit runs again as an update");
        status.FailureCount.Should().Be(1);
        status.LastResult.Should().Be(JobRunResult.Failed);
        (await RowCount()).Should().Be(2, "the rolled-back unit took its row with it, and the rerun wrote it once");
    }

    /// <summary>
    /// A row a 4.3 node wrote has no result: it reads as its <c>SUCCEEDED</c> flag says, and is kept, capped
    /// and filtered by that result.
    /// </summary>
    /// <remarks>
    /// Written with the 4.3 insert's columns, so every 4.4 column is <c>NULL</c> — the row a mixed cluster
    /// leaves behind while it rolls.
    /// </remarks>
    [Test]
    public async Task ARowA43NodeWroteReadsAndIsKeptByTheResultItsFlagImplies()
    {
        IExecutionHistoryStore store = await CreateStore(options =>
        {
            options.Retention = TimeSpan.FromHours(1);
            options.RetentionByResult[JobRunResult.Failed] = TimeSpan.FromDays(30);
            options.MaxEntriesPerJob = 1;
        });

        await InsertLegacyRow("legacy-success-old", Start.AddMinutes(-2), succeeded: true);
        await InsertLegacyRow("legacy-failure", Start.AddMinutes(-1), succeeded: false);
        await InsertLegacyRow("legacy-success-new", Start, succeeded: true);

        List<ExecutionHistoryEntry> rows = (await Executions(store)).Items.ToList();
        rows.Should().OnlyContain(row => row.Result == null && row.Summary == null && row.MetricsJson == null
                                         && !row.Manual && row.FireInstanceId == null,
            "a 4.3 node writes none of the outcome columns");
        rows.Single(row => row.EntryId == "legacy-failure").EffectiveResult.Should().Be(JobRunResult.Failed);

        (await store.QueryExecutions(new ExecutionHistoryQuery { SchedulerName = SchedulerName, Results = [JobRunResult.Failed] }))
            .Items.Should().ContainSingle().Which.EntryId.Should().Be("legacy-failure",
                "a result filter reads a row without a result through its SUCCEEDED flag");

        await ApplyBounds(store);
        (await Executions(store)).Items.Select(row => row.EntryId).Should().BeEquivalentTo(["legacy-success-new", "legacy-failure"],
            "the per-job cap counts the old success and not the failure, which it exempts");

        Clock.Advance(TimeSpan.FromDays(1));
        await ApplyBounds(store);
        (await Executions(store)).Items.Should().ContainSingle().Which.EntryId.Should().Be("legacy-failure",
            "the success is kept for the default hour and the failure for the Failed tier's month");
    }

    /// <summary>
    /// A misfire a 4.2 node wrote has no <c>REASON</c>, reads as <see cref="MisfireReason.Missed" />, and is
    /// found by that reason and by no other.
    /// </summary>
    [Test]
    public async Task AMisfireWithoutAReasonIsFoundAsMissed()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });

        await using (SqliteConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO QRTZ_MISFIRE_HISTORY (SCHED_NAME, ENTRY_ID, INSTANCE_NAME, TRIGGER_NAME, TRIGGER_GROUP, JOB_NAME, "
                + "JOB_GROUP, MISFIRE_TIME, SCHED_TIME) VALUES (@scheduler, 'a-4.2-row', 'node-a', 'at-midnight', @triggerGroup, "
                + "NULL, NULL, @misfired, NULL)";
            command.Parameters.AddWithValue("@scheduler", SchedulerName);
            command.Parameters.AddWithValue("@triggerGroup", TriggerGroup);
            command.Parameters.AddWithValue("@misfired", Start.UtcTicks);
            (await command.ExecuteNonQueryAsync()).Should().Be(1);
        }

        await store.AddMisfire(Misfire(Start.AddMinutes(-1), "skipped") with { Reason = MisfireReason.Overlap });

        PagedResult<MisfireHistoryEntry> missed = await store.QueryMisfires(new MisfireHistoryQuery
        {
            SchedulerName = SchedulerName,
            Reasons = [MisfireReason.Missed],
            IncludeTotalCount = true
        });

        missed.Items.Should().ContainSingle().Which.Reason.Should().Be(MisfireReason.Missed,
            "a 4.2 node recorded misfires only, so a row without a reason is one, and is filtered as it reads");
        missed.TotalCount.Should().Be(1);

        (await store.QueryMisfires(new MisfireHistoryQuery { SchedulerName = SchedulerName, Reasons = [MisfireReason.Overlap] }))
            .Items.Should().ContainSingle().Which.TriggerName.Should().Be("skipped");
    }

    /// <summary>
    /// A live node with a lower instance id sweeps the cluster, and this one leaves the pass to it.
    /// </summary>
    [Test]
    public async Task ALiveNodeWithALowerInstanceIdSweepsInsteadOfThisOne()
    {
        await CreateStore(_ => { });
        RecordingLoggerProvider logs = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
        using AdoExecutionHistoryStore history = StoreOver(
            new SQLiteDelegate(), options => options.Retention = TimeSpan.FromHours(1), loggerFactory);

        // The write is what tells the store which node it is.
        await history.AddExecution(Execution(Start, "expired"));
        Clock.Advance(TimeSpan.FromHours(2));
        await CheckIn("node-0", Clock.GetUtcNow());

        await history.Sweep();
        await history.Sweep();

        (await RowCount()).Should().Be(1, "node-0 checked in a moment ago and sorts before node-a, so the sweep is node-0's");
        logs.Entries.Where(entry => entry.EventId.Id == 3163).Should().ContainSingle(
                "the node says it is leaving the sweep to another once, not every pass")
            .Which.Message.Should().Contain("node-0");

        Clock.Advance(TimeSpan.FromMinutes(1));
        await history.Sweep();

        (await RowCount()).Should().Be(0,
            "node-0 has missed twice its check-in interval, so it is not sweeping, and the next node in line does");
    }

    /// <summary>
    /// With no live node below it — no rows at all, or only higher or stale ones — a node sweeps.
    /// </summary>
    [Test]
    public async Task ANodeWithNoLiveLowerNodeSweeps()
    {
        await CreateStore(_ => { });
        using AdoExecutionHistoryStore history = StoreOver(new SQLiteDelegate(), options => options.Retention = TimeSpan.FromHours(1));

        await history.AddExecution(Execution(Start, "first"));
        Clock.Advance(TimeSpan.FromHours(2));
        await history.Sweep();
        (await RowCount()).Should().Be(0, "a store with no check-ins at all — not clustered, or SQLite — sweeps");

        await CheckIn("node-z", Clock.GetUtcNow());
        await CheckIn("node-0", Clock.GetUtcNow() - TimeSpan.FromMinutes(5));
        await history.AddExecution(Execution(Clock.GetUtcNow(), "second"));
        Clock.Advance(TimeSpan.FromHours(2));
        await CheckIn("node-z", Clock.GetUtcNow());
        await history.Sweep();

        (await RowCount()).Should().Be(0, "a live node sorting after this one, and a lower one long gone, leave the sweep here");
    }

    /// <summary>
    /// The status of a job that is gone is forgotten, but only once it is past the longest window.
    /// </summary>
    [Test]
    public async Task TheStatusOfADeletedJobIsForgottenOncePastTheLongestWindow()
    {
        IExecutionHistoryStore store = await CreateStore(options =>
        {
            options.Retention = TimeSpan.FromHours(1);
            options.RetentionByResult[JobRunResult.Failed] = TimeSpan.FromDays(2);
        });

        JobKey kept = new("still-scheduled", JobGroup);
        JobKey gone = new("deleted", JobGroup);
        await KeepJob(kept);

        await store.AddExecution(Run(Start, kept.Name, JobRunResult.Succeeded));
        await store.AddExecution(Run(Start, gone.Name, JobRunResult.Succeeded));

        Clock.Advance(TimeSpan.FromDays(1));
        await ApplyBounds(store);
        (await StatusNames(store)).Should().BeEquivalentTo([kept.Name, gone.Name],
            "within the longest window a status is kept, job or no job: a job deleted and scheduled again keeps its counts");

        Clock.Advance(TimeSpan.FromDays(2));
        await ApplyBounds(store);
        (await StatusNames(store)).Should().Equal([kept.Name],
            "a deleted job's status goes once it is older than any row could be, and a stored job's never does");
    }

    /// <summary>
    /// A window of <see cref="TimeSpan.MaxValue" /> on every result keeps everything, and still leaves a
    /// sweep timer a real clock can run.
    /// </summary>
    /// <remarks>
    /// On the system clock, because the fake one takes any period: a tenth of forever is a period
    /// <see cref="System.Threading.Timer" /> refuses, and the refusal came out of the write that started it.
    /// </remarks>
    [Test]
    public async Task AWindowOfForeverOnEveryResultKeepsEverythingAndATimerCanRunIt()
    {
        await CreateStore(_ => { });
        using AdoExecutionHistoryStore history = StoreOver(
            new SQLiteDelegate(), options => options.Retention = TimeSpan.MaxValue, timeProvider: TimeProvider.System);

        history.SweepInterval().Should().Be(AdoExecutionHistoryStore.MaximumSweepInterval,
            "a sweep interval of a tenth of forever is capped at a day");

        Func<Task> write = async () => await history.AddExecution(Execution(DateTimeOffset.UtcNow.AddYears(-50), "ancient"));
        await write.Should().NotThrowAsync("the first write starts the sweep timer, and its period has to be one a timer takes");

        await history.WaitForSweep();
        await history.Sweep();

        (await RowCount()).Should().Be(1, "nothing is older than forever");
        (await history.QueryExecutions(new ExecutionHistoryQuery { SchedulerName = SchedulerName })).Items.Should().ContainSingle(
            "the read floor of a window reaching past the start of time is the start of time");
    }

    /// <summary>
    /// The database folds each run into its job's status exactly as <see cref="JobRunStatusFold" /> does.
    /// </summary>
    /// <remarks>
    /// The fold is the one definition of the rollup and the in-memory history's; the SQL is a second
    /// spelling of it. Every sequence is compared after every run, so a column that drifts on one step
    /// and is put right by the next is still caught.
    /// </remarks>
    [TestCaseSource(nameof(FoldSequences))]
    public async Task TheDatabaseFoldsEachRunIntoItsStatusAsTheFoldDoes(ExecutionHistoryEntry[] runs)
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });
        JobRunStatus? folded = null;

        foreach (ExecutionHistoryEntry run in runs)
        {
            folded = JobRunStatusFold.Apply(folded, run);
            await store.AddExecution(run);

            (await store.GetJobRunStatus(SchedulerName, reports)).Should().BeEquivalentTo(folded,
                "the row the database keeps is the fold's, after {0}", run.EntryId);
        }
    }

    private static IEnumerable<TestCaseData> FoldSequences()
    {
        yield return new TestCaseData((object) new[]
        {
            Folded("s1", 0, JobRunResult.Succeeded),
            Folded("s2", 1, JobRunResult.Skipped, summary: "nothing to do"),
            Folded("s3", 2, JobRunResult.Succeeded, summary: "released 3")
        }).SetArgDisplayNames("successes");

        yield return new TestCaseData((object) new[]
        {
            Folded("r1", 0, JobRunResult.Failed, retried: true, message: "attempt 1"),
            Folded("r2", 1, JobRunResult.Failed, retried: true, message: "attempt 2"),
            Folded("r3", 2, JobRunResult.Failed, message: "gave up"),
            Folded("r4", 3, JobRunResult.Failed, message: "gave up again"),
            Folded("r5", 4, JobRunResult.Succeeded),
            Folded("r6", 5, JobRunResult.Failed, summary: "reported, not thrown")
        }).SetArgDisplayNames("a retry chain");

        yield return new TestCaseData((object) new[]
        {
            Folded("o1", 10, JobRunResult.Succeeded, summary: "latest"),
            Folded("o2", 5, JobRunResult.Failed, message: "late failure"),
            Folded("o3", 12, JobRunResult.Cancelled),
            Folded("o4", 11, JobRunResult.Failed, retried: true, message: "late retry"),
            Folded("o5", 1, JobRunResult.Skipped),
            Folded("o6", 13, JobRunResult.Failed, message: "newest")
        }).SetArgDisplayNames("out of order");

        yield return new TestCaseData((object) new[]
        {
            Folded("t1", 5, JobRunResult.Failed, message: "first"),
            Folded("t2", 5, JobRunResult.Succeeded),
            Folded("t3", 5, JobRunResult.Failed, retried: true, message: "third")
        }).SetArgDisplayNames("one instant");

        yield return new TestCaseData((object) new[]
        {
            Folded("l1", 1, result: null, message: "a 4.3 failure"),
            Folded("l2", 2, result: null),
            Folded("l3", 3, JobRunResult.Cancelled),
            Folded("l4", 0, result: null, message: "older still")
        }).SetArgDisplayNames("rows without a result");
    }

    /// <summary>One run of <see cref="reports" />, named, and <paramref name="minutes" /> after <c>Start</c>.</summary>
    private static ExecutionHistoryEntry Folded(
        string entryId,
        int minutes,
        JobRunResult? result,
        bool retried = false,
        string? message = null,
        string? summary = null)
    {
        bool succeeded = result is null ? message is null : result is JobRunResult.Succeeded or JobRunResult.Skipped;

        return new ExecutionHistoryEntry(
            SchedulerName: SchedulerName,
            SchedulerInstanceId: minutes % 2 == 0 ? "node-a" : "node-b",
            JobGroup: reports.Group,
            JobName: reports.Name,
            TriggerGroup: TriggerGroup,
            TriggerName: "at-midnight",
            FiredAtUtc: Start.AddMinutes(minutes),
            Duration: TimeSpan.FromMilliseconds(10 + minutes),
            Succeeded: succeeded,
            ExceptionMessage: message)
        {
            EntryId = entryId,
            Result = result,
            RetryScheduled = retried,
            Summary = summary
        };
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A store over the contract's database, built directly so that a case can hand it a delegate, a
    /// logger or a clock of its own. <see cref="CreateStore" /> has to have provisioned the schema.
    /// </summary>
    private AdoExecutionHistoryStore StoreOver(
        StdAdoDelegate driverDelegate,
        Action<ExecutionHistoryOptions>? configure = null,
        ILoggerFactory? loggerFactory = null,
        TimeProvider? timeProvider = null)
    {
        IDbProvider dbProvider = new DbProvider("SQLite-Microsoft", database.ConnectionString);
        driverDelegate.Initialize(new DriverDelegateContext
        {
            TablePrefix = AdoConstants.DefaultTablePrefix,
            SchedulerName = SchedulerName,
            InstanceId = "node-a",
            DbProvider = dbProvider,
            TypeLoader = new SimpleTypeLoader(),
        });

        ExecutionHistoryOptions options = new();
        configure?.Invoke(options);

        return new AdoExecutionHistoryStore(
            dbProvider,
            driverDelegate,
            Options.Create(options),
            Options.Create(new QuartzSchedulerOptions { InstanceName = SchedulerName }),
            timeProvider ?? new ClockWithoutHistorySweeps(Clock),
            loggerFactory);
    }

    /// <summary>A check-in row, as a clustered node's job store writes one.</summary>
    private async Task CheckIn(string instanceId, DateTimeOffset at)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT OR REPLACE INTO QRTZ_SCHEDULER_STATE (SCHED_NAME, INSTANCE_NAME, LAST_CHECKIN_TIME, CHECKIN_INTERVAL) "
            + "VALUES (@scheduler, @instance, @checkin, 7500)";
        command.Parameters.AddWithValue("@scheduler", SchedulerName);
        command.Parameters.AddWithValue("@instance", instanceId);
        command.Parameters.AddWithValue("@checkin", at.UtcTicks);

        await command.ExecuteNonQueryAsync();
    }

    /// <summary>An execution row as a 4.3 node writes one: none of the 4.4 columns named.</summary>
    private async Task InsertLegacyRow(string entryId, DateTimeOffset firedAt, bool succeeded)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO QRTZ_EXECUTION_HISTORY (SCHED_NAME, ENTRY_ID, INSTANCE_NAME, JOB_NAME, JOB_GROUP, TRIGGER_NAME, "
            + "TRIGGER_GROUP, FIRED_TIME, RUN_TIME, SUCCEEDED, ERROR_MESSAGE, RETRY_ATTEMPT, RETRY_SCHEDULED, EXECUTION_LOG) "
            + "VALUES (@scheduler, @entry, 'node-a', @job, @jobGroup, 'at-midnight', @triggerGroup, @fired, 10000, @succeeded, "
            + "@message, 0, 0, NULL)";
        command.Parameters.AddWithValue("@scheduler", SchedulerName);
        command.Parameters.AddWithValue("@entry", entryId);
        command.Parameters.AddWithValue("@job", reports.Name);
        command.Parameters.AddWithValue("@jobGroup", reports.Group);
        command.Parameters.AddWithValue("@triggerGroup", TriggerGroup);
        command.Parameters.AddWithValue("@fired", firedAt.UtcTicks);
        command.Parameters.AddWithValue("@succeeded", succeeded);
        command.Parameters.AddWithValue("@message", succeeded ? DBNull.Value : "a 4.3 node's failure");

        (await command.ExecuteNonQueryAsync()).Should().Be(1);
    }

    private async Task<long> StatusRowCount()
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM QRTZ_JOB_STATUS";

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    /// <summary>The shipped SQLite delegate, but for a failed run's status update, which fails too.</summary>
    private sealed class FailedRunStatusFailingDelegate : SQLiteDelegate
    {
        internal override ValueTask<int> UpdateJobRunStatus(
            ConnectionAndTransactionHolder conn,
            ExecutionHistoryEntry entry,
            CancellationToken cancellationToken = default)
        {
            return entry.EffectiveResult == JobRunResult.Failed
                ? throw new InvalidOperationException("the status table refused the update")
                : base.UpdateJobRunStatus(conn, entry, cancellationToken);
        }
    }

    /// <summary>
    /// The shipped SQLite delegate, whose first status update finds no row: the moment before another
    /// node's insert of the job's first status has committed.
    /// </summary>
    private sealed class LateToTheFirstRunDelegate : SQLiteDelegate
    {
        private int updates;

        internal override ValueTask<int> UpdateJobRunStatus(
            ConnectionAndTransactionHolder conn,
            ExecutionHistoryEntry entry,
            CancellationToken cancellationToken = default)
        {
            return Interlocked.Increment(ref updates) == 1
                ? new ValueTask<int>(0)
                : base.UpdateJobRunStatus(conn, entry, cancellationToken);
        }
    }
}

/// <summary>
/// Which node sweeps a cluster's history: the live one with the lowest instance id.
/// </summary>
public sealed class AdoExecutionHistoryElectionTest
{
    private static readonly DateTimeOffset now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void ANodeIsLiveForTwiceItsCheckInIntervalAndNeverLessThanFifteenSeconds()
    {
        AdoExecutionHistoryStore.IsLive(State("n", now.AddSeconds(-14), TimeSpan.FromSeconds(1)), now).Should().BeTrue(
            "an interval shorter than the check-in misfire threshold is judged by the threshold");
        AdoExecutionHistoryStore.IsLive(State("n", now.AddSeconds(-16), TimeSpan.FromSeconds(1)), now).Should().BeFalse();

        AdoExecutionHistoryStore.IsLive(State("n", now.AddSeconds(-39), TimeSpan.FromSeconds(20)), now).Should().BeTrue(
            "twice a 20-second interval is 40 seconds");
        AdoExecutionHistoryStore.IsLive(State("n", now.AddSeconds(-41), TimeSpan.FromSeconds(20)), now).Should().BeFalse();
    }

    [Test]
    public void TheLowestLiveInstanceIdBelowThisNodesSweeps()
    {
        SchedulerStateRecord[] states =
        [
            State("node-1", now, TimeSpan.FromSeconds(7.5)),
            State("node-0", now.AddMinutes(-5), TimeSpan.FromSeconds(7.5)),
            State("node-2", now, TimeSpan.FromSeconds(7.5)),
            State("Node-9", now, TimeSpan.FromSeconds(7.5))
        ];

        AdoExecutionHistoryStore.Sweeper(states, "node-2", now).Should().Be("Node-9",
            "instance ids compare ordinally, where an upper-case letter sorts first, and node-0 is not live");
        AdoExecutionHistoryStore.Sweeper(states, "Node-1", now).Should().BeNull(
            "no live node sorts before Node-1, so it sweeps");
        AdoExecutionHistoryStore.Sweeper(states, "Node-9", now).Should().BeNull("a node's own row does not outrank it");
        AdoExecutionHistoryStore.Sweeper([], "node-a", now).Should().BeNull("with no check-ins the node sweeps");
    }

    private static SchedulerStateRecord State(string instanceId, DateTimeOffset checkin, TimeSpan interval) => new(instanceId, checkin, interval);
}
