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

using System.Globalization;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The races a SQL Server firing round that takes no lock can lose (#4117), each run to the instant it is
/// lost at. With <c>READ_COMMITTED_SNAPSHOT</c> on, such a round claims in one transaction and fires in a
/// second: another node writes the row between the two, or while the fire holds it, and the trigger is
/// still fired at most once and the row left as the serialised order says.
/// </summary>
/// <remarks>
/// <para>
/// Two job stores on one database, as two clustered nodes are: the round's, whose
/// <see cref="SqlServerDelegate" /> is subclassed to pause the round where the test says, and the other
/// node's, which writes while the round is paused. A SQL Server round takes no lock only beside another live
/// node, so each test that needs one starts with both nodes checked in (<see cref="PeersCheckedIn" />).
/// </remarks>
[Category("db-sqlserver")]
[NonParallelizable]
public sealed class LockFreeRoundRacesSqlServerTest : ClusteredJobStoreTestBase
{
    private const string Group = "lockFreeRacesSqlServer";

    private static readonly JobKey JobKey = new("race", Group);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan WaitsOnTheRow = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly List<LocalTransactionJobStore> stores = [];

    private HookedSqlServerDelegate hooks;
    private LocalTransactionJobStore roundStore;
    private LocalTransactionJobStore otherStore;

    public LockFreeRoundRacesSqlServerTest() : base(ClusteredTestDatabase.SqlServerSnapshot)
    {
    }

    protected override string SchedulerName => "LockFreeRoundRacesSqlServer";

    [SetUp]
    public async Task CreateStores()
    {
        hooks = new HookedSqlServerDelegate();
        roundStore = await CreateStore("round-node", hooks, Database);
        otherStore = await CreateStore("other-node", new SqlServerDelegate(), Database);
    }

    [TearDown]
    public async Task ShutDownStores()
    {
        foreach (LocalTransactionJobStore store in stores)
        {
            await store.Shutdown();
        }

        stores.Clear();
    }

    /// <summary>
    /// The baseline: with the snapshot on and another live node beside it, a round of one concurrent-allowed
    /// trigger takes no lock, claims and reserves in one transaction, and fires in a second, which leaves the
    /// row advanced and the reservation <c>EXECUTING</c>.
    /// </summary>
    [Test]
    public async Task ARoundOfConcurrentAllowedWorkFiresApartWithoutTheLock()
    {
        roundStore.StaticRoundLockReason.Should().BeNull("the premise: the store found the database reading committed snapshots");
        await PeersCheckedIn();
        DateTimeOffset due = await Stored(Trigger("plain", DateTimeOffset.UtcNow.AddSeconds(-5)));

        TriggerAcquisitionResult round = await roundStore.AcquireNextTriggersAndFireDue(Request());

        round.LockName.Should().Be("none", "nothing in a round of one concurrent-allowed trigger needs TRIGGER_ACCESS");
        round.Fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();
        hooks.Guards.Should().Be(1, "the fire claimed its row again, in a transaction of its own");
        (await ReadRow("plain")).Should().Be(("WAITING", due + Interval));
        (await CountRows(
                "SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName AND ENTRY_ID = @entryId AND STATE = 'EXECUTING'",
                ("schedulerName", SchedulerName), ("entryId", round.Due[0].FireInstanceId)))
            .Should().Be(1, "the fire updated the reservation its claim wrote");
    }

    /// <summary>
    /// A pause committed by another node between the claim and the fire: the fire's guard finds the row no
    /// longer <c>ACQUIRED</c>, fires nothing and writes nothing over the pause. The reservation is the
    /// scheduler's to release, which leaves a paused row as it is.
    /// </summary>
    [Test]
    public async Task APauseBetweenTheClaimAndTheFireLeavesTheTriggerPausedAndUnfired()
    {
        await PeersCheckedIn();
        DateTimeOffset due = await Stored(Trigger("pausedBetween", DateTimeOffset.UtcNow.AddSeconds(-5)));
        Gate gate = new();
        hooks.BeforeGuard = gate.Pause;

        Task<TriggerAcquisitionResult> round = Run(() => roundStore.AcquireNextTriggersAndFireDue(Request()));
        await gate.Reached.WaitAsync(Timeout);

        await otherStore.PauseTrigger(new TriggerKey("pausedBetween", Group));

        gate.Release();
        TriggerAcquisitionResult result = await round;

        result.Fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().BeNull("the guard found the row paused");
        (await ReadRow("pausedBetween")).Should().Be(("PAUSED", due), "the fire wrote nothing over the pause");

        await roundStore.ReleaseAcquiredTrigger(result.Due[0]);
        (await ReadRow("pausedBetween")).Should().Be(("PAUSED", due), "a release moves a row from ACQUIRED only");
        (await CountRows("SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName", ("schedulerName", SchedulerName)))
            .Should().Be(0, "the release took the reservation");
    }

    /// <summary>
    /// A pause of the trigger's group on another node once the fire's guard holds the row waits on it, and
    /// lands over the fired row: paused at the fire time the firing advanced it to, round then pause.
    /// </summary>
    [Test]
    public async Task APauseAfterTheGuardWaitsForTheFireAndLandsOnTheRowItLeft()
    {
        await PeersCheckedIn();
        DateTimeOffset due = await Stored(Trigger("pausedAfter", DateTimeOffset.UtcNow.AddSeconds(-5)));
        Gate gate = new();
        hooks.AfterGuard = gate.Pause;

        Task<TriggerAcquisitionResult> round = Run(() => roundStore.AcquireNextTriggersAndFireDue(Request()));
        await gate.Reached.WaitAsync(Timeout);

        Task<List<string>> pause = Run(() => otherStore.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals(Group)));
        await Task.Delay(WaitsOnTheRow);
        pause.IsCompleted.Should().BeFalse("the pause's write of the row waits on the row the fire's guard holds");

        gate.Release();
        TriggerAcquisitionResult result = await round;
        (await pause.WaitAsync(Timeout)).Should().Contain(Group);

        result.Fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();
        (await ReadRow("pausedAfter")).Should().Be(("PAUSED", due + Interval),
            "the pause found the row WAITING at the fire time the firing advanced it to, and paused it there");
    }

    /// <summary>
    /// The job replaced by a serial one between the claim and the fire: the fire cannot take the lock while
    /// it holds the row, nor roll the committed claim back, so it hands the due trigger to the scheduler, still
    /// reserved, and the scheduler's <c>TriggersFired</c> fires it once, under the lock.
    /// </summary>
    [Test]
    public async Task AJobMadeSerialBetweenTheClaimAndTheFireIsFiredOnceUnderTheLock()
    {
        await PeersCheckedIn();
        DateTimeOffset due = await Stored(Trigger("serialised", DateTimeOffset.UtcNow.AddSeconds(-5)));
        Gate gate = new();
        hooks.BeforeGuard = gate.Pause;

        Task<TriggerAcquisitionResult> round = Run(() => roundStore.AcquireNextTriggersAndFireDue(Request()));
        await gate.Reached.WaitAsync(Timeout);

        await otherStore.AddJob(JobBuilder.Create<RaceJob>().WithIdentity(JobKey).StoreDurably().DisallowConcurrentExecution().Build(), AddJobOptions.Replacing);

        gate.Release();
        TriggerAcquisitionResult result = await round;

        result.Due.Should().BeEmpty();
        result.Pending.Should().ContainSingle().Which.Key.Name.Should().Be("serialised");
        result.LockReason.Should().Be("definition_changed");

        List<TriggerFiredResult> fired = await roundStore.TriggersFired(result.Pending);

        fired.Should().ContainSingle().Which.TriggerFiredBundle.JobDetail.ConcurrentExecutionDisallowed.Should().BeTrue("the fire ran the replaced job");
        (await ReadRow("serialised")).Should().Be(("BLOCKED", due + Interval), "fired once, under the lock, which holds a serial job's trigger BLOCKED");
        (await CountRows("SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName AND STATE = 'EXECUTING'", ("schedulerName", SchedulerName)))
            .Should().Be(1);
    }

    /// <summary>
    /// Two clustered 4.7 nodes reach for the same backlog. The round paused after its candidate read loses
    /// both rows to the other node's round, and fires only what was due behind them: a claim lost to another
    /// node is never fired twice, though the winner fires it in a second transaction.
    /// </summary>
    [Test]
    public async Task ARowAnotherNodeClaimedFirstIsFiredOnce()
    {
        HookedSqlServerDelegate contenderHooks = new();
        LocalTransactionJobStore contender = await CreateStore("contender-node", contenderHooks, Database, clustered: true);
        LocalTransactionJobStore rival = await CreateStore("rival-node", new SqlServerDelegate(), Database, clustered: true);

        // Each node's check-in has to have read the other's 4.7 row before its rounds may take no lock.
        await contender.CheckIn(Guid.NewGuid());
        await rival.CheckIn(Guid.NewGuid());
        await contender.CheckIn(Guid.NewGuid());

        DateTimeOffset now = DateTimeOffset.UtcNow;
        await Stored(Trigger("first", now.AddSeconds(-5)));
        await Stored(Trigger("second", now.AddSeconds(-4)));
        await Stored(Trigger("behind", now.AddSeconds(-3)));

        Gate gate = new();
        contenderHooks.AfterCandidateRead = gate.Pause;
        TriggerAcquisitionRequest two = Request() with { MaxCount = 2 };

        Task<TriggerAcquisitionResult> contended = Run(() => contender.AcquireNextTriggersAndFireDue(two));
        await gate.Reached.WaitAsync(Timeout);

        TriggerAcquisitionResult won = await rival.AcquireNextTriggersAndFireDue(two);
        won.LockName.Should().Be("none");
        won.Due.Select(x => x.Key.Name).Should().Equal(["first", "second"], "the rival's round reads the same two rows and commits both transactions first");

        gate.Release();
        TriggerAcquisitionResult lost = await contended;
        lost.Due.Select(x => x.Key.Name).Should().Equal(["behind"], "both claims found their row moved, and the read after them found what was due behind");
        lost.Fired.Should().OnlyContain(x => x.TriggerFiredBundle != null);
        (await CountRows("SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName AND STATE = 'EXECUTING'", ("schedulerName", SchedulerName)))
            .Should().Be(3, "each trigger fired once, whichever node won it");
    }

    /// <summary>
    /// A node alone in its cluster keeps 4.5's rounds, reason <c>no_live_peer</c>: a single node has no
    /// contention for a round without the lock to save (#4117). Once another live node has checked in and
    /// this node's check-in has seen it, its rounds take no lock.
    /// </summary>
    [Test]
    public async Task ANodeAloneKeeps45sRoundsUntilAnotherNodeIsLive()
    {
        await roundStore.CheckIn(Guid.NewGuid());
        await Stored(Trigger("alone", DateTimeOffset.UtcNow.AddSeconds(-5)));

        TriggerAcquisitionResult alone = await roundStore.AcquireNextTriggersAndFireDue(Request() with { MaxCount = 1 });

        alone.LockReason.Should().Be("no_live_peer");
        alone.Due.Should().BeEmpty("a round of one that takes the lock is 4.5's: acquired, and fired by the scheduler");
        alone.Pending.Should().ContainSingle().Which.Key.Name.Should().Be("alone");
        hooks.Guards.Should().Be(0, "nothing fired apart");
        await roundStore.ReleaseAcquiredTrigger(alone.Pending[0]);

        await otherStore.CheckIn(Guid.NewGuid());
        await roundStore.CheckIn(Guid.NewGuid());

        TriggerAcquisitionResult beside = await roundStore.AcquireNextTriggersAndFireDue(Request() with { MaxCount = 1 });
        beside.LockName.Should().Be("none", "another live 4.7 node has checked in");
        beside.Fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();
        hooks.Guards.Should().Be(1);
    }

    /// <summary>
    /// A store that is not clustered never takes a round without the lock on SQL Server, and does not ask the
    /// database.
    /// </summary>
    [Test]
    public async Task AStoreThatIsNotClusteredKeepsEveryRoundLocked()
    {
        LocalTransactionJobStore store = await CreateStore("single-node", new SqlServerDelegate(), Database, clustered: false);

        store.StaticRoundLockReason.Should().Be(RoundLockReason.NoLivePeer);
    }

    /// <summary>
    /// The same server's default database, where <c>READ_COMMITTED_SNAPSHOT</c> is off: the store asks at
    /// startup and keeps 4.5's rounds, and says why.
    /// </summary>
    [Test]
    public async Task ADatabaseWithoutReadCommittedSnapshotKeepsEveryRoundLocked()
    {
        LocalTransactionJobStore store = await CreateStore("default-node", new SqlServerDelegate(), ClusteredTestDatabase.SqlServer);

        store.StaticRoundLockReason.Should().Be(RoundLockReason.ReadCommittedSnapshotOff,
            "a SQL Server database that takes shared locks for read committed keeps every round locked");
    }

    private static TriggerAcquisitionRequest Request() => new()
    {
        NoLaterThan = DateTimeOffset.UtcNow.AddSeconds(1),
        MaxCount = 5,
        TimeWindow = TimeSpan.Zero,
    };

    /// <summary>
    /// A simple trigger of the one job, repeating every <see cref="Interval" /> from <paramref name="due" />,
    /// stored due at <paramref name="due" /> however far in the past that is.
    /// </summary>
    private static IOperableTrigger Trigger(string name, DateTimeOffset due)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(JobKey)
            .StartAt(due)
            .WithSimpleSchedule(x => x.WithInterval(Interval).RepeatForever())
            .Build();
        trigger.ComputeFirstFireTimeUtc(null);
        trigger.NextFireTimeUtc = due;
        return trigger;
    }

    private static Task<T> Run<T>(Func<ValueTask<T>> operation) => Task.Run(() => operation().AsTask());

    /// <summary>
    /// Both nodes checked in, and the round's node has read the other's 4.7 row: what a SQL Server round
    /// without the lock needs beside it.
    /// </summary>
    private async Task PeersCheckedIn()
    {
        await roundStore.CheckIn(Guid.NewGuid());
        await otherStore.CheckIn(Guid.NewGuid());
        await roundStore.CheckIn(Guid.NewGuid());
        roundStore.PeerRoundLockReason.Should().BeNull("the premise: another live 4.7 node is beside the round's");
    }

    private async Task<DateTimeOffset> Stored(IOperableTrigger trigger)
    {
        await roundStore.AddJob(JobBuilder.Create<RaceJob>().WithIdentity(JobKey).StoreDurably().Build(), AddJobOptions.Replacing);
        await roundStore.AddTrigger(trigger, AddTriggerOptions.Replacing);
        return trigger.NextFireTimeUtc!.Value;
    }

    /// <summary>The trigger's state and fire time, as the row holds them.</summary>
    private async Task<(string State, DateTimeOffset NextFireTimeUtc)> ReadRow(string name)
    {
        string state = (string) await ExecuteScalar(
            "SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_GROUP = @group AND TRIGGER_NAME = @name",
            ("schedulerName", SchedulerName), ("group", Group), ("name", name));
        object nextFireTime = await ExecuteScalar(
            "SELECT NEXT_FIRE_TIME FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_GROUP = @group AND TRIGGER_NAME = @name",
            ("schedulerName", SchedulerName), ("group", Group), ("name", name));

        return (state, new DateTimeOffset(Convert.ToInt64(nextFireTime, CultureInfo.InvariantCulture), TimeSpan.Zero));
    }

    /// <summary>
    /// One node's store on <paramref name="database" />, clustered unless <paramref name="clustered" /> says
    /// otherwise, and locking through the database. Nothing checks in on its own: a test that needs a round
    /// without the lock checks the nodes in itself.
    /// </summary>
    private async Task<LocalTransactionJobStore> CreateStore(
        string instanceId,
        IDriverDelegate driverDelegate,
        ClusteredTestDatabase database,
        bool clustered = true)
    {
        LocalTransactionJobStore store = new(TestJobStores.Dependencies(
            schedulerOptions: TestJobStores.SchedulerOptions(SchedulerName, instanceId),
            storeOptions: TestJobStores.StoreOptions("default", "QRTZ_", options =>
            {
                options.UseDbLocks = true;
                options.MisfireThreshold = TimeSpan.FromMinutes(10);
            }),
            clusteringOptions: TestJobStores.ClusteringOptions(options => options.Enabled = clustered),
            dbProvider: new DbProvider(database.Provider, database.ConnectionString),
            driverDelegate: driverDelegate) with
        {
            LockHandler = null,
        });

        await store.Initialize(TestJobStores.Identity(SchedulerName, instanceId));
        stores.Add(store);
        return store;
    }

    /// <summary>A job that never runs here: the stores are driven directly, and the firing is the fire's rows.</summary>
    public sealed class RaceJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// Where the round can be paused: after its candidate read, before the claim; and just before or just
    /// after the fire's guard, in the fire's own transaction. Each hook runs once.
    /// </summary>
    private sealed class HookedSqlServerDelegate : SqlServerDelegate
    {
        private Func<Task> afterCandidateRead;
        private Func<Task> beforeGuard;
        private Func<Task> afterGuard;
        private int guards;

        /// <summary>
        /// A subclass takes part in fire-on-acquire rounds only when it says so: these overrides call the base,
        /// so the round's members may stand in for the single-trigger ones as they do for the shipped delegate.
        /// </summary>
        public override bool SupportsFireOnAcquire => true;

        /// <summary>How many times a fire claimed its rows again.</summary>
        public int Guards => Volatile.Read(ref guards);

        public Func<Task> AfterCandidateRead
        {
            set => afterCandidateRead = value;
        }

        /// <summary>Pauses the next fire once, after the claim has committed and before the guard.</summary>
        public Func<Task> BeforeGuard
        {
            set => beforeGuard = value;
        }

        /// <summary>Pauses the next fire once, after its guard holds the rows and before it fires them.</summary>
        public Func<Task> AfterGuard
        {
            set => afterGuard = value;
        }

        public override async ValueTask<List<TriggerAcquireResult>> SelectTriggersToAcquire(
            ConnectionAndTransactionHolder conn,
            TriggerAcquisitionCriteria criteria,
            CancellationToken cancellationToken = default)
        {
            List<TriggerAcquireResult> candidates = await base.SelectTriggersToAcquire(conn, criteria, cancellationToken);
            if (candidates.Count > 0 && Interlocked.Exchange(ref afterCandidateRead, null) is { } hook)
            {
                await hook();
            }

            return candidates;
        }

        public override async ValueTask<List<TriggerKey>> UpdateTriggerStatesFromOtherStateWithNextFireTime(
            ConnectionAndTransactionHolder conn,
            IReadOnlyList<TriggerClaim> claims,
            StoredTriggerState newState,
            StoredTriggerState oldState,
            CancellationToken cancellationToken = default)
        {
            bool guard = oldState == StoredTriggerState.Acquired && newState == StoredTriggerState.Acquired;
            if (guard)
            {
                Interlocked.Increment(ref guards);
                if (Interlocked.Exchange(ref beforeGuard, null) is { } before)
                {
                    await before();
                }
            }

            List<TriggerKey> moved = await base.UpdateTriggerStatesFromOtherStateWithNextFireTime(conn, claims, newState, oldState, cancellationToken);
            if (guard && Interlocked.Exchange(ref afterGuard, null) is { } after)
            {
                await after();
            }

            return moved;
        }
    }

    /// <summary>
    /// Pauses the round where a hook is, until the test lets it go.
    /// </summary>
    private sealed class Gate
    {
        private readonly TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource proceed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes when the round has reached the hook and is waiting.</summary>
        public Task Reached => reached.Task;

        public void Release() => proceed.TrySetResult();

        public async Task Pause()
        {
            reached.TrySetResult();
            await proceed.Task;
        }
    }
}
