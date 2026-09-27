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
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The misfire pass settles a continuation whose parent no longer exists, which is what a completion
/// running without the lock (#3863) can leave behind when a continuation is added to its parent in
/// the parent's last moments, and what a parent completing on a 4.1 node always left behind.
/// </summary>
/// <remarks>
/// <para>
/// The stranded state is written directly — the parent's row deleted from under its waiting child —
/// rather than raced for. The race itself needs two transactions interleaving under read-committed
/// isolation, which SQLite's single writer does not allow; <c>StrandedContinuationPostgresTest</c>
/// reproduces it on PostgreSQL through a seam in the delegate. What is asserted here is the sweep:
/// what it settles, how, and that a pass with nothing stranded costs one statement and changes nothing.
/// </para>
/// <para>
/// Driven against the store on a clock the test owns, with the store initialized but never started,
/// as <c>OverlapPolicyStoreTest</c> is. SQLite runs every operation under the lock, which is also
/// where the sweep runs on every dialect.
/// </para>
/// </remarks>
public sealed class StrandedContinuationSweepSqliteTest
{
    private const string Group = "stranded";
    private const string SchedulerName = "StrandedContinuationSweepSqliteTest";
    private const string DataSourceName = "stranded-continuations";

    /// <summary>On the hour, UTC, far from any machine's own clock.</summary>
    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private SqliteTestDatabase database = null!;
    private IDbProvider dbProvider = null!;
    private FakeTimeProvider clock = null!;
    private RecordingSignaler signals = null!;
    private CountingSqliteDelegate statements = null!;
    private LocalTransactionJobStore store = null!;

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        database = new SqliteTestDatabase("stranded-continuations");

        await using (SqliteConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = new(LoadSqliteTableScript(), connection);
            await command.ExecuteNonQueryAsync();
        }

        dbProvider = new DbProvider("SQLite-Microsoft", database.ConnectionString);
    }

    [OneTimeTearDown]
    public void DropDatabase()
    {
        database.Dispose();
    }

    [SetUp]
    public async Task BuildStore()
    {
        clock = new FakeTimeProvider(epoch);
        signals = new RecordingSignaler();
        statements = new CountingSqliteDelegate();
        store = new LocalTransactionJobStore(TestJobStores.Dependencies(
            signaler: signals,
            timeProvider: clock,
            schedulerOptions: TestJobStores.SchedulerOptions(instanceName: SchedulerName, instanceId: "node-a"),
            storeOptions: TestJobStores.StoreOptions(DataSourceName),
            dbProvider: dbProvider,
            driverDelegate: statements));

        // Initialized but deliberately not started, so no misfire loop races an assertion.
        await store.Initialize(TestJobStores.Identity(instanceName: SchedulerName, instanceId: "node-a"));
        await store.Clear();
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await store.Shutdown();
    }

    [Test]
    public async Task AContinuationWhoseParentWentIsSettledByTheNextMisfirePass()
    {
        TriggerKey parent = await ScheduleParent("parent");
        TriggerKey indifferent = await ScheduleContinuation("indifferent", parent, ContinuationCondition.OnAnyOutcome);
        TriggerKey particular = await ScheduleContinuation("particular", parent, ContinuationCondition.OnSuccess);

        await DeleteRowsOf(parent);
        (await store.GetTriggerState(indifferent)).Should().Be(TriggerState.Awaiting, "the parent is gone and nothing settled its children: the state under test");

        statements.Reset();
        RecoverMisfiredJobsResult pass = await store.RecoverMisfires(Guid.NewGuid());

        pass.ProcessedMisfiredTriggerCount.Should().Be(2, "both settlements change the schedule, so the pass says so and the scheduler thread is woken");

        (await store.GetTriggerState(indifferent)).Should().Be(TriggerState.Normal,
            "'however it ends' did not care how the parent ended, and the parent has ended one way or another");
        (await store.GetTrigger(indifferent))!.NextFireTimeUtc.Should().Be(clock.GetUtcNow(),
            "a released continuation fires at the later of now and its start time, and its start time is behind");
        (await ReadColumn("CONTINUES_TRIGGER_NAME", indifferent)).Should().BeNull("a released trigger is an ordinary one and names no parent");

        (await store.GetTriggerState(particular)).Should().Be(TriggerState.Error,
            "it asked how the parent ended, and nobody can answer that now: parked for an operator to see and reset, as a deleted parent parks it");
        signals.TriggersInError.Should().Equal([particular],
            "the listeners hear of the parked trigger once the pass has committed, and of the released one not at all");

        statements.StrandedScans.Should().Be(1, "the pass holds the lock on SQLite, so the one scan is the sweep's own");
    }

    [Test]
    public async Task APassWithNothingStrandedIssuesOneStatementAndChangesNothing()
    {
        TriggerKey parent = await ScheduleParent("parent");
        TriggerKey child = await ScheduleContinuation("child", parent, ContinuationCondition.OnSuccess);
        Dictionary<string, string> before = await ReadStates();

        statements.Reset();
        RecoverMisfiredJobsResult pass = await store.RecoverMisfires(Guid.NewGuid());

        pass.ProcessedMisfiredTriggerCount.Should().Be(0);
        statements.StrandedScans.Should().Be(1, "the sweep asks its one question and finds nothing");
        statements.Releases.Should().Be(0);
        statements.StateWrites.Should().Be(0);
        (await ReadStates()).Should().Equal(before, "a continuation whose parent is still there is nobody's to settle");
        (await store.GetTriggerState(child)).Should().Be(TriggerState.Awaiting);
        signals.TriggersInError.Should().BeEmpty();
    }

    /// <summary>
    /// The fired-row probe: a parent whose row went while a firing of it still runs is that firing's
    /// to settle, and the completion does so by parent key without needing the row.
    /// </summary>
    [Test]
    public async Task AParentStillRunningSomewhereIsLeftToItsOwnCompletion()
    {
        TriggerKey parent = await ScheduleParent("parent");
        TriggerKey child = await ScheduleContinuation("child", parent, ContinuationCondition.OnSuccess);
        IOperableTrigger firing = await Fire(parent);

        await DeleteRowsOf(parent);
        (await FiredRowCount(parent)).Should().Be(1, "the firing is still running: its fired row stands");

        statements.Reset();
        RecoverMisfiredJobsResult pass = await store.RecoverMisfires(Guid.NewGuid());

        pass.ProcessedMisfiredTriggerCount.Should().Be(0);
        statements.StrandedScans.Should().Be(1);
        (await store.GetTriggerState(child)).Should().Be(TriggerState.Awaiting, "a fired row of the parent says a completion is still to come, and it will settle the child");

        await store.FiringComplete(new TriggeredJobCompleteContext
        {
            Trigger = firing,
            JobDetail = (await store.GetJob(firing.JobKey))!,
            Instruction = SchedulerInstruction.DeleteTrigger,
            Outcome = ExecutionOutcome.Succeeded
        });

        (await store.GetTriggerState(child)).Should().Be(TriggerState.Normal,
            "the completion settles by the parent's key, which it carries, and the parent succeeded");
    }

    private async Task<TriggerKey> ScheduleParent(string name)
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(name, Group).StoreDurably().Build();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(clock.GetUtcNow())
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await store.ScheduleJob(job, trigger);
        return trigger.Key;
    }

    private async Task<TriggerKey> ScheduleContinuation(string name, TriggerKey parent, ContinuationCondition condition)
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity(name, Group).Build();
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            // Behind now, so the release's max(now, START_TIME) is now.
            .StartAt(clock.GetUtcNow().AddMinutes(-1))
            .StartAfter(parent, condition)
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await store.ScheduleJob(job, trigger);
        (await store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Awaiting, "the continuation waits for its parent");
        return trigger.Key;
    }

    /// <summary>Acquires and fires the one trigger due, which must be <paramref name="key" />.</summary>
    private async Task<IOperableTrigger> Fire(TriggerKey key)
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddSeconds(1),
            MaxCount = 10,
            TimeWindow = TimeSpan.Zero
        });

        acquired.Select(x => x.Key).Should().Equal([key], "the trigger under test is the one due");

        List<TriggerFiredResult> results = await store.TriggersFired(acquired);
        results.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull("a firing has to start before it can complete");
        return results[0].TriggerFiredBundle!.Trigger;
    }

    /// <summary>
    /// What a lock-free completion that never saw the child leaves of the parent: no trigger row and
    /// no type row. Written directly, because the store's own deletion would settle the child.
    /// </summary>
    private async Task DeleteRowsOf(TriggerKey key)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        foreach (string table in (string[]) ["QRTZ_SIMPLE_TRIGGERS", "QRTZ_TRIGGERS"])
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"DELETE FROM {table} WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";
            command.Parameters.AddWithValue("@name", key.Name);
            command.Parameters.AddWithValue("@group", key.Group);
            (await command.ExecuteNonQueryAsync()).Should().Be(1, "the fixture stored one row of {0} for {1}", table, key);
        }
    }

    private async Task<object?> ReadColumn(string column, TriggerKey key)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";
        command.Parameters.AddWithValue("@name", key.Name);
        command.Parameters.AddWithValue("@group", key.Group);

        object? value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }

    /// <summary>Every trigger's state, keyed by name, as one snapshot to compare with another.</summary>
    private async Task<Dictionary<string, string>> ReadStates()
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT TRIGGER_NAME, TRIGGER_STATE FROM QRTZ_TRIGGERS ORDER BY TRIGGER_NAME";

        Dictionary<string, string> states = new(StringComparer.Ordinal);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            states[reader.GetString(0)] = reader.GetString(1);
        }

        return states;
    }

    private async Task<long> FiredRowCount(TriggerKey key)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";
        command.Parameters.AddWithValue("@name", key.Name);
        command.Parameters.AddWithValue("@group", key.Group);

        return (long) (await command.ExecuteScalarAsync())!;
    }

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    /// <summary>
    /// The shipped SQLite delegate, counting the sweep's scan and the two writes a settlement makes.
    /// </summary>
    private sealed class CountingSqliteDelegate : SQLiteDelegate
    {
        private int strandedScans;
        private int releases;
        private int stateWrites;

        public int StrandedScans => Volatile.Read(ref strandedScans);

        public int Releases => Volatile.Read(ref releases);

        public int StateWrites => Volatile.Read(ref stateWrites);

        public void Reset()
        {
            Volatile.Write(ref strandedScans, 0);
            Volatile.Write(ref releases, 0);
            Volatile.Write(ref stateWrites, 0);
        }

        internal override ValueTask<List<StrandedContinuation>> SelectStrandedContinuations(
            ConnectionAndTransactionHolder conn,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref strandedScans);
            return base.SelectStrandedContinuations(conn, cancellationToken);
        }

        public override ValueTask<int> ReleaseContinuation(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            StoredTriggerState newState,
            DateTimeOffset fireTime,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref releases);
            return base.ReleaseContinuation(conn, triggerKey, newState, fireTime, cancellationToken);
        }

        public override ValueTask<int> UpdateTriggerState(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            StoredTriggerState state,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref stateWrites);
            return base.UpdateTriggerState(conn, triggerKey, state, cancellationToken);
        }
    }

    /// <summary>Records which triggers the store reported parked in error.</summary>
    private sealed class RecordingSignaler : ISchedulerSignaler
    {
        public List<TriggerKey> TriggersInError { get; } = [];

        public ValueTask NotifySchedulerListenersTriggerInError(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            TriggersInError.Add(triggerKey);
            return default;
        }

        public ValueTask NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => default;

        public ValueTask SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersError(SchedulerErrorContext errorContext, CancellationToken cancellationToken = default) => default;
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
