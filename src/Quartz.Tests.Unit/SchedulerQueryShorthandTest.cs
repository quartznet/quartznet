using Quartz.Extensibility;
using Quartz.Jobs;

namespace Quartz.Tests.Unit;

/// <summary>
/// The <c>Query*</c> shorthands on <see cref="SchedulerQueryExtensions" />: each one is the query
/// member called with a query record the caller did not have to name.
/// </summary>
/// <remarks>
/// What is worth pinning is not that a shorthand compiles but that it keeps the member's contract —
/// the same page size, the same filters, and in the reset companion's case the same effect on a
/// trigger. A shorthand that quietly became unbounded, or that reset a trigger some other way, would
/// be a different feature wearing a convenient name.
/// </remarks>
[NonParallelizable]
public class SchedulerQueryShorthandTest
{
    private IScheduler scheduler = null!;

    [SetUp]
    public async Task SetUp()
    {
        scheduler = await QuartzSchedulerBuilder.Create().BuildScheduler();
    }

    [TearDown]
    public async Task TearDown()
    {
        await scheduler.Shutdown(waitForJobsToComplete: false);
    }

    [Test]
    public async Task TheJobAndTriggerShorthandsListWhatTheQueryRecordWouldHave()
    {
        await Schedule("alpha", "j1", "t1");
        await Schedule("beta", "j2", "t2");

        PagedResult<JobHeader> jobs = await scheduler.QueryJobs();
        PagedResult<JobHeader> spelledOut = await scheduler.QueryJobs(new JobQuery());

        jobs.Items.Select(x => x.Key).Should().Equal(spelledOut.Items.Select(x => x.Key),
            "the shorthand is the member with a query record that filters nothing");

        PagedResult<TriggerHeader> triggers = await scheduler.QueryTriggers();

        triggers.Items.Select(x => x.Key).Should().Equal(
            [new TriggerKey("t1", "alpha"), new TriggerKey("t2", "beta")]);
    }

    /// <summary>
    /// The shorthand takes the query record's default page size, not everything. A shorthand that
    /// materialized the whole store would be the trap <see cref="PagedQuery.DefaultTake" /> exists to
    /// close, and it would be invisible until the store was big.
    /// </summary>
    [Test]
    public async Task TheShorthandsPageLikeTheMemberTheyCall()
    {
        for (int i = 0; i <= PagedQuery.DefaultTake; i++)
        {
            await scheduler.AddJob(JobBuilder.Create<NoOpJob>()
                .WithIdentity($"job-{i:D4}", "bulk")
                .StoreDurably()
                .Build());
        }

        PagedResult<JobHeader> page = await scheduler.QueryJobs();

        page.Items.Should().HaveCount(PagedQuery.DefaultTake);
        page.HasMore.Should().BeTrue("the shorthand pages exactly as the member does");
        page.TotalCount.Should().BeNull("a total count costs a second query and stays opt-in");
    }

    [Test]
    public async Task TheFireInstanceShorthandsListWhatIsRunning()
    {
        GatedJob.Reset();

        JobKey running = new("running", "gated");
        await scheduler.ScheduleJob(
            JobBuilder.Create<GatedJob>().WithIdentity(running).Build(),
            TriggerBuilder.Create().WithIdentity("now", "gated").StartNow().Build());

        await scheduler.AddJob(JobBuilder.Create<NoOpJob>().WithIdentity("idle", "gated").StoreDurably().Build());

        await scheduler.Start();
        GatedJob.Started.Wait(TimeSpan.FromSeconds(30)).Should().BeTrue("the assertions are about a firing that exists");

        try
        {
            PagedResult<FireInstance> everything = await scheduler.QueryFireInstances();
            everything.Items.Should().ContainSingle()
                .Which.JobKey.Should().Be(running, "the query record's own default state is Executing");

            PagedResult<FireInstance> ofRunning = await scheduler.QueryFireInstancesOfJob(running);
            ofRunning.Items.Should().ContainSingle().Which.JobKey.Should().Be(running);

            PagedResult<FireInstance> ofIdle = await scheduler.QueryFireInstancesOfJob(new JobKey("idle", "gated"));
            ofIdle.Items.Should().BeEmpty("a job that is not running has no firing to list");
        }
        finally
        {
            GatedJob.Release.Set();
        }
    }

    [Test]
    public async Task TriggersInErrorAreListedAndResetByGroup()
    {
        IScheduler failing = await QuartzSchedulerBuilder
            .Create(q => q.UseJobFactory(new ThrowingJobFactory()))
            .BuildScheduler();

        try
        {
            await ScheduleFailing(failing, "alpha", "t1");
            await ScheduleFailing(failing, "alpha", "t2");
            await ScheduleFailing(failing, "beta", "t3");

            await failing.Start();
            await WaitUntilInError(failing, expected: 3);

            PagedResult<TriggerHeader> failed = await failing.QueryTriggersInError();
            failed.Items.Select(x => x.Key).Should().Equal(
                [new TriggerKey("t1", "alpha"), new TriggerKey("t2", "alpha"), new TriggerKey("t3", "beta")],
                "the shorthand is the trigger query with the error state filter");

            await failing.Standby();

            List<TriggerKey> reset = await failing.ResetTriggersFromErrorState(GroupMatcher<TriggerKey>.GroupEquals("alpha"));

            reset.Should().BeEquivalentTo([new TriggerKey("t1", "alpha"), new TriggerKey("t2", "alpha")],
                "the companion names the set by group and resets exactly that set");

            (await failing.GetTriggerState(new TriggerKey("t1", "alpha"))).Should().Be(TriggerState.Normal);
            (await failing.GetTriggerState(new TriggerKey("t2", "alpha"))).Should().Be(TriggerState.Normal);
            (await failing.GetTriggerState(new TriggerKey("t3", "beta"))).Should().Be(TriggerState.Error,
                "a group the matcher did not name is not touched");
        }
        finally
        {
            await failing.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task ResettingAGroupWithNothingInErrorAnswersEmpty()
    {
        await Schedule("alpha", "j1", "t1");

        List<TriggerKey> reset = await scheduler.ResetTriggersFromErrorState(GroupMatcher<TriggerKey>.AnyGroup());

        reset.Should().BeEmpty("a healthy trigger is not in the set the companion names");
        (await scheduler.GetTriggerState(new TriggerKey("t1", "alpha"))).Should().Be(TriggerState.Normal);
    }

    [Test]
    public async Task ANullMatcherIsRejectedRatherThanTreatedAsEveryGroup()
    {
        Func<Task> reset = async () => await scheduler.ResetTriggersFromErrorState((GroupMatcher<TriggerKey>) null!);

        await reset.Should().ThrowAsync<ArgumentNullException>(
            "silently widening a reset to every group is the mistake this argument can make");
    }

    private async Task Schedule(string group, string jobName, string triggerName)
    {
        await scheduler.ScheduleJob(
            JobBuilder.Create<NoOpJob>().WithIdentity(jobName, group).Build(),
            TriggerBuilder.Create()
                .WithIdentity(triggerName, group)
                .StartAt(DateTimeOffset.UtcNow.AddDays(1))
                .Build());
    }

    private static async Task ScheduleFailing(IScheduler target, string group, string triggerName)
    {
        await target.ScheduleJob(
            JobBuilder.Create<NoOpJob>().WithIdentity("job-" + triggerName, group).Build(),
            TriggerBuilder.Create().WithIdentity(triggerName, group).StartNow().Build());
    }

    /// <summary>
    /// The job factory throws for every firing, which moves each trigger to
    /// <see cref="TriggerState.Error" /> — but on the scheduler's own thread, so the test waits for the
    /// state rather than assuming it has already been reached.
    /// </summary>
    private static async Task WaitUntilInError(IScheduler target, int expected)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            PagedResult<TriggerHeader> failed = await target.QueryTriggersInError();
            if (failed.Items.Count >= expected)
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail($"only {(await target.QueryTriggersInError()).Items.Count} of {expected} triggers reached the error state");
    }

    private sealed class ThrowingJobFactory : IJobFactory
    {
        public ValueTask<JobScope> CreateJob(TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("no job for you");
        }

        public ValueTask ReturnJob(JobScope scope, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// Runs until the test lets it go, so that a firing is observably in flight while the fire instance
    /// listing is read.
    /// </summary>
    public sealed class GatedJob : IJob
    {
        public static readonly ManualResetEventSlim Started = new(false);
        public static readonly ManualResetEventSlim Release = new(false);

        public static void Reset()
        {
            Started.Reset();
            Release.Reset();
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Started.Set();
            Release.Wait(TimeSpan.FromSeconds(30));
            return default;
        }
    }
}
