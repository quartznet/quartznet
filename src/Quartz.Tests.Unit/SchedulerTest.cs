using System.Collections.Generic;
using System.Collections.Specialized;

using NUnit.Framework;

using Quartz.Impl;

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
        }

        [Test]
        public void TestBasicStorageFunctions()
        {
            NameValueCollection config = new NameValueCollection();
            config["quartz.threadPool.threadCount"] = "2";
            config["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool, Quartz";
            IScheduler sched = new StdSchedulerFactory(config).GetScheduler();

            // test basic storage functions of scheduler...

            IJobDetail job = JobBuilder.NewJob()
                .OfType<TestJob>()
                .WithIdentity("j1")
                .StoreDurably()
                .Build();

            Assert.IsFalse(sched.CheckExists(new JobKey("j1")), "Unexpected existence of job named 'j1'.");

            sched.AddJob(job, false);

            Assert.IsTrue(sched.CheckExists(new JobKey("j1")), "Expected existence of job named 'j1' but checkExists return false.");

            job = sched.GetJobDetail(new JobKey("j1"));

            Assert.IsNotNull(job, "Stored job not found!");

            sched.DeleteJob(new JobKey("j1"));

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("t1")
                .ForJob(job)
                .StartNow()
                .WithSchedule(SimpleScheduleBuilder.Create()
                                  .RepeatForever()
                                  .WithIntervalInSeconds(5))
                .Build();

            Assert.IsFalse(sched.CheckExists(new TriggerKey("t1")), "Unexpected existence of trigger named '11'.");

            sched.ScheduleJob(job, trigger);

            Assert.IsTrue(sched.CheckExists(new TriggerKey("t1")), "Expected existence of trigger named 't1' but checkExists return false.");

            job = sched.GetJobDetail(new JobKey("j1"));

            Assert.IsNotNull(job, "Stored job not found!");

            trigger = sched.GetTrigger(new TriggerKey("t1"));

            Assert.IsNotNull(trigger, "Stored trigger not found!");

            job = JobBuilder.NewJob()
                .OfType<TestJob>()
                .WithIdentity("j2", "g1")
                .Build();

            trigger = TriggerBuilder.Create()
                .WithIdentity("t2", "g1")
                .ForJob(job)
                .StartNow()
                .WithSchedule(SimpleScheduleBuilder.Create()
                                  .RepeatForever()
                                  .WithIntervalInSeconds(5))
                .Build();

            sched.ScheduleJob(job, trigger);

            job = JobBuilder.NewJob()
                .OfType<TestJob>()
                .WithIdentity("j3", "g1")
                .Build();

            trigger = TriggerBuilder.Create()
                .WithIdentity("t3", "g1")
                .ForJob(job)
                .StartNow()
                .WithSchedule(SimpleScheduleBuilder.Create()
                                  .RepeatForever()
                                  .WithIntervalInSeconds(5))
                .Build();

            sched.ScheduleJob(job, trigger);


            IList<string> jobGroups = sched.GetJobGroupNames();
            IList<string> triggerGroups = sched.GetTriggerGroupNames();

            Assert.AreEqual(2, jobGroups.Count, "Job group list size expected to be = 2 ");
            Assert.AreEqual(2,triggerGroups.Count, "Trigger group list size expected to be = 2 ");

            IList<JobKey> jobKeys = sched.GetJobKeys(JobKey.DefaultGroup);
            IList<TriggerKey> triggerKeys = sched.GetTriggerKeys(TriggerKey.DefaultGroup);

            Assert.AreEqual(1, jobKeys.Count, "Number of jobs expected in default group was 1 ");
            Assert.AreEqual(1, triggerKeys.Count, "Number of triggers expected in default group was 1 ");

            jobKeys = sched.GetJobKeys("g1");
            triggerKeys = sched.GetTriggerKeys("g1");

            Assert.AreEqual(2, jobKeys.Count, "Number of jobs expected in 'g1' group was 2 ");
            Assert.AreEqual(2, triggerKeys.Count, "Number of triggers expected in 'g1' group was 2 ");


            TriggerState s = sched.GetTriggerState(new TriggerKey("t2", "g1"));
            Assert.AreEqual(TriggerState.Normal, s, "State of trigger t2 expected to be NORMAL ");

            sched.PauseTrigger(new TriggerKey("t2", "g1"));
            s = sched.GetTriggerState(new TriggerKey("t2", "g1"));
            Assert.AreEqual(TriggerState.Paused, s, "State of trigger t2 expected to be PAUSED ");

            sched.ResumeTrigger(new TriggerKey("t2", "g1"));
            s = sched.GetTriggerState(new TriggerKey("t2", "g1"));
            Assert.AreEqual(TriggerState.Normal, s, "State of trigger t2 expected to be NORMAL ");

            Collection.ISet<string> pausedGroups = sched.GetPausedTriggerGroups();
            Assert.AreEqual(0, pausedGroups.Count, "Size of paused trigger groups list expected to be 0 ");

            sched.PauseTriggerGroup("g1");

            // test that adding a trigger to a paused group causes the new trigger to be paused also... 
            job = JobBuilder.NewJob()
                .OfType<TestJob>()
                .WithIdentity("j4", "g1")
                .Build();

            trigger = TriggerBuilder.Create()
                .WithIdentity("t4", "g1")
                .ForJob(job)
                .StartNow()
                .WithSchedule(SimpleScheduleBuilder.Create()
                                  .RepeatForever()
                                  .WithIntervalInSeconds(5))
                .Build();

            sched.ScheduleJob(job, trigger);

            pausedGroups = sched.GetPausedTriggerGroups();
            Assert.AreEqual(1,pausedGroups.Count, "Size of paused trigger groups list expected to be 1 ");

            s = sched.GetTriggerState(new TriggerKey("t2", "g1"));
            Assert.AreEqual(TriggerState.Paused, s, "State of trigger t2 expected to be PAUSED ");

            s = sched.GetTriggerState(new TriggerKey("t4", "g1"));
            Assert.AreEqual(TriggerState.Paused, s, "State of trigger t4 expected to be PAUSED");

            sched.ResumeTriggerGroup("g1");
            
            s = sched.GetTriggerState(new TriggerKey("t2", "g1"));
            Assert.AreEqual(TriggerState.Normal, s, "State of trigger t2 expected to be NORMAL ");
            
            s = sched.GetTriggerState(new TriggerKey("t4", "g1"));
            Assert.AreEqual(TriggerState.Normal, s, "State of trigger t2 expected to be NORMAL ");
            
            pausedGroups = sched.GetPausedTriggerGroups();
            Assert.AreEqual(0, pausedGroups.Count, "Size of paused trigger groups list expected to be 0 ");


            Assert.IsFalse(sched.UnscheduleJob(new TriggerKey("foasldfksajdflk")), "Scheduler should have returned 'false' from attempt to unschedule non-existing trigger. ");

            Assert.IsTrue(sched.UnscheduleJob(new TriggerKey("t3", "g1")), "Scheduler should have returned 'true' from attempt to unschedule existing trigger. ");

            jobKeys = sched.GetJobKeys("g1");
            triggerKeys = sched.GetTriggerKeys("g1");

            Assert.AreEqual(2, jobKeys.Count, "Number of jobs expected in 'g1' group was 1 "); // job should have been deleted also, because it is non-durable
            Assert.AreEqual(2, triggerKeys.Count, "Number of triggers expected in 'g1' group was 1 ");

            Assert.IsTrue(sched.UnscheduleJob(new TriggerKey("t1")), "Scheduler should have returned 'true' from attempt to unschedule existing trigger. ");

            jobKeys = sched.GetJobKeys(JobKey.DefaultGroup);
            triggerKeys = sched.GetTriggerKeys(TriggerKey.DefaultGroup);

            Assert.AreEqual(1, jobKeys.Count, "Number of jobs expected in default group was 1 "); // job should have been left in place, because it is non-durable
            Assert.AreEqual(0, triggerKeys.Count, "Number of triggers expected in default group was 0 ");
        }
    }
}