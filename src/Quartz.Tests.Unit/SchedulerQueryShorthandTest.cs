using Quartz.Extensibility;
using Quartz.Jobs;

namespace Quartz.Tests.Unit;

/// <summary>
/// <see cref="SchedulerQueryExtensions.QueryTriggersInError" />, the one <c>Query*</c> shorthand
/// that is a preset rather than a synonym, and
/// <see cref="IScheduler.ResetTriggersFromErrorState(GroupMatcher{TriggerKey}, CancellationToken)" />,
/// which names the same set and acts on it.
/// </summary>
/// <remarks>
/// What is worth pinning is not that either compiles but that they keep the member's contract — the
/// same page size, the same filter, and the same effect on a trigger as resetting it by key. A
/// preset that quietly became unbounded, or that reset a trigger some other way, would be a
/// different feature wearing a convenient name.
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

    /// <summary>
    /// The preset is the member called with one filter set and nothing else — in particular it takes
    /// the query record's own page size rather than asking for everything, which is the trap
    /// <see cref="PagedQuery.DefaultTake" /> exists to close and would be invisible until the store
    /// was big.
    /// </summary>
    [Test]
    public async Task TheErrorPresetIsTheMemberWithTheOneFilterItKnows()
    {
        await Schedule("alpha", "j1", "t1");
        await Schedule("beta", "j2", "t2");

        PagedResult<TriggerHeader> page = await scheduler.QueryTriggersInError();

        page.Items.Should().BeEmpty(
            "the two scheduled triggers are healthy, so the state filter reached the store rather than "
            + "the preset listing everything");
        page.HasMore.Should().BeFalse();
        page.TotalCount.Should().BeNull("a total count costs a second query and stays opt-in");

        PagedResult<TriggerHeader> spelledOut = await scheduler.QueryTriggers(
            new TriggerQuery { State = TriggerState.Error });

        page.Items.Select(x => x.Key).Should().Equal(spelledOut.Items.Select(x => x.Key));

        PagedResult<TriggerHeader> unfiltered = await scheduler.QueryTriggers(new TriggerQuery());
        unfiltered.Items.Should().HaveCount(2, "which is what the preset would have answered without its filter");
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

}
