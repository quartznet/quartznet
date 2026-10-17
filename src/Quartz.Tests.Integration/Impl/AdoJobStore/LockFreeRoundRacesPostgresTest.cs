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

using System.Collections.Concurrent;
using System.Globalization;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The races a firing round that takes no <c>TRIGGER_ACCESS</c> lock can lose on PostgreSQL (#4067), each
/// run to the instant it is lost at: another node writes the row between the round's candidate read and
/// its claim, or between its claim and its commit, and the round still fires each trigger once, as the
/// definition the row holds, and leaves the row as the serialised order says.
/// </summary>
/// <remarks>
/// <para>
/// Two job stores on one database, as two nodes are: the round's, whose <see cref="PostgreSQLDelegate" />
/// is subclassed to pause the round where the test says, and the other node's, which writes while the round
/// is paused. The stores are not clustered — a clustered round takes the lock until its first check-in has
/// read the state table — but take the database lock a clustered node takes, so the other node's writes
/// run under <c>TRIGGER_ACCESS</c> as they would there. What the round and the writes do to rows is read
/// straight off the tables.
/// </para>
/// <para>
/// A write made after the claim waits on the row the round holds until the round commits, and the test
/// asserts that it did: the write is still in flight a second later, and lands only once the round is
/// let go.
/// </para>
/// </remarks>
[Category("db-postgres")]
[NonParallelizable]
public sealed class LockFreeRoundRacesPostgresTest : ClusteredPostgresTestBase
{
    private const string Group = "lockFreeRaces";

    private static readonly JobKey JobKey = new("race", Group);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan WaitsOnTheRow = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private readonly List<LocalTransactionJobStore> stores = [];

    private HookedPostgresDelegate hooks;
    private RecordingSignaler otherSignaler;
    private LocalTransactionJobStore roundStore;
    private LocalTransactionJobStore otherStore;

    protected override string SchedulerName => "LockFreeRoundRaces";

    [SetUp]
    public async Task CreateStores()
    {
        hooks = new HookedPostgresDelegate();
        otherSignaler = new RecordingSignaler();

        // The round's store calls a trigger misfired only after ten minutes, so that it acquires the one
        // the misfire case stores two minutes overdue; the other store's pass calls it misfired after one.
        roundStore = await CreateStore("round-node", hooks, TimeSpan.FromMinutes(10), signaler: null);
        otherStore = await CreateStore("other-node", new PostgreSQLDelegate(), TimeSpan.FromMinutes(1), otherSignaler);
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
    /// The baseline: a round of one concurrent-allowed trigger on PostgreSQL takes no lock, claims, fires
    /// and commits, and leaves the row advanced with its firing recorded.
    /// </summary>
    [Test]
    public async Task ARoundOfConcurrentAllowedWorkFiresWithoutTheLock()
    {
        DateTimeOffset due = await Stored(Trigger("plain", DateTimeOffset.UtcNow.AddSeconds(-5)));

        TriggerAcquisitionResult round = await roundStore.AcquireNextTriggersAndFireDue(Request());

        round.LockName.Should().Be("none", "nothing in a round of one concurrent-allowed trigger needs TRIGGER_ACCESS");
        round.Fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();
        (await ReadRow("plain")).Should().Be(("WAITING", due + Interval));
        (await CountRows("SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName AND STATE = 'EXECUTING'", ("schedulerName", SchedulerName)))
            .Should().Be(1, "the fire's row is committed with the claim");
    }

    /// <summary>
    /// An edit landing between the candidate read and the claim is what the round fires: the claim holds the
    /// row to its fire time, which the edit did not move, and the read after the claim is of the edited row.
    /// </summary>
    [Test]
    public async Task AnEditBetweenTheCandidateReadAndTheClaimIsWhatTheRoundFires()
    {
        DateTimeOffset due = await Stored(Trigger("edited", DateTimeOffset.UtcNow.AddSeconds(-5), "original"));
        Gate gate = new();
        hooks.AfterCandidateRead = gate.Pause;

        Task<TriggerAcquisitionResult> round = Run(() => roundStore.AcquireNextTriggersAndFireDue(Request()));
        await gate.Reached.WaitAsync(Timeout);

        (await otherStore.UpdateTriggerDetails(new TriggerKey("edited", Group), new TriggerDetailsUpdate().WithDescription("edited")))
            .Should().BeTrue("the round has only read the row, so the edit lands at once");

        gate.Release();
        TriggerAcquisitionResult result = await round;

        result.LockName.Should().Be("none");
        result.Fired.Should().ContainSingle().Which.TriggerFiredBundle.Trigger.Description.Should().Be("edited",
            "the round reads the row after its claim, so what it fires is the row as the edit left it");
        IOperableTrigger stored = await roundStore.GetTrigger(new TriggerKey("edited", Group));
        stored.Description.Should().Be("edited", "the fire's write is of the edited trigger, so the edit survives it");
        stored.NextFireTimeUtc.Should().Be(due + Interval);
    }

    /// <summary>
    /// A trigger replaced between the candidate read and the claim by one due at the same instant is claimed
    /// — the compare-and-swap sees the fire time it read — and fired as the new definition, which is the one
    /// the fire's write advances.
    /// </summary>
    [Test]
    public async Task AReplacementDueAtTheSameInstantIsFiredAsTheNewDefinition()
    {
        DateTimeOffset due = DateTimeOffset.UtcNow.AddSeconds(-5);
        await Stored(Trigger("replaced", due, "original"));
        Gate gate = new();
        hooks.AfterCandidateRead = gate.Pause;

        Task<TriggerAcquisitionResult> round = Run(() => roundStore.AcquireNextTriggersAndFireDue(Request()));
        await gate.Reached.WaitAsync(Timeout);

        TimeSpan newInterval = TimeSpan.FromHours(2);
        (await otherStore.ReplaceTrigger(new TriggerKey("replaced", Group), Trigger("replaced", due, "replaced", newInterval)))
            .Should().BeTrue();

        gate.Release();
        TriggerAcquisitionResult result = await round;

        result.LockName.Should().Be("none");
        result.Fired.Should().ContainSingle().Which.TriggerFiredBundle.Trigger.Description.Should().Be("replaced",
            "the claim took the row by its fire time, which the replacement kept, and the read after it built the replacement");
        (await ReadRow("replaced")).Should().Be(("WAITING", due + newInterval),
            "the fire advances the definition it fired, which is the replacement's schedule");
    }

    /// <summary>
    /// The misfire handler on another node read the trigger overdue before the round claimed it. Its write
    /// waits on the row the claim holds, lands only once the round has committed, and then finds the fire
    /// time moved: the trigger is not re-armed, the pass reports no misfire, and the firing stands.
    /// </summary>
    [Test]
    public async Task AMisfirePassThatReadTheRowBeforeTheClaimWritesNothingOverTheFiring()
    {
        // Two minutes overdue: misfired by the other store's reckoning, still acquirable by the round's.
        DateTimeOffset due = await Stored(Trigger("overdue", DateTimeOffset.UtcNow.AddMinutes(-2)));
        Gate gate = new();
        hooks.AfterClaim = gate.Pause;

        Task<TriggerAcquisitionResult> round = Run(() => roundStore.AcquireNextTriggersAndFireDue(Request()));
        await gate.Reached.WaitAsync(Timeout);

        Task<RecoverMisfiredJobsResult> pass = Run(() => otherStore.RecoverMisfires(Guid.NewGuid()));
        await Task.Delay(WaitsOnTheRow);
        pass.IsCompleted.Should().BeFalse("the pass read the row WAITING and overdue, and its write waits on the row the round's claim holds");

        gate.Release();
        TriggerAcquisitionResult result = await round;
        RecoverMisfiredJobsResult recovered = await pass.WaitAsync(Timeout);

        result.LockName.Should().Be("none");
        result.Fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();
        recovered.ProcessedMisfiredTriggerCount.Should().Be(1, "the pass looked at the trigger");
        otherSignaler.Misfired.Should().BeEmpty("a trigger fired between the pass's read and its write was not misfired, and is not reported as one");
        (await ReadRow("overdue")).Should().Be(("WAITING", due + Interval),
            "the fire's advance stands; the misfire policy's would have moved the trigger to now or later");
    }

    /// <summary>
    /// A pause of the trigger's group on another node after the claim waits on the row and lands over the
    /// fired row: the trigger is paused at the fire time the firing advanced it to, which is the serialised
    /// order, round then pause.
    /// </summary>
    [Test]
    public async Task APauseAfterTheClaimLandsOnTheRowTheFiringLeft()
    {
        DateTimeOffset due = await Stored(Trigger("paused", DateTimeOffset.UtcNow.AddSeconds(-5)));
        Gate gate = new();
        hooks.AfterClaim = gate.Pause;

        Task<TriggerAcquisitionResult> round = Run(() => roundStore.AcquireNextTriggersAndFireDue(Request()));
        await gate.Reached.WaitAsync(Timeout);

        Task<List<string>> pause = Run(() => otherStore.PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupEquals(Group)));
        await Task.Delay(WaitsOnTheRow);
        pause.IsCompleted.Should().BeFalse("the pause's write of the row waits on the round's claim");

        gate.Release();
        TriggerAcquisitionResult result = await round;
        (await pause.WaitAsync(Timeout)).Should().Contain(Group);

        result.Fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();
        (await ReadRow("paused")).Should().Be(("PAUSED", due + Interval),
            "the pause found the row WAITING at the fire time the firing advanced it to, and paused it there");

        TriggerFiredBundle bundle = result.Fired[0].TriggerFiredBundle;
        Func<Task> complete = async () => await roundStore.TriggeredJobComplete(bundle.Trigger, bundle.JobDetail, SchedulerInstruction.NoInstruction);
        await complete.Should().NotThrowAsync("the firing completes over a row another node paused meanwhile");
    }

    /// <summary>
    /// Deleting the job on another node after the claim: the two transactions want the trigger's two rows in
    /// opposite orders — the delete takes the schedule row first, as the foreign key makes it, and then waits
    /// on the trigger row the claim holds; the fire holds the trigger row and then writes the schedule row.
    /// The database breaks the deadlock, the store retries whichever it chose, and either way the trigger is
    /// gone and fired at most once: the round fired it and the delete landed after, or the round was the
    /// victim and its retry found nothing left to fire.
    /// </summary>
    [Test]
    public async Task ADeleteAfterTheClaimIsResolvedWithTheTriggerGoneAndFiredAtMostOnce()
    {
        await Stored(Trigger("deleted", DateTimeOffset.UtcNow.AddSeconds(-5)));
        Gate gate = new();
        hooks.AfterClaim = gate.Pause;

        Task<TriggerAcquisitionResult> round = Run(() => roundStore.AcquireNextTriggersAndFireDue(Request()));
        await gate.Reached.WaitAsync(Timeout);

        Task<bool> delete = Run(() => otherStore.DeleteJob(JobKey));
        await Task.Delay(WaitsOnTheRow);
        delete.IsCompleted.Should().BeFalse("the delete of the trigger row waits on the round's claim");

        gate.Release();
        TriggerAcquisitionResult result = await round;
        (await delete.WaitAsync(Timeout)).Should().BeTrue("the job was there to delete, whichever transaction the database chose as the victim");

        (await roundStore.GetTrigger(new TriggerKey("deleted", Group))).Should().BeNull("the delete landed, after the round committed or after its retry found the row gone");
        result.Fired.Count.Should().BeLessThanOrEqualTo(1, "a trigger is fired at most once, deadlock or no deadlock");
        TestContext.Out.WriteLine(result.Fired.Count == 1
            ? "The round fired the trigger and the delete landed after it."
            : "The round was the deadlock's victim; its retry found the trigger gone.");

        if (result.Fired.Count == 1)
        {
            TriggerFiredBundle bundle = result.Fired[0].TriggerFiredBundle;
            bundle.Should().NotBeNull();
            Func<Task> complete = async () => await roundStore.TriggeredJobComplete(bundle.Trigger, bundle.JobDetail, SchedulerInstruction.NoInstruction);
            await complete.Should().NotThrowAsync("a firing whose trigger was deleted while it ran completes as a firing of a gone trigger always has");
        }

        (await CountRows("SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName", ("schedulerName", SchedulerName)))
            .Should().Be(0, "the delete takes the fired rows with the trigger, and a firing that never happened wrote none");
    }

    /// <summary>
    /// The job replaced between the candidate read and the claim by one that disallows concurrent execution:
    /// the round's decision that nothing in it needed the lock no longer holds. It reads the job after the
    /// claim, finds it serial, rolls back and runs again under the lock, and the job fires once.
    /// </summary>
    [Test]
    public async Task AJobMadeSerialBetweenTheReadAndTheClaimRunsTheRoundAgainUnderTheLock()
    {
        DateTimeOffset due = await Stored(Trigger("serialised", DateTimeOffset.UtcNow.AddSeconds(-5)));
        Gate gate = new();
        hooks.AfterCandidateRead = gate.Pause;

        Task<TriggerAcquisitionResult> round = Run(() => roundStore.AcquireNextTriggersAndFireDue(Request()));
        await gate.Reached.WaitAsync(Timeout);

        await otherStore.AddJob(JobBuilder.Create<RaceJob>().WithIdentity(JobKey).StoreDurably().DisallowConcurrentExecution().Build(), AddJobOptions.Replacing);

        gate.Release();
        TriggerAcquisitionResult result = await round;

        result.LockName.Should().Be("TRIGGER_ACCESS");
        result.LockReason.Should().Be("definition_changed", "the job the round read after its claim was not the job its candidate read described");
        result.Fired.Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();
        result.Fired[0].TriggerFiredBundle.JobDetail.ConcurrentExecutionDisallowed.Should().BeTrue("the fire ran the replaced job");
        (await ReadRow("serialised")).Should().Be(("BLOCKED", due + Interval),
            "fired once, by the attempt under the lock, which holds a serial job's trigger BLOCKED while the firing runs");
        (await CountRows("SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName", ("schedulerName", SchedulerName)))
            .Should().Be(1, "the attempt without the lock rolled back, claim and all");
    }

    /// <summary>
    /// The trigger moved into an execution group the round's node does not serve between the candidate read
    /// and the claim (#4069): the read after the claim finds the group, and the round rolls back to the lock
    /// as it does for a job made serial. The locked read leaves the trigger to a node that serves the group,
    /// so this node fires nothing, and the row is left as the edit wrote it.
    /// </summary>
    [Test]
    public async Task ATriggerMovedToAGroupTheNodeDoesNotServeIsLeftForANodeThatDoes()
    {
        IOperableTrigger trigger = Trigger("regrouped", DateTimeOffset.UtcNow.AddSeconds(-5));
        trigger.ExecutionGroup = "batch";
        DateTimeOffset due = await Stored(trigger);
        Gate gate = new();
        hooks.AfterCandidateRead = gate.Pause;

        TriggerAcquisitionRequest servingBatch = Request() with { ServedExecutionGroups = [ExecutionGroupScope.Named("batch")] };
        Task<TriggerAcquisitionResult> round = Run(() => roundStore.AcquireNextTriggersAndFireDue(servingBatch));
        await gate.Reached.WaitAsync(Timeout);

        (await otherStore.UpdateTriggerDetails(new TriggerKey("regrouped", Group), new TriggerDetailsUpdate().WithExecutionGroup("reports")))
            .Should().BeTrue("the round has only read the row, so the edit lands at once");

        gate.Release();
        TriggerAcquisitionResult result = await round;

        result.LockName.Should().Be("TRIGGER_ACCESS");
        result.LockReason.Should().Be("definition_changed", "the trigger the round read after its claim was in a group its candidate read did not return");
        result.Due.Should().BeEmpty("the locked read leaves a trigger in a group this node does not serve");
        result.Pending.Should().BeEmpty();
        (await ReadRow("regrouped")).Should().Be(("WAITING", due), "nothing fired it, and the attempt without the lock rolled its claim back");
        (await ExecuteScalar(
                "SELECT EXECUTION_GROUP FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_GROUP = @group AND TRIGGER_NAME = @name",
                ("schedulerName", SchedulerName), ("group", Group), ("name", "regrouped")))
            .Should().Be("reports");
        (await CountRows("SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName", ("schedulerName", SchedulerName)))
            .Should().Be(0, "neither attempt left a firing or a reservation");
    }

    private static Task<T> Run<T>(Func<ValueTask<T>> operation) => Task.Run(() => operation().AsTask());

    private async Task<DateTimeOffset> Stored(IOperableTrigger trigger)
    {
        await roundStore.AddJob(JobBuilder.Create<RaceJob>().WithIdentity(JobKey).StoreDurably().Build(), AddJobOptions.Replacing);
        await roundStore.AddTrigger(trigger, AddTriggerOptions.Replacing);
        return trigger.NextFireTimeUtc!.Value;
    }

    private static TriggerAcquisitionRequest Request() => new()
    {
        NoLaterThan = DateTimeOffset.UtcNow.AddSeconds(1),
        MaxCount = 5,
        TimeWindow = TimeSpan.Zero,
    };

    /// <summary>
    /// A simple trigger of the one job, repeating every <paramref name="interval" /> from <paramref name="due" />,
    /// stored due at <paramref name="due" /> however far in the past that is.
    /// </summary>
    private static IOperableTrigger Trigger(string name, DateTimeOffset due, string description = null, TimeSpan? interval = null)
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity(name, Group)
            .ForJob(JobKey)
            .StartAt(due)
            .WithDescription(description)
            .WithSimpleSchedule(x => x.WithInterval(interval ?? Interval).RepeatForever())
            .Build();
        trigger.ComputeFirstFireTimeUtc(null);
        trigger.NextFireTimeUtc = due;
        return trigger;
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
    /// One node's store on the shared database: not clustered, so that no check-in has to read the state
    /// table before a round may take no lock, and locking through the database as a clustered node does.
    /// </summary>
    private async Task<LocalTransactionJobStore> CreateStore(string instanceId, IDriverDelegate driverDelegate, TimeSpan misfireThreshold, ISchedulerSignaler signaler)
    {
        LocalTransactionJobStore store = new(TestJobStores.Dependencies(
            signaler: signaler,
            schedulerOptions: TestJobStores.SchedulerOptions(SchedulerName, instanceId),
            storeOptions: TestJobStores.StoreOptions("default", "QRTZ_", options =>
            {
                options.UseDbLocks = true;
                options.MisfireThreshold = misfireThreshold;
            }),
            clusteringOptions: TestJobStores.ClusteringOptions(options => options.Enabled = false),
            dbProvider: new DbProvider(Database.Provider, Database.ConnectionString),
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
    /// Where the round can be paused: after its candidate read, before the claim; or after the claim, while
    /// the claimed rows are held and before the round commits. Each hook runs once.
    /// </summary>
    private sealed class HookedPostgresDelegate : PostgreSQLDelegate
    {
        private Func<Task> afterCandidateRead;
        private Func<Task> afterClaim;

        /// <summary>
        /// A subclass takes part in fire-on-acquire rounds only when it says so: these overrides call the
        /// base, so the round's members may stand in for the single-trigger ones as they do for the
        /// shipped delegate.
        /// </summary>
        public override bool SupportsFireOnAcquire => true;

        /// <summary>Pauses the next round once, after its candidate read and before its claim.</summary>
        public Func<Task> AfterCandidateRead
        {
            set => afterCandidateRead = value;
        }

        /// <summary>Pauses the next round once, after its claim and before it reads the claimed rows.</summary>
        public Func<Task> AfterClaim
        {
            set => afterClaim = value;
        }

        public override async ValueTask<List<TriggerAcquireResult>> SelectTriggersToAcquire(
            ConnectionAndTransactionHolder conn,
            TriggerAcquisitionCriteria criteria,
            CancellationToken cancellationToken = default)
        {
            List<TriggerAcquireResult> candidates = await base.SelectTriggersToAcquire(conn, criteria, cancellationToken);

            // Taken before it runs, so the attempt after an escalation reads straight through.
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
            List<TriggerKey> claimed = await base.UpdateTriggerStatesFromOtherStateWithNextFireTime(conn, claims, newState, oldState, cancellationToken);
            if (claimed.Count > 0 && Interlocked.Exchange(ref afterClaim, null) is { } hook)
            {
                await hook();
            }

            return claimed;
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

    /// <summary>Records what the other node's store tells its scheduler.</summary>
    private sealed class RecordingSignaler : ISchedulerSignaler
    {
        public ConcurrentQueue<TriggerKey> Misfired { get; } = new();

        public ValueTask NotifyTriggerListenersMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
        {
            Misfired.Enqueue(trigger.Key);
            return default;
        }

        public ValueTask NotifySchedulerListenersFinalized(ITrigger trigger, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersJobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => default;

        public ValueTask SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc, CancellationToken cancellationToken = default) => default;

        public ValueTask NotifySchedulerListenersError(SchedulerErrorContext errorContext, CancellationToken cancellationToken = default) => default;
    }
}
