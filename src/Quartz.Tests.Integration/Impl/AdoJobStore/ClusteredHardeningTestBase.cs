using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Globalization;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The clustered failures nobody can produce by shutting a scheduler down politely: a node that died
/// mid-flight and left its rows behind, and two live nodes racing for the same due triggers.
/// <para>
/// These run against every engine that has a fixture, because the interesting code is the SQL — the row
/// locking that stops two nodes acquiring the same trigger, and the recovery statements that undo a
/// dead node's residue — and that SQL differs per engine. PostgreSQL locks with
/// <c>SELECT ... FOR UPDATE</c>, SQL Server with an <c>(UPDLOCK,ROWLOCK)</c> hint; only running both
/// says the store works on both.
/// </para>
/// </summary>
public abstract class ClusteredHardeningTestBase : ClusteredJobStoreTestBase
{
    private const string Group = "clusterHardening";

    /// <summary>
    /// The instance id of a node that never existed. Nothing ever starts under this name, so every row
    /// carrying it is residue by construction and no live check-in can rewrite it underneath a test.
    /// </summary>
    private const string DeadNode = "killed-node";

    protected ClusteredHardeningTestBase(string provider) : base(provider)
    {
    }

    protected override string SchedulerName => "ClusterHardeningTest";

    [SetUp]
    public void ResetFirings() => FiringRecordingJob.Reset();

    /// <summary>
    /// A node killed while it held an acquired trigger leaves the trigger row in ACQUIRED and a
    /// fired-trigger row naming itself. Nothing acquires an ACQUIRED trigger a second time, so unless a
    /// survivor recovers it the trigger is lost for good — the "the job stopped running after the pod
    /// was OOM-killed" report. A node shut down politely cannot produce this state; it releases what it
    /// holds on the way out. So the state is written directly.
    /// </summary>
    [Test]
    public async Task KilledNode_AcquiredTriggerIsReleasedAndFiredBySurvivor()
    {
        IScheduler survivor = await CreateScheduler("survivor");
        var triggerKey = new TriggerKey("orphanedAcquiredTrigger", Group);

        try
        {
            // Created but not started: an unstarted node never checks in and never acquires anything, so
            // the residue written below is the only thing in play until Start() runs the first check-in.
            IJobDetail job = JobBuilder.Create<FiringRecordingJob>()
                .WithIdentity("orphanedAcquiredJob", Group)
                .StoreDurably()
                .Build();
            await survivor.AddJob(job, new AddJobOptions { Replace = true });

            await survivor.ScheduleJob(TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(job)
                .StartAt(DateTimeOffset.UtcNow)
                .Build());

            // The residue of `kill -9` between acquisition and firing. The job columns stay null and the
            // flags false because that is exactly what StdAdoDelegate.InsertFiredTrigger writes for an
            // ACQUIRED row — the job has not been loaded at that point.
            await InsertDeadNodeCheckin();
            await InsertDeadNodeFiredTrigger(
                entryId: "killed-node-acquired-1",
                triggerKey: triggerKey,
                state: "ACQUIRED",
                jobKey: null,
                requestsRecovery: false);
            await ExecuteNonQuery(
                "UPDATE QRTZ_TRIGGERS SET TRIGGER_STATE = 'ACQUIRED' "
                + "WHERE SCHED_NAME = @schedulerName AND TRIGGER_NAME = @triggerName AND TRIGGER_GROUP = @triggerGroup",
                ("schedulerName", SchedulerName),
                ("triggerName", triggerKey.Name),
                ("triggerGroup", triggerKey.Group));

            await survivor.Start();

            await WaitForFirings(1, timeoutMs: 30_000, "the survivor to recover and fire the trigger the dead node was holding");
            await SettleForRepeatFirings();

            FiringRecordingJob.Firings.Should().ContainSingle(
                    "the released trigger fires once; a fired-trigger row that survived recovery would keep the trigger stuck instead")
                .Which.InstanceId.Should().Be("survivor");

            (await CountDeadNodeRows("QRTZ_FIRED_TRIGGERS")).Should().Be(0,
                "ClusterRecover deletes the dead node's fired-trigger rows; leaving them makes the corpse "
                + "look busy to QueryFireInstances and re-recovered on every subsequent check-in");
            (await CountDeadNodeRows("QRTZ_SCHEDULER_STATE")).Should().Be(0,
                "ClusterRecover deletes the dead node's state row once it has finished with it, so the "
                + "cluster stops paying to rediscover the same corpse");
        }
        finally
        {
            await survivor.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// The other half of the same crash: the node died while a job that asked for recovery was actually
    /// executing. Here the survivor has to build a replacement trigger rather than merely release one,
    /// and the job's own trigger is deliberately parked an hour out so the replacement is the only thing
    /// that can run it.
    /// </summary>
    [Test]
    public async Task KilledNode_ExecutingRecoverableJobIsRescheduledBySurvivor()
    {
        IScheduler survivor = await CreateScheduler("survivor");
        var triggerKey = new TriggerKey("interruptedTrigger", Group);

        try
        {
            IJobDetail job = JobBuilder.Create<FiringRecordingJob>()
                .WithIdentity("interruptedJob", Group)
                .StoreDurably()
                .RequestRecovery()
                .Build();
            await survivor.AddJob(job, new AddJobOptions { Replace = true });

            await survivor.ScheduleJob(TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(job)
                .StartAt(DateTimeOffset.UtcNow.AddHours(1))
                .Build());

            await InsertDeadNodeCheckin();
            await InsertDeadNodeFiredTrigger(
                entryId: "killed-node-executing-1",
                triggerKey: triggerKey,
                state: "EXECUTING",
                jobKey: job.Key,
                requestsRecovery: true);

            await survivor.Start();

            await WaitForFirings(1, timeoutMs: 30_000, "the survivor to schedule and run the recovery trigger");
            await SettleForRepeatFirings();

            FiringRecord firing = FiringRecordingJob.Firings.Should().ContainSingle(
                "the interrupted execution is recovered once, not once per check-in").Subject;
            firing.InstanceId.Should().Be("survivor");
            firing.TriggerKey.Group.Should().Be(SchedulerConstants.DefaultRecoveryGroup,
                "the job runs again through a recovery trigger, not through its own trigger, which is still an hour out");
            firing.OriginalTriggerName.Should().Be(triggerKey.Name,
                "a recovered job is told through its data map which trigger's firing it is replaying");

            (await CountDeadNodeRows("QRTZ_FIRED_TRIGGERS")).Should().Be(0);
            (await CountDeadNodeRows("QRTZ_SCHEDULER_STATE")).Should().Be(0);

            (await survivor.GetTrigger(triggerKey)).Should().NotBeNull(
                "recovery replays the interrupted firing; it does not consume the original schedule");
        }
        finally
        {
            await survivor.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// The property the clustered job store exists to provide: two nodes, one set of triggers, every
    /// trigger fired exactly once.
    /// </summary>
    [Test]
    public Task TwoNodes_EveryOneShotTriggerFiresExactlyOnce()
    {
        return AssertNoDoubleFire(configure: null);
    }

    /// <summary>
    /// Starts two contending nodes, schedules thirty one-shot triggers due at the same instant, and
    /// asserts each fired exactly once. Both nodes acquire in batches so they genuinely reach for
    /// overlapping sets of rows rather than politely taking turns, which is the only arrangement in
    /// which a broken lock or a lost <c>WHERE TRIGGER_STATE = 'WAITING'</c> guard shows up at all.
    /// </summary>
    protected async Task AssertNoDoubleFire(Action<NameValueCollection> configure)
    {
        const int TriggerCount = 30;

        void ConfigureNode(NameValueCollection properties)
        {
            // A node that acquires ten at a time reaches for overlapping sets of rows rather than
            // politely taking one each; the thread pool has to grow with it, because the scheduler
            // refuses a batch larger than the number of threads that could run it.
            properties["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = "10";
            properties["quartz.threadPool.maxConcurrency"] = "10";
            configure?.Invoke(properties);
        }

        IScheduler nodeA = await CreateScheduler("nodeA", configure: ConfigureNode);
        IScheduler nodeB = await CreateScheduler("nodeB", configure: ConfigureNode);

        try
        {
            await nodeA.Start();
            await nodeB.Start();

            IJobDetail job = JobBuilder.Create<FiringRecordingJob>()
                .WithIdentity("noDoubleFireJob", Group)
                .StoreDurably()
                .Build();
            await nodeA.AddJob(job, new AddJobOptions { Replace = true });

            // Far enough out that every trigger is stored before any of them is due, so all thirty
            // become eligible at the same instant with both nodes awake to see them.
            DateTimeOffset start = DateTimeOffset.UtcNow.AddSeconds(5);
            string[] expected = new string[TriggerCount];
            for (int i = 0; i < TriggerCount; i++)
            {
                expected[i] = "oneShot-" + i.ToString(CultureInfo.InvariantCulture);
                await nodeA.ScheduleJob(TriggerBuilder.Create()
                    .WithIdentity(expected[i], Group)
                    .ForJob(job)
                    .StartAt(start)
                    .Build());
            }

            await WaitForCondition(
                () => Task.FromResult(FiringRecordingJob.Firings.Count >= TriggerCount),
                timeoutMs: 90_000,
                async () =>
                {
                    string[] missing = expected.Except(FiredTriggerNames()).ToArray();
                    return $"all {TriggerCount} one-shot triggers to fire; {missing.Length} never did "
                           + $"([{string.Join(", ", missing)}]). State:\n{await DumpDatabaseState()}";
                });

            // Absence cannot be polled for, only waited out: a duplicate acquisition that lost the race by
            // a few hundred milliseconds arrives after the thirtieth firing, not before it.
            await SettleForRepeatFirings();

            TestContext.Out.WriteLine("Firings per node: " + string.Join(", ", FiringRecordingJob.Firings
                .GroupBy(x => x.InstanceId)
                .OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => $"{x.Key}={x.Count()}")));

            FiredTriggerNames().Should().BeEquivalentTo(expected,
                "a clustered store hands each one-shot trigger to exactly one node — a repeated name means "
                + "two nodes acquired the same row, and a missing one means a row was acquired and dropped");
        }
        finally
        {
            await nodeA.Shutdown(waitForJobsToComplete: false);
            await nodeB.Shutdown(waitForJobsToComplete: false);
        }
    }

    private static string[] FiredTriggerNames() => FiringRecordingJob.Firings.Select(x => x.TriggerKey.Name).ToArray();

    private Task WaitForFirings(int count, int timeoutMs, string what)
    {
        return WaitForCondition(
            () => Task.FromResult(FiringRecordingJob.Firings.Count >= count),
            timeoutMs,
            async () => $"{what}. State:\n{await DumpDatabaseState()}");
    }

    /// <summary>
    /// Gives a repeat firing time to arrive before the caller asserts there was none. Recovery is driven
    /// by the check-in loop, so a recovery that failed to clean up after itself would run again on the
    /// next check-in — three of those, at this fixture's one-second interval, pass inside this wait.
    /// </summary>
    private static Task SettleForRepeatFirings() => Task.Delay(3000);

    /// <summary>
    /// Writes the SCHEDULER_STATE row a node leaves behind, then ages it past the failure threshold. The
    /// row is inserted at the current time and backdated rather than written already stale, so that the
    /// helper the staleness waits use is also what makes this node look dead.
    /// </summary>
    private async Task InsertDeadNodeCheckin()
    {
        await ExecuteNonQuery(
            "INSERT INTO QRTZ_SCHEDULER_STATE (SCHED_NAME, INSTANCE_NAME, LAST_CHECKIN_TIME, CHECKIN_INTERVAL) "
            + "VALUES (@schedulerName, @instanceName, @lastCheckinTime, @checkinInterval)",
            ("schedulerName", SchedulerName),
            ("instanceName", DeadNode),
            ("lastCheckinTime", DateTimeOffset.UtcNow.UtcTicks),
            ("checkinInterval", 1000L));

        await BackdateCheckin(DeadNode, TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// Writes a fired-trigger row owned by the dead node, in the shape
    /// <c>StdAdoDelegate.InsertFiredTrigger</c> writes for that state.
    /// </summary>
    private async Task InsertDeadNodeFiredTrigger(
        string entryId,
        TriggerKey triggerKey,
        string state,
        JobKey jobKey,
        bool requestsRecovery)
    {
        // Recent enough that a recovery trigger built from it is merely overdue rather than misfired,
        // which keeps the assertions about what ran clear of misfire policy.
        long firedTime = DateTimeOffset.UtcNow.AddSeconds(-5).UtcTicks;

        await ExecuteNonQuery(
            "INSERT INTO QRTZ_FIRED_TRIGGERS "
            + "(SCHED_NAME, ENTRY_ID, TRIGGER_NAME, TRIGGER_GROUP, INSTANCE_NAME, FIRED_TIME, SCHED_TIME, "
            + "PRIORITY, STATE, JOB_NAME, JOB_GROUP, IS_NONCONCURRENT, REQUESTS_RECOVERY, EXECUTION_GROUP) "
            + "VALUES (@schedulerName, @entryId, @triggerName, @triggerGroup, @instanceName, @firedTime, @schedTime, "
            + "@priority, @state, @jobName, @jobGroup, @isNonConcurrent, @requestsRecovery, NULL)",
            ("schedulerName", SchedulerName),
            ("entryId", entryId),
            ("triggerName", triggerKey.Name),
            ("triggerGroup", triggerKey.Group),
            ("instanceName", DeadNode),
            ("firedTime", firedTime),
            ("schedTime", firedTime),
            ("priority", 5),
            ("state", state),
            ("jobName", jobKey?.Name),
            ("jobGroup", jobKey?.Group),
            ("isNonConcurrent", false),
            ("requestsRecovery", requestsRecovery));
    }

    private Task<int> CountDeadNodeRows(string table)
    {
        return CountRows(
            $"SELECT COUNT(*) FROM {table} WHERE SCHED_NAME = @schedulerName AND INSTANCE_NAME = @instanceName",
            ("schedulerName", SchedulerName),
            ("instanceName", DeadNode));
    }

    private sealed record FiringRecord(TriggerKey TriggerKey, string InstanceId, string OriginalTriggerName);

    /// <summary>
    /// Records the trigger, the node, and — for a recovered firing — the trigger whose firing is being
    /// replayed. Concurrent by design: the exactly-once property under test belongs to trigger
    /// acquisition, and <c>[DisallowConcurrentExecution]</c> would hide it behind a queue.
    /// </summary>
    private sealed class FiringRecordingJob : IJob
    {
        private static volatile ConcurrentQueue<FiringRecord> firings = new();

        public static ConcurrentQueue<FiringRecord> Firings => firings;

        public static void Reset() => Interlocked.Exchange(ref firings, new ConcurrentQueue<FiringRecord>());

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            JobDataMap map = context.MergedJobDataMap;
            string originalTriggerName = map.ContainsKey(SchedulerConstants.FailedJobOriginalTriggerName)
                ? map.GetString(SchedulerConstants.FailedJobOriginalTriggerName)
                : null;

            Firings.Enqueue(new FiringRecord(context.Trigger.Key, context.Scheduler.SchedulerInstanceId, originalTriggerName));
            return default;
        }
    }
}
