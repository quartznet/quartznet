using System;
using System.Collections.Generic;
using System.Threading;

using NUnit.Framework;

using Quartz.Impl.Matchers;

namespace Quartz.Tests.Integration
{
    public abstract class AbstractSchedulerTest
    {
        private const string Barrier = "BARRIER";
        private const string DateStamps = "DATE_STAMPS";

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

        private static readonly TimeSpan testTimeout = TimeSpan.FromSeconds(125);

#if NET_40
        public class TestJobWithSync : IJob
        {
            public void Execute(IJobExecutionContext context)
            {
                try
                {
                    List<DateTime> jobExecTimestamps = (List<DateTime>) context.Scheduler.Context.Get(DateStamps);
                    Barrier barrier = (Barrier) context.Scheduler.Context.Get(Barrier);

                    jobExecTimestamps.Add(DateTime.UtcNow);

                    barrier.SignalAndWait(testTimeout);
                }
                catch (Exception e)
                {
                    Console.Write(e);
                    Assert.Fail("Await on barrier was interrupted: " + e);
                }
            }
        }
#endif

        [DisallowConcurrentExecution]
        [PersistJobDataAfterExecution]
        public class TestAnnotatedJob : IJob
        {
            public void Execute(IJobExecutionContext context)
            {
            }
        }

        protected abstract IScheduler CreateScheduler(string name, int threadPoolSize);

        [Test]
        public void TestBasicStorageFunctions()
        {
            IScheduler sched = CreateScheduler("testBasicStorageFunctions", 2);
            sched.Clear();

            // test basic storage functions of scheduler...
            IJobDetail job = JobBuilder.Create<TestJob>()
                .WithIdentity("j1")
                .StoreDurably()
                .Build();

            Assert.That(sched.CheckExists(new JobKey("j1")), Is.False, "Unexpected existence of job named 'j1'.");

            sched.AddJob(job, false);

            Assert.That(sched.CheckExists(new JobKey("j1")), "Expected existence of job named 'j1' but checkExists return false.");

            job = sched.GetJobDetail(new JobKey("j1"));

            Assert.That(job, Is.Not.Null, "Stored job not found!");

            sched.DeleteJob(new JobKey("j1"));

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("t1")
                .ForJob(job)
                .StartNow()
                .WithSimpleSchedule(x => x
                    .RepeatForever()
                    .WithIntervalInSeconds(5))
                .Build();

            Assert.That(sched.CheckExists(new TriggerKey("t1")), Is.False, "Unexpected existence of trigger named '11'.");

            sched.ScheduleJob(job, trigger);

            Assert.That(sched.CheckExists(new TriggerKey("t1")), "Expected existence of trigger named 't1' but checkExists return false.");

            job = sched.GetJobDetail(new JobKey("j1"));

            Assert.That(job, Is.Not.Null, "Stored job not found!");

            trigger = sched.GetTrigger(new TriggerKey("t1"));

            Assert.That(trigger, Is.Not.Null, "Stored trigger not found!");

            job = JobBuilder.Create<TestJob>()
                .WithIdentity("j2", "g1")
                .Build();

            trigger = TriggerBuilder.Create()
                .WithIdentity("t2", "g1")
                .ForJob(job)
                .StartNow()
                .WithSimpleSchedule(x => x
                    .RepeatForever()
                    .WithIntervalInSeconds(5))
                .Build();

            sched.ScheduleJob(job, trigger);

            job = JobBuilder.Create<TestJob>()
                .WithIdentity("j3", "g1")
                .Build();

            trigger = TriggerBuilder.Create()
                .WithIdentity("t3", "g1")
                .ForJob(job)
                .StartNow()
                .WithSimpleSchedule(x => x
                    .RepeatForever()
                    .WithIntervalInSeconds(5))
                .Build();

            sched.ScheduleJob(job, trigger);

            IList<string> jobGroups = sched.GetJobGroupNames();
            IList<string> triggerGroups = sched.GetTriggerGroupNames();

            Assert.That(jobGroups.Count, Is.EqualTo(2), "Job group list size expected to be = 2 ");
            Assert.That(triggerGroups.Count, Is.EqualTo(2), "Trigger group list size expected to be = 2 ");

            Collection.ISet<JobKey> jobKeys = sched.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(JobKey.DefaultGroup));
            Collection.ISet<TriggerKey> triggerKeys = sched.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(TriggerKey.DefaultGroup));

            Assert.That(jobKeys.Count, Is.EqualTo(1), "Number of jobs expected in default group was 1 ");
            Assert.That(triggerKeys.Count, Is.EqualTo(1), "Number of triggers expected in default group was 1 ");

            jobKeys = sched.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("g1"));
            triggerKeys = sched.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("g1"));

            Assert.That(jobKeys.Count, Is.EqualTo(2), "Number of jobs expected in 'g1' group was 2 ");
            Assert.That(triggerKeys.Count, Is.EqualTo(2), "Number of triggers expected in 'g1' group was 2 ");

            TriggerState s = sched.GetTriggerState(new TriggerKey("t2", "g1"));
            Assert.That(s.Equals(TriggerState.Normal), "State of trigger t2 expected to be NORMAL ");

            sched.PauseTrigger(new TriggerKey("t2", "g1"));
            s = sched.GetTriggerState(new TriggerKey("t2", "g1"));
            Assert.That(s.Equals(TriggerState.Paused), "State of trigger t2 expected to be PAUSED ");

            sched.ResumeTrigger(new TriggerKey("t2", "g1"));
            s = sched.GetTriggerState(new TriggerKey("t2", "g1"));
            Assert.That(s.Equals(TriggerState.Normal), "State of trigger t2 expected to be NORMAL ");

            Collection.ISet<string> pausedGroups = sched.GetPausedTriggerGroups();
            Assert.That(pausedGroups, Is.Empty, "Size of paused trigger groups list expected to be 0 ");

            sched.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals("g1"));

            // test that adding a trigger to a paused group causes the new trigger to be paused also... 
            job = JobBuilder.Create<TestJob>()
                .WithIdentity("j4", "g1")
                .Build();

            trigger = TriggerBuilder.Create()
                .WithIdentity("t4", "g1")
                .ForJob(job)
                .StartNow()
                .WithSimpleSchedule(x => x.RepeatForever().WithIntervalInSeconds(5))
                .Build();

            sched.ScheduleJob(job, trigger);

            pausedGroups = sched.GetPausedTriggerGroups();
            Assert.That(pausedGroups.Count, Is.EqualTo(1), "Size of paused trigger groups list expected to be 1 ");

            s = sched.GetTriggerState(new TriggerKey("t2", "g1"));
            Assert.That(s.Equals(TriggerState.Paused), "State of trigger t2 expected to be PAUSED ");

            s = sched.GetTriggerState(new TriggerKey("t4", "g1"));
            Assert.That(s.Equals(TriggerState.Paused), "State of trigger t4 expected to be PAUSED ");

            sched.ResumeTriggers(GroupMatcher<TriggerKey>.GroupEquals("g1"));
            s = sched.GetTriggerState(new TriggerKey("t2", "g1"));
            Assert.That(s.Equals(TriggerState.Normal), "State of trigger t2 expected to be NORMAL ");
            s = sched.GetTriggerState(new TriggerKey("t4", "g1"));
            Assert.That(s.Equals(TriggerState.Normal), "State of trigger t4 expected to be NORMAL ");
            pausedGroups = sched.GetPausedTriggerGroups();
            Assert.That(pausedGroups, Is.Empty, "Size of paused trigger groups list expected to be 0 ");

            Assert.That(sched.UnscheduleJob(new TriggerKey("foasldfksajdflk")), Is.False, "Scheduler should have returned 'false' from attempt to unschedule non-existing trigger. ");

            Assert.That(sched.UnscheduleJob(new TriggerKey("t3", "g1")), "Scheduler should have returned 'true' from attempt to unschedule existing trigger. ");

            jobKeys = sched.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("g1"));
            triggerKeys = sched.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("g1"));

            Assert.That(jobKeys.Count, Is.EqualTo(2), "Number of jobs expected in 'g1' group was 1 "); // job should have been deleted also, because it is non-durable
            Assert.That(triggerKeys.Count, Is.EqualTo(2), "Number of triggers expected in 'g1' group was 1 ");

            Assert.That(sched.UnscheduleJob(new TriggerKey("t1")), "Scheduler should have returned 'true' from attempt to unschedule existing trigger. ");

            jobKeys = sched.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(JobKey.DefaultGroup));
            triggerKeys = sched.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(TriggerKey.DefaultGroup));

            Assert.That(jobKeys.Count, Is.EqualTo(1), "Number of jobs expected in default group was 1 "); // job should have been left in place, because it is non-durable
            Assert.That(triggerKeys, Is.Empty, "Number of triggers expected in default group was 0 ");

            sched.Shutdown();
        }

#if NET_40

        [Test]
        public void TestAbilityToFireImmediatelyWhenStartedBefore()
        {
            List<DateTime> jobExecTimestamps = new List<DateTime>();
            Barrier barrier = new Barrier(2);

            IScheduler sched = CreateScheduler("testAbilityToFireImmediatelyWhenStartedBefore", 5);
            sched.Context.Put(Barrier, barrier);
            sched.Context.Put(DateStamps, jobExecTimestamps);
            sched.Start();

            Thread.Yield();

            IJobDetail job1 = JobBuilder.Create<TestJobWithSync>()
                .WithIdentity("job1")
                .Build();

            ITrigger trigger1 = TriggerBuilder.Create()
                .ForJob(job1)
                .Build();

            DateTime sTime = DateTime.UtcNow;

            sched.ScheduleJob(job1, trigger1);

            barrier.SignalAndWait(testTimeout);

            sched.Shutdown(false);

            DateTime fTime = jobExecTimestamps[0];

            Assert.That(fTime - sTime < TimeSpan.FromMilliseconds(7000), "Immediate trigger did not fire within a reasonable amount of time.");
        }

        [Test]
        public void TestAbilityToFireImmediatelyWhenStartedBeforeWithTriggerJob()
        {
            List<DateTime> jobExecTimestamps = new List<DateTime>();
            Barrier barrier = new Barrier(2);

            IScheduler sched = CreateScheduler("testAbilityToFireImmediatelyWhenStartedBeforeWithTriggerJob", 5);
            sched.Clear();

            sched.Context.Put(Barrier, barrier);
            sched.Context.Put(DateStamps, jobExecTimestamps);

            sched.Start();

            Thread.Yield();

            IJobDetail job1 = JobBuilder.Create<TestJobWithSync>()
                .WithIdentity("job1").
                StoreDurably().Build();
            sched.AddJob(job1, false);

            DateTime sTime = DateTime.UtcNow;

            sched.TriggerJob(job1.Key);

            barrier.SignalAndWait(testTimeout);

            sched.Shutdown(false);

            DateTime fTime = jobExecTimestamps[0];

            Assert.That(fTime - sTime < TimeSpan.FromMilliseconds(7000), "Immediate trigger did not fire within a reasonable amount of time."); // This is dangerously subjective!  but what else to do?
        }

        [Test]
        public void TestAbilityToFireImmediatelyWhenStartedAfter()
        {
            List<DateTime> jobExecTimestamps = new List<DateTime>();

            Barrier barrier = new Barrier(2);

            IScheduler sched = CreateScheduler("testAbilityToFireImmediatelyWhenStartedAfter", 5);
            sched.Context.Put(Barrier, barrier);
            sched.Context.Put(DateStamps, jobExecTimestamps);

            IJobDetail job1 = JobBuilder.Create<TestJobWithSync>().WithIdentity("job1").Build();
            ITrigger trigger1 = TriggerBuilder.Create().ForJob(job1).Build();

            DateTime sTime = DateTime.UtcNow;

            sched.ScheduleJob(job1, trigger1);
            sched.Start();

            barrier.SignalAndWait(testTimeout);

            sched.Shutdown(false);

            DateTime fTime = jobExecTimestamps[0];

            Assert.That((fTime - sTime < TimeSpan.FromMilliseconds(7000)), "Immediate trigger did not fire within a reasonable amount of time."); // This is dangerously subjective!  but what else to do?
        }
#endif

        [Test]
        public void TestScheduleMultipleTriggersForAJob()
        {
            IJobDetail job = JobBuilder.Create<TestJob>().WithIdentity("job1", "group1").Build();
            ITrigger trigger1 = TriggerBuilder.Create()
                .WithIdentity("trigger1", "group1")
                .StartNow()
                .WithSimpleSchedule(x => x.WithIntervalInSeconds(1).RepeatForever())
                .Build();
            ITrigger trigger2 = TriggerBuilder.Create()
                .WithIdentity("trigger2", "group1")
                .StartNow()
                .WithSimpleSchedule(x => x.WithIntervalInSeconds(1).RepeatForever())
                .Build();

            Collection.ISet<ITrigger> triggersForJob = new Collection.HashSet<ITrigger>();
            triggersForJob.Add(trigger1);
            triggersForJob.Add(trigger2);

            IScheduler sched = CreateScheduler("testScheduleMultipleTriggersForAJob", 5);
            sched.ScheduleJob(job, triggersForJob, true);

            IList<ITrigger> triggersOfJob = sched.GetTriggersOfJob(job.Key);
            Assert.That(triggersOfJob.Count, Is.EqualTo(2));
            Assert.That(triggersOfJob.Contains(trigger1));
            Assert.That(triggersOfJob.Contains(trigger2));

            sched.Shutdown(false);
        }

        [Test]
        public void TestDurableStorageFunctions()
        {
            IScheduler sched = CreateScheduler("testDurableStorageFunctions", 2);
            sched.Clear();

            // test basic storage functions of scheduler...

            IJobDetail job = JobBuilder.Create<TestJob>()
                .WithIdentity("j1")
                .StoreDurably()
                .Build();

            Assert.That(sched.CheckExists(new JobKey("j1")), Is.False, "Unexpected existence of job named 'j1'.");

            sched.AddJob(job, false);

            Assert.That(sched.CheckExists(new JobKey("j1")), "Unexpected non-existence of job named 'j1'.");

            IJobDetail nonDurableJob = JobBuilder.Create<TestJob>()
                .WithIdentity("j2")
                .Build();

            try
            {
                sched.AddJob(nonDurableJob, false);
                Assert.Fail("Storage of non-durable job should not have succeeded.");
            }
            catch (SchedulerException)
            {
                Assert.That(sched.CheckExists(new JobKey("j2")), Is.False, "Unexpected existence of job named 'j2'.");
            }

            sched.AddJob(nonDurableJob, false, true);

            Assert.That(sched.CheckExists(new JobKey("j2")), "Unexpected non-existence of job named 'j2'.");
        }

#if NET_40
        [Test]
        public void TestShutdownWithoutWaitIsUnclean()
        {
            List<DateTime> jobExecTimestamps = new List<DateTime>();
            Barrier barrier = new Barrier(2);
            IScheduler scheduler = CreateScheduler("testShutdownWithoutWaitIsUnclean", 8);
            try
            {
                scheduler.Context.Put(Barrier, barrier);
                scheduler.Context.Put(DateStamps, jobExecTimestamps);
                scheduler.Start();
                string jobName = Guid.NewGuid().ToString();
                scheduler.AddJob(JobBuilder.Create<TestJobWithSync>().WithIdentity(jobName).StoreDurably().Build(), false);
                scheduler.ScheduleJob(TriggerBuilder.Create().ForJob(jobName).StartNow().Build());
                while (scheduler.GetCurrentlyExecutingJobs().Count == 0)
                {
                    Thread.Sleep(50);
                }
            }
            finally
            {
                scheduler.Shutdown(false);
            }

            barrier.SignalAndWait(testTimeout);
        }

        [Test]
        public void TestShutdownWithWaitIsClean()
        {
            bool shutdown = false;
            List<DateTime> jobExecTimestamps = new List<DateTime>();
            Barrier barrier = new Barrier(2);
            IScheduler scheduler = CreateScheduler("testShutdownWithoutWaitIsUnclean", 8);
            try
            {
                scheduler.Context.Put(Barrier, barrier);
                scheduler.Context.Put(DateStamps, jobExecTimestamps);
                scheduler.Start();
                string jobName = Guid.NewGuid().ToString();
                scheduler.AddJob(JobBuilder.Create<TestJobWithSync>().WithIdentity(jobName).StoreDurably().Build(), false);
                scheduler.ScheduleJob(TriggerBuilder.Create().ForJob(jobName).StartNow().Build());
                while (scheduler.GetCurrentlyExecutingJobs().Count == 0)
                {
                    Thread.Sleep(50);
                }
            }
            finally
            {
                ThreadStart threadStart = () =>
                                          {
                                              try
                                              {
                                                  scheduler.Shutdown(true);
                                                  shutdown = true;
                                              }
                                              catch (SchedulerException ex)
                                              {
                                                  throw new Exception("exception: " + ex.Message, ex);
                                              }
                                          };

                var t = new Thread(threadStart);
                t.Start();
                Thread.Sleep(1000);
                Assert.That(shutdown, Is.False);
                barrier.SignalAndWait(testTimeout);
                t.Join();
            }
        }
#endif
    }
}