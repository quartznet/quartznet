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
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A recovered firing of a trigger with no execution group is limited under its trigger's group when the
/// limits stand the trigger group in for one, on each of the three paths that build a recovery trigger:
/// cluster recovery of a dead node, a firing a shutdown hands back, and a node recovering its own firing
/// as it starts again (#4142).
/// </summary>
/// <remarks>
/// <para>
/// A recovery trigger is stored in <see cref="SchedulerConstants.DefaultRecoveryGroup" />, so
/// <see cref="ExecutionLimitsBuilder.UseTriggerGroupWhenUnset" /> limited the replay under that group: it
/// ran beside the firings its own group's limit held back, and was missing from that group's in-flight
/// count until it ended. #4084 kept a grouped firing's execution group; this is the ungrouped half.
/// </para>
/// <para>
/// SQLite refuses to be clustered, so the dead node is a second container on the same file that fires a
/// trigger and never completes it, as in <see cref="RecoveredExecutionGroupSqliteTest" />.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class RecoveredLimitGroupSqliteTest
{
    private const string SchedulerName = "recovered-limit-group";
    private const string Tenant = "tenant-a";

    private static readonly JobKey jobKey = new("render", "recovery");
    private static readonly TriggerKey triggerKey = new("t-render", Tenant);

    private SqliteTestDatabase database = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("recovered-limit-group");
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database.Dispose();
    }

    [Test]
    public async Task ClusterRecoveryLimitsTheReplayUnderItsTriggerGroup()
    {
        await using ServiceProvider dead = BuildNode("node-a");
        await LeaveAFiringBehind(dead);

        await using ServiceProvider survivor = BuildNode("node-b");
        await survivor.GetRequiredService<ISchedulerFactory>().GetScheduler();
        IJobStore store = survivor.GetRequiredService<IJobStore>();
        await RecoverTheDeadNode(store);

        await TheReplayWaitsForASlotOfItsTriggerGroup(store);
    }

    [Test]
    public async Task AFiringHandedBackAtShutdownIsLimitedUnderItsTriggerGroup()
    {
        await using ServiceProvider node = BuildNode("one", handBack: true);
        IScheduler scheduler = await node.GetRequiredService<ISchedulerFactory>().GetScheduler();
        IJobStore store = node.GetRequiredService<IJobStore>();
        await scheduler.ScheduleJob(Job(), Hourly(triggerKey));
        await HandBack(store, await Fire(store, triggerKey));

        await TheReplayWaitsForASlotOfItsTriggerGroup(store);
    }

    [Test]
    public async Task StartupRecoveryLimitsTheReplayUnderItsTriggerGroup()
    {
        await using (ServiceProvider killed = BuildNode("one"))
        {
            await LeaveAFiringBehind(killed);
        }

        await using ServiceProvider restarted = BuildNode("one");
        IScheduler scheduler = await restarted.GetRequiredService<ISchedulerFactory>().GetScheduler();
        IJobStore store = restarted.GetRequiredService<IJobStore>();

        // What Start() asks of the store first, without the scheduler thread that would then fire the
        // recovery trigger before the test could ask about it. It starts the store's misfire handler too,
        // which only a shutdown stops.
        await store.SchedulerStarted();
        try
        {
            await TheReplayWaitsForASlotOfItsTriggerGroup(store);
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// A node killed again while it runs the replay its startup made replays that replay on its next start,
    /// and the new recovery trigger still names the first firing, as cluster recovery's does: named after the
    /// replay instead, it lost the original group, and with it the limit the replay counts against.
    /// </summary>
    [Test]
    public async Task StartupRecoveryOfAReplayKeepsTheFiringItReplays()
    {
        await using (ServiceProvider killed = BuildNode("one"))
        {
            await LeaveAFiringBehind(killed);
        }

        TriggerFiredBundle firstReplay;
        await using (ServiceProvider killedAgain = BuildNode("one"))
        {
            IScheduler scheduler = await killedAgain.GetRequiredService<ISchedulerFactory>().GetScheduler();
            IJobStore store = killedAgain.GetRequiredService<IJobStore>();
            await store.SchedulerStarted();
            try
            {
                firstReplay = await FireWhatIsDue(store);
            }
            finally
            {
                await scheduler.Shutdown(waitForJobsToComplete: false);
            }
        }

        firstReplay.Trigger.Key.Group.Should().Be(SchedulerConstants.DefaultRecoveryGroup, "the premise: the first start replays the firing");

        await using ServiceProvider restarted = BuildNode("one");
        IScheduler third = await restarted.GetRequiredService<ISchedulerFactory>().GetScheduler();
        IJobStore thirdStore = restarted.GetRequiredService<IJobStore>();
        await thirdStore.SchedulerStarted();
        try
        {
            List<TriggerKey> recoveries = await third.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(SchedulerConstants.DefaultRecoveryGroup));
            TriggerKey secondReplay = recoveries.Should().ContainSingle("the replay that never completed is replayed, and the spent one removed").Which;
            secondReplay.Should().NotBe(firstReplay.Trigger.Key);

            JobDataMap markers = (await third.GetTrigger(secondReplay))!.JobDataMap;
            markers.GetString(SchedulerConstants.FailedJobOriginalTriggerName).Should().Be(triggerKey.Name,
                "the second replay stands for the same firing as the first, not for the first replay");
            markers.GetString(SchedulerConstants.FailedJobOriginalTriggerGroup).Should().Be(Tenant,
                "the original group is what an ungrouped replay's limit is derived from");
            markers.GetString(SchedulerConstants.FailedJobOriginalTriggerFireTime).Should().Be(
                firstReplay.Trigger.JobDataMap.GetString(SchedulerConstants.FailedJobOriginalTriggerFireTime),
                "and the time the original firing fired, which the first replay carried");

            await TheReplayWaitsForASlotOfItsTriggerGroup(thirdStore);
        }
        finally
        {
            await third.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// The in-flight half: while the replay runs it holds its trigger group's slot across the cluster, so a
    /// due trigger of the same group waits. The cluster count reads fired rows, and a replay's row is in the
    /// recovery group.
    /// </summary>
    [Test]
    public async Task ARecoveredFiringInFlightHoldsItsTriggerGroupsClusterSlot()
    {
        await using ServiceProvider node = BuildNode("one", handBack: true);
        IScheduler scheduler = await node.GetRequiredService<ISchedulerFactory>().GetScheduler();
        IJobStore store = node.GetRequiredService<IJobStore>();
        await scheduler.ScheduleJob(Job(), Hourly(triggerKey));
        await HandBack(store, await Fire(store, triggerKey));

        List<IOperableTrigger> replay = await store.AcquireNextTriggers(RequestWithTenantSlots(1, ExecutionLimitScope.Cluster));
        replay.Should().ContainSingle("the premise: the tenant's one slot is free, so the replay takes it")
            .Which.Key.Group.Should().Be(SchedulerConstants.DefaultRecoveryGroup);
        (await store.TriggersFired(replay)).Should().ContainSingle().Which.TriggerFiredBundle.Should().NotBeNull();

        await scheduler.ScheduleJob(Hourly(new TriggerKey("t-render-2", Tenant)));

        TriggerAcquisitionResult round = await store.AcquireNextTriggersAndFireDue(RequestWithTenantSlots(1, ExecutionLimitScope.Cluster));

        round.Due.Should().BeEmpty();
        round.Pending.Should().BeEmpty(
            "the replay running is a firing of the tenant's trigger, so it holds the tenant's one slot across the cluster");
        round.GroupsAtLimit.Should().BeEquivalentTo([Tenant]);
    }

    /// <summary>
    /// The control: with the limits not deriving a group from the trigger group, a replay of an ungrouped
    /// firing is in the ungrouped bucket, as it always was, and a tenant limit does not touch it.
    /// </summary>
    [Test]
    public async Task WithoutTheDerivationTheReplayIsUngrouped()
    {
        await using ServiceProvider node = BuildNode("one", handBack: true);
        IScheduler scheduler = await node.GetRequiredService<ISchedulerFactory>().GetScheduler();
        IJobStore store = node.GetRequiredService<IJobStore>();
        await scheduler.ScheduleJob(Job(), Hourly(triggerKey));
        await HandBack(store, await Fire(store, triggerKey));

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = TimeProvider.System.GetUtcNow().AddMinutes(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
            ExecutionLimits = ExecutionLimitsBuilder.Create().ForGroup(Tenant, 0).Build()
        });

        acquired.Should().ContainSingle("the tenant's limit names a group no trigger carries, and nothing derives one")
            .Which.Key.Group.Should().Be(SchedulerConstants.DefaultRecoveryGroup);
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Helpers
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// With no slot of the tenant's group the replay is held back, and the round names the tenant as the
    /// group it waits for; with one it is acquired.
    /// </summary>
    private static async Task TheReplayWaitsForASlotOfItsTriggerGroup(IJobStore store)
    {
        // A node-scoped limit reaches the store already lowered by what the node runs, so a tenant limit of
        // one with a live tenant firing holding the slot arrives as 0.
        TriggerAcquisitionResult heldBack = await store.AcquireNextTriggersAndFireDue(RequestWithTenantSlots(0));
        heldBack.Due.Should().BeEmpty();
        heldBack.Pending.Should().BeEmpty("the replay is a firing of the tenant's trigger, and the tenant has no slot left");
        heldBack.GroupsAtLimit.Should().BeEquivalentTo([Tenant],
            "named by the key the scheduler counts the tenant's firings under, so that one of them ending wakes it for the replay");

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(RequestWithTenantSlots(1));
        acquired.Should().ContainSingle("a free tenant slot lets the replay run")
            .Which.Key.Group.Should().Be(SchedulerConstants.DefaultRecoveryGroup);
    }

    /// <summary>
    /// What a node killed mid-execution leaves in the database: the job and its trigger, and one
    /// fired-trigger row saying the trigger is executing on that node and asks to be recovered.
    /// </summary>
    private static async Task LeaveAFiringBehind(ServiceProvider node)
    {
        IScheduler scheduler = await node.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.ScheduleJob(Job(), Hourly(triggerKey));
        await Fire(node.GetRequiredService<IJobStore>(), triggerKey);

        // No completion: this is where the node dies.
    }

    /// <summary>
    /// The survivor's first check-in, as a clustered node. The dead node never checked in, so its row
    /// has no scheduler state beside it and the check-in recovers it as orphaned.
    /// </summary>
    private static async Task RecoverTheDeadNode(IJobStore survivor)
    {
        // A SQLite store refuses to be configured clustered, so it is told after it is built.
        AdoJobStoreBase store = (AdoJobStoreBase) survivor;
        store.Clustered = true;

        (await store.CheckIn(Guid.NewGuid())).Should().BeTrue("the premise: the first check-in finds the dead node's row and recovers it");
    }

    /// <summary>
    /// What the run shell reports for a firing the shutdown cancelled, to a store that hands such firings
    /// back.
    /// </summary>
    private static async Task HandBack(IJobStore store, TriggerFiredBundle bundle)
    {
        await store.FiringComplete(new TriggeredJobCompleteContext
        {
            Trigger = bundle.Trigger,
            JobDetail = bundle.JobDetail,
            Instruction = SchedulerInstruction.NoInstruction,
            Outcome = ExecutionOutcome.Cancelled,
            HandBackForRecovery = true
        });
    }

    private static IJobDetail Job()
    {
        return JobBuilder.Create<NoOpJob>().WithIdentity(jobKey).RequestRecovery().Build();
    }

    /// <remarks>
    /// Repeating, so the firing moves the trigger an hour on and the recovery trigger is the only one due.
    /// No execution group: the limits derive the tenant from the trigger group.
    /// </remarks>
    private static ITrigger Hourly(TriggerKey key)
    {
        return TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob(jobKey)
            .StartNow()
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();
    }

    /// <summary>
    /// Acquires and fires the trigger through the store, as the scheduler thread would.
    /// </summary>
    private static async Task<TriggerFiredBundle> Fire(IJobStore store, TriggerKey key)
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = TimeProvider.System.GetUtcNow().AddMinutes(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero
        });
        acquired.Should().ContainSingle().Which.Key.Should().Be(key);

        List<TriggerFiredResult> fired = await store.TriggersFired(acquired);
        return fired.Should().ContainSingle().Which.TriggerFiredBundle!;
    }

    /// <summary>
    /// Acquires and fires the one trigger that is due through the store, whatever its key.
    /// </summary>
    private static async Task<TriggerFiredBundle> FireWhatIsDue(IJobStore store)
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = TimeProvider.System.GetUtcNow().AddMinutes(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero
        });
        acquired.Should().ContainSingle();

        List<TriggerFiredResult> fired = await store.TriggersFired(acquired);
        return fired.Should().ContainSingle().Which.TriggerFiredBundle!;
    }

    private static TriggerAcquisitionRequest RequestWithTenantSlots(int slots, ExecutionLimitScope scope = ExecutionLimitScope.Node)
    {
        return new TriggerAcquisitionRequest
        {
            NoLaterThan = TimeProvider.System.GetUtcNow().AddMinutes(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
            ExecutionLimits = ExecutionLimitsBuilder.Create().UseTriggerGroupWhenUnset().ForGroup(Tenant, slots, scope).Build()
        };
    }

    /// <param name="instanceId">The node's instance id; two ids on one file are two nodes.</param>
    /// <param name="handBack">Whether the store hands back what a shutdown cancels.</param>
    private ServiceProvider BuildNode(string instanceId, bool handBack = false)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = SchedulerName;
                options.InstanceId = instanceId;
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
                store.ConfigureStore(options => options.RecoverFiringsCancelledByShutdown = handBack);
            });
        });

        return services.BuildServiceProvider();
    }

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}
