using System.Collections.Specialized;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A node shutting down hands a cancelled firing of a job that requests recovery back to the cluster:
/// a peer runs it again at once, rather than a check-in timeout later or never (#4014).
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
    public async Task APeerRunsAHandedBackFiringAtOnce()
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
    /// A trigger waiting on the firing's cancellation is still waiting once the firing is handed back:
    /// the occurrence has not ended.
    /// </summary>
    [Test]
    public async Task NothingWaitingOnTheFiringIsSettled()
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
        }
        finally
        {
            await nodeA.Shutdown(waitForJobsToComplete: false);
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

    private async Task<string> TriggerStateOf(TriggerKey key)
    {
        return (string) await ExecuteScalar(
            "SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @triggerName AND TRIGGER_GROUP = @triggerGroup",
            ("schedulerName", SchedulerName),
            ("triggerName", key.Name),
            ("triggerGroup", key.Group));
    }

    public sealed record Started(string InstanceId);

    public sealed record Recovered(string InstanceId, string TriggerGroup, TriggerKey RecoveringTriggerKey);

    /// <summary>
    /// Runs until it is cancelled and lets the cancellation out, unless it is recovering, when it records
    /// where and as what it ran and returns.
    /// </summary>
    public class LongJob : IJob
    {
        private static TaskCompletionSource<Started> started = NewSource<Started>();
        private static TaskCompletionSource<Recovered> recovered = NewSource<Recovered>();
        private static TaskCompletionSource<bool> ended = NewSource<bool>();

        public static Task<Started> Started => started.Task;

        public static Task<Recovered> Recovered => recovered.Task;

        public static Task Ended => ended.Task;

        public static void Reset()
        {
            started = NewSource<Started>();
            recovered = NewSource<Recovered>();
            ended = NewSource<bool>();
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (context.Recovering)
            {
                recovered.TrySetResult(new Recovered(context.Scheduler.SchedulerInstanceId, context.Trigger.Key.Group, context.RecoveringTriggerKey));
                return;
            }

            started.TrySetResult(new Started(context.Scheduler.SchedulerInstanceId));
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

    public sealed class FollowUpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
