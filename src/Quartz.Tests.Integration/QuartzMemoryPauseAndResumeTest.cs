using Quartz.Impl;
using Quartz.Impl.Matchers;
using Quartz.Job;

namespace Quartz.Tests.Integration;

[TestFixture]
public class QuartzMemoryPauseAndResumeTest
{
    private IScheduler scheduler;

    [SetUp]
    public async Task SetUp()
    {
        ISchedulerFactory sf = new StdSchedulerFactory();
        scheduler = await sf.GetScheduler();
    }

    [TearDown]
    public async Task TearDown()
    {
        await scheduler.Shutdown(true);
    }

    [Test]
    public async Task TestPauseAndResumeTriggers()
    {
        IJobDetail jobDetail = JobBuilder.Create<NoOpJob>()
            .WithIdentity("test")
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("test", "abc")
            .WithCronSchedule("* * * * * ?")
            .Build();

        await scheduler.ScheduleJob(jobDetail, trigger);

        TriggerState state = await scheduler.GetTriggerState(new TriggerKey("test", "abc"));
        Assert.That(state, Is.EqualTo(TriggerState.Normal));
        Assert.That(state, Is.Not.EqualTo(TriggerState.Paused));

        await scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals("abc"));
        state = await scheduler.GetTriggerState(new TriggerKey("test", "abc"));
        Assert.That(state, Is.EqualTo(TriggerState.Paused));
        Assert.That(state, Is.Not.EqualTo(TriggerState.Normal));

        await scheduler.ResumeTriggers(GroupMatcher<TriggerKey>.GroupEquals("abc"));
        state = await scheduler.GetTriggerState(new TriggerKey("test", "abc"));
        Assert.That(state, Is.EqualTo(TriggerState.Normal));
        Assert.That(state, Is.Not.EqualTo(TriggerState.Paused));
    }

    [Test]
    public async Task TestResumeTriggersBeforeAddJob()
    {
        await scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals("abc"));
        await scheduler.ResumeTriggers(GroupMatcher<TriggerKey>.GroupEquals("abc"));

        IJobDetail jobDetail = JobBuilder.Create<NoOpJob>()
            .WithIdentity("test")
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("test", "abc")
            .WithCronSchedule("* * * * * ?")
            .Build();

        await scheduler.ScheduleJob(jobDetail, trigger);

        TriggerState state = await scheduler.GetTriggerState(new TriggerKey("test", "abc"));
        Assert.That(state, Is.EqualTo(TriggerState.Normal));
        Assert.That(state, Is.Not.EqualTo(TriggerState.Paused));

        await scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals("abc"));
        state = await scheduler.GetTriggerState(new TriggerKey("test", "abc"));
        Assert.That(state, Is.EqualTo(TriggerState.Paused));
        Assert.That(state, Is.Not.EqualTo(TriggerState.Normal));

        await scheduler.ResumeTriggers(GroupMatcher<TriggerKey>.GroupEquals("abc"));
        state = await scheduler.GetTriggerState(new TriggerKey("test", "abc"));
        Assert.That(state, Is.EqualTo(TriggerState.Normal));
        Assert.That(state, Is.Not.EqualTo(TriggerState.Paused));
    }

    [Test]
    public async Task TestPauseAndResumeJobs()
    {
        IJobDetail jobDetail = JobBuilder.Create<NoOpJob>()
            .WithIdentity("test", "abc")
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("test", "abc")
            .WithCronSchedule("* * * * * ?")
            .Build();

        await scheduler.ScheduleJob(jobDetail, trigger);

        TriggerState state = await scheduler.GetTriggerState(new TriggerKey("test", "abc"));
        Assert.That(state, Is.EqualTo(TriggerState.Normal));
        Assert.That(state, Is.Not.EqualTo(TriggerState.Paused));

        await scheduler.PauseJobs(GroupMatcher<JobKey>.GroupEquals("abc"));
        state = await scheduler.GetTriggerState(new TriggerKey("test", "abc"));
        Assert.That(state, Is.EqualTo(TriggerState.Paused));
        Assert.That(state, Is.Not.EqualTo(TriggerState.Normal));

        await scheduler.ResumeJobs(GroupMatcher<JobKey>.GroupEquals("abc"));
        state = await scheduler.GetTriggerState(new TriggerKey("test", "abc"));
        Assert.That(state, Is.EqualTo(TriggerState.Normal));
        Assert.That(state, Is.Not.EqualTo(TriggerState.Paused));
    }

    [Test]
    public async Task TestResumeJobsBeforeAddJobs()
    {
        await scheduler.PauseJobs(GroupMatcher<JobKey>.GroupEquals("abc"));
        await scheduler.ResumeJobs(GroupMatcher<JobKey>.GroupEquals("abc"));

        IJobDetail jobDetail = JobBuilder.Create<NoOpJob>()
            .WithIdentity("test", "abc")
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("test", "abc")
            .WithCronSchedule("* * * * * ?")
            .Build();

        await scheduler.ScheduleJob(jobDetail, trigger);

        TriggerState state = await scheduler.GetTriggerState(new TriggerKey("test", "abc"));
        Assert.That(state, Is.EqualTo(TriggerState.Normal));
        Assert.That(state, Is.Not.EqualTo(TriggerState.Paused));

        await scheduler.PauseJobs(GroupMatcher<JobKey>.GroupEquals("abc"));
        state = await scheduler.GetTriggerState(new TriggerKey("test", "abc"));
        Assert.That(state, Is.EqualTo(TriggerState.Paused));
        Assert.That(state, Is.Not.EqualTo(TriggerState.Normal));

        await scheduler.ResumeJobs(GroupMatcher<JobKey>.GroupEquals("abc"));
        state = await scheduler.GetTriggerState(new TriggerKey("test", "abc"));
        Assert.That(state, Is.EqualTo(TriggerState.Normal));
        Assert.That(state, Is.Not.EqualTo(TriggerState.Paused));
    }
}