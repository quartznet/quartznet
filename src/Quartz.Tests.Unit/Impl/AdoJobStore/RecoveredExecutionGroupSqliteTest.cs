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
/// A recovery trigger keeps the execution group of the firing it stands in for, on each of the three
/// paths that build one: cluster recovery of a dead node, a firing a shutdown hands back, and a node
/// recovering its own firing when it starts again (#4081).
/// </summary>
/// <remarks>
/// <para>
/// Each path builds the trigger from the firing's fired-trigger row, which has always carried
/// <c>EXECUTION_GROUP</c>. None of them read it, so a recovered <c>gpu</c> job ran ungrouped, beside the
/// live one holding the only <c>gpu</c> slot. The ungrouped cases are the control: a trigger with no
/// group recovers with none.
/// </para>
/// <para>
/// SQLite refuses to be clustered, so the dead node is a second container on the same file that fires a
/// trigger and never completes it. The survivor is told it is clustered after it is built, as in
/// <see cref="AcquisitionBehindExecutingJobSqliteTest" />, and its first check-in finds the dead node's
/// row orphaned, which is what recovers it.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class RecoveredExecutionGroupSqliteTest
{
    private const string SchedulerName = "recovered-execution-group";
    private const string Gpu = "gpu";

    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(30);
    private static readonly JobKey jobKey = new("render", "recovery");
    private static readonly TriggerKey triggerKey = new("t-render", "recovery");

    private SqliteTestDatabase database = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("recovered-execution-group");
        Recordings.Reset();
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database.Dispose();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ClusterRecoveryOfADeadNodeKeepsTheExecutionGroup(bool grouped)
    {
        string? executionGroup = grouped ? Gpu : null;
        await using ServiceProvider dead = BuildNode("node-a");
        await LeaveAFiringBehind(dead, executionGroup);

        await using ServiceProvider survivor = BuildNode("node-b");
        IScheduler scheduler = await survivor.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await RecoverTheDeadNode(survivor.GetRequiredService<IJobStore>());

        ITrigger recovery = await TheRecoveryTrigger(scheduler);
        recovery.ExecutionGroup.Should().Be(executionGroup,
            "the recovery trigger replays the dead node's firing, and is limited as that firing was");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task AFiringHandedBackAtShutdownKeepsTheExecutionGroup(bool grouped)
    {
        string? executionGroup = grouped ? Gpu : null;
        await using ServiceProvider node = BuildNode("one", handBack: true);
        IScheduler scheduler = await node.GetRequiredService<ISchedulerFactory>().GetScheduler();
        IJobStore store = node.GetRequiredService<IJobStore>();
        await scheduler.ScheduleJob(Job(), Hourly(executionGroup));
        TriggerFiredBundle bundle = await Fire(store);

        // What the run shell reports for a firing the shutdown cancelled, to a store that hands such
        // firings back.
        await store.FiringComplete(new TriggeredJobCompleteContext
        {
            Trigger = bundle.Trigger,
            JobDetail = bundle.JobDetail,
            Instruction = SchedulerInstruction.NoInstruction,
            Outcome = ExecutionOutcome.Cancelled,
            HandBackForRecovery = true
        });

        ITrigger recovery = await TheRecoveryTrigger(scheduler);
        recovery.ExecutionGroup.Should().Be(executionGroup,
            "the recovery trigger replays the firing the shutdown cancelled, and is limited as that firing was");
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task StartupRecoveryOfTheNodesOwnFiringKeepsTheExecutionGroup(bool grouped)
    {
        string? executionGroup = grouped ? Gpu : null;
        await using (ServiceProvider killed = BuildNode("one"))
        {
            await LeaveAFiringBehind(killed, executionGroup);
        }

        await using ServiceProvider restarted = BuildNode("one");
        IScheduler scheduler = await restarted.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();

        RecoveredRun recovered = await Recordings.Recovered.WaitAsync(waitLimit);
        await scheduler.Shutdown(waitForJobsToComplete: true);

        recovered.TriggerGroup.Should().Be(SchedulerConstants.DefaultRecoveryGroup, "the premise: this is the recovery trigger's run");
        recovered.ExecutionGroup.Should().Be(executionGroup,
            "the run's trigger is the stored recovery trigger, read back at acquisition, and it keeps the interrupted firing's group");
    }

    /// <summary>
    /// What keeping the group is for: a recovered <c>gpu</c> firing waits for a <c>gpu</c> slot rather than
    /// running beside the live job that holds it, and once it runs it is counted under <c>gpu</c>.
    /// </summary>
    [Test]
    public async Task ARecoveredFiringWaitsForASlotInItsExecutionGroup()
    {
        await using ServiceProvider dead = BuildNode("node-a");
        await LeaveAFiringBehind(dead, Gpu);

        await using ServiceProvider survivor = BuildNode("node-b");
        await survivor.GetRequiredService<ISchedulerFactory>().GetScheduler();
        IJobStore store = survivor.GetRequiredService<IJobStore>();
        await RecoverTheDeadNode(store);

        // A node-scoped limit reaches the store already lowered by what the node runs, so gpu = 1 with a
        // live gpu job holding the slot arrives as 0.
        List<IOperableTrigger> heldBack = await store.AcquireNextTriggers(RequestWithGpuSlots(0));
        heldBack.Should().BeEmpty("the recovered firing is a gpu firing, and gpu has no slot left");

        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(RequestWithGpuSlots(1));
        acquired.Should().ContainSingle("a free gpu slot lets the recovered firing run")
            .Which.Key.Group.Should().Be(SchedulerConstants.DefaultRecoveryGroup);
        await store.TriggersFired(acquired);

        (await ReadColumn($"SELECT EXECUTION_GROUP FROM QRTZ_FIRED_TRIGGERS WHERE TRIGGER_GROUP = '{SchedulerConstants.DefaultRecoveryGroup}'"))
            .Should().Equal([Gpu], "the firing is counted under gpu, which is the row a cluster-scoped limit counts");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Helpers
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// What a node killed mid-execution leaves in the database: the job and its trigger, and one
    /// fired-trigger row saying the trigger is executing on that node and asks to be recovered.
    /// </summary>
    private async Task LeaveAFiringBehind(ServiceProvider node, string? executionGroup)
    {
        IScheduler scheduler = await node.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.ScheduleJob(Job(), Hourly(executionGroup));
        await Fire(node.GetRequiredService<IJobStore>());

        // No completion: this is where the node dies.
        (await ReadColumn("SELECT EXECUTION_GROUP FROM QRTZ_FIRED_TRIGGERS WHERE STATE = 'EXECUTING' AND REQUESTS_RECOVERY = 1"))
            .Should().Equal([executionGroup], "the premise: the row left behind asks to be recovered and carries the trigger's group");
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

    private static IJobDetail Job()
    {
        return JobBuilder.Create<RecordingJob>().WithIdentity(jobKey).RequestRecovery().Build();
    }

    /// <remarks>
    /// Repeating, so the firing moves the trigger an hour on and the recovery trigger is the only one due.
    /// </remarks>
    private static ITrigger Hourly(string? executionGroup)
    {
        return TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey)
            .StartNow()
            .WithExecutionGroup(executionGroup)
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();
    }

    /// <summary>
    /// Acquires and fires the trigger through the store, as the scheduler thread would.
    /// </summary>
    private static async Task<TriggerFiredBundle> Fire(IJobStore store)
    {
        List<IOperableTrigger> acquired = await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = TimeProvider.System.GetUtcNow().AddMinutes(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero
        });
        acquired.Should().ContainSingle().Which.Key.Should().Be(triggerKey);

        List<TriggerFiredResult> fired = await store.TriggersFired(acquired);
        return fired.Should().ContainSingle().Which.TriggerFiredBundle!;
    }

    private static TriggerAcquisitionRequest RequestWithGpuSlots(int slots)
    {
        return new TriggerAcquisitionRequest
        {
            NoLaterThan = TimeProvider.System.GetUtcNow().AddMinutes(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero,
            ExecutionLimits = ExecutionLimitsBuilder.Create().ForGroup(Gpu, slots).Build()
        };
    }

    private static async Task<ITrigger> TheRecoveryTrigger(IScheduler scheduler)
    {
        List<TriggerKey> keys = await scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(SchedulerConstants.DefaultRecoveryGroup));
        TriggerKey key = keys.Should().ContainSingle("one firing was recovered").Subject;

        ITrigger? trigger = await scheduler.GetTrigger(key);
        trigger.Should().NotBeNull();
        return trigger!;
    }

    private async Task<List<string?>> ReadColumn(string sql)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        List<string?> values = [];
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.IsDBNull(0) ? null : reader.GetString(0));
        }

        return values;
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

    private sealed record RecoveredRun(string TriggerGroup, string? ExecutionGroup);

    /// <summary>The recovery run, once it happens.</summary>
    private static class Recordings
    {
        private static TaskCompletionSource<RecoveredRun> recovered = NewSource();

        public static Task<RecoveredRun> Recovered => recovered.Task;

        public static void Reset()
        {
            recovered = NewSource();
        }

        public static void Record(IJobExecutionContext context)
        {
            if (context.Recovering)
            {
                recovered.TrySetResult(new RecoveredRun(context.Trigger.Key.Group, context.Trigger.ExecutionGroup));
            }
        }

        private static TaskCompletionSource<RecoveredRun> NewSource()
        {
            return new TaskCompletionSource<RecoveredRun>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public sealed class RecordingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Recordings.Record(context);
            return default;
        }
    }
}
