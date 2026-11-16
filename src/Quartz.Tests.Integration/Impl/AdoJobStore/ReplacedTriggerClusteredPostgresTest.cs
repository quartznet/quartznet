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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FakeItEasy;

using Npgsql;

using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;
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
/// driven call by call, and the node that replaces or re-applies is a clustered scheduler that is never
/// started. The firing between the read and the replace is made by the scheduler the processor is
/// handed, right after the processor's read. The same scenarios on one SQLite file are
/// <see cref="ReplacedTriggerAcquisitionSqliteTest" /> and <c>DeclaredTriggerReappliedUnderAFiringTest</c>.
/// </para>
/// </remarks>
[Category("db-postgres")]
[NonParallelizable]
public sealed class ReplacedTriggerClusteredPostgresTest : ClusteredPostgresTestBase
{
    private const string Group = "replaced";

    private static readonly JobKey JobKey = new JobKey("job", Group);
    private static readonly TriggerKey TriggerKey = new TriggerKey("trigger", Group);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan TwiceTheInterval = TimeSpan.FromHours(2);

    private readonly List<JobStoreTX> stores = new List<JobStoreTX>();
    private readonly List<IScheduler> schedulers = new List<IScheduler>();

    private JobStoreTX nodeA;
    private JobStoreTX nodeB;
    private IScheduler admin;
    private DateTimeOffset due;

    protected override string SchedulerName => "ReplacedTriggerCluster";

    [SetUp]
    public async Task CreateNodes()
    {
        nodeA = await CreateStore("node-a");
        nodeB = await CreateStore("node-b");

        // The node that replaces or re-applies: a scheduler of the same cluster that is never started, as
        // the issue's node-b is when it replaces, and as every node is while its start applies declarations.
        admin = await CreateScheduler("node-admin");
        schedulers.Add(admin);

        due = DateTimeOffset.UtcNow.AddSeconds(30);
        due = due.AddTicks(-(due.Ticks % TimeSpan.TicksPerSecond));

        await admin.AddJob(JobBuilder.Create<NoOpJob>().WithIdentity(JobKey).StoreDurably().Build(), replace: false);
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

        foreach (JobStoreTX store in stores)
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
            .WithSimpleSchedule(x => x.WithInterval(TwiceTheInterval).RepeatForever()).Build())).Should().Be(due);
        IOperableTrigger heldByB = await ReserveOne(nodeB);
        heldByB.GetNextFireTimeUtc().Should().Be(heldByA.GetNextFireTimeUtc(), "the premise: the replacement is due when A's copy is");

        (await nodeA.TriggersFired(new[] { heldByA })).Should().ContainSingle().Which.TriggerFiredBundle.Should().BeNull(
            "A holds a copy of the trigger that was replaced, and the trigger stored is not that one");
        (await IntervalStored()).Should().Be(TwiceTheInterval, "A's copy is not written over the replacement");

        await nodeA.ReleaseAcquiredTrigger(heldByA);
        (await StateStored()).Should().Be("ACQUIRED", "the row is B's acquisition, and A's release is not B's to undo");
        (await FiredRows()).Should().Equal(new[] { "node-b ACQUIRED" }, "A's reservation went with the trigger it reserved");

        TriggerFiredBundle firedOnB = (await nodeB.TriggersFired(new[] { heldByB })).Should().ContainSingle().Subject.TriggerFiredBundle;
        firedOnB.Should().NotBeNull("B's acquisition is still its own");
        ((ISimpleTrigger) firedOnB.Trigger).RepeatInterval.Should().Be(TwiceTheInterval);
        (await IntervalStored()).Should().Be(TwiceTheInterval);
        (await ReadRow()).NextFireTimeUtc.Should().Be(due.Add(TwiceTheInterval));
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

        await Reapply(FiringAfterRead(admin, () => FireAndComplete(nodeA)));

        (await ReadRow()).Should().Be(("WAITING", due.Add(TwiceTheInterval), due.Add(Interval)),
            "the start read the trigger before node A fired it again, and the replace works from the row as it is when it is made");
        (await CountRows("SELECT COUNT(*) FROM qrtz_simple_triggers WHERE sched_name = @sched AND times_triggered = 2"))
            .Should().Be(1, "both firings are counted");
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
        (await FiredRows()).Should().Equal(new[] { "node-a ACQUIRED" });
        (await nodeA.TriggersFired(new[] { heldByA })).Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull(
            "re-applying the same declaration is not a new trigger, and A's acquisition of it still holds");
    }

    /// <summary>
    /// What a start does with the declared trigger: the processor <c>AddQuartz</c> declarations go through,
    /// with relative scheduling on, over <paramref name="scheduler" />.
    /// </summary>
    private static async Task Reapply(IScheduler scheduler)
    {
        DeclaringProcessor processor = new DeclaringProcessor(
            JobBuilder.Create<NoOpJob>().WithIdentity(JobKey).StoreDurably().Build(),
            TriggerBuilder.Create().WithIdentity(TriggerKey).ForJob(JobKey).StartNow()
                .WithSimpleSchedule(x => x.WithInterval(Interval).RepeatForever()).Build());

        await processor.ScheduleJobs(scheduler);
    }

    /// <summary>
    /// <paramref name="scheduler" />, which runs <paramref name="afterRead" /> once, right after the
    /// processor's first read of the trigger: another node fires it between the read and the replace.
    /// </summary>
    private static IScheduler FiringAfterRead(IScheduler scheduler, Func<Task> afterRead)
    {
        Func<Task> pending = afterRead;
        IScheduler firing = A.Fake<IScheduler>(x => x.Wrapping(scheduler));
        A.CallTo(() => firing.GetTrigger(A<TriggerKey>._, A<CancellationToken>._))
            .ReturnsLazily(async (TriggerKey key, CancellationToken cancellationToken) =>
            {
                ITrigger read = await scheduler.GetTrigger(key, cancellationToken);
                if (read != null && Interlocked.Exchange(ref pending, null) is { } fire)
                {
                    await fire();
                }

                return read;
            });

        return firing;
    }

    private static async Task<IOperableTrigger> ReserveOne(JobStoreTX store)
    {
        IReadOnlyCollection<IOperableTrigger> acquired = await store.AcquireNextTriggers(DateTimeOffset.UtcNow.AddDays(1), 1, TimeSpan.Zero);
        return acquired.Should().ContainSingle("the trigger is the only one stored, and it is waiting").Subject;
    }

    private static async Task FireAndComplete(JobStoreTX store)
    {
        IOperableTrigger acquired = await ReserveOne(store);
        TriggerFiredBundle bundle = (await store.TriggersFired(new[] { acquired })).Should().ContainSingle().Subject.TriggerFiredBundle;
        bundle.Should().NotBeNull("the premise: the node's acquisition is its own to fire");
        await store.TriggeredJobComplete(bundle.Trigger, bundle.JobDetail, SchedulerInstruction.NoInstruction);
    }

    private async Task<(string State, DateTimeOffset NextFireTimeUtc, DateTimeOffset PreviousFireTimeUtc)> ReadRow()
    {
        string state = await StateStored();
        object next = await ExecuteScalar("SELECT next_fire_time FROM qrtz_triggers WHERE sched_name = @sched AND trigger_name = @name");
        object previous = await ExecuteScalar("SELECT prev_fire_time FROM qrtz_triggers WHERE sched_name = @sched AND trigger_name = @name");

        return (state, Instant(next), Instant(previous));

        static DateTimeOffset Instant(object ticks) => new DateTimeOffset(Convert.ToInt64(ticks, CultureInfo.InvariantCulture), TimeSpan.Zero);
    }

    private async Task<string> StateStored()
    {
        return (string) await ExecuteScalar("SELECT trigger_state FROM qrtz_triggers WHERE sched_name = @sched AND trigger_name = @name");
    }

    private async Task<TimeSpan> IntervalStored()
    {
        object interval = await ExecuteScalar("SELECT repeat_interval FROM qrtz_simple_triggers WHERE sched_name = @sched AND trigger_name = @name");
        return TimeSpan.FromMilliseconds(Convert.ToInt64(interval, CultureInfo.InvariantCulture));
    }

    private async Task<List<string>> FiredRows()
    {
        List<string> rows = new List<string>();
        foreach (string instance in new[] { "node-a", "node-b", "node-admin" })
        {
            foreach (string state in new[] { "ACQUIRED", "EXECUTING" })
            {
                int count = await CountRows(
                    "SELECT COUNT(*) FROM qrtz_fired_triggers WHERE sched_name = @sched AND instance_name = @instance AND state = @state",
                    ("instance", instance),
                    ("state", state));
                rows.AddRange(Enumerable.Repeat($"{instance} {state}", count));
            }
        }

        return rows;
    }

    private async Task<int> CountRows(string sql, params (string Name, object Value)[] parameters)
    {
        return Convert.ToInt32(await ExecuteScalar(sql, parameters), CultureInfo.InvariantCulture);
    }

    private async Task<object> ExecuteScalar(string sql, params (string Name, object Value)[] parameters)
    {
        using NpgsqlConnection connection = new NpgsqlConnection(TestConstants.PostgresConnectionString);
        await connection.OpenAsync();
        using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("sched", SchedulerName);
        if (sql.Contains("@name"))
        {
            command.Parameters.AddWithValue("name", TriggerKey.Name);
        }

        foreach ((string name, object value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteScalarAsync();
    }

    /// <summary>
    /// One node's clustered store on the shared database, driven by the test call by call.
    /// </summary>
    private async Task<JobStoreTX> CreateStore(string instanceId)
    {
        // A data source per node, as two processes would have: a store's shutdown shuts its own down.
        string dataSource = $"replaced-trigger-cluster-{instanceId}-{Guid.NewGuid():N}";
        DBConnectionManager.Instance.AddConnectionProvider(dataSource, new DbProvider(TestConstants.PostgresProvider, TestConstants.PostgresConnectionString));

        SystemTextJsonObjectSerializer serializer = new SystemTextJsonObjectSerializer();
        serializer.Initialize();

        JobStoreTX store = new JobStoreTX
        {
            DataSource = dataSource,
            TablePrefix = "QRTZ_",
            InstanceName = SchedulerName,
            InstanceId = instanceId,
            Clustered = true,
            DriverDelegateType = typeof(PostgreSQLDelegate).AssemblyQualifiedName,
            ObjectSerializer = serializer,
        };

        // Initialized and never started: no scheduler thread, misfire pass or check-in moves the rows read here.
        await store.Initialize(new SimpleTypeLoadHelper(), A.Fake<ISchedulerSignaler>());
        stores.Add(store);
        return store;
    }

    /// <summary>
    /// The processor a start runs declarations through, given its declarations in code, with existing data
    /// overwritten and relative scheduling on: the setting that keeps the stored schedule across a start.
    /// </summary>
    private sealed class DeclaringProcessor : XMLSchedulingDataProcessor
    {
        private readonly List<IJobDetail> jobs;
        private readonly List<ITrigger> triggers;

        public DeclaringProcessor(IJobDetail job, ITrigger trigger) : base(new SimpleTypeLoadHelper())
        {
            jobs = [job];
            triggers = [trigger];
            OverWriteExistingData = true;
            ScheduleTriggerRelativeToReplacedTrigger = true;
        }

        protected override IReadOnlyList<IJobDetail> LoadedJobs => jobs;

        protected override IReadOnlyList<ITrigger> LoadedTriggers => triggers;
    }

    public sealed class NoOpJob : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }
}
