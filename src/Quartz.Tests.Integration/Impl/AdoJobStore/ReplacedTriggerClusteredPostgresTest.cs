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

using Microsoft.Extensions.Logging.Abstractions;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Xml;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A trigger replaced on one node of a cluster while another node holds it, and a declared trigger
/// re-applied at one node's start while another node fires it, on PostgreSQL with the shipped delegate.
/// </summary>
/// <remarks>
/// <para>
/// #4323: a node that acquired a trigger ahead of its fire time does not fire the copy it holds once
/// another node has replaced the trigger, does not write its definition back over the replacement, and
/// does not undo the other node's acquisition of the replacement when it lets go of its own.
/// </para>
/// <para>
/// #4324: a start re-applies a declared trigger from a read made outside the store's lock. A firing on
/// another node between that read and the replace is not undone by it, and an unchanged declaration
/// leaves another node's acquisition of the trigger firable.
/// </para>
/// <para>
/// Deterministic, as the reports' programs are: the holding and firing nodes are clustered job stores
/// driven call by call, as in <see cref="LockFreeRoundRacesPostgresTest" />, and the node that replaces
/// or re-applies is a clustered scheduler that is never started. The firing between the read and the
/// replace is made by the scheduler the processor is handed, right after the processor's read. The
/// same scenarios on one SQLite file are <c>ReplacedTriggerAcquisitionSqliteTest</c> and
/// <c>DeclaredTriggerReappliedUnderAFiringTest</c>.
/// </para>
/// </remarks>
[Category("db-postgres")]
[NonParallelizable]
public sealed class ReplacedTriggerClusteredPostgresTest : ClusteredPostgresTestBase
{
    private const string Group = "replaced";

    private static readonly JobKey JobKey = new("job", Group);
    private static readonly TriggerKey TriggerKey = new("trigger", Group);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly List<LocalTransactionJobStore> stores = [];
    private readonly List<IScheduler> schedulers = [];

    private LocalTransactionJobStore nodeA;
    private LocalTransactionJobStore nodeB;
    private IScheduler admin;
    private DateTimeOffset due;

    protected override string SchedulerName => "ReplacedTriggerCluster";

    [SetUp]
    public async Task CreateNodes()
    {
        nodeA = await CreateStore("node-a");
        nodeB = await CreateStore("node-b");
        await nodeA.CheckIn(Guid.NewGuid());
        await nodeB.CheckIn(Guid.NewGuid());

        // The node that replaces or re-applies: a scheduler of the same cluster that is never started, as
        // the issue's node-b is when it replaces, and as every node is while its start applies declarations.
        admin = await CreateScheduler("node-admin");
        schedulers.Add(admin);

        due = DateTimeOffset.UtcNow.AddSeconds(30);
        due = due.AddTicks(-(due.Ticks % TimeSpan.TicksPerSecond));

        await admin.AddJob(JobBuilder.Create<NoOpJob>().WithIdentity(JobKey).StoreDurably().Build());
        await admin.ScheduleJob(TriggerBuilder.Create().WithIdentity(TriggerKey).ForJob(JobKey).StartAt(due)
            .WithSimpleSchedule(x => x.WithInterval(Interval).RepeatForever()).Build());
    }

    [TearDown]
    public async Task ShutDownNodes()
    {
        foreach (IScheduler scheduler in schedulers)
        {
            await scheduler.Shutdown();
        }

        foreach (LocalTransactionJobStore store in stores)
        {
            await store.Shutdown();
        }

        schedulers.Clear();
        stores.Clear();
    }

    /// <summary>
    /// The issue's case, at the fire time comparing fire times alone would miss: the replacement is due when
    /// the held copy is.
    /// </summary>
    [Test]
    public async Task AHeldCopyOfATriggerAnotherNodeReplacedIsNotFiredAndLeavesTheReplacementAsTheOtherNodeHoldsIt()
    {
        IOperableTrigger heldByA = await ReserveOne(nodeA);

        (await admin.RescheduleJob(TriggerKey, TriggerBuilder.Create().WithIdentity(TriggerKey).ForJob(JobKey).StartAt(due)
            .WithSimpleSchedule(x => x.WithInterval(2 * Interval).RepeatForever()).Build())).Should().Be(due);
        IOperableTrigger heldByB = await ReserveOne(nodeB);
        heldByB.NextFireTimeUtc.Should().Be(heldByA.NextFireTimeUtc, "the premise: the replacement is due when A's copy is");

        (await nodeA.TriggersFired([heldByA])).Should().ContainSingle().Which.TriggerFiredBundle.Should().BeNull(
            "A holds a copy of the trigger that was replaced, and the trigger stored is not that one");
        (await IntervalStored()).Should().Be(2 * Interval, "A's copy is not written over the replacement");

        await nodeA.ReleaseAcquiredTrigger(heldByA);
        (await StateStored()).Should().Be("ACQUIRED", "the row is B's acquisition, and A's release is not B's to undo");
        (await FiredRows()).Should().Equal(["node-b ACQUIRED"], "A's reservation went with the trigger it reserved");

        TriggerFiredBundle firedOnB = (await nodeB.TriggersFired([heldByB])).Should().ContainSingle().Subject.TriggerFiredBundle;
        firedOnB.Should().NotBeNull("B's acquisition is still its own");
        ((ISimpleTrigger) firedOnB.Trigger).RepeatInterval.Should().Be(2 * Interval);
        (await IntervalStored()).Should().Be(2 * Interval);
        (await ReadRow()).NextFireTimeUtc.Should().Be(due.Add(2 * Interval));
    }

    /// <summary>
    /// The firing between the read and the replace: the start's replace does not put back the fire time
    /// another node has just fired.
    /// </summary>
    [Test]
    public async Task ADeclarationReappliedWhileAnotherNodeFiresTheTriggerDoesNotPutTheFiredTimeBack()
    {
        await FireAndComplete(nodeA);
        (await ReadRow()).Should().Be(("WAITING", due.Add(Interval), due), "the premise: the trigger has fired once");

        await Reapply(new FiringAfterReadScheduler(admin, () => FireAndComplete(nodeA)));

        (await ReadRow()).Should().Be(("WAITING", due.Add(2 * Interval), due.Add(Interval)),
            "the start read the trigger before node A fired it again, and the replace works from the row as it is when it is made");
        (await CountRows("SELECT COUNT(*) FROM QRTZ_SIMPLE_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TIMES_TRIGGERED = 2",
            ("schedulerName", SchedulerName))).Should().Be(1, "both firings are counted");
    }

    /// <summary>
    /// An unchanged declaration re-applied while another node holds the trigger acquired leaves that
    /// acquisition firable.
    /// </summary>
    [Test]
    public async Task AnUnchangedDeclarationReappliedWhileAnotherNodeHoldsTheTriggerLeavesItsAcquisitionFirable()
    {
        await FireAndComplete(nodeA);
        IOperableTrigger heldByA = await ReserveOne(nodeA);

        await Reapply(admin);

        (await StateStored()).Should().Be("ACQUIRED", "the start re-applied the trigger stored, and left its row as it is");
        (await FiredRows()).Should().Equal(["node-a ACQUIRED"]);
        (await nodeA.TriggersFired([heldByA])).Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull(
            "re-applying the same declaration is not a new trigger, and A's acquisition of it still holds");
    }

    /// <summary>
    /// What a start does with the declared trigger: the processor <c>AddQuartz</c> declarations go through,
    /// with relative scheduling on, over <paramref name="scheduler" />.
    /// </summary>
    private static async Task Reapply(IScheduler scheduler)
    {
        DeclaringProcessor processor = new(
            JobBuilder.Create<NoOpJob>().WithIdentity(JobKey).StoreDurably().Build(),
            TriggerBuilder.Create().WithIdentity(TriggerKey).ForJob(JobKey).StartNow()
                .WithSimpleSchedule(x => x.WithInterval(Interval).RepeatForever()).Build());

        await processor.ScheduleJobs(scheduler);
    }

    private static async Task<IOperableTrigger> ReserveOne(LocalTransactionJobStore store)
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = DateTimeOffset.UtcNow.AddDays(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
        });

        return acquired.Should().ContainSingle("the trigger is the only one stored, and it is waiting").Subject;
    }

    private static async Task FireAndComplete(LocalTransactionJobStore store)
    {
        IOperableTrigger acquired = await ReserveOne(store);
        TriggerFiredBundle bundle = (await store.TriggersFired([acquired])).Should().ContainSingle().Subject.TriggerFiredBundle;
        bundle.Should().NotBeNull("the premise: the node's acquisition is its own to fire");
        await store.TriggeredJobComplete(bundle.Trigger, bundle.JobDetail, SchedulerInstruction.NoInstruction);
    }

    private async Task<(string State, DateTimeOffset NextFireTimeUtc, DateTimeOffset PreviousFireTimeUtc)> ReadRow()
    {
        string state = await StateStored();
        object next = await ExecuteScalar(
            "SELECT NEXT_FIRE_TIME FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @name",
            ("schedulerName", SchedulerName), ("name", TriggerKey.Name));
        object previous = await ExecuteScalar(
            "SELECT PREV_FIRE_TIME FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @name",
            ("schedulerName", SchedulerName), ("name", TriggerKey.Name));

        return (state, Instant(next), Instant(previous));

        static DateTimeOffset Instant(object ticks) => new(Convert.ToInt64(ticks, CultureInfo.InvariantCulture), TimeSpan.Zero);
    }

    private async Task<string> StateStored()
    {
        return (string) await ExecuteScalar(
            "SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @name",
            ("schedulerName", SchedulerName), ("name", TriggerKey.Name));
    }

    private async Task<TimeSpan> IntervalStored()
    {
        object interval = await ExecuteScalar(
            "SELECT REPEAT_INTERVAL FROM QRTZ_SIMPLE_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @name",
            ("schedulerName", SchedulerName), ("name", TriggerKey.Name));
        return TimeSpan.FromMilliseconds(Convert.ToInt64(interval, CultureInfo.InvariantCulture));
    }

    private async Task<List<string>> FiredRows()
    {
        List<string> rows = [];
        foreach (string instance in new[] { "node-a", "node-b", "node-admin" })
        {
            foreach (string state in new[] { "ACQUIRED", "EXECUTING" })
            {
                int count = await CountRows(
                    "SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName AND INSTANCE_NAME = @instance AND STATE = @state",
                    ("schedulerName", SchedulerName), ("instance", instance), ("state", state));
                rows.AddRange(Enumerable.Repeat($"{instance} {state}", count));
            }
        }

        return rows;
    }

    /// <summary>
    /// One node's clustered store on the shared database, driven by the test call by call.
    /// </summary>
    private async Task<LocalTransactionJobStore> CreateStore(string instanceId)
    {
        LocalTransactionJobStore store = new(TestJobStores.Dependencies(
            schedulerOptions: TestJobStores.SchedulerOptions(SchedulerName, instanceId),
            storeOptions: TestJobStores.StoreOptions("default", "QRTZ_", options => options.UseDbLocks = true),
            clusteringOptions: TestJobStores.ClusteringOptions(options => options.Enabled = true),
            dbProvider: new DbProvider(Database.Provider, Database.ConnectionString),
            driverDelegate: new PostgreSQLDelegate()) with
        {
            LockHandler = null,
        });

        await store.Initialize(TestJobStores.Identity(SchedulerName, instanceId));
        stores.Add(store);
        return store;
    }

    /// <summary>
    /// The processor a start runs declarations through, given its declarations in code, with relative
    /// scheduling on: the setting that keeps the stored schedule across a start.
    /// </summary>
    private sealed class DeclaringProcessor : XmlSchedulingDataProcessor
    {
        private readonly IJobDetail job;
        private readonly ITrigger trigger;

        public DeclaringProcessor(IJobDetail job, ITrigger trigger)
            : base(NullLogger<XmlSchedulingDataProcessor>.Instance, TestJobStores.TypeLoader(), TimeProvider.System)
        {
            this.job = job;
            this.trigger = trigger;
            ScheduleTriggerRelativeToReplacedTrigger = true;
        }

        protected override List<IJobDetail> LoadedJobs => [job];

        protected override List<ITrigger> LoadedTriggers => [trigger];
    }

    /// <summary>
    /// Runs <c>afterRead</c> once, right after the processor's first read of the trigger: another node fires
    /// it between the read and the replace.
    /// </summary>
    private sealed class FiringAfterReadScheduler : DelegatingScheduler
    {
        private Func<Task> afterRead;

        public FiringAfterReadScheduler(IScheduler scheduler, Func<Task> afterRead) : base(scheduler)
        {
            this.afterRead = afterRead;
        }

        public override async ValueTask<ITrigger> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
        {
            ITrigger read = await base.GetTrigger(triggerKey, cancellationToken);
            if (read is not null && Interlocked.Exchange(ref afterRead, null) is { } fire)
            {
                await fire();
            }

            return read;
        }
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
