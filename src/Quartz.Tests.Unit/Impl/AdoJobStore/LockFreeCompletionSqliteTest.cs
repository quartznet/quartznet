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
/// A completion that takes no lock, run against a real database: the statements it issues leave the
/// rows a locked completion would have left, and a pause that landed while the firing ran survives it
/// (#3863).
/// </summary>
/// <remarks>
/// <para>
/// SQLite is the one dialect the lock-free path never runs on — the store serializes every operation
/// there — so <c>LockAllOperations</c> is switched off again after the store has initialized, on a file
/// nobody else touches, and the lock handler is replaced with one that counts. What runs is then the
/// real completion over the real statements, with the lock taken exactly where
/// <see cref="LockFreeCompletionTest" /> says it is.
/// </para>
/// <para>
/// Driven against the store rather than through a scheduler, on a clock the test owns, with the store
/// initialized but never started, as <c>OverlapPolicyStoreTest</c> is.
/// </para>
/// </remarks>
public sealed class LockFreeCompletionSqliteTest
{
    private const string Group = "lockFree";
    private const string SchedulerName = "LockFreeCompletionSqliteTest";
    private const string DataSourceName = "lock-free-completion";

    /// <summary>On the hour, UTC, far from any machine's own clock.</summary>
    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private SqliteTestDatabase database = null!;
    private IDbProvider dbProvider = null!;
    private FakeTimeProvider clock = null!;
    private CountingLockHandler locks = null!;
    private LocalTransactionJobStore store = null!;

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        database = new SqliteTestDatabase("lock-free-completion");

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
        store = new LocalTransactionJobStore(TestJobStores.Dependencies(
            timeProvider: clock,
            schedulerOptions: TestJobStores.SchedulerOptions(instanceName: SchedulerName, instanceId: "node-a"),
            storeOptions: TestJobStores.StoreOptions(DataSourceName),
            dbProvider: dbProvider,
            driverDelegate: new SQLiteDelegate()));

        // Initialized but deliberately not started, so no misfire loop races an assertion.
        await store.Initialize(TestJobStores.Identity(instanceName: SchedulerName, instanceId: "node-a"));
        await store.Clear();

        // Initialize forced both, for SQLite's single writer; one test at a time on a private file has
        // no second writer, and the lock-free path is what is under test.
        store.LockAllOperations = false;
        locks = new CountingLockHandler();
        store.LockHandler = locks;
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await store.Shutdown();
    }

    [Test]
    public async Task ASpentOneOffOfADurableJobCompletesWithoutTheLock()
    {
        (IJobDetail job, IOperableTrigger trigger) = await Schedule("one-off", durable: true, oneOff: true);
        IOperableTrigger firing = await Fire(trigger.Key);

        locks.Reset();
        await Complete(firing, job, SchedulerInstruction.DeleteTrigger);

        locks.TriggerAccess.Should().Be(0, "a spent one-off of a durable job writes its own two rows and nothing else");
        (await store.GetTrigger(trigger.Key)).Should().BeNull("the trigger has nothing left to fire");
        (await FiredRowCount(trigger.Key)).Should().Be(0, "the trigger's fired rows go with it");
        (await store.GetJob(job.Key)).Should().NotBeNull("a durable job outlives its triggers");
    }

    [Test]
    public async Task ATriggerPausedWhileItsFiringRanIsStillPausedAfterTheLockFreeCompletion()
    {
        (IJobDetail job, IOperableTrigger trigger) = await Schedule("repeating", durable: true, oneOff: false);
        IOperableTrigger firing = await Fire(trigger.Key);
        DateTimeOffset? nextFireTime = (await store.GetTrigger(trigger.Key))!.NextFireTimeUtc;

        (await store.PauseTrigger(trigger.Key)).Should().BeTrue("the trigger is WAITING for its next occurrence while this one runs, so it can be paused");

        locks.Reset();
        await Complete(firing, job, SchedulerInstruction.NoInstruction);

        locks.TriggerAccess.Should().Be(0, "a repeating trigger's completion writes nothing to the trigger row");
        (await ReadColumn("TRIGGER_STATE", trigger.Key)).Should().Be(AdoConstants.StatePaused,
            "the completion writes no state, so the pause that landed while the firing ran stands");
        (await store.GetTrigger(trigger.Key))!.NextFireTimeUtc.Should().Be(nextFireTime, "and no fire time");
        (await FiredRowCount(trigger.Key)).Should().Be(0, "its own fired row is what the completion deletes");
    }

    [Test]
    public async Task AParentWithAContinuationCompletesUnderTheLockOnceAndSettlesIt()
    {
        (IJobDetail job, IOperableTrigger parent) = await Schedule("parent", durable: true, oneOff: true);
        IOperableTrigger child = await ScheduleContinuation("child", parent.Key);
        IOperableTrigger firing = await Fire(parent.Key);

        locks.Reset();
        await Complete(firing, job, SchedulerInstruction.DeleteTrigger);

        locks.TriggerAccess.Should().Be(1,
            "the lock-free attempt found the continuation and the whole completion ran again under the lock, once");
        (await store.GetTrigger(parent.Key)).Should().BeNull("the parent is spent");
        (await store.GetTriggerState(child.Key)).Should().Be(TriggerState.Normal, "the parent succeeded, which is what the child waited for");
        (await store.GetTrigger(child.Key))!.NextFireTimeUtc.Should().Be(clock.GetUtcNow(),
            "a released continuation fires at the later of now and its start time, and its start time is behind");
    }

    [Test]
    public async Task ANonDurableJobsLastTriggerCompletesUnderTheLockAndTakesTheJobWithIt()
    {
        (IJobDetail job, IOperableTrigger trigger) = await Schedule("one-off", durable: false, oneOff: true);
        IOperableTrigger firing = await Fire(trigger.Key);

        locks.Reset();
        await Complete(firing, job, SchedulerInstruction.DeleteTrigger);

        locks.TriggerAccess.Should().Be(1, "whether the job is an orphan is a count of its triggers, which only the lock keeps still");
        (await store.GetTrigger(trigger.Key)).Should().BeNull();
        (await store.GetJob(job.Key)).Should().BeNull("a job that is not durable goes with its last trigger, as it always has");
    }

    [Test]
    public async Task ADisallowConcurrentJobsFiringCompletesUnderTheLock()
    {
        (IJobDetail job, IOperableTrigger trigger) = await Schedule("serial", durable: true, oneOff: false, nonConcurrent: true);
        IOperableTrigger firing = await Fire(trigger.Key);

        locks.Reset();
        await Complete(firing, job, SchedulerInstruction.NoInstruction);

        locks.TriggerAccess.Should().Be(1, "unblocking the job's other triggers reads their states, which only the lock keeps still");
        (await ReadColumn("TRIGGER_STATE", trigger.Key)).Should().Be(AdoConstants.StateWaiting, "the locked path is the path it always was");
    }

    [Test]
    public async Task ABufferOneFiringCompletesUnderTheLockAndLetsGoOfTheTrigger()
    {
        (IJobDetail job, IOperableTrigger trigger) = await Schedule("buffered", durable: true, oneOff: false, policy: OverlapPolicy.BufferOne);
        IOperableTrigger firing = await Fire(trigger.Key);
        (await ReadColumn("TRIGGER_STATE", trigger.Key)).Should().Be(AdoConstants.StateBlocked, "a BufferOne firing holds its trigger while it runs");

        locks.Reset();
        await Complete(firing, job, SchedulerInstruction.NoInstruction);

        locks.TriggerAccess.Should().Be(1, "letting go is BLOCKED to WAITING beside a pause that writes the row unconditionally, so it keeps the lock");
        (await ReadColumn("TRIGGER_STATE", trigger.Key)).Should().Be(AdoConstants.StateWaiting);
    }

    private async Task<(IJobDetail Job, IOperableTrigger Trigger)> Schedule(
        string name,
        bool durable,
        bool oneOff,
        bool nonConcurrent = false,
        OverlapPolicy policy = OverlapPolicy.Default)
    {
        IJobDetail job = nonConcurrent
            ? JobBuilder.Create<NonConcurrentJob>().WithIdentity(name, Group).StoreDurably(durable).Build()
            : JobBuilder.Create<ConcurrentJob>().WithIdentity(name, Group).StoreDurably(durable).Build();

        TriggerBuilder<IJob> builder = TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(clock.GetUtcNow())
            .WithOverlapPolicy(policy);

        if (!oneOff)
        {
            builder = builder.WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever());
        }

        IOperableTrigger trigger = (IOperableTrigger) builder.Build();
        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await store.ScheduleJob(job, trigger);
        return (job, trigger);
    }

    private async Task<IOperableTrigger> ScheduleContinuation(string name, TriggerKey parent)
    {
        IJobDetail job = JobBuilder.Create<ConcurrentJob>().WithIdentity(name, Group).Build();

        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            // Behind now, so the release's max(now, START_TIME) is now.
            .StartAt(clock.GetUtcNow().AddMinutes(-1))
            .StartAfter(parent, ContinuationCondition.OnSuccess)
            .Build();

        trigger.ComputeFirstFireTimeUtc(calendar: null);
        await store.ScheduleJob(job, trigger);
        (await store.GetTriggerState(trigger.Key)).Should().Be(TriggerState.Awaiting, "the continuation waits for its parent");
        return trigger;
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

    private Task Complete(IOperableTrigger firing, IJobDetail job, SchedulerInstruction instruction)
    {
        return store.FiringComplete(new TriggeredJobCompleteContext
        {
            Trigger = firing,
            JobDetail = job,
            Instruction = instruction,
            Outcome = ExecutionOutcome.Succeeded
        }).AsTask();
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
    /// The in-process lock, counting how often <see cref="SchedulerLock.TriggerAccess" /> is taken.
    /// </summary>
    private sealed class CountingLockHandler : ILockHandler
    {
        private readonly InProcessLockHandler inner = new();
        private int triggerAccess;

        public int TriggerAccess => Volatile.Read(ref triggerAccess);

        public void Reset() => Interlocked.Exchange(ref triggerAccess, 0);

        public ValueTask<bool> AcquireLock(Guid requestorId, ConnectionAndTransactionHolder? conn, SchedulerLock lockKind, CancellationToken cancellationToken = default)
        {
            if (lockKind == SchedulerLock.TriggerAccess)
            {
                Interlocked.Increment(ref triggerAccess);
            }

            return inner.AcquireLock(requestorId, conn, lockKind, cancellationToken);
        }

        public ValueTask ReleaseLock(Guid requestorId, SchedulerLock lockKind, CancellationToken cancellationToken = default)
        {
            return inner.ReleaseLock(requestorId, lockKind, cancellationToken);
        }

        public bool RequiresConnection => false;
    }

    public sealed class ConcurrentJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [DisallowConcurrentExecution]
    public sealed class NonConcurrentJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
