using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Two clustered nodes carrying every kind of work the scheduler can be asked to do, for half an hour,
/// with the failures a cluster meets in production induced along the way — and then asked what they
/// left behind.
/// </summary>
/// <remarks>
/// <para>
/// <b>It is a release gate, not a CI leg.</b> The fixtures carry <c>[Category("LongRunning")]</c> and
/// every integration leg excludes that category (<c>build/Build.cs</c>, <c>GetTestFilter</c>), because
/// half an hour of wall time in a pull request's leg reads as a hung job rather than as thoroughness.
/// Run it by hand before a tag:
/// </para>
/// <code>
/// $env:QUARTZ_TEST_DATABASE = 'postgres'
/// $env:QUARTZ_SOAK_MINUTES  = '30'
/// dotnet test src/Quartz.Tests.Integration/Quartz.Tests.Integration.csproj `
///   --filter 'FullyQualifiedName~ClusteredSoakPostgresTest'
/// </code>
/// <para>
/// <b>What it is for.</b> Every other clustered fixture here asserts one property over a run of
/// minutes: exactly-once acquisition, a killed node's residue, node affinity, tenancy. None of them
/// runs long enough for a leak to show, none of them runs a
/// <see cref="DisallowConcurrentExecutionAttribute" /> job on two live nodes and watches for an
/// overlap — <c>ClusteredExactlyOnceTestBase</c> says in as many words why its own job is concurrent
/// by design — and none of them puts retries, timeouts, misfires and a failover through the same
/// scheduler at the same time. That combination is what a night in production is, and it is what this
/// runs.
/// </para>
/// <para>
/// <b>Both nodes are in this process</b>, which is what makes the overlap detector possible at all: a
/// static counter sees a firing on either node, so "these two firings of the same job overlapped" is
/// observable rather than inferred from rows. It is also the one way in which this is not a real
/// two-machine cluster — the nodes share a GC heap and a thread pool, and the sampling below therefore
/// watches the pair rather than either one.
/// </para>
/// <para>
/// <b>The clock is the database's.</b> No <c>FakeTimeProvider</c>, for the reason
/// <see cref="ClusteredJobStoreTestBase" /> gives: a cluster agrees on time through
/// <c>LAST_CHECKIN_TIME</c>, and a node with a fake clock is a node in a cluster that does not exist.
/// Where the test needs the past it moves a row, with <c>BackdateCheckin</c>.
/// </para>
/// </remarks>
public abstract class ClusteredSoakTestBase : ClusteredJobStoreTestBase
{
    private const string Group = "clusterSoak";
    private const string NodeA = "soak-node-a";
    private const string NodeB = "soak-node-b";

    /// <summary>
    /// The entry id of the fired-trigger row the killed node leaves behind. Named rather than
    /// generated so the end assertions can ask about that row specifically.
    /// </summary>
    private const string KilledEntryId = "soak-killed-executing-1";

    /// <summary>
    /// How long the run lasts unless <c>QUARTZ_SOAK_MINUTES</c> says otherwise.
    /// </summary>
    private const double DefaultSoakMinutes = 30;

    /// <summary>
    /// Each node's pool. Small on purpose: the workload below asks for a couple of firings a second,
    /// and a pool with room to spare would never queue anything behind the serial job.
    /// </summary>
    private const int MaxConcurrency = 5;

    /// <summary>
    /// How stale a trigger has to be before the store calls it misfired. Ten seconds rather than the
    /// default minute, so that the standby window can be seconds rather than minutes and still produce
    /// real misfires.
    /// </summary>
    private static readonly TimeSpan MisfireThreshold = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How long both nodes sit in standby. Three misfire thresholds, so every trigger that would have
    /// fired inside it is unambiguously misfired rather than merely late.
    /// </summary>
    private static readonly TimeSpan StandbyWindow = TimeSpan.FromSeconds(30);

    protected ClusteredSoakTestBase(string provider) : base(provider)
    {
    }

    protected override string SchedulerName => "ClusterSoakTest";

    /// <summary>
    /// The soak. One test, because the phases are one run: the failures are induced in the middle of
    /// a workload that is running throughout, and the assertions are about what the whole run left.
    /// </summary>
    [Test]
    public async Task TwoNodes_SurviveAFullWorkloadWithFailuresInducedThroughout()
    {
        TimeSpan duration = SoakDuration();
        SoakRecorder.Reset();

        List<ResourceSample> samples = [];
        List<string> timeline = [];

        IScheduler nodeA = await CreateSoakScheduler(NodeA);
        IScheduler nodeB = await CreateSoakScheduler(NodeB);

        try
        {
            AttachRecorder(nodeA);
            AttachRecorder(nodeB);

            await nodeA.Start();
            await nodeB.Start();

            DateTimeOffset started = DateTimeOffset.UtcNow;
            await ScheduleWorkload(nodeA, started);

            Note(timeline, started, $"workload scheduled; running for {duration}");

            // The phases, as fractions of the run, so a five-minute smoke exercises the same sequence a
            // half-hour gate does. Each one is a failure a cluster meets in production.
            DateTimeOffset standbyAt = started + duration * 0.25;
            DateTimeOffset resumeAt = standbyAt + StandbyWindow;
            DateTimeOffset killAt = started + duration * 0.55;
            DateTimeOffset replaceAt = started + duration * 0.70;
            DateTimeOffset deadline = started + duration;

            // Long enough that a sample is a settled heap rather than a moment in a collection, short
            // enough that a five-minute smoke still produces a series to read a trend off.
            TimeSpan sampleInterval = TimeSpan.FromSeconds(Math.Min(60, duration.TotalSeconds / 6));
            DateTimeOffset sampleAt = started + sampleInterval;

            bool standbyDone = false;
            bool resumeDone = false;
            bool killDone = false;
            bool replaceDone = false;

            while (DateTimeOffset.UtcNow < deadline)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;

                if (!standbyDone && now >= standbyAt)
                {
                    // Both nodes, not one. A single node in standby leaves the other acquiring, so
                    // nothing misfires; what produces a misfire is a due trigger that no node takes.
                    await nodeA.Standby();
                    await nodeB.Standby();
                    standbyDone = true;
                    Note(timeline, now, $"both nodes in standby for {StandbyWindow} to induce misfires");
                }
                else if (standbyDone && !resumeDone && now >= resumeAt)
                {
                    await nodeA.Start();
                    await nodeB.Start();
                    resumeDone = true;
                    Note(timeline, now, "both nodes resumed");
                }
                else if (resumeDone && !killDone && now >= killAt)
                {
                    await KillNodeB(nodeB);
                    nodeB = null;
                    killDone = true;
                    Note(timeline, now, $"'{NodeB}' killed mid-flight, leaving an EXECUTING row behind");

                    await WaitForCondition(
                        async () => await CountRowsFor("QRTZ_SCHEDULER_STATE", NodeB) == 0,
                        timeoutMs: 60_000,
                        async () => $"'{NodeA}' to declare '{NodeB}' dead and clear its row. State:\n{await DumpDatabaseState()}");

                    await WaitForCondition(
                        () => Task.FromResult(SoakRecorder.RecoveredFirings.Count >= 1),
                        timeoutMs: 60_000,
                        async () => $"'{NodeA}' to replay the killed node's interrupted firing. State:\n{await DumpDatabaseState()}");

                    Note(timeline, DateTimeOffset.UtcNow, "survivor recovered the killed node's work");
                }
                else if (killDone && !replaceDone && now >= replaceAt)
                {
                    nodeB = await CreateSoakScheduler(NodeB);
                    AttachRecorder(nodeB);
                    await nodeB.Start();
                    replaceDone = true;
                    Note(timeline, now, $"'{NodeB}' replaced and rejoined the cluster");
                }

                if (now >= sampleAt)
                {
                    samples.Add(ResourceSample.Take(now - started));
                    sampleAt = now + sampleInterval;
                }

                await Task.Delay(500);
            }

            samples.Add(ResourceSample.Take(DateTimeOffset.UtcNow - started));
            Note(timeline, DateTimeOffset.UtcNow, "run complete, shutting the cluster down");
        }
        finally
        {
            // Waiting for the jobs is the point rather than politeness: the "nothing left behind"
            // assertions below are about a cluster that finished its work, not one that was cut off
            // mid-firing.
            await nodeA.Shutdown(waitForJobsToComplete: true);
            if (nodeB is not null)
            {
                await nodeB.Shutdown(waitForJobsToComplete: true);
            }

            // Here rather than at the end of the run, so that a soak which fell over halfway through
            // still says how far it got and what it had seen. That is most of what a run this long is
            // for, and it is exactly the run that will not reach the end.
            TestContext.Out.WriteLine(Report(timeline, samples, duration));
        }

        await AssertNothingLeftBehind();
        AssertWorkloadRan(duration);
        AssertNoOverlap();
        AssertNoUnobservedFailures();
        AssertResourcesFlat(samples);
    }

    /// <summary>
    /// How long to run for, from <c>QUARTZ_SOAK_MINUTES</c>. A double so a smoke run can ask for a
    /// fraction of a minute without a second variable.
    /// </summary>
    private static TimeSpan SoakDuration()
    {
        string configured = Environment.GetEnvironmentVariable("QUARTZ_SOAK_MINUTES");
        if (string.IsNullOrWhiteSpace(configured))
        {
            return TimeSpan.FromMinutes(DefaultSoakMinutes);
        }

        double.TryParse(configured, NumberStyles.Float, CultureInfo.InvariantCulture, out double minutes)
            .Should().BeTrue("QUARTZ_SOAK_MINUTES is '{0}', which is not a number of minutes", configured);

        minutes.Should().BeGreaterThan(0, "a soak of no time asserts nothing");

        return TimeSpan.FromMinutes(minutes);
    }

    /// <summary>
    /// One node of the soak cluster: the fixture's shared database, a short check-in interval so a
    /// death is noticed in seconds, a misfire threshold short enough for the standby window to matter,
    /// and the timeout middleware, which is the one thing here that flat properties cannot register.
    /// </summary>
    /// <remarks>
    /// The check-in threshold is seven and a half intervals rather than the two the shorter fixtures
    /// use. Those run for a minute; this runs for thirty, so it gets thirty times the opportunities
    /// for a garbage collection and a slow round trip to line up and make a live node look dead — and
    /// a spurious failover would replay work the soak then counts. The induced death does not depend
    /// on the threshold at all: <c>KillNodeB</c> ages the row by five minutes.
    /// </remarks>
    private Task<IScheduler> CreateSoakScheduler(string instanceId)
    {
        return CreateScheduler(
            instanceId,
            checkinIntervalMs: 2000,
            checkinMisfireThresholdMs: 15_000,
            configure: properties =>
            {
                properties["quartz.threadPool.maxConcurrency"] = MaxConcurrency.ToString(CultureInfo.InvariantCulture);
                properties["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = MaxConcurrency.ToString(CultureInfo.InvariantCulture);
                properties["quartz.jobStore.misfireThreshold"] =
                    ((int) MisfireThreshold.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
            },
            configureBuilder: quartz => quartz.AddJobTimeout());
    }

    /// <summary>
    /// Every trigger family, the serial job the overlap detector watches, the failing job the retry
    /// policy is attached to, the job that overruns its budget, and the job that only a recovery
    /// trigger can run.
    /// </summary>
    /// <remarks>
    /// The intervals are seconds rather than milliseconds because a soak is about duration, not
    /// throughput: what is being watched is whether firing for half an hour leaves anything behind, and
    /// a saturated cluster would only add queueing to the picture. <c>ExecutionCeilingBenchmark</c> and
    /// <c>FireThroughputBenchmark</c> are where the rate is the question.
    /// </remarks>
    private async Task ScheduleWorkload(IScheduler scheduler, DateTimeOffset started)
    {
        IJobDetail counting = JobBuilder.Create<SoakCountingJob>()
            .WithIdentity(SoakJobs.Counting, Group)
            .StoreDurably()
            .Build();
        await scheduler.AddJob(counting, new AddJobOptions { Replace = true });

        // One trigger per family, so a family that stopped firing is named by the assertion rather
        // than hidden in an aggregate.
        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(SoakTriggers.Simple, Group)
            .ForJob(counting)
            .StartAt(started.AddSeconds(2))
            .WithSimpleSchedule(x => x.RepeatForever().WithInterval(SoakTriggers.SimpleInterval))
            .Build());

        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(SoakTriggers.Cron, Group)
            .ForJob(counting)
            .WithCronSchedule("0/5 * * * * ?")
            .Build());

        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(SoakTriggers.DailyTimeInterval, Group)
            .ForJob(counting)
            .StartAt(started.AddSeconds(2))
            .WithDailyTimeIntervalSchedule(x => x.OnEveryDay().WithInterval(3, IntervalUnit.Second))
            .Build());

        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(SoakTriggers.CalendarInterval, Group)
            .ForJob(counting)
            .StartAt(started.AddSeconds(2))
            .WithCalendarIntervalSchedule(x => x.WithInterval(4, IntervalUnit.Second))
            .Build());

        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(SoakTriggers.Recurrence, Group)
            .ForJob(counting)
            .StartAt(started.AddSeconds(2))
            .WithRecurrenceSchedule("FREQ=SECONDLY;INTERVAL=6")
            .Build());

        // Three triggers on one [DisallowConcurrentExecution] job, deliberately: the property under
        // test is that the store queues them behind one another across both nodes, and one trigger
        // could never show it.
        IJobDetail serial = JobBuilder.Create<SoakSerialJob>()
            .WithIdentity(SoakJobs.Serial, Group)
            .StoreDurably()
            .Build();
        await scheduler.AddJob(serial, new AddJobOptions { Replace = true });

        for (int i = 0; i < 3; i++)
        {
            await scheduler.ScheduleJob(TriggerBuilder.Create()
                .WithIdentity(SoakTriggers.Serial + "-" + i.ToString(CultureInfo.InvariantCulture), Group)
                .ForJob(serial)
                .StartAt(started.AddSeconds(2 + i))
                .WithSimpleSchedule(x => x.RepeatForever().WithInterval(TimeSpan.FromSeconds(2)))
                .Build());
        }

        // Fails every time, so each scheduled firing costs one execution plus the policy's two
        // retries. What is being watched is that the trigger goes back on its schedule when the
        // attempts are spent rather than into the error state.
        IJobDetail failing = JobBuilder.Create<SoakFailingJob>()
            .WithIdentity(SoakJobs.Failing, Group)
            .StoreDurably()
            .Build();
        await scheduler.AddJob(failing, new AddJobOptions { Replace = true });

        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(SoakTriggers.Failing, Group)
            .ForJob(failing)
            .StartAt(started.AddSeconds(5))
            .WithRetryPolicy(RetryPolicy.Fixed(2, TimeSpan.FromSeconds(1)))
            .WithSimpleSchedule(x => x.RepeatForever().WithInterval(SoakTriggers.FailingInterval))
            .Build());

        // Carries [JobTimeout("00:00:02")] and sleeps far past it, so every firing is interrupted by
        // the middleware and reported as a failure.
        IJobDetail overrunning = JobBuilder.Create<SoakOverrunningJob>()
            .WithIdentity(SoakJobs.Overrunning, Group)
            .StoreDurably()
            .Build();
        await scheduler.AddJob(overrunning, new AddJobOptions { Replace = true });

        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(SoakTriggers.Overrunning, Group)
            .ForJob(overrunning)
            .StartAt(started.AddSeconds(5))
            .WithSimpleSchedule(x => x.RepeatForever().WithInterval(SoakTriggers.OverrunningInterval))
            .Build());

        // Its own trigger is a day out, so the only thing that can ever run it is the recovery trigger
        // the survivor builds for the killed node's interrupted firing.
        IJobDetail recoverable = JobBuilder.Create<SoakRecoverableJob>()
            .WithIdentity(SoakJobs.Recoverable, Group)
            .StoreDurably()
            .RequestRecovery()
            .Build();
        await scheduler.AddJob(recoverable, new AddJobOptions { Replace = true });

        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity(SoakTriggers.Recoverable, Group)
            .ForJob(recoverable)
            .StartAt(started.AddDays(1))
            .Build());
    }

    /// <summary>
    /// Takes a node out the way a crash does: the row saying it was mid-firing stays behind, the
    /// process stops, and its check-in goes stale. A polite shutdown cannot produce this state — it
    /// releases what it holds on the way out — which is why the row is written by hand, exactly as
    /// <c>ClusteredHardeningTestBase</c> does.
    /// </summary>
    private async Task KillNodeB(IScheduler nodeB)
    {
        await InsertFiredTrigger(
            entryId: KilledEntryId,
            triggerKey: new TriggerKey(SoakTriggers.Recoverable, Group),
            jobKey: new JobKey(SoakJobs.Recoverable, Group),
            instanceName: NodeB);

        await nodeB.Shutdown(waitForJobsToComplete: false);

        // Shutdown leaves the node's own SCHEDULER_STATE row behind — only a peer's ClusterRecover
        // deletes it — so ageing it is what turns "stopped" into "died".
        await BackdateCheckin(NodeB, TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// Writes an <c>EXECUTING</c> fired-trigger row for a node, in the shape
    /// <c>StdAdoDelegate.InsertFiredTrigger</c> writes for a running recoverable job.
    /// </summary>
    /// <remarks>
    /// A near-copy of <c>ClusteredHardeningTestBase</c>'s private helper rather than a shared one:
    /// that fixture is a sibling of this one, not an ancestor, and deriving from it to reach twenty
    /// lines would drag its four <c>[Test]</c> methods into this fixture — where the
    /// <c>LongRunning</c> category would then exclude them from the leg that is meant to run them.
    /// </remarks>
    private async Task InsertFiredTrigger(string entryId, TriggerKey triggerKey, JobKey jobKey, string instanceName)
    {
        long firedTime = DateTimeOffset.UtcNow.AddSeconds(-5).UtcTicks;

        await ExecuteNonQuery(
            "INSERT INTO QRTZ_FIRED_TRIGGERS "
            + "(SCHED_NAME, ENTRY_ID, TRIGGER_NAME, TRIGGER_GROUP, INSTANCE_NAME, FIRED_TIME, SCHED_TIME, "
            + "PRIORITY, STATE, JOB_NAME, JOB_GROUP, IS_NONCONCURRENT, REQUESTS_RECOVERY, EXECUTION_GROUP) "
            + "VALUES (@schedulerName, @entryId, @triggerName, @triggerGroup, @instanceName, @firedTime, @schedTime, "
            + "@priority, 'EXECUTING', @jobName, @jobGroup, @isNonConcurrent, @requestsRecovery, NULL)",
            ("schedulerName", SchedulerName),
            ("entryId", entryId),
            ("triggerName", triggerKey.Name),
            ("triggerGroup", triggerKey.Group),
            ("instanceName", instanceName),
            ("firedTime", firedTime),
            ("schedTime", firedTime),
            ("priority", 5),
            ("jobName", jobKey.Name),
            ("jobGroup", jobKey.Group),
            ("isNonConcurrent", false),
            ("requestsRecovery", true));
    }

    private Task<int> CountRowsFor(string table, string instanceName)
    {
        return CountRows(
            $"SELECT COUNT(*) FROM {table} WHERE SCHED_NAME = @schedulerName AND INSTANCE_NAME = @instanceName",
            ("schedulerName", SchedulerName),
            ("instanceName", instanceName));
    }

    /// <summary>
    /// What a cluster that has finished its work leaves in the tables: nothing in flight, and nothing
    /// reserved.
    /// </summary>
    /// <remarks>
    /// Polled rather than asserted outright. <c>Shutdown</c> returns once the scheduler thread has
    /// stopped and the jobs have finished, but the last <c>TriggeredJobComplete</c> writes land on
    /// their own connections, so an instant assertion here would be racing the teardown it is meant to
    /// be checking.
    /// </remarks>
    private async Task AssertNothingLeftBehind()
    {
        await WaitForCondition(
            async () => await CountRows(
                "SELECT COUNT(*) FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_STATE IN ('ACQUIRED', 'BLOCKED')",
                ("schedulerName", SchedulerName)) == 0,
            timeoutMs: 60_000,
            async () =>
                "every trigger to be released. An ACQUIRED row is one a node reserved and never fired, and a "
                + "BLOCKED row is a serial job's sibling that was never unblocked — both are triggers that will "
                + $"never fire again without a recovery. State:\n{await DumpDatabaseState()}");

        await WaitForCondition(
            async () => await CountRows(
                "SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName",
                ("schedulerName", SchedulerName)) == 0,
            timeoutMs: 60_000,
            async () =>
                "QRTZ_FIRED_TRIGGERS to drain. A row left here makes a finished firing look in-flight to the "
                + $"execution ceiling, to QueryFireInstances and to the next node that recovers this one. State:\n{await DumpDatabaseState()}");

        int killedRows = await CountRows(
            "SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName AND ENTRY_ID = @entryId",
            ("schedulerName", SchedulerName),
            ("entryId", KilledEntryId));

        killedRows.Should().Be(0,
            "the survivor deletes the row when it takes the killed node's firing over; leaving it makes the "
            + "corpse look busy and re-recovered on every subsequent check-in");
    }

    /// <summary>
    /// That every family fired, roughly as often as its schedule says, and that the retry and timeout
    /// paths produced what they promise.
    /// </summary>
    /// <remarks>
    /// The band is wide on purpose. Two nodes share the work, thirty seconds of the run are spent in
    /// standby, a node is killed and replaced, and misfire recovery decides for itself whether to
    /// catch up — so the count a schedule implies is an order of magnitude, not a number. What a
    /// tighter bound would buy is flakiness; what this catches is the failure that matters, which is a
    /// family that stopped firing.
    /// </remarks>
    private void AssertWorkloadRan(TimeSpan duration)
    {
        foreach ((string trigger, TimeSpan interval) in SoakTriggers.CountedFamilies)
        {
            int fired = SoakRecorder.FiringsFor(trigger);
            double expected = duration.TotalSeconds / interval.TotalSeconds;

            fired.Should().BeGreaterThan(0,
                "'{0}' is a whole trigger family; zero firings means that family never ran at all", trigger);

            fired.Should().BeInRange((int) (expected * 0.3), (int) (expected * 2.0) + 5,
                "'{0}' fires every {1} over a {2} run, so roughly {3:F0} times — a count far outside that is "
                + "either a family that stalled or one that ran away", trigger, interval, duration, expected);
        }

        SoakRecorder.SerialFirings.Should().BeGreaterThan(0,
            "the serial job is what the overlap detector watches; it has to have run for the check to mean anything");

        SoakRecorder.FailingAttempts.Should().BeGreaterThan(0, "the retry policy needs a failure to act on");

        SoakRecorder.FailingRetryAttempts.Should().BeGreaterThan(0,
            "RetryPolicy.Fixed(2, 1s) re-fires a failed trigger twice more before letting the schedule take "
            + "it back, and a re-fire carries a non-zero RetryAttempt. None at all means the policy was "
            + "stored and never consulted");

        SoakRecorder.TimedOutFirings.Should().BeGreaterThan(0,
            "the overrunning job sleeps far past the two-second budget [JobTimeout] gives it, so AddJobTimeout "
            + "has to have interrupted it and reported the overrun as a failure");

        SoakRecorder.RecoveredFirings.Should().ContainSingle(
            "the killed node left exactly one interrupted firing behind, and recovery replays it once — a "
            + "second replay is one interrupted firing becoming two jobs")
            .Which.Should().Be(NodeA, "the survivor is the only node that could have recovered it");
    }

    /// <summary>
    /// The property N2b §4(b) records as settled by mechanism and never shown end to end: two live
    /// nodes, one <see cref="DisallowConcurrentExecutionAttribute" /> job, no two firings of it at
    /// once.
    /// </summary>
    private static void AssertNoOverlap()
    {
        SoakRecorder.Overlaps.Should().BeEmpty(
            "[DisallowConcurrentExecution] means one firing of this job at a time across the whole cluster: "
            + "acquisition takes only WAITING rows, firing one of a job's triggers moves its siblings to "
            + "BLOCKED, and the BLOCKED row is in the shared database where the other node reads it. An entry "
            + "here is two firings that overlapped, which is that chain broken somewhere");

        SoakRecorder.MaxSerialConcurrency.Should().Be(1,
            "the serial job's peak observed concurrency is the same statement counted rather than listed");
    }

    /// <summary>
    /// Nothing failed that was not meant to fail, and nothing failed where nobody was looking.
    /// </summary>
    private static void AssertNoUnobservedFailures()
    {
        SoakRecorder.UnexpectedJobFailures.Should().BeEmpty(
            "only the failing job and the overrunning one are supposed to throw; anything else that did is a "
            + "job the scheduler could not run");

        SoakRecorder.SchedulerErrors.Should().BeEmpty(
            "a SchedulerError that does not name one of the two jobs this soak asks to fail is the scheduler "
            + "saying it could not do its own work — instantiate a job, read the store, notify a listener. A "
            + "run that produced one has found something");

        // Finalizers are what raise UnobservedTaskException, so a run that never collected would report
        // clean whatever it dropped.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        SoakRecorder.UnobservedTaskExceptions.Should().BeEmpty(
            "a faulted task nobody awaited is a failure the scheduler swallowed rather than reported");
    }

    /// <summary>
    /// That half an hour of firing did not accumulate anything: the live heap after a full collection,
    /// and the process's handle count, are where they were once the run had settled.
    /// </summary>
    /// <remarks>
    /// The baseline is the second sample rather than the first. The first is taken while the pools,
    /// the connection pool and the JIT are still filling, so a run measured against it would be
    /// measuring startup. The bounds are generous — a factor and an absolute floor — because the
    /// failure this is for is a leak, which is unbounded growth rather than a percentage.
    /// </remarks>
    private static void AssertResourcesFlat(IReadOnlyList<ResourceSample> samples)
    {
        samples.Count.Should().BeGreaterThanOrEqualTo(3,
            "a trend needs a series; fewer samples than this says the run was too short to have taken any");

        ResourceSample baseline = samples[1];
        ResourceSample last = samples[^1];

        last.HeapBytes.Should().BeLessThan(Math.Max(baseline.HeapBytes * 3, baseline.HeapBytes + 64L * 1024 * 1024),
            "the heap is measured after a full blocking collection, so what it holds is live. Growing it "
            + "threefold over a run whose workload never changes is something the run is keeping hold of");

        last.HandleCount.Should().BeLessThan(Math.Max(baseline.HandleCount * 3, baseline.HandleCount + 500),
            "handles are connections, timers, events and files. A cluster doing the same work at the end as "
            + "at the start should need the same number of them");
    }

    /// <summary>
    /// Wires a node's scheduler listener up, so that anything the scheduler could not do is recorded
    /// rather than left in a log nobody reads.
    /// </summary>
    private static void AttachRecorder(IScheduler scheduler)
    {
        scheduler.ListenerManager.AddSchedulerListener(new SoakSchedulerListener());
        scheduler.ListenerManager.AddJobListener(new SoakJobListener());
    }

    private static void Note(List<string> timeline, DateTimeOffset at, string what)
    {
        timeline.Add(string.Create(CultureInfo.InvariantCulture, $"{at:HH:mm:ss} {what}"));
    }

    /// <summary>
    /// What the run did and what it cost, printed whether or not the assertions pass — a soak that
    /// merely says "passed" has thrown away most of what it was run for.
    /// </summary>
    private static string Report(IReadOnlyList<string> timeline, IReadOnlyList<ResourceSample> samples, TimeSpan duration)
    {
        StringBuilder report = new();
        report.AppendLine(CultureInfo.InvariantCulture, $"Soak over {duration}:");

        foreach (string entry in timeline)
        {
            report.AppendLine("  " + entry);
        }

        report.AppendLine("Firings:");
        foreach ((string trigger, int count) in SoakRecorder.FiringCounts())
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"  {trigger,-28} {count}");
        }

        report.AppendLine(CultureInfo.InvariantCulture,
            $"  {"serial (max concurrent)",-28} {SoakRecorder.SerialFirings} ({SoakRecorder.MaxSerialConcurrency})");
        report.AppendLine(CultureInfo.InvariantCulture,
            $"  {"failing (attempts)",-28} {SoakRecorder.FailingAttempts}, of which {SoakRecorder.FailingRetryAttempts} were retries");
        report.AppendLine(CultureInfo.InvariantCulture, $"  {"timed out",-28} {SoakRecorder.TimedOutFirings}");
        report.AppendLine(CultureInfo.InvariantCulture, $"  {"recovered",-28} {SoakRecorder.RecoveredFirings.Count}");

        report.AppendLine("Resources:");
        foreach (ResourceSample sample in samples)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  t+{sample.Elapsed.TotalMinutes,6:F1} min  heap {sample.HeapBytes / (1024 * 1024),5} MB  handles {sample.HandleCount,6}  threads {sample.ThreadCount,4}");
        }

        return report.ToString();
    }

    /// <summary>
    /// The job names, and which of them are supposed to fail. Two of the workload's jobs throw by
    /// design, and both the job listener and the scheduler listener have to be able to tell them from
    /// a job that failed because something is wrong.
    /// </summary>
    private static class SoakJobs
    {
        public const string Counting = "countingJob";
        public const string Serial = "serialJob";
        public const string Failing = "failingJob";
        public const string Overrunning = "overrunningJob";
        public const string Recoverable = "recoverableJob";

        public static bool MayFail(string jobName) => jobName is Failing or Overrunning;
    }

    /// <summary>The trigger names and intervals the workload is made of, in one place.</summary>
    private static class SoakTriggers
    {
        public const string Simple = "family-simple";
        public const string Cron = "family-cron";
        public const string DailyTimeInterval = "family-dailyTimeInterval";
        public const string CalendarInterval = "family-calendarInterval";
        public const string Recurrence = "family-recurrence";
        public const string Serial = "serial";
        public const string Failing = "failing";
        public const string Overrunning = "overrunning";
        public const string Recoverable = "recoverable";

        public static readonly TimeSpan SimpleInterval = TimeSpan.FromSeconds(2);
        public static readonly TimeSpan FailingInterval = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan OverrunningInterval = TimeSpan.FromSeconds(10);

        /// <summary>
        /// The five trigger families, with the interval each one's schedule implies. Every one is a
        /// different implementation of "when does this fire next", and a soak that ran only simple
        /// triggers would exercise one of them.
        /// </summary>
        public static readonly (string Trigger, TimeSpan Interval)[] CountedFamilies =
        [
            (Simple, SimpleInterval),
            (Cron, TimeSpan.FromSeconds(5)),
            (DailyTimeInterval, TimeSpan.FromSeconds(3)),
            (CalendarInterval, TimeSpan.FromSeconds(4)),
            (Recurrence, TimeSpan.FromSeconds(6)),
        ];
    }

    /// <summary>
    /// One reading of what the process is holding. The heap is taken after a full blocking collection,
    /// so it is the live set rather than whatever had not been collected yet.
    /// </summary>
    private sealed record ResourceSample(TimeSpan Elapsed, long HeapBytes, int HandleCount, int ThreadCount)
    {
        public static ResourceSample Take(TimeSpan elapsed)
        {
            long heap = GC.GetTotalMemory(forceFullCollection: true);
            using Process process = Process.GetCurrentProcess();
            process.Refresh();
            return new ResourceSample(elapsed, heap, process.HandleCount, process.Threads.Count);
        }
    }

    /// <summary>
    /// Everything the run observed, in statics because both nodes are in this process and the jobs are
    /// constructed by the scheduler rather than by the test.
    /// </summary>
    private static class SoakRecorder
    {
        private static readonly ConcurrentDictionary<string, int> firings = new();

        private static int serialFirings;
        private static int serialConcurrency;
        private static int maxSerialConcurrency;
        private static int failingAttempts;
        private static int failingRetryAttempts;
        private static int timedOutFirings;

        public static ConcurrentQueue<string> Overlaps { get; private set; } = new();

        public static ConcurrentQueue<string> RecoveredFirings { get; private set; } = new();

        public static ConcurrentQueue<string> SchedulerErrors { get; private set; } = new();

        public static ConcurrentQueue<string> UnexpectedJobFailures { get; private set; } = new();

        public static ConcurrentQueue<string> UnobservedTaskExceptions { get; private set; } = new();

        public static int SerialFirings => serialFirings;

        public static int MaxSerialConcurrency => maxSerialConcurrency;

        public static int FailingAttempts => failingAttempts;

        /// <summary>
        /// How many of those attempts were re-fires the policy asked for rather than occurrences the
        /// schedule produced, told apart by the trigger's own <c>RetryAttempt</c> — zero on the
        /// occurrence, one and then two on the retries, because <c>TryScheduleRetry</c> increments it
        /// as it moves the fire time.
        /// </summary>
        public static int FailingRetryAttempts => failingRetryAttempts;

        public static int TimedOutFirings => timedOutFirings;

        public static void Reset()
        {
            firings.Clear();
            Overlaps = new ConcurrentQueue<string>();
            RecoveredFirings = new ConcurrentQueue<string>();
            SchedulerErrors = new ConcurrentQueue<string>();
            UnexpectedJobFailures = new ConcurrentQueue<string>();
            UnobservedTaskExceptions = new ConcurrentQueue<string>();
            Interlocked.Exchange(ref serialFirings, 0);
            Interlocked.Exchange(ref serialConcurrency, 0);
            Interlocked.Exchange(ref maxSerialConcurrency, 0);
            Interlocked.Exchange(ref failingAttempts, 0);
            Interlocked.Exchange(ref failingRetryAttempts, 0);
            Interlocked.Exchange(ref timedOutFirings, 0);

            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            UnobservedTaskExceptions.Enqueue(e.Exception?.ToString() ?? "<null>");
            e.SetObserved();
        }

        public static void RecordFiring(string triggerName)
        {
            firings.AddOrUpdate(triggerName, 1, static (_, count) => count + 1);
        }

        public static int FiringsFor(string triggerName) => firings.TryGetValue(triggerName, out int count) ? count : 0;

        public static IEnumerable<(string Trigger, int Count)> FiringCounts()
        {
            return firings.Select(x => (x.Key, x.Value)).OrderBy(x => x.Key, StringComparer.Ordinal);
        }

        /// <summary>
        /// Enters the serial job, and records the peak number of firings of it that were ever in
        /// flight together. Anything above one is the property under test broken.
        /// </summary>
        public static void EnterSerial(string instanceId, string fireInstanceId)
        {
            Interlocked.Increment(ref serialFirings);
            int inFlight = Interlocked.Increment(ref serialConcurrency);

            int observed = Volatile.Read(ref maxSerialConcurrency);
            while (inFlight > observed)
            {
                int previous = Interlocked.CompareExchange(ref maxSerialConcurrency, inFlight, observed);
                if (previous == observed)
                {
                    break;
                }

                observed = previous;
            }

            if (inFlight > 1)
            {
                Overlaps.Enqueue(string.Create(CultureInfo.InvariantCulture,
                    $"{inFlight} concurrent firings of the serial job; this one on '{instanceId}' as {fireInstanceId}"));
            }
        }

        public static void ExitSerial() => Interlocked.Decrement(ref serialConcurrency);

        public static void RecordFailingAttempt(bool isRetry)
        {
            Interlocked.Increment(ref failingAttempts);
            if (isRetry)
            {
                Interlocked.Increment(ref failingRetryAttempts);
            }
        }

        public static void RecordRecovery(string instanceId) => RecoveredFirings.Enqueue(instanceId);

        /// <summary>
        /// Records a <c>SchedulerError</c> that is not one of the two jobs this soak asks to fail.
        /// </summary>
        /// <remarks>
        /// A job that throws is reported through <em>both</em> channels — <c>JobWasExecuted</c> gets
        /// the <see cref="JobExecutionException" /> and scheduler listeners get a
        /// <c>SchedulerError</c> naming the same firing (<c>JobRunShell</c>, the
        /// <c>JobExecutionProcessException</c> path). That is by design: a listener that only watches
        /// the scheduler should still learn that a job blew up. So "the scheduler could not do its own
        /// work" has to be read as "an error for a job that was not supposed to fail", which is what
        /// the key check below makes it.
        /// </remarks>
        public static void RecordSchedulerError(SchedulerErrorContext errorContext)
        {
            if (errorContext.JobKey is not null && SoakJobs.MayFail(errorContext.JobKey.Name))
            {
                return;
            }

            SchedulerErrors.Enqueue($"{errorContext.Message}: {errorContext.Exception}");
        }

        public static void RecordJobOutcome(IJobExecutionContext context, JobExecutionException exception)
        {
            if (exception is null)
            {
                return;
            }

            string jobName = context.JobDetail.Key.Name;
            if (jobName == SoakJobs.Failing)
            {
                return;
            }

            if (jobName == SoakJobs.Overrunning)
            {
                Interlocked.Increment(ref timedOutFirings);
                return;
            }

            UnexpectedJobFailures.Enqueue(string.Create(CultureInfo.InvariantCulture,
                $"{context.JobDetail.Key} through {context.Trigger.Key}: {exception.Message}"));
        }
    }

    /// <summary>Counts a firing per trigger, which is how each family is shown to have kept running.</summary>
    public sealed class SoakCountingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            SoakRecorder.RecordFiring(context.Trigger.Key.Name);
            return default;
        }
    }

    /// <summary>
    /// The job the overlap detector watches. It holds its slot long enough that a second firing would
    /// have to overlap it rather than merely follow it.
    /// </summary>
    [DisallowConcurrentExecution]
    public sealed class SoakSerialJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            SoakRecorder.EnterSerial(context.Scheduler.SchedulerInstanceId, context.FireInstanceId);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                SoakRecorder.ExitSerial();
            }
        }
    }

    /// <summary>Fails every time, so the trigger's retry policy has something to do.</summary>
    public sealed class SoakFailingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            SoakRecorder.RecordFailingAttempt(context.Trigger.RetryAttempt > 0);
            throw new InvalidOperationException("The soak's failing job fails on purpose, so the retry policy has something to retry.");
        }
    }

    /// <summary>
    /// Runs far past the budget it declares, so <c>AddJobTimeout</c> has to interrupt it. It does not
    /// swallow the cancellation: what is under test is the ordinary path, where the job honours the
    /// token it was handed.
    /// </summary>
    [JobTimeout("00:00:02")]
    public sealed class SoakOverrunningJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Only ever reached through a recovery trigger: its own trigger is a day out, so a firing of this
    /// job is proof that the survivor replayed the killed node's work.
    /// </summary>
    public sealed class SoakRecoverableJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            SoakRecorder.RecordRecovery(context.Scheduler.SchedulerInstanceId);
            return default;
        }
    }

    /// <summary>Records what the scheduler could not do.</summary>
    private sealed class SoakSchedulerListener : ISchedulerListener
    {
        public ValueTask SchedulerError(IScheduler scheduler, SchedulerErrorContext errorContext, CancellationToken cancellationToken = default)
        {
            SoakRecorder.RecordSchedulerError(errorContext);
            return default;
        }
    }

    /// <summary>Records which firings failed, and separates the ones that are supposed to.</summary>
    private sealed class SoakJobListener : IJobListener
    {
        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException, CancellationToken cancellationToken = default)
        {
            SoakRecorder.RecordJobOutcome(context, jobException);
            return default;
        }
    }
}
