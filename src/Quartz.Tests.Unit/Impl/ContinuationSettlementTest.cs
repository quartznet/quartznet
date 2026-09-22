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
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// Which store <see cref="ContinuationSettlementTest" /> is run against.
/// </summary>
public enum ContinuationStoreKind
{
    InMemory,
    Sqlite
}

/// <summary>
/// What happens to a continuation at the moment it is settled, and to the triggers around it, asserted
/// against the in-memory store and against a SQLite-backed ADO store.
/// </summary>
/// <remarks>
/// <para>
/// <c>JobStoreContractTest</c> makes the same claims of every store on every dialect leg, but it lives in
/// the integration project, whose dialect legs need Docker. SQLite is a file, so a whole persistent store
/// is a temporary path, and the unit run sees both stores through the same assertions.
/// </para>
/// <para>
/// Driven against the store rather than through a scheduler, on a clock the test owns, with the store
/// initialized but never started — so no background loop can move a trigger between an arrangement and
/// its assertion (#3303), and "now" is an instant rather than a race.
/// </para>
/// </remarks>
[TestFixture(ContinuationStoreKind.InMemory)]
[TestFixture(ContinuationStoreKind.Sqlite)]
public sealed class ContinuationSettlementTest
{
    private const string Group = "settlement";
    private const string SchedulerName = "ContinuationSettlementTest";
    private const string DataSourceName = "continuation-settlement";

    /// <summary>
    /// Mid-morning UTC, on a date no test machine's own clock is near, so nothing here can agree with
    /// real time by accident.
    /// </summary>
    private static readonly DateTimeOffset epoch = new(2031, 6, 17, 10, 0, 0, TimeSpan.Zero);

    private readonly ContinuationStoreKind kind;

    private SqliteTestDatabase? database;
    private IDbProvider? dbProvider;

    private FakeTimeProvider clock = null!;
    private RecordingSignaler signals = null!;
    private IJobStore store = null!;

    public ContinuationSettlementTest(ContinuationStoreKind kind)
    {
        this.kind = kind;
    }

    [OneTimeSetUp]
    public async Task CreateDatabase()
    {
        if (kind != ContinuationStoreKind.Sqlite)
        {
            return;
        }

        database = new SqliteTestDatabase("continuation-settlement");

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
        database?.Dispose();
    }

    [SetUp]
    public async Task BuildStore()
    {
        clock = new FakeTimeProvider(epoch);
        signals = new RecordingSignaler();
        store = kind == ContinuationStoreKind.InMemory ? await InMemoryStore() : await SqliteStore();
    }

    [TearDown]
    public async Task ShutDownStore()
    {
        await store.Shutdown();
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // A released continuation joins the schedule the way any trigger stored at that moment would
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task AContinuationReleasedWhileItsJobRunsIsBlockedUntilTheJobFinishes()
    {
        IJobDetail busy = Job("busy", nonConcurrent: true);
        IOperableTrigger running = Hourly("running", busy.Key);
        await store.ScheduleJob(busy, running);

        IOperableTrigger parent = await ScheduleParent("parent");
        IOperableTrigger continuation = await ScheduleContinuation("continuation", parent.Key, ContinuationCondition.OnSuccess, busy.Key);

        List<IOperableTrigger> fired = await Fire(running.Key, parent.Key);

        await Complete(Firing(fired, parent.Key), ExecutionOutcome.Succeeded);

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Blocked,
            "its job disallows concurrent execution and is running under another trigger, so the release puts it "
            + "where any trigger of that job stored now would go — behind the execution, not beside it");

        (await AcquireKeys()).Should().NotContain(continuation.Key,
            "a blocked trigger is not schedulable, and firing it now would run the job twice at once");

        await Complete(Firing(fired, running.Key), ExecutionOutcome.Succeeded);

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Normal,
            "the execution it was blocked behind has finished, which unblocks it like every other trigger of the job");

        (await AcquireKeys()).Should().Contain(continuation.Key,
            "the release gave it the fire time it would have had, and nothing holds it back any more");
    }

    [Test]
    public async Task AContinuationReleasedIntoAPausedGroupWhileItsJobRunsIsHeldByBoth()
    {
        IJobDetail busy = Job("busy", nonConcurrent: true);
        IOperableTrigger running = Hourly("running", busy.Key);
        await store.ScheduleJob(busy, running);

        IOperableTrigger parent = await ScheduleParent("parent");
        IOperableTrigger continuation = await ScheduleContinuation(
            "continuation", parent.Key, ContinuationCondition.OnSuccess, busy.Key, group: "paused-group");

        await store.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals("paused-group"));

        List<IOperableTrigger> fired = await Fire(running.Key, parent.Key);
        await Complete(Firing(fired, parent.Key), ExecutionOutcome.Succeeded);

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Paused,
            "the release is not somebody resuming the group");

        await store.ResumeTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals("paused-group"));

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Blocked,
            "resuming the group while the job still runs leaves the trigger behind the execution — the pause was "
            + "one of two things holding it, and the other is still true");

        await Complete(Firing(fired, running.Key), ExecutionOutcome.Succeeded);

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Normal);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // A released continuation is an ordinary trigger: the release clears what it waited for
    //////////////////////////////////////////////////////////////////////////////////////////////

    [Test]
    public async Task AReleasedContinuationWaitsForNothingAnyMore()
    {
        IOperableTrigger parent = await ScheduleParent("parent");
        IOperableTrigger continuation = await ScheduleContinuation("released", parent.Key, ContinuationCondition.OnFailure);

        await Complete(Firing(await Fire(parent.Key), parent.Key), ExecutionOutcome.Failed);

        IOperableTrigger released = (await store.GetTrigger(continuation.Key))!;

        released.Continuation.Should().Be(Continuation.None,
            "the wait is over, and a trigger that is still said to wait for its parent is one every later path "
            + "has to remember to treat as released");

        released.GetTriggerBuilder().Build().Continuation.Should().Be(Continuation.None,
            "rebuilding a trigger is how a reschedule keeps what the caller did not change, and a wait that has "
            + "ended is not part of the definition any more — rebuilt, it would wait again for a firing that has been");

        TriggerHeader header = (await store.QueryTriggers(new TriggerQuery { Group = GroupMatcher<TriggerKey>.GroupEquals(Group) }))
            .Items.Single(x => x.Key.Equals(continuation.Key));

        header.ContinuesAfter.Should().BeNull("a listing says what a trigger waits for, and this one waits for nothing");
        header.ContinuationCondition.Should().BeNull();
    }

    [Test]
    public async Task AReleasedCronContinuationResetFromErrorKeepsItsSchedule()
    {
        IOperableTrigger parent = await ScheduleParent("parent");
        IOperableTrigger continuation = await ScheduleContinuation(
            "nightly",
            parent.Key,
            ContinuationCondition.OnSuccess,
            configure: x => x.WithCronSchedule("0 0 2 * * ?", cron => cron.InTimeZone(TimeZoneInfo.Utc)));

        await Complete(Firing(await Fire(parent.Key), parent.Key), ExecutionOutcome.Succeeded);

        // The release fires it at once, and that firing moves it on to its own schedule.
        IOperableTrigger firing = Firing(await Fire(continuation.Key), continuation.Key);

        DateTimeOffset nextNightly = new(2031, 6, 18, 2, 0, 0, TimeSpan.Zero);
        (await store.GetTrigger(continuation.Key))!.NextFireTimeUtc.Should().Be(nextNightly,
            "a released cron continuation keeps the schedule it was given, from the firing the release made on");

        await Complete(firing, ExecutionOutcome.Failed, SchedulerInstruction.SetTriggerError);
        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Error);

        (await store.ResetTriggerFromErrorState(continuation.Key)).Should().BeTrue();

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Normal);
        (await store.GetTrigger(continuation.Key))!.NextFireTimeUtc.Should().Be(nextNightly,
            "this is an ordinary cron trigger reset from an ordinary error, so it keeps its 02:00 — resetting it "
            + "into 'fire now' is for a continuation parked because its parent was deleted, which this is not");
    }

    [Test]
    public async Task ResettingAContinuationParkedByItsDeletedParentRunsItOnceAndForgetsTheParent()
    {
        IOperableTrigger parent = await ScheduleParent("doomed");
        IOperableTrigger continuation = await ScheduleContinuation("orphaned", parent.Key, ContinuationCondition.OnSuccess);

        await store.DeleteTrigger(parent.Key);

        (await store.GetTriggerState(continuation.Key)).Should().Be(TriggerState.Error,
            "the firing it asked about is never going to happen");

        (await store.ResetTriggerFromErrorState(continuation.Key)).Should().BeTrue();

        IOperableTrigger reset = (await store.GetTrigger(continuation.Key))!;
        reset.NextFireTimeUtc.Should().Be(clock.GetUtcNow(),
            "resetting a parked continuation runs it, at the instant a release would have given it");
        reset.Continuation.Should().Be(Continuation.None,
            "the reset is its release: from here it is an ordinary trigger, so a later error and reset of it keeps "
            + "whatever schedule it is on rather than firing it now again, and a reschedule does not wait for a "
            + "parent that no longer exists");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Scaffolding
    //////////////////////////////////////////////////////////////////////////////////////////////

    private async ValueTask<IJobStore> InMemoryStore()
    {
        RAMJobStore ram = TestJobStores.Ram(signals, clock);
        await ram.Initialize(TestJobStores.Identity(instanceName: SchedulerName));
        return ram;
    }

    private async ValueTask<IJobStore> SqliteStore()
    {
        LocalTransactionJobStore ado = new(TestJobStores.Dependencies(
            signaler: signals,
            timeProvider: clock,
            schedulerOptions: TestJobStores.SchedulerOptions(instanceName: SchedulerName),
            storeOptions: TestJobStores.StoreOptions(DataSourceName),
            dbProvider: dbProvider,
            driverDelegate: new SQLiteDelegate()));

        // Initialized but deliberately not started: SchedulerStarted() spawns the misfire loop, which
        // would sweep on its own thread and race every assertion here.
        await ado.Initialize(TestJobStores.Identity(instanceName: SchedulerName));

        // One database for the fixture, so each test clears what the previous one left.
        await ado.Clear();
        return ado;
    }

    private static IJobDetail Job(string name, bool nonConcurrent = false)
    {
        if (nonConcurrent)
        {
            return JobBuilder.Create<NonConcurrentSettlementJob>().WithIdentity(name, Group).StoreDurably().Build();
        }

        return JobBuilder.Create<SettlementJob>().WithIdentity(name, Group).StoreDurably().Build();
    }

    /// <summary>
    /// A trigger that is due now and fires every hour after, so a completion leaves it waiting for its
    /// next hour rather than deleting it.
    /// </summary>
    private IOperableTrigger Hourly(string name, JobKey job)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create(clock)
            .WithIdentity(name, Group)
            .ForJob(job)
            .StartAt(clock.GetUtcNow())
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        trigger.ComputeFirstFireTimeUtc(null);
        return trigger;
    }

    /// <summary>
    /// A parent: a job of its own and an hourly trigger due now, so the test can fire it and complete it.
    /// </summary>
    private async Task<IOperableTrigger> ScheduleParent(string name)
    {
        IJobDetail job = Job(name);
        IOperableTrigger parent = Hourly(name, job.Key);
        await store.ScheduleJob(job, parent);
        return parent;
    }

    /// <summary>
    /// A continuation of <paramref name="parent" />, with a start time already behind it so that only its
    /// state can keep it from being acquired. Fires <paramref name="job" /> when one is given — the job
    /// has to exist already — and a job of its own otherwise.
    /// </summary>
    private async Task<IOperableTrigger> ScheduleContinuation(
        string name,
        TriggerKey parent,
        ContinuationCondition condition,
        JobKey? job = null,
        string group = Group,
        Func<TriggerBuilder<IJob>, TriggerBuilder<IJob>>? configure = null,
        ICalendar? calendar = null)
    {
        IJobDetail? ownJob = job is null ? Job(name) : null;

        TriggerBuilder<IJob> builder = TriggerBuilder.Create(clock)
            .WithIdentity(name, group)
            .ForJob(job ?? ownJob!.Key)
            .StartAt(clock.GetUtcNow().AddMinutes(-1))
            .StartAfter(parent, condition);

        IOperableTrigger continuation = (IOperableTrigger) (configure?.Invoke(builder) ?? builder).Build();
        continuation.ComputeFirstFireTimeUtc(calendar);

        if (ownJob is not null)
        {
            await store.ScheduleJob(ownJob, continuation);
        }
        else
        {
            await store.AddTrigger(continuation);
        }

        return continuation;
    }

    /// <summary>
    /// Acquires and fires the named triggers, which must be exactly what is due.
    /// </summary>
    private async Task<List<IOperableTrigger>> Fire(params TriggerKey[] keys)
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddMinutes(1),
            MaxCount = 10,
            TimeWindow = TimeSpan.FromMinutes(1)
        });

        acquired.Select(x => x.Key).Should().BeEquivalentTo(keys, "the triggers under test are the ones due");

        List<TriggerFiredResult> results = await store.TriggersFired(acquired);
        results.Should().OnlyContain(x => x.TriggerFiredBundle != null,
            "a firing has to be committed before completing it says anything");

        return acquired;
    }

    private static IOperableTrigger Firing(List<IOperableTrigger> fired, TriggerKey key)
    {
        return fired.Single(x => x.Key.Equals(key));
    }

    /// <summary>
    /// What an acquisition would take right now, handed straight back so it changes nothing.
    /// </summary>
    private async Task<List<TriggerKey>> AcquireKeys()
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddMinutes(1),
            MaxCount = 10,
            TimeWindow = TimeSpan.FromMinutes(1)
        });

        foreach (IOperableTrigger trigger in acquired)
        {
            await store.ReleaseAcquiredTrigger(trigger);
        }

        return acquired.Select(x => x.Key).ToList();
    }

    private async Task Complete(
        IOperableTrigger firing,
        ExecutionOutcome outcome,
        SchedulerInstruction instruction = SchedulerInstruction.NoInstruction)
    {
        await store.FiringComplete(new TriggeredJobCompleteContext
        {
            Trigger = firing,
            JobDetail = (await store.GetJob(firing.JobKey))!,
            Instruction = instruction,
            Outcome = outcome
        });
    }

    private static string LoadSqliteTableScript()
    {
        string path = File.Exists("../../../../database/tables/tables_sqlite.sql")
            ? "../../../../database/tables/tables_sqlite.sql"
            : "../../../../../database/tables/tables_sqlite.sql";

        return File.ReadAllText(path);
    }

    /// <summary>
    /// Records what the store told the scheduler about, in the order it said it.
    /// </summary>
    private sealed class RecordingSignaler : ISchedulerSignaler
    {
        public List<TriggerKey> Finalized { get; } = [];

        public List<TriggerKey> InError { get; } = [];

        public ValueTask NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            Finalized.Add(trigger.Key);
            return default;
        }

        public ValueTask NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersTriggerInError(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            InError.Add(triggerKey);
            return default;
        }

        public ValueTask SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersError(SchedulerErrorContext errorContext, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>Never executed: these tests drive the store, not a scheduler.</summary>
    public sealed class SettlementJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>A job that forbids concurrent execution, which is what makes a store block its other triggers.</summary>
    [DisallowConcurrentExecution]
    public sealed class NonConcurrentSettlementJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
