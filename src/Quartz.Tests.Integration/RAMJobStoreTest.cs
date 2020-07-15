﻿using System;
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
            public Task Execute(IJobExecutionContext context)
            {
                return Task.CompletedTask;
            }
        }

        public class TestJob : IJob
        {
            public Task Execute(IJobExecutionContext context)
            {
                return Task.CompletedTask;
            }
        }

        private static readonly TimeSpan testTimeout = TimeSpan.FromSeconds(125);

        public class TestJobWithSync : IJob
        {
            public Task Execute(IJobExecutionContext context)
            {
                try
                {
                    List<DateTime> jobExecTimestamps = (List<DateTime>) context.Scheduler.Context.Get(DateStamps);
                    Barrier barrier = (Barrier) context.Scheduler.Context.Get(Barrier);

                    jobExecTimestamps.Add(DateTime.UtcNow);

                    barrier.SignalAndWait(testTimeout);
                    return Task.CompletedTask;
                }
                catch (Exception e)
                {
                    Console.Write(e);
                    Assert.Fail("Await on barrier was interrupted: " + e);
                }
                return Task.CompletedTask;
            }
        }

        [DisallowConcurrentExecution]
        [PersistJobDataAfterExecution]
        public class TestAnnotatedJob : IJob
        {
            public Task Execute(IJobExecutionContext context)
            {
                return Task.CompletedTask;
            }
        }

        protected abstract Task<IScheduler> CreateScheduler(string name, int threadPoolSize);

        [Test]
        public async Task TestBasicStorageFunctions()
        {
            IScheduler sched = await CreateScheduler("testBasicStorageFunctions", 2);
            await sched.Clear();

            // test basic storage functions of scheduler...
            IJobDetail job = JobBuilder.Create<TestJob>()
                .WithIdentity("j1")
                .StoreDurably()
                .Build();

            Assert.That(await sched.CheckExists(new JobKey("j1")), Is.False, "Unexpected existence of job named 'j1'.");

            await sched.AddJob(job, false);

            Assert.That(await sched.CheckExists(new JobKey("j1")), "Expected existence of job named 'j1' but checkExists return false.");

            job = await sched.GetJobDetail(new JobKey("j1"));

            Assert.That(job, Is.Not.Null, "Stored job not found!");

            await sched.DeleteJob(new JobKey("j1"));

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("t1")
                .ForJob(job)
                .StartNow()
                .WithSimpleSchedule(x => x
                    .RepeatForever()
                    .WithIntervalInSeconds(5))
                .Build();

            Assert.That(await sched.CheckExists(new TriggerKey("t1")), Is.False, "Unexpected existence of trigger named '11'.");

            await sched.ScheduleJob(job, trigger);

            Assert.That(await sched.CheckExists(new TriggerKey("t1")), "Expected existence of trigger named 't1' but checkExists return false.");

            job = await sched.GetJobDetail(new JobKey("j1"));

            Assert.That(job, Is.Not.Null, "Stored job not found!");

            trigger = await sched.GetTrigger(new TriggerKey("t1"));

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

            await sched.ScheduleJob(job, trigger);

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

            await sched.ScheduleJob(job, trigger);

            var jobGroups = await sched.GetJobGroupNames();
            var triggerGroups = await sched.GetTriggerGroupNames();

            Assert.That(jobGroups.Count, Is.EqualTo(2), "Job group list size expected to be = 2 ");
            Assert.That(triggerGroups.Count, Is.EqualTo(2), "Trigger group list size expected to be = 2 ");

            var jobKeys = await sched.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(JobKey.DefaultGroup));
            var triggerKeys = await sched.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(TriggerKey.DefaultGroup));

            Assert.That(jobKeys.Count, Is.EqualTo(1), "Number of jobs expected in default group was 1 ");
            Assert.That(triggerKeys.Count, Is.EqualTo(1), "Number of triggers expected in default group was 1 ");

            jobKeys = await sched.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("g1"));
            triggerKeys = await sched.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("g1"));

            Assert.That(jobKeys.Count, Is.EqualTo(2), "Number of jobs expected in 'g1' group was 2 ");
            Assert.That(triggerKeys.Count, Is.EqualTo(2), "Number of triggers expected in 'g1' group was 2 ");

            TriggerState s = await sched.GetTriggerState(new TriggerKey("t2", "g1"));
            Assert.That(s.Equals(TriggerState.Normal), "State of trigger t2 expected to be NORMAL ");

            await sched.PauseTrigger(new TriggerKey("t2", "g1"));
            s = await sched.GetTriggerState(new TriggerKey("t2", "g1"));
            Assert.That(s.Equals(TriggerState.Paused), "State of trigger t2 expected to be PAUSED ");

            await sched.ResumeTrigger(new TriggerKey("t2", "g1"));
            s = await sched.GetTriggerState(new TriggerKey("t2", "g1"));
            Assert.That(s.Equals(TriggerState.Normal), "State of trigger t2 expected to be NORMAL ");

            var pausedGroups = await sched.GetPausedTriggerGroups();
            Assert.That(pausedGroups, Is.Empty, "Size of paused trigger groups list expected to be 0 ");

            await sched.PauseTriggers(GroupMatcher<TriggerKey>.GroupEquals("g1"));

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

            await sched.ScheduleJob(job, trigger);

            pausedGroups = await sched.GetPausedTriggerGroups();
            Assert.That(pausedGroups.Count, Is.EqualTo(1), "Size of paused trigger groups list expected to be 1 ");

            s = await sched.GetTriggerState(new TriggerKey("t2", "g1"));
            Assert.That(s.Equals(TriggerState.Paused), "State of trigger t2 expected to be PAUSED ");

            s = await sched.GetTriggerState(new TriggerKey("t4", "g1"));
            Assert.That(s.Equals(TriggerState.Paused), "State of trigger t4 expected to be PAUSED ");

            await sched.ResumeTriggers(GroupMatcher<TriggerKey>.GroupEquals("g1"));
            s = await sched.GetTriggerState(new TriggerKey("t2", "g1"));
            Assert.That(s.Equals(TriggerState.Normal), "State of trigger t2 expected to be NORMAL ");
            s = await sched.GetTriggerState(new TriggerKey("t4", "g1"));
            Assert.That(s.Equals(TriggerState.Normal), "State of trigger t4 expected to be NORMAL ");
            pausedGroups = await sched.GetPausedTriggerGroups();
            Assert.That(pausedGroups, Is.Empty, "Size of paused trigger groups list expected to be 0 ");

            Assert.That(await sched.UnscheduleJob(new TriggerKey("foasldfksajdflk")), Is.False, "Scheduler should have returned 'false' from attempt to unschedule non-existing trigger. ");

            Assert.That(await sched.UnscheduleJob(new TriggerKey("t3", "g1")), "Scheduler should have returned 'true' from attempt to unschedule existing trigger. ");

            jobKeys = await sched.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("g1"));
            triggerKeys = await sched.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("g1"));

            Assert.That(jobKeys.Count, Is.EqualTo(2), "Number of jobs expected in 'g1' group was 1 "); // job should have been deleted also, because it is non-durable
            Assert.That(triggerKeys.Count, Is.EqualTo(2), "Number of triggers expected in 'g1' group was 1 ");

            Assert.That(await sched.UnscheduleJob(new TriggerKey("t1")), "Scheduler should have returned 'true' from attempt to unschedule existing trigger. ");

            jobKeys = await sched.GetJobKeys(GroupMatcher<JobKey>.GroupEquals(JobKey.DefaultGroup));
            triggerKeys = await sched.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals(TriggerKey.DefaultGroup));

            Assert.That(jobKeys.Count, Is.EqualTo(1), "Number of jobs expected in default group was 1 "); // job should have been left in place, because it is non-durable
            Assert.That(triggerKeys, Is.Empty, "Number of triggers expected in default group was 0 ");

            await sched.Shutdown();
        }

        [Test]
        public async Task TestAbilityToFireImmediatelyWhenStartedBefore()
        {
            List<DateTime> jobExecTimestamps = new List<DateTime>();
            Barrier barrier = new Barrier(2);

            IScheduler sched = await CreateScheduler("testAbilityToFireImmediatelyWhenStartedBefore", 5);
            sched.Context.Put(Barrier, barrier);
            sched.Context.Put(DateStamps, jobExecTimestamps);
            await sched.Start();

            IJobDetail job1 = JobBuilder.Create<TestJobWithSync>()
                .WithIdentity("job1")
                .Build();

            ITrigger trigger1 = TriggerBuilder.Create()
                .ForJob(job1)
                .Build();

            DateTime sTime = DateTime.UtcNow;

            await sched.ScheduleJob(job1, trigger1);

            barrier.SignalAndWait(testTimeout);

            await sched.Shutdown(false);

            DateTime fTime = jobExecTimestamps[0];

            Assert.That(fTime - sTime < TimeSpan.FromMilliseconds(7000), "Immediate trigger did not fire within a reasonable amount of time.");
        }

        [Test]
        public async Task TestAbilityToFireImmediatelyWhenStartedBeforeWithTriggerJob()
        {
            List<DateTime> jobExecTimestamps = new List<DateTime>();
            Barrier barrier = new Barrier(2);

            IScheduler sched = await CreateScheduler("testAbilityToFireImmediatelyWhenStartedBeforeWithTriggerJob", 5);
            await sched.Clear();

            sched.Context.Put(Barrier, barrier);
            sched.Context.Put(DateStamps, jobExecTimestamps);

            await sched.Start();

            IJobDetail job1 = JobBuilder.Create<TestJobWithSync>()
                .WithIdentity("job1").
                StoreDurably().Build();
            await sched.AddJob(job1, false);

            DateTime sTime = DateTime.UtcNow;

            await sched.TriggerJob(job1.Key);

            barrier.SignalAndWait(testTimeout);

            await sched.Shutdown(false);

            DateTime fTime = jobExecTimestamps[0];

            Assert.That(fTime - sTime < TimeSpan.FromMilliseconds(7000), "Immediate trigger did not fire within a reasonable amount of time."); // This is dangerously subjective!  but what else to do?
        }

        [Test]
        public async Task TestAbilityToFireImmediatelyWhenStartedAfter()
        {
            List<DateTime> jobExecTimestamps = new List<DateTime>();

            Barrier barrier = new Barrier(2);

            IScheduler sched = await CreateScheduler("testAbilityToFireImmediatelyWhenStartedAfter", 5);
            await sched.Clear();
            sched.Context.Put(Barrier, barrier);
            sched.Context.Put(DateStamps, jobExecTimestamps);

            IJobDetail job1 = JobBuilder.Create<TestJobWithSync>().WithIdentity("job1").Build();
            ITrigger trigger1 = TriggerBuilder.Create().ForJob(job1).Build();

            DateTime sTime = DateTime.UtcNow;

            await sched.ScheduleJob(job1, trigger1);
            await sched.Start();

            barrier.SignalAndWait(testTimeout);

            await sched.Shutdown(false);

            DateTime fTime = jobExecTimestamps[0];

            Assert.That(fTime - sTime < TimeSpan.FromMilliseconds(7000), "Immediate trigger did not fire within a reasonable amount of time."); // This is dangerously subjective!  but what else to do?
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

            var triggersForJob = new List<ITrigger>();
            triggersForJob.Add(trigger1);
            triggersForJob.Add(trigger2);

            IScheduler sched = await CreateScheduler("testScheduleMultipleTriggersForAJob", 5);
            await sched.ScheduleJob(job, triggersForJob, true);

            var triggersOfJob = await sched.GetTriggersOfJob(job.Key);
            Assert.That(triggersOfJob.Count, Is.EqualTo(2));
            Assert.That(triggersOfJob.Contains(trigger1));
            Assert.That(triggersOfJob.Contains(trigger2));

            await sched.Shutdown(false);
        }

        [Test]
        public async Task TestDurableStorageFunctions()
        {
            IScheduler sched = await CreateScheduler("testDurableStorageFunctions", 2);
            await sched.Clear();

            // test basic storage functions of scheduler...

            IJobDetail job = JobBuilder.Create<TestJob>()
                .WithIdentity("j1")
                .StoreDurably()
                .Build();

            Assert.That(await sched.CheckExists(new JobKey("j1")), Is.False, "Unexpected existence of job named 'j1'.");

            await sched.AddJob(job, false);

            Assert.That(await sched.CheckExists(new JobKey("j1")), "Unexpected non-existence of job named 'j1'.");

            IJobDetail nonDurableJob = JobBuilder.Create<TestJob>()
                .WithIdentity("j2")
                .Build();

            try
            {
                await sched.AddJob(nonDurableJob, false);
                Assert.Fail("Storage of non-durable job should not have succeeded.");
            }
            catch (SchedulerException)
            {
                Assert.That(await sched.CheckExists(new JobKey("j2")), Is.False, "Unexpected existence of job named 'j2'.");
            }

            await sched.AddJob(nonDurableJob, false, true);

            Assert.That(await sched.CheckExists(new JobKey("j2")), "Unexpected non-existence of job named 'j2'.");
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
                await scheduler.Start();
                string jobName = Guid.NewGuid().ToString();
                await scheduler.AddJob(JobBuilder.Create<TestJobWithSync>().WithIdentity(jobName).StoreDurably().Build(), false);
                await scheduler.ScheduleJob(TriggerBuilder.Create().ForJob(jobName).StartNow().Build());
                while ((await scheduler.GetCurrentlyExecutingJobs()).Count == 0)
                {
                    await Task.Delay(50);
                }
            }
            finally
            {
                await scheduler.Shutdown(false);
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
                await scheduler.Start();
                string jobName = Guid.NewGuid().ToString();
                await scheduler.AddJob(JobBuilder.Create<TestJobWithSync>().WithIdentity(jobName).StoreDurably().Build(), false);
                await scheduler.ScheduleJob(TriggerBuilder.Create().ForJob(jobName).StartNow().Build());
                while ((await scheduler.GetCurrentlyExecutingJobs()).Count == 0)
                {
                    await Task.Delay(50);
                }
            }
            finally
            {
                var task = Task.Run(async () =>
                {
                    try
                    {
                        await scheduler.Shutdown(true);
                        shutdown = true;
                    }
                    catch (SchedulerException ex)
                    {
                        throw new Exception("exception: " + ex.Message, ex);
                    }
                });
                await Task.Delay(1000);
                Assert.That(shutdown, Is.False);
                barrier.SignalAndWait(testTimeout);
                await task;
                Assert.That(shutdown, Is.True);
            }
        }
    }
}