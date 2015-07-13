using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.Matchers;
using Quartz.Job;
using Quartz.Spi;

namespace Quartz.Tests.Unit
{
    [TestFixture]
    public class SchedulerTest
    {
        [DisallowConcurrentExecution]
        [PersistJobDataAfterExecution]
        public class TestStatefulJob : IJob
        {
            public void Execute(IJobExecutionContext context)
            {
            }
        }

        public class TestJob : IJob
        {
            public void Execute(IJobExecutionContext context)
            {
            }
        }

        [DisallowConcurrentExecution]
        [PersistJobDataAfterExecution]
        public class TestAnnotatedJob : IJob
        {
            public void Execute(IJobExecutionContext context)
            {
            }
        }

        [SetUp]
        protected void SetUp()
        {
            string input = "0 0 03-07 ? * MON-FRI | 0 35/15 07 ? * MON-FRI | 0 05/15 08-14 ? * MON-FRI | 0 0/10 15-16 ? * MON-FRI | 0 05/15 17-23 ? * MON-FRI";
            IScheduler scheduler = new StdSchedulerFactory().GetScheduler().GetAwaiter().GetResult();
            var job = JobBuilder.Create<NoOpJob>().Build();
            var crontTriggers = input.Split('|').Select(x => x.Trim()).Select(cronExpression => TriggerBuilder.Create().WithCronSchedule(cronExpression).Build());
            scheduler.ScheduleJob(job, new HashSet<ITrigger>(crontTriggers), replace: false);
        }

        [Test]
        public async Task TestBasicStorageFunctions()
        {
            NameValueCollection config = new NameValueCollection();
            config["quartz.scheduler.instanceName"] = "SchedulerTest_Scheduler";
            config["quartz.scheduler.instanceId"] = "AUTO";
            config["quartz.threadPool.threadCount"] = "2";
            config["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool, Quartz";
            IScheduler sched = await new StdSchedulerFactory(config).GetScheduler();

            // test basic storage functions of scheduler...

            IJobDetail job = JobBuilder.Create()
                .OfType<TestJob>()
                .WithIdentity("j1")
                .StoreDurably()
                .Build();

            var exists = await sched.CheckExists(new JobKey("j1"));
            Assert.IsFalse(exists, "Unexpected existence of job named 'j1'.");

            await sched.AddJob(job, false);

            exists = await sched.CheckExists(new JobKey("j1"));
            Assert.IsTrue(exists, "Expected existence of job named 'j1' but checkExists return false.");

            job = await sched.GetJobDetail(new JobKey("j1"));

            Assert.IsNotNull(job, "Stored job not found!");

            await sched.DeleteJob(new JobKey("j1"));

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("t1")
                .ForJob(job)
                .StartNow()
                .WithSimpleSchedule(x => x.RepeatForever().WithIntervalInSeconds(5))
                .Build();

            exists = await sched.CheckExists(new TriggerKey("t1"));
            Assert.IsFalse(exists, "Unexpected existence of trigger named '11'.");

            await sched.ScheduleJob(job, trigger);

            exists = await sched.CheckExists(new TriggerKey("t1"));
            Assert.IsTrue(exists, "Expected existence of trigger named 't1' but checkExists return false.");

            job = await sched.GetJobDetail(new JobKey("j1"));

            Assert.IsNotNull(job, "Stored job not found!");

            trigger = await sched.GetTrigger(new TriggerKey("t1"));

            Assert.IsNotNull(trigger, "Stored trigger not found!");

            job = JobBuilder.Create()
                .OfType<TestJob>()
                .WithIdentity("j2", "g1")
                .Build();

            trigger = TriggerBuilder.Create()
                .WithIdentity("t2", "g1")
                .ForJob(job)
                .StartNow()
                .WithSimpleSchedule(x => x.RepeatForever().WithIntervalInSeconds(5))
                .Build();

            await sched.ScheduleJob(job, trigger);

            job = JobBuilder.Create()
                .OfType<TestJob>()
                .WithIdentity("j3", "g1")
                .Build();

            trigger = TriggerBuilder.Create()
                .WithIdentity("t3", "g1")
                .ForJob(job)
                .StartNow()
                .WithSimpleSchedule(x => x.RepeatForever().WithIntervalInSeconds(5))
                .Build();

            await sched.ScheduleJob(job, trigger);

            var jobGroups = await sched.GetJobGroupNames();
            var triggerGroups = await sched.GetTriggerGroupNames();

            Assert.AreEqual(2, jobGroups.Count, "Job group list size expected to be = 2 ");
            Assert.AreEqual(2, triggerGroups.Count, "Trigger group list size expected to be = 2 ");

            ISet<JobKey> jobKeys = await sched.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(JobKey.DefaultGroup));
            ISet<TriggerKey> triggerKeys = await sched.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(TriggerKey.DefaultGroup));

            Assert.AreEqual(1, jobKeys.Count, "Number of jobs expected in default group was 1 ");
            Assert.AreEqual(1, triggerKeys.Count, "Number of triggers expected in default group was 1 ");

            jobKeys = await sched.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("g1"));
            triggerKeys = await sched.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("g1"));

            Assert.AreEqual(2, jobKeys.Count, "Number of jobs expected in 'g1' group was 2 ");
            Assert.AreEqual(2, triggerKeys.Count, "Number of triggers expected in 'g1' group was 2 ");

            TriggerState s = await sched.GetTriggerState(new TriggerKey("t2", "g1"));
            Assert.AreEqual(TriggerState.Normal, s, "State of trigger t2 expected to be NORMAL ");

            await sched.PauseTrigger(new TriggerKey("t2", "g1"));
            s = await sched.GetTriggerState(new TriggerKey("t2", "g1"));
            Assert.AreEqual(TriggerState.Paused, s, "State of trigger t2 expected to be PAUSED ");

            await sched.ResumeTrigger(new TriggerKey("t2", "g1"));
            s = await sched.GetTriggerState(new TriggerKey("t2", "g1"));
            Assert.AreEqual(TriggerState.Normal, s, "State of trigger t2 expected to be NORMAL ");

            ISet<string> pausedGroups = await sched.GetPausedTriggerGroups();
            Assert.AreEqual(0, pausedGroups.Count, "Size of paused trigger groups list expected to be 0 ");

            await sched.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals("g1"));

            // test that adding a trigger to a paused group causes the new trigger to be paused also... 
            job = JobBuilder.Create()
                .OfType<TestJob>()
                .WithIdentity("j4", "g1")
                .Build();

            trigger = TriggerBuilder.Create()
                .WithIdentity("t4", "g1")
                .ForJob(job)
                .StartNow()
                .WithSimpleSchedule(x => x.RepeatForever().WithIntervalInSeconds(5))
                .Build();

            await sched.ScheduleJob(job, trigger);

            pausedGroups = await sched.GetPausedTriggerGroups();
            Assert.AreEqual(1, pausedGroups.Count, "Size of paused trigger groups list expected to be 1 ");

            s = await sched.GetTriggerState(new TriggerKey("t2", "g1"));
            Assert.AreEqual(TriggerState.Paused, s, "State of trigger t2 expected to be PAUSED ");

            s = await sched.GetTriggerState(new TriggerKey("t4", "g1"));
            Assert.AreEqual(TriggerState.Paused, s, "State of trigger t4 expected to be PAUSED");

            await sched.ResumeTriggers(GroupMatcher<TriggerKey>.GroupEquals("g1"));

            s = await sched.GetTriggerState(new TriggerKey("t2", "g1"));
            Assert.AreEqual(TriggerState.Normal, s, "State of trigger t2 expected to be NORMAL ");

            s = await sched.GetTriggerState(new TriggerKey("t4", "g1"));
            Assert.AreEqual(TriggerState.Normal, s, "State of trigger t2 expected to be NORMAL ");

            pausedGroups = await sched.GetPausedTriggerGroups();
            Assert.AreEqual(0, pausedGroups.Count, "Size of paused trigger groups list expected to be 0 ");

            Assert.IsFalse(await sched.UnscheduleJob(new TriggerKey("foasldfksajdflk")), "Scheduler should have returned 'false' from attempt to unschedule non-existing trigger. ");

            Assert.IsTrue(await sched.UnscheduleJob(new TriggerKey("t3", "g1")), "Scheduler should have returned 'true' from attempt to unschedule existing trigger. ");

            jobKeys = await sched.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("g1"));
            triggerKeys = await sched.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("g1"));

            Assert.AreEqual(2, jobKeys.Count, "Number of jobs expected in 'g1' group was 1 "); // job should have been deleted also, because it is non-durable
            Assert.AreEqual(2, triggerKeys.Count, "Number of triggers expected in 'g1' group was 1 ");

            Assert.IsTrue(await sched.UnscheduleJob(new TriggerKey("t1")), "Scheduler should have returned 'true' from attempt to unschedule existing trigger. ");

            jobKeys = await sched.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(JobKey.DefaultGroup));
            triggerKeys = await sched.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(TriggerKey.DefaultGroup));

            Assert.AreEqual(1, jobKeys.Count, "Number of jobs expected in default group was 1 "); // job should have been left in place, because it is non-durable
            Assert.AreEqual(0, triggerKeys.Count, "Number of triggers expected in default group was 0 ");

            await sched.Shutdown();
        }

        [Test]
        public async Task TestShutdownWithSleepReturnsAfterAllThreadsAreStopped()
        {
            int activeThreads = Process.GetCurrentProcess().Threads.Count;
            int threadPoolSize = 5;
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.threadPool.threadCount"] = threadPoolSize.ToString();
            ISchedulerFactory factory = new StdSchedulerFactory(properties);
            IScheduler scheduler = await factory.GetScheduler();
            await scheduler.Start();

            await Task.Delay(500);

            await scheduler.Shutdown(true);

            Assert.True(Process.GetCurrentProcess().Threads.Count <= activeThreads);
        }

        [Test]
        public void SerializationExceptionTest()
        {
            SchedulerException before;
            SchedulerException after;

            try
            {
                try
                {
                    throw new Exception("INNER");
                }
                catch (Exception ex)
                {
                    throw new SchedulerException("OUTER", ex);
                }
            }
            catch (SchedulerException ex)
            {
                before = ex;
            }

            using (var stream = new MemoryStream())
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(stream, before);
                stream.Seek(0, SeekOrigin.Begin);
                after = (SchedulerException) formatter.Deserialize(stream);
            }

            Assert.NotNull(before.InnerException);
            Assert.NotNull(after.InnerException);
            Assert.AreEqual(before.ToString(), after.ToString());
        }

        [Test]
        public async Task ReschedulingTriggerShouldKeepOriginalNextFireTime()
        {
            ISchedulerFactory factory = new StdSchedulerFactory();
            IScheduler scheduler = await factory.GetScheduler();
            await scheduler.Start();

            var job = JobBuilder.Create<NoOpJob>().Build();
            IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
                .WithSimpleSchedule(x => x.WithIntervalInHours(1).RepeatForever())
                .ForJob(job)
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(job, trigger);

            trigger = (IOperableTrigger) await scheduler.GetTrigger(trigger.Key);
            Assert.That(trigger.GetPreviousFireTimeUtc(), Is.EqualTo(null));

            var previousFireTimeUtc = DateTimeOffset.UtcNow.AddDays(1);
            trigger.SetPreviousFireTimeUtc(previousFireTimeUtc);
            trigger.SetNextFireTimeUtc(trigger.GetFireTimeAfter(previousFireTimeUtc));

            await scheduler.RescheduleJob(trigger.Key, trigger);

            trigger = (IOperableTrigger) await scheduler.GetTrigger(trigger.Key);
            Assert.That(trigger.GetNextFireTimeUtc().Value.UtcDateTime, Is.EqualTo(previousFireTimeUtc.AddHours(1).UtcDateTime).Within(TimeSpan.FromSeconds(5)));

            await scheduler.Shutdown(true);
        }
    }
}