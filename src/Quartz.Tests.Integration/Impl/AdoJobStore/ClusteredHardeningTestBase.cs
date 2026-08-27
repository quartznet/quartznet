namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The clustered failures nobody can produce by shutting a scheduler down politely: a node that died
/// mid-flight and left its rows behind, and a live node that a peer decided was dead.
/// <para>
/// These run on PostgreSQL and SQL Server, because the interesting code is the SQL — the row locking
/// that stops two nodes acquiring the same trigger, and the recovery statements that undo a dead node's
/// residue — and that SQL differs per engine. PostgreSQL locks with <c>SELECT ... FOR UPDATE</c>, SQL
/// Server with an <c>(UPDLOCK,ROWLOCK)</c> hint; only running both says the store works on both.
/// </para>
/// <para>
/// The exactly-once case these inherit from <see cref="ClusteredExactlyOnceTestBase" /> runs on three
/// more engines besides. What keeps the cases below off those is that they write a dead node's residue
/// by hand, so the fixture has to spell a boolean and a timestamp the way each engine stores them —
/// which is fixture work rather than coverage of the store.
/// </para>
/// </summary>
public abstract class ClusteredHardeningTestBase : ClusteredExactlyOnceTestBase
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
    /// What the node listing says while a corpse is still on the table, and after it is cleared. This is
    /// the operator's question — "which of my nodes is alive" — asked of the same rows recovery reads,
    /// so the two can be seen to agree.
    /// </summary>
    /// <remarks>
    /// The survivor is created but not started until the residue is written, because starting it runs
    /// the first check-in, which is also the pass that sweeps the dead node away. The listing is
    /// therefore read once before <c>Start()</c> — where the dead node is still there and reported
    /// <see cref="ClusterNodeState.Failed" /> — and once after recovery, where it is gone.
    /// </remarks>
    [Test]
    public async Task KilledNode_IsListedAsFailedAndThenDisappearsWhenRecovered()
    {
        IScheduler survivor = await CreateScheduler("survivor");

        try
        {
            await InsertDeadNodeCheckin();

            List<ClusterNode> beforeRecovery = await survivor.QueryClusterNodes();

            beforeRecovery[0].InstanceId.Should().Be("survivor",
                "the node answering is listed first whether or not it has checked in yet");
            beforeRecovery[0].IsCurrentNode.Should().BeTrue();

            ClusterNode dead = beforeRecovery.Should().ContainSingle(x => x.InstanceId == DeadNode,
                    "a node that died leaves its state row behind, and that row is what an operator needs to see")
                .Subject;
            dead.State.Should().Be(ClusterNodeState.Failed,
                "the row was backdated five minutes past a two-second misfire threshold, which is the same "
                + "arithmetic that decides recovery — the listing must not call it merely overdue");
            dead.IsCurrentNode.Should().BeFalse();
            dead.LastCheckInUtc.Should().NotBeNull("the row carries the stamp the dead node last wrote");

            await survivor.Start();

            await WaitForCondition(
                async () => (await CountDeadNodeRows("QRTZ_SCHEDULER_STATE")) == 0,
                timeoutMs: 30_000,
                async () => $"the survivor's check-in to recover the dead node. State:\n{await DumpDatabaseState()}");

            List<ClusterNode> afterRecovery = await survivor.QueryClusterNodes();

            afterRecovery.Should().NotContain(x => x.InstanceId == DeadNode,
                "recovery deletes the row, so a corpse is reported until it is cleaned up and never after");

            ClusterNode current = afterRecovery.Should().ContainSingle(x => x.IsCurrentNode).Subject;
            current.InstanceId.Should().Be("survivor");
            current.State.Should().Be(ClusterNodeState.Alive,
                "a node whose check-in loop is running reports itself alive");
            current.LastCheckInUtc.Should().NotBeNull(
                "once the loop is running, this node has a row of its own like any other");
            current.CheckInInterval.Should().Be(TimeSpan.FromSeconds(1),
                "the interval reported is the one this fixture configured the node with");
        }
        finally
        {
            await survivor.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// A node that is alive and has been failed out anyway: a peer decided it was dead, took over the
    /// firing it had in flight, and deleted its check-in row. The node has to notice, register itself
    /// again, and leave the work the peer recovered alone — running recovery over its own rows a second
    /// time is how one interrupted firing becomes two.
    /// </summary>
    /// <remarks>
    /// The peer is a real second node rather than hand-written SQL, so the recovery under test is the
    /// one the store performs. What makes the sequence deterministic is the two check-in intervals: the
    /// failed-out node checks in every ten seconds and the peer every second, so backdating the first
    /// node's row immediately after its start leaves the peer most of ten seconds to notice — it needs
    /// one — and the node's own next check-in, which is the pass under test, follows within ten.
    /// </remarks>
    [Test]
    public async Task NodeFailedOutByAPeer_RegistersItselfAgainAndDoesNotReplayItsOwnWork()
    {
        const string FailedOut = "failed-out";
        const string Peer = "recovering-peer";

        IScheduler failedOut = await CreateScheduler(FailedOut, checkinIntervalMs: 10_000);
        IScheduler peer = null;
        var triggerKey = new TriggerKey("failedOutTrigger", Group);

        try
        {
            IJobDetail job = JobBuilder.Create<FiringRecordingJob>()
                .WithIdentity("failedOutJob", Group)
                .StoreDurably()
                .RequestRecovery()
                .Build();
            await failedOut.AddJob(job, new AddJobOptions { Replace = true });

            // An hour out, so the only thing that can run this job is a recovery trigger.
            await failedOut.ScheduleJob(TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(job)
                .StartAt(DateTimeOffset.UtcNow.AddHours(1))
                .Build());

            await failedOut.Start();

            await WaitForCondition(
                async () => (await CountRowsFor("QRTZ_SCHEDULER_STATE", FailedOut)) == 1,
                timeoutMs: 30_000,
                async () => $"the first check-in of '{FailedOut}'. State:\n{await DumpDatabaseState()}");

            // What this node has in flight at the moment the cluster stops believing in it.
            await InsertDeadNodeFiredTrigger(
                entryId: "failed-out-executing-1",
                triggerKey: triggerKey,
                state: "EXECUTING",
                jobKey: job.Key,
                requestsRecovery: true,
                instanceName: FailedOut);

            // And what makes the cluster stop believing in it: a check-in row five minutes old, which is
            // what a stalled process or a paused container leaves behind while it is still running.
            await BackdateCheckin(FailedOut, TimeSpan.FromMinutes(5));

            peer = await CreateScheduler(Peer);
            await peer.Start();

            await WaitForCondition(
                async () => (await CountRowsFor("QRTZ_SCHEDULER_STATE", FailedOut)) == 0,
                timeoutMs: 30_000,
                async () => $"'{Peer}' to declare '{FailedOut}' failed and delete its row. State:\n{await DumpDatabaseState()}");

            await WaitForFirings(1, timeoutMs: 30_000, $"the recovery trigger '{Peer}' scheduled to run");

            await WaitForCondition(
                async () => (await CountRowsFor("QRTZ_SCHEDULER_STATE", FailedOut)) == 1,
                timeoutMs: 30_000,
                async () => $"'{FailedOut}' to write its own check-in row back. State:\n{await DumpDatabaseState()}");

            // A second recovery would arrive a check-in later than the first, so waiting is the only way
            // to assert it did not happen.
            await SettleForRepeatFirings();

            FiringRecord firing = FiringRecordingJob.Firings.Should().ContainSingle(
                "the peer replayed the interrupted firing once; the failed-out node re-running recovery "
                + "over its own rows is what would make it twice").Subject;
            firing.TriggerKey.Group.Should().Be(SchedulerConstants.DefaultRecoveryGroup,
                "what ran is the replacement firing, not the job's own trigger, which is still an hour out");
            firing.OriginalTriggerName.Should().Be(triggerKey.Name);

            // That entry id specifically, rather than every row this node owns: by now it may legitimately
            // be running something of its own, including the recovery trigger the peer scheduled.
            int takenOverRows = await CountRows(
                "SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName AND ENTRY_ID = @entryId",
                ("schedulerName", SchedulerName),
                ("entryId", "failed-out-executing-1"));

            takenOverRows.Should().Be(0,
                "the peer deleted the row when it took the firing over, and the node it belonged to must "
                + "not have written it back");

            List<ClusterNode> nodes = await peer.QueryClusterNodes();
            nodes.Should().Contain(x => x.InstanceId == FailedOut && x.State == ClusterNodeState.Alive,
                "a node that has been failed out and has checked in since is a running node again, and its "
                + "peers have to be able to see that");
        }
        finally
        {
            await failedOut.Shutdown(waitForJobsToComplete: false);
            if (peer is not null)
            {
                await peer.Shutdown(waitForJobsToComplete: false);
            }
        }
    }

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
    /// Writes a fired-trigger row owned by <paramref name="instanceName" /> — the dead node unless a
    /// caller says otherwise — in the shape <c>StdAdoDelegate.InsertFiredTrigger</c> writes for that
    /// state.
    /// </summary>
    private async Task InsertDeadNodeFiredTrigger(
        string entryId,
        TriggerKey triggerKey,
        string state,
        JobKey jobKey,
        bool requestsRecovery,
        string instanceName = DeadNode)
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
            ("instanceName", instanceName),
            ("firedTime", firedTime),
            ("schedTime", firedTime),
            ("priority", 5),
            ("state", state),
            ("jobName", jobKey?.Name),
            ("jobGroup", jobKey?.Group),
            ("isNonConcurrent", false),
            ("requestsRecovery", requestsRecovery));
    }

    private Task<int> CountDeadNodeRows(string table) => CountRowsFor(table, DeadNode);

    private Task<int> CountRowsFor(string table, string instanceName)
    {
        return CountRows(
            $"SELECT COUNT(*) FROM {table} WHERE SCHED_NAME = @schedulerName AND INSTANCE_NAME = @instanceName",
            ("schedulerName", SchedulerName),
            ("instanceName", instanceName));
    }
}
