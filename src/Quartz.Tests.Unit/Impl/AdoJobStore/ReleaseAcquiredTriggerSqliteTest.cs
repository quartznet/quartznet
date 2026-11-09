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
/// <see cref="IJobStore.ReleaseAcquiredTrigger" /> on the persistent store, given a reservation that never
/// fired and a fire whose commit landed although the scheduler was told it failed (#4259).
/// </summary>
/// <remarks>
/// <para>
/// The scheduler thread releases every trigger of a batch whose fire it believes failed. When the commit
/// had in fact reached the database, the trigger's fired row is <c>EXECUTING</c>, and for a job that
/// disallows concurrent execution the fire has moved the job's other triggers to <c>BLOCKED</c> and
/// <c>PAUSED_BLOCKED</c>. No completion is coming for a firing the scheduler never dispatched, so the
/// release is what has to let go of them, unless another firing of the job is still executing.
/// </para>
/// <para>
/// A fire that commits normally leaves the database exactly as one whose commit landed unseen, so these
/// tests fire normally and then release, as the scheduler thread does when it could not tell.
/// <see cref="TriggersFiredBrokenCommitSqliteTest" /> drives the broken commit itself. The store is set up
/// as that test sets it up: a clock the test owns, initialized but never started, with the SQLite
/// serialization switched off on a file nobody else touches.
/// </para>
/// </remarks>
public sealed class ReleaseAcquiredTriggerSqliteTest
{
    private const string Group = "release";
    private const string SchedulerName = "ReleaseAcquiredTriggerSqliteTest";
    private const string DataSourceName = "release-acquired-trigger";
    private const string InstanceId = "node-a";

    /// <summary>On the hour, UTC, far from any machine's own clock.</summary>
    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private SqliteTestDatabase database = null!;
    private CountingDbProvider provider = null!;
    private FakeTimeProvider clock = null!;
    private LocalTransactionJobStore store = null!;

    [SetUp]
    public async Task BuildStore()
    {
        database = new SqliteTestDatabase(DataSourceName);

        await using (SqliteConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = new(LoadSqliteTableScript(), connection);
            await command.ExecuteNonQueryAsync();
        }

        provider = new CountingDbProvider(new DbProvider("SQLite-Microsoft", database.ConnectionString), emulateBatches: false);
        clock = new FakeTimeProvider(epoch);
        store = new LocalTransactionJobStore(TestJobStores.Dependencies(
            timeProvider: clock,
            schedulerOptions: TestJobStores.SchedulerOptions(instanceName: SchedulerName, instanceId: InstanceId),
            storeOptions: TestJobStores.StoreOptions(DataSourceName),
            dbProvider: provider,
            driverDelegate: new SQLiteDelegate()));

        // Initialized but deliberately not started, so no misfire loop races an assertion.
        await store.Initialize(TestJobStores.Identity(instanceName: SchedulerName, instanceId: InstanceId));
        await store.Clear();

        // Initialize forced the SQLite serialization on; one test at a time on a private file has no second
        // writer, and without it the store takes its locks as every other dialect's does.
        store.LockAllOperations = false;
        store.LockHandler = new InProcessLockHandler();
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await store.Shutdown();
        database.Dispose();
    }

    /// <summary>
    /// The fire committed and the scheduler released it. Nothing else of the job is executing, so the
    /// job's other triggers go back to where the fire found them, as a completion would put them.
    /// </summary>
    [Test]
    public async Task ReleasingAFireThatCommittedLetsGoOfItsSerialJobsOtherTriggers()
    {
        IJobDetail job = SerialJob();
        DateTimeOffset now = clock.GetUtcNow();
        IOperableTrigger due = Hourly("due", job, now);
        IOperableTrigger waiting = OneShot("waiting", job, now.AddHours(1));
        IOperableTrigger paused = OneShot("paused", job, now.AddHours(1));
        await Schedule(job, due, waiting, paused);
        await store.PauseTrigger(paused.Key);

        IOperableTrigger acquired = await AcquireOne(due.Key);
        await FireCommitted(acquired);

        (await StoredState(waiting.Key)).Should().Be(AdoConstants.StateBlocked, "the premise: the fire holds the job's other triggers back");
        (await StoredState(paused.Key)).Should().Be(AdoConstants.StatePausedBlocked, "the premise: a paused one too");

        await store.ReleaseAcquiredTrigger(acquired);

        (await FiredEntries()).Should().BeEmpty("the release deletes the fired row whatever its state");
        (await StoredState(waiting.Key)).Should().Be(AdoConstants.StateWaiting,
            "nothing of the job is executing and no completion is coming, so the release lets go of the job's other triggers");
        (await StoredState(paused.Key)).Should().Be(AdoConstants.StatePaused, "a paused trigger the fire held back goes back to paused");
        (await StoredState(due.Key)).Should().Be(AdoConstants.StateWaiting, "the released trigger is let go of too, at the fire time the fire moved it to");
    }

    /// <summary>
    /// A trigger held back by the fire may have gone past its fire time by the time the release lets go of
    /// it. The release applies its misfire policy, as a completion does for the triggers it lets go of.
    /// </summary>
    [Test]
    public async Task ReleasingAFireThatCommittedAppliesTheMisfirePolicyOfATriggerThatWentLateWhileHeld()
    {
        IJobDetail job = SerialJob();
        DateTimeOffset now = clock.GetUtcNow();
        IOperableTrigger due = OneShot("due", job, now);
        IOperableTrigger late = OneShot("late", job, now.AddMinutes(1), SimpleTriggerMisfireInstruction.NextWithRemainingCount);
        await Schedule(job, due, late);

        IOperableTrigger acquired = await AcquireOne(due.Key);
        await FireCommitted(acquired);
        (await StoredState(late.Key)).Should().Be(AdoConstants.StateBlocked, "the premise: the fire holds it back");

        // Well past the misfire threshold of a trigger due a minute after the fire.
        clock.Advance(TimeSpan.FromHours(1));

        await store.ReleaseAcquiredTrigger(acquired);

        (await StoredState(late.Key)).Should().BeNull(
            "its only firing was missed while it was held, and its policy leaves it nothing to fire, so it is deleted as a completion deletes it");
    }

    /// <summary>
    /// The fire committed, but another firing of the same job is executing too, as a firing on another
    /// node of a cluster can be. That firing's completion is what lets go of the job's triggers, so the
    /// release leaves them held.
    /// </summary>
    [Test]
    public async Task ReleasingAFireThatCommittedLeavesItsJobsTriggersHeldWhileAnotherFiringOfTheJobExecutes()
    {
        IJobDetail job = SerialJob();
        DateTimeOffset now = clock.GetUtcNow();
        IOperableTrigger due = Hourly("due", job, now);
        IOperableTrigger waiting = OneShot("waiting", job, now.AddHours(1));
        IOperableTrigger paused = OneShot("paused", job, now.AddHours(1));
        await Schedule(job, due, waiting, paused);
        await store.PauseTrigger(paused.Key);

        IOperableTrigger acquired = await AcquireOne(due.Key);
        await FireCommitted(acquired);
        await InsertExecutingFiringOnAnotherNode("node-b-firing", waiting.Key, job.Key);

        await store.ReleaseAcquiredTrigger(acquired);

        (await FiredEntries()).Should().Equal(["node-b-firing"], "the released row is gone and the other node's firing is not the release's to touch");
        (await StoredState(waiting.Key)).Should().Be(AdoConstants.StateBlocked, "the job is still executing on the other node");
        (await StoredState(paused.Key)).Should().Be(AdoConstants.StatePausedBlocked, "the job is still executing on the other node");
        (await StoredState(due.Key)).Should().Be(AdoConstants.StateBlocked, "the released trigger is held back with the rest of the job");
    }

    /// <summary>
    /// The release of a reservation that never fired, while another firing of its job executes: the
    /// reservation stays where that fire put it, and so do the job's other triggers (#3926).
    /// </summary>
    [Test]
    public async Task ReleasingAReservationLeavesItsJobsTriggersHeldWhileAnotherFiringOfTheJobExecutes()
    {
        IJobDetail job = SerialJob();
        DateTimeOffset now = clock.GetUtcNow();
        IOperableTrigger reserved = OneShot("reserved", job, now);
        IOperableTrigger running = OneShot("running", job, now.AddMilliseconds(500));
        IOperableTrigger waiting = OneShot("waiting", job, now.AddHours(1));
        await Schedule(job, reserved, running, waiting);

        IOperableTrigger acquired = await AcquireOne(reserved.Key);
        IOperableTrigger other = await AcquireOne(running.Key);
        TriggerFiredBundle executing = await FireCommitted(other);

        (await StoredState(reserved.Key)).Should().Be(AdoConstants.StateBlocked, "the premise: the other fire holds back the reservation as well");

        await store.ReleaseAcquiredTrigger(acquired);

        (await FiredEntries()).Should().Equal([executing.Trigger.FireInstanceId!], "the reservation's row is gone and the running firing's is left");
        (await StoredState(reserved.Key)).Should().Be(AdoConstants.StateBlocked, "the job is executing, so the reservation is held back with the rest");
        (await StoredState(waiting.Key)).Should().Be(AdoConstants.StateBlocked, "the job is executing");
    }

    /// <summary>
    /// The ordinary release: a reservation the scheduler gives back unfired — a sooner trigger, standby,
    /// shutdown. Only its own trigger moves, with the four statements the release has always made.
    /// </summary>
    [Test]
    public async Task ReleasingAReservationThatNeverFiredPutsBackItsTriggerAloneWithTheStatementsItAlwaysMade()
    {
        IJobDetail job = SerialJob();
        DateTimeOffset now = clock.GetUtcNow();
        IOperableTrigger due = OneShot("due", job, now);
        IOperableTrigger waiting = OneShot("waiting", job, now.AddHours(1));
        await Schedule(job, due, waiting);

        IOperableTrigger acquired = await AcquireOne(due.Key);

        provider.Reset();
        await store.ReleaseAcquiredTrigger(acquired);
        List<string> statements = provider.RoundTrips.Select(x => x.Statements[0]).ToList();

        (await FiredEntries()).Should().BeEmpty();
        (await StoredState(due.Key)).Should().Be(AdoConstants.StateWaiting);
        (await StoredState(waiting.Key)).Should().Be(AdoConstants.StateWaiting);

        statements.Should().HaveCount(4,
            "the job's fired rows are read, the reservation's row is deleted, and the trigger is moved from ACQUIRED and from BLOCKED: "
            + string.Join(" | ", statements));
        statements.Should().NotContain(Sql(StdAdoConstants.SqlUpdateJobTriggerStatesFromOtherState),
            "a reservation that never fired held nothing of its job back, so there is nothing of the job's to let go of");
    }

    /// <summary>
    /// A job that allows concurrent execution has nothing of the job's to let go of when its fire is
    /// released, and a trigger of it may be held back by its own overlap policy while its own firing
    /// runs. That hold is the running firing's to let go of, so the release leaves it alone.
    /// </summary>
    [Test]
    public async Task ReleasingAFireThatCommittedForAJobThatAllowsConcurrencyLeavesAnotherTriggersOverlapHoldAlone()
    {
        IJobDetail job = JobBuilder.Create<ConcurrentJobUnderTest>().WithIdentity("concurrent", Group).StoreDurably().Build();
        DateTimeOffset now = clock.GetUtcNow();
        IOperableTrigger held = Hourly("held", job, now, OverlapPolicy.BufferOne);
        IOperableTrigger due = OneShot("due", job, now);
        await Schedule(job, held, due);

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = now.AddSeconds(1),
            MaxCount = 2,
            TimeWindow = TimeSpan.Zero,
        });
        acquired.Select(x => x.Key).Should().BeEquivalentTo([held.Key, due.Key], "the premise: both are due, and the job allows both to run");

        List<TriggerFiredResult> fired = await store.TriggersFired(acquired);
        fired.Should().AllSatisfy(x => x.TriggerFiredBundle.Should().NotBeNull());
        (await StoredState(held.Key)).Should().Be(AdoConstants.StateBlocked, "the premise: a BufferOne trigger is held while its firing runs");

        await store.ReleaseAcquiredTrigger(acquired.Single(x => x.Key.Equals(due.Key)));

        (await StoredState(held.Key)).Should().Be(AdoConstants.StateBlocked, "its own firing is still running, and its completion lets go of it");
        (await FiredEntries()).Should().ContainSingle("the held trigger's firing is left alone");
    }

    private static IJobDetail SerialJob()
    {
        return JobBuilder.Create<SerialJobUnderTest>().WithIdentity("serial", Group).StoreDurably().Build();
    }

    private Task Schedule(IJobDetail job, params IOperableTrigger[] triggers)
    {
        return store.ScheduleJobs(
            new Dictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>> { [job] = triggers },
            ScheduleJobOptions.Replacing).AsTask();
    }

    /// <summary>Acquires the next trigger due, as the scheduler thread does, and says it is the expected one.</summary>
    private async Task<IOperableTrigger> AcquireOne(TriggerKey expected)
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddSeconds(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
        });

        IOperableTrigger trigger = acquired.Should().ContainSingle().Subject;
        trigger.Key.Should().Be(expected, "the premise: the trigger under test is the one acquired");
        return trigger;
    }

    /// <summary>Fires an acquired trigger with a commit that succeeds, and returns its bundle.</summary>
    private async Task<TriggerFiredBundle> FireCommitted(IOperableTrigger acquired)
    {
        List<TriggerFiredResult> fired = await store.TriggersFired([acquired]);
        TriggerFiredBundle? bundle = fired.Should().ContainSingle().Subject.TriggerFiredBundle;
        bundle.Should().NotBeNull("the premise: the fire committed");
        return bundle!;
    }

    /// <summary>
    /// Writes the fired row a firing of the job on another node leaves while it runs, as that node's fire
    /// would have written it.
    /// </summary>
    private async Task InsertExecutingFiringOnAnotherNode(string entryId, TriggerKey trigger, JobKey job)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO QRTZ_FIRED_TRIGGERS (SCHED_NAME, ENTRY_ID, TRIGGER_NAME, TRIGGER_GROUP, INSTANCE_NAME, FIRED_TIME, SCHED_TIME, PRIORITY, STATE, JOB_NAME, JOB_GROUP, IS_NONCONCURRENT, REQUESTS_RECOVERY) "
            + "VALUES (@scheduler, @entry, @name, @group, 'node-b', @time, @time, 5, 'EXECUTING', @jobName, @jobGroup, 1, 0)";
        command.Parameters.AddWithValue("@scheduler", SchedulerName);
        command.Parameters.AddWithValue("@entry", entryId);
        command.Parameters.AddWithValue("@name", trigger.Name);
        command.Parameters.AddWithValue("@group", trigger.Group);
        command.Parameters.AddWithValue("@time", clock.GetUtcNow().UtcTicks);
        command.Parameters.AddWithValue("@jobName", job.Name);
        command.Parameters.AddWithValue("@jobGroup", job.Group);
        await command.ExecuteNonQueryAsync();
    }

    private IOperableTrigger OneShot(
        string name,
        IJobDetail job,
        DateTimeOffset startAt,
        SimpleTriggerMisfireInstruction misfire = SimpleTriggerMisfireInstruction.SmartPolicy)
    {
        return Build(TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(startAt)
            .WithSimpleSchedule(x => x.WithRepeatCount(0).WithMisfireInstruction(misfire)));
    }

    private IOperableTrigger Hourly(string name, IJobDetail job, DateTimeOffset startAt, OverlapPolicy policy = OverlapPolicy.Default)
    {
        return Build(TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(startAt)
            .WithOverlapPolicy(policy)
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever()));
    }

    private static IOperableTrigger Build(TriggerBuilder<IJob> builder)
    {
        IOperableTrigger trigger = (IOperableTrigger) builder.Build();
        trigger.ComputeFirstFireTimeUtc(calendar: null);
        return trigger;
    }

    private async Task<string?> StoredState(TriggerKey key)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE TRIGGER_NAME = @name AND TRIGGER_GROUP = @group";
        command.Parameters.AddWithValue("@name", key.Name);
        command.Parameters.AddWithValue("@group", key.Group);
        return (string?) await command.ExecuteScalarAsync();
    }

    private async Task<List<string>> FiredEntries()
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ENTRY_ID FROM QRTZ_FIRED_TRIGGERS";

        List<string> entries = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            entries.Add(reader.GetString(0));
        }

        return entries;
    }

    private static string Sql(string constant) => AdoJobStoreUtil.ReplaceTablePrefixCached(constant, AdoConstants.DefaultTablePrefix);

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    /// <summary>Never executed: these tests drive the store, not a scheduler.</summary>
    [DisallowConcurrentExecution]
    public sealed class SerialJobUnderTest : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>Never executed: these tests drive the store, not a scheduler.</summary>
    public sealed class ConcurrentJobUnderTest : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
