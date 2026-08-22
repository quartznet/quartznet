namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Covers the scenario behind #1416: one process schedules and observes triggers but never executes
/// jobs, while another process executes them, the two sharing only the database. The observing process
/// has to be able to tell that a trigger's job is running — both as a trigger state and, since #3205, as
/// the list of firings themselves.
/// Runs against the assembly-wide PostgreSQL database (see ClusteredPostgresTestBase).
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public sealed class ExecutingTriggerStateClusteredPostgresTest : ClusteredPostgresTestBase
{
    protected override string SchedulerName => "ExecutingStateClusterTest";

    private static SemaphoreSlim jobStarted = new(0);
    private static SemaphoreSlim jobCanFinish = new(0);
    private static volatile bool finishedOnRelease;

    [SetUp]
    public void ResetSignals()
    {
        jobStarted = new SemaphoreSlim(0);
        jobCanFinish = new SemaphoreSlim(0);
        finishedOnRelease = false;
    }

    [TearDown]
    public void DisposeSignals()
    {
        jobStarted.Dispose();
        jobCanFinish.Dispose();
    }

    /// <summary>
    /// A repeating trigger, so that while the job runs the trigger row is WAITING or ACQUIRED rather
    /// than COMPLETE — the path the single-fire smoke test does not exercise. The job allows concurrent
    /// execution, so the trigger is never BLOCKED either: without consulting FIRED_TRIGGERS there would
    /// be nothing at all to distinguish it from an idle trigger.
    /// </summary>
    [Test]
    public async Task GetTriggerState_ReportsExecuting_OnNodeThatIsNotRunningTheJob()
    {
        IScheduler executingNode = await CreateScheduler("executing-node");
        IScheduler observingNode = await CreateScheduler("observing-node");

        var triggerKey = new TriggerKey("executingStateTrigger", "clusteredTest");

        try
        {
            // Only this node is started, so only it can acquire and fire triggers.
            await executingNode.Start();

            IJobDetail job = JobBuilder.Create<BlockingJob>()
                .WithIdentity("executingStateJob", "clusteredTest")
                .StoreDurably()
                .Build();
            await executingNode.AddJob(job, new AddJobOptions { Replace = true });

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(job)
                // Comfortably longer than every timeout below, so a slow run cannot start a second
                // execution while the first is still parked waiting for its one permit.
                .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                .StartNow()
                // Persisted onto the fired-trigger row, so the listing can report it from another node.
                .WithExecutionGroup("clusteredExecutions")
                .Build();
            await executingNode.ScheduleJob(trigger);

            (await jobStarted.WaitAsync(TimeSpan.FromSeconds(30)))
                .Should().BeTrue("the job should have started on the executing node");

            // The observing node was never started and never ran anything, so this answer can only have
            // come from the shared database.
            TriggerState observed = await observingNode.GetTriggerState(triggerKey);

            // Captured only when it is about to be needed, and before anything else can move the state on.
            string diagnostics = observed == TriggerState.Executing ? "" : await DumpDatabaseState();

            observed.Should().Be(TriggerState.Executing,
                "the observing node should see the trigger as executing. State:\n{0}", diagnostics);

            // The firing itself, seen from a node that is not running it. This is the half #1416 could
            // only work around with a trigger state: the observing node holds no context for this
            // execution and never will, yet the store can still say what is running, where, and since when.
            PagedResult<FireInstance> firings = await observingNode.QueryFireInstances(new FireInstanceQuery());
            FireInstance firing = firings.Items.Should().ContainSingle(x => x.TriggerKey.Equals(triggerKey),
                "the observing node should see the remote firing. State:\n{0}", diagnostics).Subject;

            firing.State.Should().Be(FireInstanceState.Executing);
            firing.JobKey.Should().Be(job.Key, "an executing firing knows its job");
            firing.SchedulerInstanceId.Should().Be(executingNode.SchedulerInstanceId,
                "the firing is owned by the node that is running it, not by the one that listed it");
            firing.ExecutionGroup.Should().Be("clusteredExecutions",
                "the execution group is written with the fired-trigger row and read back from it");
            firing.FireInstanceId.Should().NotBeNullOrEmpty();

            (await observingNode.QueryFireInstances(new FireInstanceQuery { SchedulerInstanceId = "no-such-node" }))
                .Items.Should().BeEmpty("filtering by another node's id excludes this firing");

            (await observingNode.QueryFireInstances(new FireInstanceQuery { State = FireInstanceState.Acquired }))
                .Items.Should().NotContain(x => x.FireInstanceId == firing.FireInstanceId,
                    "a firing that is running is no longer merely acquired");

            // The listing is the scenario the dashboard actually uses, and it is the only place the
            // per-row executing projection and the EXISTS filter run against a real database with a
            // fired-trigger row present.
            PagedResult<TriggerHeader> executing = await observingNode.QueryTriggers(new TriggerQuery { State = TriggerState.Executing });
            executing.Items.Select(x => x.Key).Should().Contain(triggerKey,
                "a listing filtered by Executing should return the running trigger");
            executing.Items.Single(x => x.Key.Equals(triggerKey)).State.Should().Be(TriggerState.Executing,
                "the listing should report the same state GetTriggerState does");

            PagedResult<TriggerHeader> normal = await observingNode.QueryTriggers(new TriggerQuery { State = TriggerState.Normal });
            normal.Items.Select(x => x.Key).Should().NotContain(triggerKey,
                "filtering by Normal must not return a trigger the same listing reports as Executing");

            jobCanFinish.Release();

            await WaitForCondition(
                async () => await observingNode.GetTriggerState(triggerKey) == TriggerState.Normal,
                timeoutMs: 30000,
                "observing node should see the trigger return to normal once the job finishes");

            finishedOnRelease.Should().BeTrue(
                "the job must have finished because the test released it, not because its wait timed out");
        }
        finally
        {
            // Release first, so a failed assertion cannot leave the job thread parked on shutdown.
            jobCanFinish.Release();
            await executingNode.Shutdown(waitForJobsToComplete: true);
            await observingNode.Shutdown();
        }
    }

    /// <summary>
    /// The self/sibling split, against a real database. The ADO store writes BLOCKED to TRIGGER_STATE for
    /// <em>every</em> trigger of a non-concurrent job, including the one that fired, so the only thing
    /// telling them apart is the fired-trigger row — which makes this the case the whole `Blocked`
    /// narrowing rests on, and the one a faked delegate cannot exercise.
    /// </summary>
    [Test]
    public async Task GetTriggerState_ReportsExecutingForTheFiringTrigger_AndBlockedForItsSibling()
    {
        IScheduler scheduler = await CreateScheduler("nonconcurrent-node");

        var firstKey = new TriggerKey("nonConcurrentTriggerOne", "clusteredTest");
        var secondKey = new TriggerKey("nonConcurrentTriggerTwo", "clusteredTest");

        try
        {
            await scheduler.Start();

            IJobDetail job = JobBuilder.Create<NonConcurrentBlockingJob>()
                .WithIdentity("nonConcurrentJob", "clusteredTest")
                .StoreDurably()
                .Build();
            await scheduler.AddJob(job, new AddJobOptions { Replace = true });

            foreach (TriggerKey key in (TriggerKey[]) [firstKey, secondKey])
            {
                await scheduler.ScheduleJob(TriggerBuilder.Create()
                    .WithIdentity(key)
                    .ForJob(job)
                    .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
                    .StartNow()
                    .Build());
            }

            (await jobStarted.WaitAsync(TimeSpan.FromSeconds(30)))
                .Should().BeTrue("the job should have started");

            TriggerState firstState = await scheduler.GetTriggerState(firstKey);
            TriggerState secondState = await scheduler.GetTriggerState(secondKey);

            string diagnostics = await DumpDatabaseState();

            // Whichever won the race is the one running; the other is gated behind it.
            TriggerState[] states = [firstState, secondState];
            states.Should().BeEquivalentTo([TriggerState.Executing, TriggerState.Blocked],
                "exactly one trigger is running the job and the other is blocked by it. State:\n{0}", diagnostics);

            // Two permits: completing the first execution unblocks the sibling, which is itself overdue
            // and fires straight away, so both have to be let through before either trigger settles.
            jobCanFinish.Release(2);

            await WaitForCondition(
                async () => await scheduler.GetTriggerState(firstKey) == TriggerState.Normal
                            && await scheduler.GetTriggerState(secondKey) == TriggerState.Normal,
                timeoutMs: 30000,
                "both triggers should return to normal once the job finishes");
        }
        finally
        {
            jobCanFinish.Release();
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    /// <summary>
    /// Allows concurrent execution, and blocks until the test releases it.
    /// </summary>
    private sealed class BlockingJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            jobStarted.Release();

            // Recorded rather than asserted: an exception thrown here is caught by JobRunShell and never
            // reaches the test runner. The test body checks it, so a job that finished on the timeout
            // instead of on the test's release cannot make the return-to-normal assertion pass for the
            // wrong reason.
            finishedOnRelease = await jobCanFinish.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
        }
    }

    /// <summary>
    /// The same, but disallowing concurrent execution, so its triggers gate each other.
    /// </summary>
    [DisallowConcurrentExecution]
    private sealed class NonConcurrentBlockingJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            jobStarted.Release();
            finishedOnRelease = await jobCanFinish.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
        }
    }
}
