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

using System.Collections.Concurrent;

using AwesomeAssertions.Execution;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// A recovered firing reports the scheduled fire time of the firing it runs again, to the tick, on each of
/// the three paths that build a recovery trigger: cluster recovery of a dead node, a firing a shutdown hands
/// back, and a node recovering its own firing when it starts again. However late it comes round, it is never
/// a misfire.
/// </summary>
/// <remarks>
/// <para>
/// Startup recovery always started its trigger at the fired-trigger row's scheduled time and ignored the
/// misfire policy. Cluster recovery and the hand-back, which share one builder, started theirs at the time
/// the firing actually ran and fired it now on a misfire. Their
/// <see cref="TriggerFiredBundle.ScheduledFireTimeUtc" /> was the original's fire time rather than what
/// <see cref="IJobExecutionContext.RecoveringTriggerKey" /> says it is, and a long job's recovery waited for the
/// misfire handler and was announced as a misfire of a firing nobody missed.
/// </para>
/// <para>
/// The firing left behind runs ten seconds late, so its fire time and its scheduled time differ, and is due
/// a quarter second past the second, which the string markers on the recovery trigger's data cannot say.
/// The harness is <see cref="RecoveredExecutionGroupSqliteTest" />'s, on a <see cref="FakeTimeProvider" />.
/// </para>
/// </remarks>
[NonParallelizable]
public sealed class RecoveryScheduledFireTimeSqliteTest
{
    public enum RecoveryPath
    {
        ClusterRecovery,
        ShutdownHandBack,
        StartupRecovery
    }

    private const string SchedulerName = "recovery-scheduled-fire-time";

    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan late = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan misfireThreshold = TimeSpan.FromMinutes(1);
    private static readonly JobKey jobKey = new("render", "recovery");
    private static readonly TriggerKey triggerKey = new("t-render", "recovery");
    private static readonly DateTimeOffset due = new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero).AddMilliseconds(250);

    private readonly List<ServiceProvider> nodes = [];
    private SqliteTestDatabase database = null!;
    private FakeTimeProvider clock = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("recovery-scheduled-fire-time");
        clock = new FakeTimeProvider(due);
        Recordings.Reset();
    }

    [TearDown]
    public async Task DisposeNodesAndDeleteDatabase()
    {
        foreach (ServiceProvider node in nodes)
        {
            await node.DisposeAsync();
        }

        nodes.Clear();
        database.Dispose();
    }

    [TestCase(RecoveryPath.ClusterRecovery)]
    [TestCase(RecoveryPath.ShutdownHandBack)]
    [TestCase(RecoveryPath.StartupRecovery)]
    public async Task ARecoveredFiringReportsTheOriginalsScheduledFireTime(RecoveryPath path)
    {
        TriggerFiredBundle recovered = await Recover(path);

        recovered.Recovering.Should().BeTrue("the premise: this is the recovery trigger's firing");
        recovered.ScheduledFireTimeUtc.Should().Be(due,
            "a recovery runs the original firing again, so it reports the time that firing was scheduled for, "
            + "not the time it ran");
    }

    /// <summary>
    /// A recovery sixty-five seconds after the original was due, which ran fifty-five seconds before: past the
    /// one-minute threshold counted from the due time, inside it counted from the fire time. A recovery is
    /// never a misfire, so neither count matters: the next acquisition takes it.
    /// </summary>
    [TestCase(RecoveryPath.ClusterRecovery)]
    [TestCase(RecoveryPath.ShutdownHandBack)]
    public async Task ARecoveryJustPastTheThresholdIsAcquiredAtOnceAndIsNoMisfire(RecoveryPath path)
    {
        await TheRecoveryIsAcquiredAtOnceAndIsNoMisfire(path, misfireThreshold - late + TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// A recovery ten minutes after the original fired, as a long job's cluster recovery is: far past the
    /// threshold, and still taken by the next acquisition, with no misfire raised for a firing nobody missed.
    /// </summary>
    [TestCase(RecoveryPath.ClusterRecovery)]
    [TestCase(RecoveryPath.ShutdownHandBack)]
    public async Task ARecoveryLongAfterTheOriginalFiredIsAcquiredAtOnceAndIsNoMisfire(RecoveryPath path)
    {
        await TheRecoveryIsAcquiredAtOnceAndIsNoMisfire(path, TimeSpan.FromMinutes(10));
    }

    /// <summary>
    /// Stores <paramref name="path" />'s recovery trigger, moves the clock on, and runs what a scheduler runs: a
    /// misfire pass, then an acquisition. The recovery is acquired by it, reports the original's due time, and
    /// neither the pass nor the listeners nor the history saw a misfire.
    /// </summary>
    private async Task TheRecoveryIsAcquiredAtOnceAndIsNoMisfire(RecoveryPath path, TimeSpan afterTheOriginalFired)
    {
        (IJobStore store, IScheduler scheduler, ServiceProvider node) = await StoreTheRecoveryTrigger(path);
        MisfireRecorder misfires = new();
        scheduler.ListenerManager.AddTriggerListener(misfires);
        clock.Advance(afterTheOriginalFired);

        RecoverMisfiredJobsResult pass = await ((AdoJobStoreBase) store).RecoverMisfires(Guid.NewGuid());
        List<IOperableTrigger> acquired = await Acquire(store);
        PagedResult<MisfireHistoryEntry> misfireRows = await node.GetRequiredService<IExecutionHistoryStore>()
            .QueryMisfires(new MisfireHistoryQuery { SchedulerName = SchedulerName });

        using (new AssertionScope())
        {
            pass.ProcessedMisfiredTriggerCount.Should().Be(0, "a recovery trigger ignores the misfire policy, so the pass passes it over");
            misfires.Misfired.Should().BeEmpty("no TriggerMisfired is raised for a firing nobody missed");
            misfireRows.Items.Should().BeEmpty("the history records no misfire for it either");
            acquired.Should().ContainSingle("the next acquisition takes the recovery, however late it is")
                .Which.Key.Group.Should().Be(SchedulerConstants.DefaultRecoveryGroup);
        }

        TriggerFiredBundle recovered = (await store.TriggersFired(acquired)).Should().ContainSingle().Which.TriggerFiredBundle!;
        recovered.FireTimeUtc.Should().Be(due + late + afterTheOriginalFired, "it runs now");
        recovered.ScheduledFireTimeUtc.Should().Be(due, "it reports the time the original firing was due, to the tick");
    }

    //////////////////////////////////////////////////////////////////////////////////////////////
    // Helpers
    //////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>Leaves a firing behind, recovers it along <paramref name="path" />, and fires the recovery.</summary>
    private async Task<TriggerFiredBundle> Recover(RecoveryPath path)
    {
        if (path == RecoveryPath.StartupRecovery)
        {
            await using (ServiceProvider killed = BuildNode("one"))
            {
                await LeaveAFiringBehind(killed);
            }

            // A real start: the store recovers the node's own firing, and the scheduler runs the recovery.
            await using ServiceProvider restarted = BuildNode("one");
            IScheduler scheduler = await restarted.GetRequiredService<ISchedulerFactory>().GetScheduler();
            await scheduler.Start();

            TriggerFiredBundle recovered = await Recordings.Recovered.WaitAsync(waitLimit);
            await scheduler.Shutdown(waitForJobsToComplete: true);
            return recovered;
        }

        (IJobStore store, _, _) = await StoreTheRecoveryTrigger(path);
        return await Fire(store);
    }

    /// <summary>
    /// Leaves a firing behind and has <paramref name="path" />'s builder store the recovery trigger for it,
    /// without firing it. Answers the store holding the trigger, and its scheduler and container.
    /// </summary>
    private async Task<(IJobStore Store, IScheduler Scheduler, ServiceProvider Node)> StoreTheRecoveryTrigger(RecoveryPath path)
    {
        if (path == RecoveryPath.ShutdownHandBack)
        {
            ServiceProvider node = Own(BuildNode("one", handBack: true));
            TriggerFiredBundle bundle = await LeaveAFiringBehind(node);
            IJobStore store = node.GetRequiredService<IJobStore>();

            // What the run shell reports for a firing the shutdown cancelled, to a store that hands such firings back.
            await store.FiringComplete(new TriggeredJobCompleteContext
            {
                Trigger = bundle.Trigger,
                JobDetail = bundle.JobDetail,
                Instruction = SchedulerInstruction.NoInstruction,
                Outcome = ExecutionOutcome.Cancelled,
                HandBackForRecovery = true
            });

            return (store, await node.GetRequiredService<ISchedulerFactory>().GetScheduler(), node);
        }

        ServiceProvider dead = Own(BuildNode("node-a"));
        await LeaveAFiringBehind(dead);

        ServiceProvider survivor = Own(BuildNode("node-b"));
        IScheduler scheduler = await survivor.GetRequiredService<ISchedulerFactory>().GetScheduler();

        // A SQLite store refuses to be configured clustered, so it is told after it is built. Its first check-in
        // finds the dead node's row with no scheduler state beside it, and recovers it.
        AdoJobStoreBase survivorStore = (AdoJobStoreBase) survivor.GetRequiredService<IJobStore>();
        survivorStore.Clustered = true;
        (await survivorStore.CheckIn(Guid.NewGuid())).Should().BeTrue("the premise: the check-in recovers the dead node's firing");
        return (survivorStore, scheduler, survivor);
    }

    /// <summary>
    /// What a node killed mid-execution leaves in the database: an hourly trigger's firing, due at
    /// <see cref="due" /> and fired ten seconds later, that asks to be recovered and is never completed.
    /// </summary>
    private async Task<TriggerFiredBundle> LeaveAFiringBehind(ServiceProvider node)
    {
        IScheduler scheduler = await node.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.ScheduleJob(
            JobBuilder.Create<RecordingJob>().WithIdentity(jobKey).RequestRecovery().Build(),
            TriggerBuilder.Create(clock)
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .StartAt(due)
                .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                .Build());

        clock.Advance(late);
        TriggerFiredBundle bundle = await Fire(node.GetRequiredService<IJobStore>());
        bundle.ScheduledFireTimeUtc.Should().Be(due, "the premise: the original firing was due at the quarter second");
        bundle.FireTimeUtc.Should().Be(due + late, "and ran ten seconds later");
        return bundle;
    }

    private async Task<List<IOperableTrigger>> Acquire(IJobStore store)
    {
        return await store.AcquireNextTriggers(new TriggerAcquisitionRequest
        {
            NoLaterThan = clock.GetUtcNow().AddMinutes(1),
            MaxCount = 1,
            TimeWindow = TimeSpan.Zero
        });
    }

    /// <summary>Acquires and fires the one trigger due, as the scheduler thread would.</summary>
    private async Task<TriggerFiredBundle> Fire(IJobStore store)
    {
        List<IOperableTrigger> acquired = await Acquire(store);
        acquired.Should().ContainSingle("one trigger is due");

        List<TriggerFiredResult> fired = await store.TriggersFired(acquired);
        return fired.Should().ContainSingle().Which.TriggerFiredBundle!;
    }

    /// <summary>Keeps a node alive until the test is torn down, as a dead node's rows and a survivor both have to be.</summary>
    private ServiceProvider Own(ServiceProvider node)
    {
        nodes.Add(node);
        return node;
    }

    /// <param name="instanceId">The node's instance id; two ids on one file are two nodes.</param>
    /// <param name="handBack">Whether the store hands back what a shutdown cancels.</param>
    private ServiceProvider BuildNode(string instanceId, bool handBack = false)
    {
        ServiceCollection services = new();

        // The recorder writes a misfire row for every TriggerMisfired, which is what an operator would see.
        services.AddQuartzExecutionHistory();
        services.AddQuartz(q =>
        {
            q.UseTimeProvider(clock);
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = SchedulerName;
                options.InstanceId = instanceId;
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
                store.ConfigureStore(options =>
                {
                    options.RecoverFiringsCancelledByShutdown = handBack;
                    options.MisfireThreshold = misfireThreshold;
                });
            });
        });

        return services.BuildServiceProvider();
    }

    /// <summary>Every trigger the scheduler announced as misfired.</summary>
    private sealed class MisfireRecorder : ITriggerListener
    {
        public ConcurrentQueue<TriggerKey> Misfired { get; } = new();

        public string Name => "misfire-recorder";

        public ValueTask TriggerMisfired(ITrigger trigger, IScheduler scheduler, CancellationToken cancellationToken = default)
        {
            Misfired.Enqueue(trigger.Key);
            return default;
        }
    }

    /// <summary>The recovery run on a node that started again, once it happens.</summary>
    private static class Recordings
    {
        private static TaskCompletionSource<TriggerFiredBundle> recovered = NewSource();

        public static Task<TriggerFiredBundle> Recovered => recovered.Task;

        public static void Reset()
        {
            recovered = NewSource();
        }

        public static void Record(IJobExecutionContext context)
        {
            if (context.Recovering)
            {
                recovered.TrySetResult(new TriggerFiredBundle
                {
                    JobDetail = context.JobDetail,
                    Trigger = (IOperableTrigger) context.Trigger,
                    Recovering = context.Recovering,
                    FireTimeUtc = context.FireTimeUtc,
                    ScheduledFireTimeUtc = context.ScheduledFireTimeUtc,
                    PreviousFireTimeUtc = context.PreviousFireTimeUtc,
                    NextFireTimeUtc = context.NextFireTimeUtc
                });
            }
        }

        private static TaskCompletionSource<TriggerFiredBundle> NewSource()
        {
            return new TaskCompletionSource<TriggerFiredBundle>(TaskCreationOptions.RunContinuationsAsynchronously);
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
