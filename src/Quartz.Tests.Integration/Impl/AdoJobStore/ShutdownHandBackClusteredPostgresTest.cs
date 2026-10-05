using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Globalization;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A node shutting down hands a cancelled firing of a job that requests recovery back to the cluster:
/// a peer runs it again on its next acquisition, rather than a check-in timeout later or never (#4014).
/// </summary>
/// <remarks>
/// <para>
/// The check-in misfire threshold is thirty seconds against a one-second check-in, so a peer cannot
/// recover the leaver within the window these tests assert over: a recovery run here is the hand-back's
/// doing, not cluster recovery's. <c>ShutdownHandBackSqliteTest</c> is the same set at unit cost,
/// without a cluster.
/// </para>
/// </remarks>
[Category("db-postgres")]
[NonParallelizable]
public sealed class ShutdownHandBackClusteredPostgresTest : ClusteredPostgresTestBase
{
    private const string Group = "handBack";
    private const int CheckinMisfireThresholdMs = 30_000;

    private static readonly JobKey jobKey = new("longJob", Group);
    private static readonly TriggerKey triggerKey = new("longTrigger", Group);
    private static readonly TriggerKey siblingKey = new("siblingTrigger", Group);
    private static readonly JobKey followUpJobKey = new("followUpJob", Group);
    private static readonly TriggerKey continuationKey = new("afterTrigger", Group);

    private static readonly TimeSpan waitLimit = TimeSpan.FromSeconds(30);

    protected override string SchedulerName => "ShutdownHandBackTest";

    [SetUp]
    public void ResetJob() => LongJob.Reset();

    /// <summary>
    /// Node A's shutdown cancels the job and hands the firing back; node B, already running, runs it
    /// again as a recovery, well inside the time cluster recovery would take.
    /// </summary>
    [Test]
    public async Task APeerRunsAHandedBackFiringOnItsNextAcquisition()
    {
        IScheduler nodeA = await CreateNode("handBackNodeA", recover: true);
        IScheduler nodeB = await CreateNode("handBackNodeB", recover: true);

        try
        {
            await nodeA.Start();
            await ScheduleLongJob(nodeA);
            Started started = await LongJob.Started.WaitAsync(waitLimit);
            started.InstanceId.Should().Be("handBackNodeA", "the premise: only node A was running when the job fired");

            await nodeB.Start();
            await nodeA.Shutdown(waitForJobsToComplete: true);

            Recovered recovered = await LongJob.Recovered.WaitAsync(TimeSpan.FromSeconds(15));

            recovered.InstanceId.Should().Be("handBackNodeB", "the node that left runs nothing more");
            recovered.RecoveringTriggerKey.Should().Be(triggerKey, "the recovery run names the trigger whose firing it stands in for");
            recovered.TriggerGroup.Should().Be(SchedulerConstants.DefaultRecoveryGroup);

            (await CountRows(
                "SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName AND INSTANCE_NAME = @instanceName",
                ("schedulerName", SchedulerName),
                ("instanceName", "handBackNodeA"))).Should().Be(0, "the hand-back completed the leaver's firing; nothing is left for a peer to recover");
        }
        finally
        {
            await nodeA.Shutdown(waitForJobsToComplete: false);
            await nodeB.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// A user's interrupt is a cancellation: nothing is stored for recovery, and the shutdown after it
    /// does not change that.
    /// </summary>
    [Test]
    public async Task AnInterruptIsStillACancellation()
    {
        IScheduler nodeA = await CreateNode("handBackNodeA", recover: true);
        try
        {
            await nodeA.Start();
            await ScheduleLongJob(nodeA);
            await LongJob.Started.WaitAsync(waitLimit);

            (await nodeA.Interrupt(jobKey)).Should().BeTrue();
            await LongJob.Ended.WaitAsync(waitLimit);
            await nodeA.Shutdown(waitForJobsToComplete: true);

            (await RecoveryTriggerCount()).Should().Be(0, "an interrupt asks the job to stop, not to run again");
            (await FiredTriggerCount()).Should().Be(0, "the firing completed as cancelled");
        }
        finally
        {
            await nodeA.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// Off, the default: the firing completes as cancelled and nothing runs it again.
    /// </summary>
    [Test]
    public async Task WithTheSettingOffAFiringTheShutdownCancelsIsCompletedAsCancelled()
    {
        IScheduler nodeA = await CreateNode("handBackNodeA", recover: false);
        try
        {
            await nodeA.Start();
            await ScheduleLongJob(nodeA);
            await LongJob.Started.WaitAsync(waitLimit);
            await nodeA.Shutdown(waitForJobsToComplete: true);

            (await RecoveryTriggerCount()).Should().Be(0);
            (await FiredTriggerCount()).Should().Be(0, "the firing completed as cancelled");
        }
        finally
        {
            await nodeA.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// The serial job's other trigger, blocked behind the firing, is let go, and the recovery trigger is
    /// stored waiting.
    /// </summary>
    [Test]
    public async Task ASerialJobsOtherTriggerIsReleased()
    {
        IScheduler nodeA = await CreateNode("handBackNodeA", recover: true);
        try
        {
            await nodeA.Start();
            await ScheduleLongJob(nodeA, serial: true);
            await nodeA.ScheduleJob(Hourly(siblingKey, jobKey, priority: 1));
            await LongJob.Started.WaitAsync(waitLimit);

            await WaitForCondition(
                async () => await TriggerStateOf(siblingKey) == "BLOCKED",
                timeoutMs: 10_000,
                "the premise: the serial job's other trigger waits behind the firing");

            await nodeA.Shutdown(waitForJobsToComplete: true);

            (await TriggerStateOf(siblingKey)).Should().Be("WAITING",
                "the hand-back completes the firing, and a completion lets go of a serial job's other triggers");
            (await RecoveryTriggerCount()).Should().Be(1);
        }
        finally
        {
            await nodeA.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// A trigger waiting on the firing's cancellation is still waiting once the firing is handed back, on
    /// the recovery trigger now: the occurrence has not ended, and the replay is what will end it.
    /// </summary>
    [Test]
    public async Task WhatWaitsOnTheFiringWaitsOnTheReplay()
    {
        IScheduler nodeA = await CreateNode("handBackNodeA", recover: true);
        try
        {
            await nodeA.Start();
            await ScheduleLongJob(nodeA);
            await nodeA.ScheduleJob(
                JobBuilder.Create<FollowUpJob>().WithIdentity(followUpJobKey).Build(),
                TriggerBuilder.Create()
                    .WithIdentity(continuationKey)
                    .StartAfter(triggerKey, ContinuationCondition.OnCancellation)
                    .Build());
            await LongJob.Started.WaitAsync(waitLimit);
            await nodeA.Shutdown(waitForJobsToComplete: true);

            (await TriggerStateOf(continuationKey)).Should().Be("AWAITING",
                "a firing handed back has not ended, so nothing waiting on how it ends is settled");
            (await RecoveryTriggerCount()).Should().Be(1);
            (await ParentOf(continuationKey)).Should().Be(await TheRecoveryTriggerName(),
                "it waits for the replay, not for the repeating trigger's next occurrence");
        }
        finally
        {
            await nodeA.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// A one-shot firing handed back by node A is replayed by node B, and the continuation that waited on
    /// it runs once, on the replay's success.
    /// </summary>
    [Test]
    public async Task AOneShotFiringsContinuationRunsOnceTheReplaySucceeds()
    {
        IScheduler nodeA = await CreateNode("handBackNodeA", recover: true);
        IScheduler nodeB = await CreateNode("handBackNodeB", recover: true);
        try
        {
            await nodeA.Start();
            await nodeA.AddJob(JobBuilder.Create<FollowUpJob>().WithIdentity(followUpJobKey).StoreDurably().Build());
            await nodeA.ScheduleJob(
                JobBuilder.Create<LongJob>().WithIdentity(jobKey).RequestRecovery().Build(),
                TriggerBuilder.Create().WithIdentity(triggerKey).StartNow().Build());
            await nodeA.ScheduleJob(TriggerBuilder.Create()
                .WithIdentity(continuationKey)
                .ForJob(followUpJobKey)
                .StartAfter(triggerKey, ContinuationCondition.OnSuccess)
                .Build());
            await LongJob.Started.WaitAsync(waitLimit);

            await nodeB.Start();
            await nodeA.Shutdown(waitForJobsToComplete: true);

            await LongJob.Recovered.WaitAsync(TimeSpan.FromSeconds(15));
            await FollowUpJob.FirstRun.WaitAsync(TimeSpan.FromSeconds(15));
            await nodeB.Shutdown(waitForJobsToComplete: true);

            FollowUpJob.RunsOf(continuationKey).Should().Be(1, "the replay's success released it, once");
        }
        finally
        {
            await nodeA.Shutdown(waitForJobsToComplete: false);
            await nodeB.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// Node A hands a firing back, node B's replay of it is handed back by B's own shutdown, and B, started
    /// again, replays it once more. That last replay still stands in for A's original firing.
    /// </summary>
    [Test]
    public async Task AReplayHandedBackAgainStillNamesTheOriginalFiring()
    {
        IScheduler nodeA = await CreateNode("handBackNodeA", recover: true);
        IScheduler nodeB = await CreateNode("handBackNodeB", recover: true);
        IScheduler restartedB = null;
        try
        {
            await nodeA.Start();
            await ScheduleLongJob(nodeA);
            Started original = await LongJob.Started.WaitAsync(waitLimit);
            await nodeA.Shutdown(waitForJobsToComplete: true);

            LongJob.RunLongWhenRecovering(times: 1);
            LongJob.ResetSignals();
            await nodeB.Start();
            Started replay = await LongJob.Started.WaitAsync(waitLimit);
            replay.TriggerGroup.Should().Be(SchedulerConstants.DefaultRecoveryGroup, "the premise: what runs long on B is A's replay");
            await nodeB.Shutdown(waitForJobsToComplete: true);

            LongJob.ResetSignals();
            restartedB = await CreateNode("handBackNodeB", recover: true);
            await restartedB.Start();
            Recovered recovered = await LongJob.Recovered.WaitAsync(waitLimit);

            recovered.RecoveringTriggerKey.Should().Be(triggerKey, "the replay of a replay still stands in for the original trigger's firing");
            recovered.OriginalFireTime.Should().BeCloseTo(original.FireTimeUtc, TimeSpan.FromSeconds(1),
                "and for that firing's fire time, not the first replay's");
        }
        finally
        {
            await nodeA.Shutdown(waitForJobsToComplete: false);
            await nodeB.Shutdown(waitForJobsToComplete: false);
            if (restartedB is not null)
            {
                await restartedB.Shutdown(waitForJobsToComplete: false);
            }
        }
    }

    private Task<IScheduler> CreateNode(string instanceId, bool recover)
    {
        return CreateScheduler(
            instanceId,
            checkinMisfireThresholdMs: CheckinMisfireThresholdMs,
            configure: (NameValueCollection properties) =>
            {
                // The shutdown asks running jobs to stop, and then waits for them to.
                properties["quartz.scheduler.interruptJobsOnShutdownWithWait"] = "true";
                properties["quartz.jobStore.recoverFiringsCancelledByShutdown"] = recover ? "true" : "false";
            });
    }

    private static async Task ScheduleLongJob(IScheduler scheduler, bool serial = false)
    {
        IJobDetail job = serial
            ? JobBuilder.Create<SerialLongJob>().WithIdentity(jobKey).RequestRecovery().Build()
            : JobBuilder.Create<LongJob>().WithIdentity(jobKey).RequestRecovery().Build();

        await scheduler.ScheduleJob(job, Hourly(triggerKey, jobKey, priority: 10));
    }

    private static ITrigger Hourly(TriggerKey key, JobKey job, int priority)
    {
        return TriggerBuilder.Create()
            .WithIdentity(key)
            .ForJob(job)
            .StartNow()
            .WithPriority(priority)
            .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();
    }

    private Task<int> RecoveryTriggerCount()
    {
        return CountRows(
            "SELECT COUNT(*) FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_GROUP = @triggerGroup",
            ("schedulerName", SchedulerName),
            ("triggerGroup", SchedulerConstants.DefaultRecoveryGroup));
    }

    private Task<int> FiredTriggerCount()
    {
        return CountRows(
            "SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName",
            ("schedulerName", SchedulerName));
    }

    private async Task<string> TheRecoveryTriggerName()
    {
        return (string) await ExecuteScalar(
            "SELECT TRIGGER_NAME FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_GROUP = @triggerGroup",
            ("schedulerName", SchedulerName),
            ("triggerGroup", SchedulerConstants.DefaultRecoveryGroup));
    }

    private async Task<string> ParentOf(TriggerKey key)
    {
        return (string) await ExecuteScalar(
            "SELECT CONTINUES_TRIGGER_NAME FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @triggerName AND TRIGGER_GROUP = @triggerGroup",
            ("schedulerName", SchedulerName),
            ("triggerName", key.Name),
            ("triggerGroup", key.Group));
    }

    private async Task<string> TriggerStateOf(TriggerKey key)
    {
        return (string) await ExecuteScalar(
            "SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @triggerName AND TRIGGER_GROUP = @triggerGroup",
            ("schedulerName", SchedulerName),
            ("triggerName", key.Name),
            ("triggerGroup", key.Group));
    }

    public sealed record Started(string InstanceId, string TriggerGroup, DateTimeOffset FireTimeUtc);

    public sealed record Recovered(string InstanceId, string TriggerGroup, TriggerKey RecoveringTriggerKey, DateTimeOffset OriginalFireTime);

    /// <summary>
    /// Runs until it is cancelled and lets the cancellation out, unless it is recovering, when it records
    /// where and as what it ran and returns. The next recovering runs <see cref="RunLongWhenRecovering" />
    /// names run long, as a first run does.
    /// </summary>
    public class LongJob : IJob
    {
        private static TaskCompletionSource<Started> started = NewSource<Started>();
        private static TaskCompletionSource<Recovered> recovered = NewSource<Recovered>();
        private static TaskCompletionSource<bool> ended = NewSource<bool>();
        private static int longRecoveries;

        public static Task<Started> Started => started.Task;

        public static Task<Recovered> Recovered => recovered.Task;

        public static Task Ended => ended.Task;

        public static void Reset()
        {
            ResetSignals();
            Interlocked.Exchange(ref longRecoveries, 0);
            FollowUpJob.Reset();
        }

        public static void ResetSignals()
        {
            started = NewSource<Started>();
            recovered = NewSource<Recovered>();
            ended = NewSource<bool>();
        }

        public static void RunLongWhenRecovering(int times) => Interlocked.Exchange(ref longRecoveries, times);

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (context.Recovering && Interlocked.Decrement(ref longRecoveries) < 0)
            {
                string fireTime = context.MergedJobDataMap.GetString(SchedulerConstants.FailedJobOriginalTriggerFireTime);
                recovered.TrySetResult(new Recovered(
                    context.Scheduler.SchedulerInstanceId,
                    context.Trigger.Key.Group,
                    context.RecoveringTriggerKey,
                    DateTimeOffset.Parse(fireTime, CultureInfo.InvariantCulture)));
                return;
            }

            started.TrySetResult(new Started(context.Scheduler.SchedulerInstanceId, context.Trigger.Key.Group, context.FireTimeUtc));
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ended.TrySetResult(true);
            }
        }

        private static TaskCompletionSource<T> NewSource<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    [DisallowConcurrentExecution]
    public sealed class SerialLongJob : LongJob;

    /// <summary>Counts its runs by trigger.</summary>
    public sealed class FollowUpJob : IJob
    {
        private static ConcurrentDictionary<TriggerKey, int> runs = new();
        private static TaskCompletionSource firstRun = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static Task FirstRun => firstRun.Task;

        public static void Reset()
        {
            runs = new ConcurrentDictionary<TriggerKey, int>();
            firstRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static int RunsOf(TriggerKey key) => runs.TryGetValue(key, out int count) ? count : 0;

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            runs.AddOrUpdate(context.Trigger.Key, 1, static (_, count) => count + 1);
            firstRun.TrySetResult();
            return default;
        }
    }
}
