using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.Matchers;
using Quartz.Job;

namespace Quartz.Tests.Integration
{
    [TestFixture]
    public class QuartzMemoryPauseAndResumeTest
    {
        private IScheduler scheduler;

        [SetUp]
        public void SetUp()
        {
            ISchedulerFactory sf = new StdSchedulerFactory();
            scheduler = sf.GetScheduler();
        }

        [TearDown]
        public void TearDown()
        {
            scheduler.Shutdown(true);
        }

        [Test]
        public void TestPauseAndResumeTriggers()
        {
            IJobDetail jobDetail = JobBuilder.Create<NoOpJob>()
                .WithIdentity("test")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("test", "abc")
                .WithCronSchedule("* * * * * ?")
                .Build();

            scheduler.ScheduleJob(jobDetail, trigger);

            TriggerState state = scheduler.GetTriggerState(new TriggerKey("test", "abc"));
            Assert.That(state, Is.EqualTo(TriggerState.Normal));
            Assert.That(state, Is.Not.EqualTo(TriggerState.Paused));

            scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals("abc"));
            state = scheduler.GetTriggerState(new TriggerKey("test", "abc"));
            Assert.That(state, Is.EqualTo(TriggerState.Paused));
            Assert.That(state, Is.Not.EqualTo(TriggerState.Normal));

            scheduler.ResumeTriggers(GroupMatcher<TriggerKey>.GroupEquals("abc"));
            state = scheduler.GetTriggerState(new TriggerKey("test", "abc"));
            Assert.That(state, Is.EqualTo(TriggerState.Normal));
            Assert.That(state, Is.Not.EqualTo(TriggerState.Paused));
        }

        [Test]
        public void TestResumeTriggersBeforeAddJob()
        {
            scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals("abc"));
            scheduler.ResumeTriggers(GroupMatcher<TriggerKey>.GroupEquals("abc"));

            IJobDetail jobDetail = JobBuilder.Create<NoOpJob>()
                .WithIdentity("test")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("test", "abc")
                .WithCronSchedule("* * * * * ?")
                .Build();

            scheduler.ScheduleJob(jobDetail, trigger);

            TriggerState state = scheduler.GetTriggerState(new TriggerKey("test", "abc"));
            Assert.That(state, Is.EqualTo(TriggerState.Normal));
            Assert.That(state, Is.Not.EqualTo(TriggerState.Paused));

            scheduler.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals("abc"));
            state = scheduler.GetTriggerState(new TriggerKey("test", "abc"));
            Assert.That(state, Is.EqualTo(TriggerState.Paused));
            Assert.That(state, Is.Not.EqualTo(TriggerState.Normal));

            scheduler.ResumeTriggers(GroupMatcher<TriggerKey>.GroupEquals("abc"));
            state = scheduler.GetTriggerState(new TriggerKey("test", "abc"));
            Assert.That(state, Is.EqualTo(TriggerState.Normal));
            Assert.That(state, Is.Not.EqualTo(TriggerState.Paused));
        }

        [Test]
        public void TestPauseAndResumeJobs()
        {
            IJobDetail jobDetail = JobBuilder.Create<NoOpJob>()
                .WithIdentity("test", "abc")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("test", "abc")
                .WithCronSchedule("* * * * * ?")
                .Build();

            scheduler.ScheduleJob(jobDetail, trigger);

            TriggerState state = scheduler.GetTriggerState(new TriggerKey("test", "abc"));
            Assert.That(state, Is.EqualTo(TriggerState.Normal));
            Assert.That(state, Is.Not.EqualTo(TriggerState.Paused));

            scheduler.PauseJobs(GroupMatcher<JobKey>.GroupEquals("abc"));
            state = scheduler.GetTriggerState(new TriggerKey("test", "abc"));
            Assert.That(state, Is.EqualTo(TriggerState.Paused));
            Assert.That(state, Is.Not.EqualTo(TriggerState.Normal));

            scheduler.ResumeJobs(GroupMatcher<JobKey>.GroupEquals("abc"));
            state = scheduler.GetTriggerState(new TriggerKey("test", "abc"));
            Assert.That(state, Is.EqualTo(TriggerState.Normal));
            Assert.That(state, Is.Not.EqualTo(TriggerState.Paused));
        }

        [Test]
        public void TestResumeJobsBeforeAddJobs()
        {
            scheduler.PauseJobs(GroupMatcher<JobKey>.GroupEquals("abc"));
            scheduler.ResumeJobs(GroupMatcher<JobKey>.GroupEquals("abc"));

            IJobDetail jobDetail = JobBuilder.Create<NoOpJob>()
                .WithIdentity("test", "abc")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("test", "abc")
                .WithCronSchedule("* * * * * ?")
                .Build();

            scheduler.ScheduleJob(jobDetail, trigger);

            TriggerState state = scheduler.GetTriggerState(new TriggerKey("test", "abc"));
            Assert.That(state, Is.EqualTo(TriggerState.Normal));
            Assert.That(state, Is.Not.EqualTo(TriggerState.Paused));

            scheduler.PauseJobs(GroupMatcher<JobKey>.GroupEquals("abc"));
            state = scheduler.GetTriggerState(new TriggerKey("test", "abc"));
            Assert.That(state, Is.EqualTo(TriggerState.Paused));
            Assert.That(state, Is.Not.EqualTo(TriggerState.Normal));

            scheduler.ResumeJobs(GroupMatcher<JobKey>.GroupEquals("abc"));
            state = scheduler.GetTriggerState(new TriggerKey("test", "abc"));
            Assert.That(state, Is.EqualTo(TriggerState.Normal));
            Assert.That(state, Is.Not.EqualTo(TriggerState.Paused));
        }
    }
}