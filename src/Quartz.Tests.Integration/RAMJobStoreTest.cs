using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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

        [DisallowConcurrentExecution]
        [PersistJobDataAfterExecution]
        public class TestAnnotatedJob : IJob
        {
            public void Execute(IJobExecutionContext context)
            {
            }
        }

        protected abstract Task<IScheduler> CreateScheduler(string name, int threadPoolSize);

        [Test]
        public async Task TestBasicStorageFunctions()
        {
            IScheduler sched = await CreateScheduler("testBasicStorageFunctions", 2);
            await sched.ClearAsync();

            // test basic storage functions of scheduler...
            IJobDetail job = JobBuilder.Create<TestJob>()
                .WithIdentity("j1")
                .StoreDurably()
                .Build();

            Assert.That(await sched.CheckExistsAsync(new JobKey("j1")), Is.False, "Unexpected existence of job named 'j1'.");

            await sched.AddJobAsync(job, false);

            Assert.That(await sched.CheckExistsAsync(new JobKey("j1")), "Expected existence of job named 'j1' but checkExists return false.");

            job = await sched.GetJobDetailAsync(new JobKey("j1"));

            Assert.That(job, Is.Not.Null, "Stored job not found!");

            await sched.DeleteJobAsync(new JobKey("j1"));

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("t1")
                .ForJob(job)
                .StartNow()
                .WithSimpleSchedule(x => x
                    .RepeatForever()
                    .WithIntervalInSeconds(5))
                .Build();

            Assert.That(await sched.CheckExistsAsync(new TriggerKey("t1")), Is.False, "Unexpected existence of trigger named '11'.");

            await sched.ScheduleJobAsync(job, trigger);

            Assert.That(await sched.CheckExistsAsync(new TriggerKey("t1")), "Expected existence of trigger named 't1' but checkExists return false.");

            job = await sched.GetJobDetailAsync(new JobKey("j1"));

            Assert.That(job, Is.Not.Null, "Stored job not found!");

            trigger = await sched.GetTriggerAsync(new TriggerKey("t1"));

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

            await sched.ScheduleJobAsync(job, trigger);

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

            await sched.ScheduleJobAsync(job, trigger);

            var jobGroups = await sched.GetJobGroupNamesAsync();
            var triggerGroups = await sched.GetTriggerGroupNamesAsync();

            Assert.That(jobGroups.Count, Is.EqualTo(2), "Job group list size expected to be = 2 ");
            Assert.That(triggerGroups.Count, Is.EqualTo(2), "Trigger group list size expected to be = 2 ");

            ISet<JobKey> jobKeys = await sched.GetJobKeysAsync(GroupMatcher<JobKey>.GroupEquals(JobKey.DefaultGroup));
            ISet<TriggerKey> triggerKeys = await sched.GetTriggerKeysAsync(GroupMatcher<TriggerKey>.GroupEquals(TriggerKey.DefaultGroup));

            Assert.That(jobKeys.Count, Is.EqualTo(1), "Number of jobs expected in default group was 1 ");
            Assert.That(triggerKeys.Count, Is.EqualTo(1), "Number of triggers expected in default group was 1 ");

            jobKeys = await sched.GetJobKeysAsync(GroupMatcher<JobKey>.GroupEquals("g1"));
            triggerKeys = await sched.GetTriggerKeysAsync(GroupMatcher<TriggerKey>.GroupEquals("g1"));

            Assert.That(jobKeys.Count, Is.EqualTo(2), "Number of jobs expected in 'g1' group was 2 ");
            Assert.That(triggerKeys.Count, Is.EqualTo(2), "Number of triggers expected in 'g1' group was 2 ");

            TriggerState s = await sched.GetTriggerStateAsync(new TriggerKey("t2", "g1"));
            Assert.That(s.Equals(TriggerState.Normal), "State of trigger t2 expected to be NORMAL ");

            await sched.PauseTriggerAsync(new TriggerKey("t2", "g1"));
            s = await sched.GetTriggerStateAsync(new TriggerKey("t2", "g1"));
            Assert.That(s.Equals(TriggerState.Paused), "State of trigger t2 expected to be PAUSED ");

            await sched.ResumeTriggerAsync(new TriggerKey("t2", "g1"));
            s = await sched.GetTriggerStateAsync(new TriggerKey("t2", "g1"));
            Assert.That(s.Equals(TriggerState.Normal), "State of trigger t2 expected to be NORMAL ");

            ISet<string> pausedGroups = await sched.GetPausedTriggerGroupsAsync();
            Assert.That(pausedGroups, Is.Empty, "Size of paused trigger groups list expected to be 0 ");

            await sched.PauseTriggersAsync(GroupMatcher<TriggerKey>.GroupEquals("g1"));

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

            await sched.ScheduleJobAsync(job, trigger);

            pausedGroups = await sched.GetPausedTriggerGroupsAsync();
            Assert.That(pausedGroups.Count, Is.EqualTo(1), "Size of paused trigger groups list expected to be 1 ");

            s = await sched.GetTriggerStateAsync(new TriggerKey("t2", "g1"));
            Assert.That(s.Equals(TriggerState.Paused), "State of trigger t2 expected to be PAUSED ");

            s = await sched.GetTriggerStateAsync(new TriggerKey("t4", "g1"));
            Assert.That(s.Equals(TriggerState.Paused), "State of trigger t4 expected to be PAUSED ");

            await sched.ResumeTriggersAsync(GroupMatcher<TriggerKey>.GroupEquals("g1"));
            s = await sched.GetTriggerStateAsync(new TriggerKey("t2", "g1"));
            Assert.That(s.Equals(TriggerState.Normal), "State of trigger t2 expected to be NORMAL ");
            s = await sched.GetTriggerStateAsync(new TriggerKey("t4", "g1"));
            Assert.That(s.Equals(TriggerState.Normal), "State of trigger t4 expected to be NORMAL ");
            pausedGroups = await sched.GetPausedTriggerGroupsAsync();
            Assert.That(pausedGroups, Is.Empty, "Size of paused trigger groups list expected to be 0 ");

            Assert.That(await sched.UnscheduleJobAsync(new TriggerKey("foasldfksajdflk")), Is.False, "Scheduler should have returned 'false' from attempt to unschedule non-existing trigger. ");

            Assert.That(await sched.UnscheduleJobAsync(new TriggerKey("t3", "g1")), "Scheduler should have returned 'true' from attempt to unschedule existing trigger. ");

            jobKeys = await sched.GetJobKeysAsync(GroupMatcher<JobKey>.GroupEquals("g1"));
            triggerKeys = await sched.GetTriggerKeysAsync(GroupMatcher<TriggerKey>.GroupEquals("g1"));

            Assert.That(jobKeys.Count, Is.EqualTo(2), "Number of jobs expected in 'g1' group was 1 "); // job should have been deleted also, because it is non-durable
            Assert.That(triggerKeys.Count, Is.EqualTo(2), "Number of triggers expected in 'g1' group was 1 ");

            Assert.That(await sched.UnscheduleJobAsync(new TriggerKey("t1")), "Scheduler should have returned 'true' from attempt to unschedule existing trigger. ");

            jobKeys = await sched.GetJobKeysAsync(GroupMatcher<JobKey>.GroupEquals(JobKey.DefaultGroup));
            triggerKeys = await sched.GetTriggerKeysAsync(GroupMatcher<TriggerKey>.GroupEquals(TriggerKey.DefaultGroup));

            Assert.That(jobKeys.Count, Is.EqualTo(1), "Number of jobs expected in default group was 1 "); // job should have been left in place, because it is non-durable
            Assert.That(triggerKeys, Is.Empty, "Number of triggers expected in default group was 0 ");

            await sched.ShutdownAsync();
        }

        [Test]
        public async Task TestAbilityToFireImmediatelyWhenStartedBefore()
        {
            List<DateTime> jobExecTimestamps = new List<DateTime>();
            Barrier barrier = new Barrier(2);

            IScheduler sched = await CreateScheduler("testAbilityToFireImmediatelyWhenStartedBefore", 5);
            sched.Context.Put(Barrier, barrier);
            sched.Context.Put(DateStamps, jobExecTimestamps);
            await sched.StartAsync();

            Thread.Yield();

            IJobDetail job1 = JobBuilder.Create<TestJobWithSync>()
                .WithIdentity("job1")
                .Build();

            ITrigger trigger1 = TriggerBuilder.Create()
                .ForJob(job1)
                .Build();

            DateTime sTime = DateTime.UtcNow;

            await sched.ScheduleJobAsync(job1, trigger1);

            barrier.SignalAndWait(testTimeout);

            await sched.ShutdownAsync(false);

            DateTime fTime = jobExecTimestamps[0];

            Assert.That(fTime - sTime < TimeSpan.FromMilliseconds(7000), "Immediate trigger did not fire within a reasonable amount of time.");
        }

        [Test]
        public async Task TestAbilityToFireImmediatelyWhenStartedBeforeWithTriggerJob()
        {
            List<DateTime> jobExecTimestamps = new List<DateTime>();
            Barrier barrier = new Barrier(2);

            IScheduler sched = await CreateScheduler("testAbilityToFireImmediatelyWhenStartedBeforeWithTriggerJob", 5);
            await sched.ClearAsync();

            sched.Context.Put(Barrier, barrier);
            sched.Context.Put(DateStamps, jobExecTimestamps);

            await sched.StartAsync();

            Thread.Yield();

            IJobDetail job1 = JobBuilder.Create<TestJobWithSync>()
                .WithIdentity("job1").
                StoreDurably().Build();
            await sched.AddJobAsync(job1, false);

            DateTime sTime = DateTime.UtcNow;

            await sched.TriggerJobAsync(job1.Key);

            barrier.SignalAndWait(testTimeout);

            await sched.ShutdownAsync(false);

            DateTime fTime = jobExecTimestamps[0];

            Assert.That(fTime - sTime < TimeSpan.FromMilliseconds(7000), "Immediate trigger did not fire within a reasonable amount of time."); // This is dangerously subjective!  but what else to do?
        }

        [Test]
        public async Task TestAbilityToFireImmediatelyWhenStartedAfter()
        {
            List<DateTime> jobExecTimestamps = new List<DateTime>();

            Barrier barrier = new Barrier(2);

            IScheduler sched = await CreateScheduler("testAbilityToFireImmediatelyWhenStartedAfter", 5);
            await sched.ClearAsync();
            sched.Context.Put(Barrier, barrier);
            sched.Context.Put(DateStamps, jobExecTimestamps);

            IJobDetail job1 = JobBuilder.Create<TestJobWithSync>().WithIdentity("job1").Build();
            ITrigger trigger1 = TriggerBuilder.Create().ForJob(job1).Build();

            DateTime sTime = DateTime.UtcNow;

            await sched.ScheduleJobAsync(job1, trigger1);
            await sched.StartAsync();

            barrier.SignalAndWait(testTimeout);

            await sched.ShutdownAsync(false);

            DateTime fTime = jobExecTimestamps[0];

            Assert.That((fTime - sTime < TimeSpan.FromMilliseconds(7000)), "Immediate trigger did not fire within a reasonable amount of time."); // This is dangerously subjective!  but what else to do?
        }

        [Test]
        public async Task TestScheduleMultipleTriggersForAJob()
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

            ISet<ITrigger> triggersForJob = new HashSet<ITrigger>();
            triggersForJob.Add(trigger1);
            triggersForJob.Add(trigger2);

            IScheduler sched = await CreateScheduler("testScheduleMultipleTriggersForAJob", 5);
            await sched.ScheduleJobAsync(job, triggersForJob, true);

            var triggersOfJob = await sched.GetTriggersOfJobAsync(job.Key);
            Assert.That(triggersOfJob.Count, Is.EqualTo(2));
            Assert.That(triggersOfJob.Contains(trigger1));
            Assert.That(triggersOfJob.Contains(trigger2));

            await sched.ShutdownAsync(false);
        }

        [Test]
        public async Task TestDurableStorageFunctions()
        {
            IScheduler sched = await CreateScheduler("testDurableStorageFunctions", 2);
            await sched.ClearAsync();

            // test basic storage functions of scheduler...

            IJobDetail job = JobBuilder.Create<TestJob>()
                .WithIdentity("j1")
                .StoreDurably()
                .Build();

            Assert.That(await sched.CheckExistsAsync(new JobKey("j1")), Is.False, "Unexpected existence of job named 'j1'.");

            await sched.AddJobAsync(job, false);

            Assert.That(await sched.CheckExistsAsync(new JobKey("j1")), "Unexpected non-existence of job named 'j1'.");

            IJobDetail nonDurableJob = JobBuilder.Create<TestJob>()
                .WithIdentity("j2")
                .Build();

            try
            {
                await sched.AddJobAsync(nonDurableJob, false);
                Assert.Fail("Storage of non-durable job should not have succeeded.");
            }
            catch (SchedulerException)
            {
                Assert.That(await sched.CheckExistsAsync(new JobKey("j2")), Is.False, "Unexpected existence of job named 'j2'.");
            }

            await sched.AddJobAsync(nonDurableJob, false, true);

            Assert.That(await sched.CheckExistsAsync(new JobKey("j2")), "Unexpected non-existence of job named 'j2'.");
        }

        [Test]
        public async Task TestShutdownWithoutWaitIsUnclean()
        {
            List<DateTime> jobExecTimestamps = new List<DateTime>();
            Barrier barrier = new Barrier(2);
            IScheduler scheduler = await CreateScheduler("testShutdownWithoutWaitIsUnclean", 8);
            try
            {
                scheduler.Context.Put(Barrier, barrier);
                scheduler.Context.Put(DateStamps, jobExecTimestamps);
                await scheduler.StartAsync();
                string jobName = Guid.NewGuid().ToString();
                await scheduler.AddJobAsync(JobBuilder.Create<TestJobWithSync>().WithIdentity(jobName).StoreDurably().Build(), false);
                await scheduler.ScheduleJobAsync(TriggerBuilder.Create().ForJob(jobName).StartNow().Build());
                while ((await scheduler.GetCurrentlyExecutingJobsAsync()).Count == 0)
                {
                    await Task.Delay(50);
                }
            }
            finally
            {
                await scheduler.ShutdownAsync(false);
            }

            barrier.SignalAndWait(testTimeout);
        }

        [Test]
        public async Task TestShutdownWithWaitIsClean()
        {
            bool shutdown = false;
            List<DateTime> jobExecTimestamps = new List<DateTime>();
            Barrier barrier = new Barrier(2);
            IScheduler scheduler = await CreateScheduler("testShutdownWithoutWaitIsUnclean", 8);
            try
            {
                scheduler.Context.Put(Barrier, barrier);
                scheduler.Context.Put(DateStamps, jobExecTimestamps);
                await scheduler.StartAsync();
                string jobName = Guid.NewGuid().ToString();
                await scheduler.AddJobAsync(JobBuilder.Create<TestJobWithSync>().WithIdentity(jobName).StoreDurably().Build(), false);
                await scheduler.ScheduleJobAsync(TriggerBuilder.Create().ForJob(jobName).StartNow().Build());
                while ((await scheduler.GetCurrentlyExecutingJobsAsync()).Count == 0)
                {
                    await Task.Delay(50);
                }
            }
            finally
            {
                ThreadStart threadStart = () =>
                                          {
                                              try
                                              {
                                                  scheduler.ShutdownAsync(true);
                                                  shutdown = true;
                                              }
                                              catch (SchedulerException ex)
                                              {
                                                  throw new Exception("exception: " + ex.Message, ex);
                                              }
                                          };

                var t = new Thread(threadStart);
                t.Start();
                await Task.Delay(1000);
                Assert.That(shutdown, Is.False);
                barrier.SignalAndWait(testTimeout);
                t.Join();
            }
        }
    }
}