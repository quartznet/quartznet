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

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// What only the database history has to get right about <c>QRTZ_TRIGGER_STATUS</c>: the SQL fold, the
/// transaction it shares with the row and the job's status, two nodes racing a trigger's first run, the sweep of
/// deleted triggers, a store window over a database without the table, and write-behind (#4243).
/// </summary>
public sealed partial class AdoExecutionHistoryStoreContractTest
{
    /// <remarks>Scheduled for a durable job of its own, far in the future, so the scheduler keeps it and never fires it.</remarks>
    protected override async ValueTask KeepTrigger(TriggerKey trigger)
    {
        IJobDetail owner = JobBuilder.Create<NoOpJob>().WithIdentity("trigger-owner", JobGroup).StoreDurably().Build();
        await scheduler.AddJob(owner, new AddJobOptions { Replace = true });
        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(trigger)
            .ForJob(owner.Key)
            .StartAt(Start.AddYears(1))
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build());
    }

    /// <summary>
    /// The database folds each run into its trigger's status exactly as <see cref="TriggerRunStatusFold" /> does.
    /// </summary>
    [TestCaseSource(nameof(FoldSequences))]
    public async Task TheDatabaseFoldsEachRunIntoItsTriggerStatusAsTheFoldDoes(ExecutionHistoryEntry[] runs)
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });
        TriggerRunStatus? folded = null;

        foreach (ExecutionHistoryEntry run in runs)
        {
            folded = TriggerRunStatusFold.Apply(folded, run);
            await store.AddExecution(run);

            (await TriggerStatus(store, "at-midnight")).Should().BeEquivalentTo(folded,
                "the row the database keeps is the fold's, after {0}", run.EntryId);
        }
    }

    /// <summary>
    /// The row, the job's status and the trigger's commit together: a trigger status write that fails takes the
    /// other two with it.
    /// </summary>
    [Test]
    public async Task ATriggerStatusWriteThatFailsTakesTheRowAndTheJobStatusWithIt()
    {
        await CreateStore(_ => { });
        using AdoExecutionHistoryStore history = StoreOver(new FailedRunTriggerStatusFailingDelegate());

        await history.AddExecution(Fired("hourly", Start, JobRunResult.Succeeded));
        await history.AddExecution(Fired("hourly", Start.AddMinutes(1), JobRunResult.Failed));

        (await RowCount()).Should().Be(1, "the failed run's row was rolled back with its trigger's status");
        (await history.GetJobRunStatus(SchedulerName, new JobKey("report", JobGroup)))!.RunCount.Should().Be(1,
            "and so was its job's status: the three commit together or not at all");
        (await TriggerStatus(history, "hourly"))!.RunCount.Should().Be(1);
    }

    /// <summary>Two nodes recording a trigger's first run at once both count, as they do for a job's.</summary>
    [Test]
    public async Task TwoNodesRecordingATriggersFirstRunAtOnceBothCount()
    {
        IExecutionHistoryStore other = await CreateStore(_ => { });
        using AdoExecutionHistoryStore late = StoreOver(new LateToTheFirstTriggerRunDelegate());

        await other.AddExecution(Fired("hourly", Start, JobRunResult.Succeeded) with { SchedulerInstanceId = "node-b" });
        await late.AddExecution(Fired("hourly", Start.AddSeconds(1), JobRunResult.Failed));

        TriggerRunStatus status = (await TriggerStatus(late, "hourly"))!;
        status.RunCount.Should().Be(2, "the insert that lost the race is rolled back and the unit runs again as an update");
        status.ConsecutiveFailures.Should().Be(1);
        (await RowCount()).Should().Be(2, "the rolled-back unit took its row with it, and the rerun wrote it once");
        (await late.GetJobRunStatus(SchedulerName, new JobKey("report", JobGroup)))!.RunCount.Should().Be(2,
            "the job's status was rolled back and folded again with it, so it counts each run once");
    }

    /// <summary>
    /// The status of a trigger that is gone is forgotten, but only once it is past the longest window; a trigger
    /// the scheduler still has keeps its status for good.
    /// </summary>
    [Test]
    public async Task TheStatusOfADeletedTriggerIsForgottenOncePastTheLongestWindow()
    {
        IExecutionHistoryStore store = await CreateStore(options =>
        {
            options.Retention = TimeSpan.FromHours(1);
            options.RetentionByResult[JobRunResult.Failed] = TimeSpan.FromDays(2);
        });

        await KeepTrigger(Trigger("still-scheduled"));

        await store.AddExecution(Fired("still-scheduled", Start, JobRunResult.Succeeded));
        await store.AddExecution(Fired("deleted", Start, JobRunResult.Succeeded));

        Clock.Advance(TimeSpan.FromDays(1));
        await ApplyBounds(store);
        (await TriggerNames(store)).Should().BeEquivalentTo(["still-scheduled", "deleted"],
            "within the longest window a status is kept, trigger or no trigger: one replaced under its key keeps its counts");

        Clock.Advance(TimeSpan.FromDays(2));
        await ApplyBounds(store);
        (await TriggerNames(store)).Should().Equal(["still-scheduled"],
            "a deleted trigger's status goes once it is older than any row could be, and a stored trigger's never does");
    }

    /// <summary>
    /// A store over a database nobody migrated to 4.9, as a store window can be, says which script creates the
    /// table rather than failing the read with the provider's words.
    /// </summary>
    [Test]
    public async Task AStoreOverADatabaseWithoutTheTableSaysWhichScriptCreatesIt()
    {
        IExecutionHistoryStore store = await CreateStore(_ => { });
        await ExecuteSql("DROP TABLE QRTZ_TRIGGER_STATUS");

        Func<Task> read = async () => await store.QueryTriggerRunStatuses(new TriggerRunStatusQuery { SchedulerName = SchedulerName });

        (await read.Should().ThrowAsync<NotSupportedException>(
                "a database without the table keeps no trigger status, which a page renders as a column it leaves out"))
            .WithMessage("*QRTZ_TRIGGER_STATUS*database/migrations/4.9/add_trigger_status_sqlite.sql*")
            .WithInnerException<SqliteException>();
    }

    /// <summary>A read that fails over a database that has the table propagates its failure as it is.</summary>
    [Test]
    public async Task AFailedReadOverADatabaseWithTheTableIsNotTakenForAMissingOne()
    {
        await CreateStore(_ => { });
        using AdoExecutionHistoryStore history = StoreOver(new FailingTriggerStatusReadDelegate());

        Func<Task> read = async () => await history.QueryTriggerRunStatuses(new TriggerRunStatusQuery { SchedulerName = SchedulerName });

        await read.Should().ThrowAsync<SqliteException>(
            "the table resolves, so the failure is the database's and the page shows it as stale, not as unsupported");
    }

    /// <summary>
    /// A read that fails for another reason over a database without the table, a connection lost after it opened,
    /// propagates as the failed read it is: only the dialect's "no such table" may name the script.
    /// </summary>
    [Test]
    public async Task AReadThatFailsForAnotherReasonIsNotTakenForAMissingTable()
    {
        await CreateStore(_ => { });
        await ExecuteSql("DROP TABLE QRTZ_TRIGGER_STATUS");
        using AdoExecutionHistoryStore history = StoreOver(new FailingTriggerStatusReadDelegate());

        Func<Task> read = async () => await history.QueryTriggerRunStatuses(new TriggerRunStatusQuery { SchedulerName = SchedulerName });

        (await read.Should().ThrowAsync<SqliteException>(
                "a database locked by another connection did not say the table is missing, whatever a probe would find"))
            .Which.SqliteErrorCode.Should().Be(5);
    }

    // ---------------------------------------------------------------------------------------------
    // Write-behind
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A flush folds each trigger's runs into one status write, in the order they were queued, and a firing that
    /// finalized its trigger leaves none: the flag travels through the queue with its run.
    /// </summary>
    [Test]
    public async Task EachTriggersRunsAreFoldedIntoOneStatusWritePerFlush()
    {
        await CreateStore(_ => { });
        TriggerStatusDelegate counting = new();
        await using AdoExecutionHistoryStore history = WriteBehindStore(counting);

        ExecutionHistoryEntry[] daily =
        [
            Fired("daily", Start, JobRunResult.Succeeded) with { EntryId = "d1" },
            Fired("daily", Start.AddMinutes(10), JobRunResult.Failed) with { EntryId = "d2", ExceptionMessage = "gave up" },
            Fired("daily", Start.AddMinutes(5), JobRunResult.Succeeded) with { EntryId = "d3", Summary = "completed late" }
        ];

        ExecutionHistoryEntry[] hourly =
        [
            Fired("hourly", Start.AddMinutes(1), JobRunResult.Failed) with { EntryId = "h1", RetryScheduled = true, ExceptionMessage = "retrying" },
            Fired("hourly", Start.AddMinutes(2), JobRunResult.Failed) with { EntryId = "h2", ExceptionMessage = "gave up" }
        ];

        await history.AddExecution(daily[0]);
        await history.AddExecution(hourly[0]);
        await history.Flush();
        counting.TriggerInserts.Should().Be(2, "each trigger's first flush inserts its status");

        await history.AddExecution(daily[1]);
        await history.AddExecution(hourly[1]);
        await history.AddExecution(Fired("one-off", Start.AddMinutes(3), JobRunResult.Failed) with { EntryId = "o1" }, triggerFinalized: true);
        await history.AddExecution(daily[2]);
        await history.Flush();

        counting.GuardedTriggerUpdates.Should().Equal([2],
            "one flush wrote both triggers' statuses, one update each, however many runs each folded in");
        counting.PerRunTriggerUpdates.Should().Be(0, "no status was written run by run");
        counting.TriggerInserts.Should().Be(2, "the one-off finalized its trigger, so the flush inserted no status for it");

        (await TriggerStatus(history, "daily")).Should().BeEquivalentTo(FoldedTrigger(daily));
        (await TriggerStatus(history, "hourly")).Should().BeEquivalentTo(FoldedTrigger(hourly));
        (await TriggerStatus(history, "one-off")).Should().BeNull("the flag says the firing finalized its trigger, and it survives the queue");
        (await history.GetJobRunStatus(SchedulerName, new JobKey("report", JobGroup)))!.RunCount.Should().Be(6,
            "the job's status counts every run, the one-off's included");
    }

    /// <summary>
    /// A trigger status another writer changed between the flush's read and its update is folded again, rows and
    /// the job's status rolled back with it.
    /// </summary>
    [Test]
    public async Task ATriggerStatusChangedSinceTheReadIsFoldedAgain()
    {
        await CreateStore(_ => { });
        TriggerStatusDelegate stale = new();
        await using AdoExecutionHistoryStore history = WriteBehindStore(stale);

        ExecutionHistoryEntry[] runs =
        [
            Fired("hourly", Start, JobRunResult.Succeeded) with { EntryId = "first" },
            Fired("hourly", Start.AddMinutes(1), JobRunResult.Failed) with { EntryId = "second", ExceptionMessage = "refused" },
            Fired("hourly", Start.AddMinutes(2), JobRunResult.Failed) with { EntryId = "third", ExceptionMessage = "refused again" }
        ];

        await history.AddExecution(runs[0]);
        await history.Flush();

        stale.StaleReads = 1;
        await history.AddExecution(runs[1]);
        await history.AddExecution(runs[2]);
        await history.Flush();

        stale.TriggerReads.Should().Be(3, "the first flush read once, and the second read again after its update missed");
        stale.PerRunTriggerUpdates.Should().Be(0, "one miss is folded again, not written run by run");
        (await RowCount()).Should().Be(3, "the rolled-back attempt took its rows with it");
        (await TriggerStatus(history, "hourly")).Should().BeEquivalentTo(FoldedTrigger(runs));
        (await history.GetJobRunStatus(SchedulerName, new JobKey("report", JobGroup)))!.RunCount.Should().Be(3,
            "the job's status was rolled back with the miss and counts each run once");
    }

    /// <summary>
    /// A trigger status that changes under every folded attempt is written run by run, and so is the job's: the
    /// fallback writes both statuses of every execution.
    /// </summary>
    [Test]
    public async Task ATriggerStatusThatKeepsChangingIsFoldedRunByRunWithItsJobs()
    {
        await CreateStore(_ => { });
        TriggerStatusDelegate stale = new();
        await using AdoExecutionHistoryStore history = WriteBehindStore(stale);

        ExecutionHistoryEntry[] runs =
        [
            Fired("hourly", Start, JobRunResult.Succeeded) with { EntryId = "first" },
            Fired("hourly", Start.AddMinutes(1), JobRunResult.Failed) with { EntryId = "second", ExceptionMessage = "refused" },
            Fired("hourly", Start.AddMinutes(2), JobRunResult.Succeeded) with { EntryId = "third" }
        ];

        await history.AddExecution(runs[0]);
        await history.Flush();

        stale.StaleReads = int.MaxValue;
        await history.AddExecution(runs[1]);
        await history.AddExecution(runs[2]);
        await history.Flush();

        stale.TriggerReads.Should().Be(1 + AdoExecutionHistoryStore.FoldedFlushAttempts, "each folded attempt read once and missed");
        stale.PerRunTriggerUpdates.Should().Be(2, "the attempt after them folded each run on its own");
        stale.PerRunJobUpdates.Should().Be(2, "and wrote the job's status run by run with it");
        (await RowCount()).Should().Be(3);
        (await TriggerStatus(history, "hourly")).Should().BeEquivalentTo(FoldedTrigger(runs));
        (await history.GetJobRunStatus(SchedulerName, new JobKey("report", JobGroup))).Should().BeEquivalentTo(Folded(runs));
    }

    /// <summary>
    /// A flush that found no trigger status inserts one, and if another writer inserted it first the flush folds
    /// onto that writer's row instead.
    /// </summary>
    [Test]
    public async Task AFirstTriggerStatusAnotherWriterInsertedFirstIsFoldedOnto()
    {
        IExecutionHistoryStore other = await CreateStore(_ => { });
        TriggerStatusDelegate blind = new() { BlindReads = 1 };
        await using AdoExecutionHistoryStore history = WriteBehindStore(blind);

        ExecutionHistoryEntry otherNodes = Fired("hourly", Start, JobRunResult.Succeeded) with { EntryId = "other-node", SchedulerInstanceId = "node-b" };
        ExecutionHistoryEntry ours = Fired("hourly", Start.AddMinutes(1), JobRunResult.Failed) with { EntryId = "ours", ExceptionMessage = "refused" };

        await other.AddExecution(otherNodes);
        await history.AddExecution(ours);
        await history.Flush();

        (await RowCount()).Should().Be(2, "the attempt whose insert failed was rolled back, rows and all");
        (await TriggerStatus(history, "hourly")).Should().BeEquivalentTo(FoldedTrigger([otherNodes, ours]),
            "the insert lost the race, so the flush read the row the other node wrote and folded onto it");
    }

    // ---------------------------------------------------------------------------------------------

    private static TriggerRunStatus? FoldedTrigger(IEnumerable<ExecutionHistoryEntry> runs)
    {
        TriggerRunStatus? status = null;
        foreach (ExecutionHistoryEntry run in runs)
        {
            status = TriggerRunStatusFold.Apply(status, run);
        }

        return status;
    }

    private async Task ExecuteSql(string sql)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>The shipped SQLite delegate, but for a failed run's trigger status update, which fails too.</summary>
    private sealed class FailedRunTriggerStatusFailingDelegate : SQLiteDelegate
    {
        internal override ValueTask<int> UpdateTriggerRunStatus(
            ConnectionAndTransactionHolder conn,
            ExecutionHistoryEntry entry,
            CancellationToken cancellationToken = default)
        {
            return entry.EffectiveResult == JobRunResult.Failed
                ? throw new InvalidOperationException("the trigger status table refused the update")
                : base.UpdateTriggerRunStatus(conn, entry, cancellationToken);
        }
    }

    /// <summary>
    /// The shipped SQLite delegate, whose first trigger status update finds no row: the moment before another
    /// node's insert of the trigger's first status has committed.
    /// </summary>
    private sealed class LateToTheFirstTriggerRunDelegate : SQLiteDelegate
    {
        private int updates;

        internal override ValueTask<int> UpdateTriggerRunStatus(
            ConnectionAndTransactionHolder conn,
            ExecutionHistoryEntry entry,
            CancellationToken cancellationToken = default)
        {
            return Interlocked.Increment(ref updates) == 1
                ? new ValueTask<int>(0)
                : base.UpdateTriggerRunStatus(conn, entry, cancellationToken);
        }
    }

    /// <summary>The shipped SQLite delegate, whose trigger status read fails although the table is there.</summary>
    private sealed class FailingTriggerStatusReadDelegate : SQLiteDelegate
    {
        internal override ValueTask<PagedResult<TriggerRunStatus>> SelectTriggerRunStatuses(
            ConnectionAndTransactionHolder conn,
            TriggerRunStatusQuery query,
            CancellationToken cancellationToken = default)
        {
            throw new SqliteException("the database is locked", 5);
        }
    }

    /// <summary>
    /// The shipped SQLite delegate, counting how the trigger statuses were written, whose trigger status reads can
    /// be made to answer one run behind or to find nothing: the read a flush took just before another writer's
    /// update or insert committed.
    /// </summary>
    private sealed class TriggerStatusDelegate : SQLiteDelegate
    {
        private readonly List<int> guardedTriggerUpdates = [];
        private int perRunTriggerUpdates;
        private int perRunJobUpdates;
        private int triggerInserts;
        private int triggerReads;

        public int StaleReads { get; set; }

        public int BlindReads { get; set; }

        public List<int> GuardedTriggerUpdates
        {
            get
            {
                lock (guardedTriggerUpdates)
                {
                    return [.. guardedTriggerUpdates];
                }
            }
        }

        public int PerRunTriggerUpdates => Volatile.Read(ref perRunTriggerUpdates);

        public int PerRunJobUpdates => Volatile.Read(ref perRunJobUpdates);

        public int TriggerInserts => Volatile.Read(ref triggerInserts);

        public int TriggerReads => Volatile.Read(ref triggerReads);

        internal override async ValueTask<Dictionary<TriggerKey, TriggerRunStatus>> SelectTriggerRunStatusesByKey(
            ConnectionAndTransactionHolder conn,
            string schedulerName,
            IReadOnlyCollection<TriggerKey> triggers,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref triggerReads);
            if (BlindReads > 0)
            {
                BlindReads--;
                return [];
            }

            Dictionary<TriggerKey, TriggerRunStatus> read = await base.SelectTriggerRunStatusesByKey(conn, schedulerName, triggers, cancellationToken);
            if (read.Count == 0 || StaleReads <= 0)
            {
                return read;
            }

            StaleReads--;
            return read.ToDictionary(static pair => pair.Key, static pair => pair.Value with { RunCount = pair.Value.RunCount - 1 });
        }

        internal override ValueTask<int> UpdateTriggerRunStatusesIfUnchanged(
            ConnectionAndTransactionHolder conn,
            IReadOnlyList<(TriggerRunStatus Status, long ReadRunCount)> writes,
            CancellationToken cancellationToken = default)
        {
            lock (guardedTriggerUpdates)
            {
                guardedTriggerUpdates.Add(writes.Count);
            }

            return base.UpdateTriggerRunStatusesIfUnchanged(conn, writes, cancellationToken);
        }

        internal override ValueTask<int> UpdateTriggerRunStatus(
            ConnectionAndTransactionHolder conn,
            ExecutionHistoryEntry entry,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref perRunTriggerUpdates);
            return base.UpdateTriggerRunStatus(conn, entry, cancellationToken);
        }

        internal override ValueTask<int> UpdateJobRunStatus(
            ConnectionAndTransactionHolder conn,
            ExecutionHistoryEntry entry,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref perRunJobUpdates);
            return base.UpdateJobRunStatus(conn, entry, cancellationToken);
        }

        internal override ValueTask<int> InsertTriggerRunStatus(
            ConnectionAndTransactionHolder conn,
            TriggerRunStatus status,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref triggerInserts);
            return base.InsertTriggerRunStatus(conn, status, cancellationToken);
        }
    }
}
