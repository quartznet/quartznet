#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using FakeItEasy;

using NUnit.Framework;

using Quartz.Core;
using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Job;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Core
{
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class QuartzSchedulerTest
    {
        [Test]
        public void TestVersionInfo()
        {
            var versionInfo = typeof(QuartzScheduler).Assembly.GetName().Version;
            Assert.AreEqual(versionInfo.Major.ToString(CultureInfo.InvariantCulture), QuartzScheduler.VersionMajor);
            Assert.AreEqual(versionInfo.Minor.ToString(CultureInfo.InvariantCulture), QuartzScheduler.VersionMinor);
            Assert.AreEqual(versionInfo.Build.ToString(CultureInfo.InvariantCulture), QuartzScheduler.VersionIteration);
        }

        [Test]
        public async Task TestInvalidCalendarScheduling()
        {
            const string ExpectedError = "Calendar not found: FOOBAR";

            NameValueCollection properties = new NameValueCollection();
            properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler sched = await sf.GetScheduler();

            DateTime runTime = DateTime.Now.AddMinutes(10);

            // define the job and tie it to our HelloJob class
            var job = JobBuilder.Create<NoOpJob>()
                                .WithIdentity(new JobKey("job1", "group1"))
                                .Build();

            // Trigger the job to run on the next round minute
            IOperableTrigger trigger = new SimpleTriggerImpl("trigger1", "group1", runTime);

            // set invalid calendar
            trigger.CalendarName = "FOOBAR";

            try
            {
                await sched.ScheduleJob(job, trigger);
                Assert.Fail("No error for non-existing calendar");
            }
            catch (SchedulerException ex)
            {
                Assert.AreEqual(ExpectedError, ex.Message);
            }

            try
            {
                await sched.ScheduleJob(trigger);
                Assert.Fail("No error for non-existing calendar");
            }
            catch (SchedulerException ex)
            {
                Assert.AreEqual(ExpectedError, ex.Message);
            }

            await sched.Shutdown(false);
        }

        [Test]
        public async Task TestStartDelayed()
        {
            NameValueCollection properties = new NameValueCollection();
            properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
            var sf = new StdSchedulerFactory(properties);

            IScheduler sched = await sf.GetScheduler();
            await sched.StartDelayed(TimeSpan.FromMilliseconds(100));
            Assert.IsFalse(sched.IsStarted);
            await Task.Delay(2000);
            Assert.IsTrue(sched.IsStarted);
        }

        [Test]
        public async Task TestRescheduleJob_SchedulerListenersCalledOnReschedule()
        {
            const string TriggerName = "triggerName";
            const string TriggerGroup = "triggerGroup";
            const string JobName = "jobName";
            const string JobGroup = "jobGroup";

            NameValueCollection properties = new NameValueCollection();
            properties["quartz.serializer.type"] = TestConstants.DefaultSerializerType;
            ISchedulerFactory sf = new StdSchedulerFactory(properties);
            IScheduler scheduler = await sf.GetScheduler();
            DateTime startTimeUtc = DateTime.UtcNow.AddSeconds(2);
            var jobDetail = JobBuilder.Create<NoOpJob>()
                                      .WithIdentity(new JobKey(JobName, JobGroup))
                                      .Build();
            SimpleTriggerImpl jobTrigger = new SimpleTriggerImpl(TriggerName, TriggerGroup, JobName, JobGroup, startTimeUtc, null, 1, TimeSpan.FromMilliseconds(1000));

            ISchedulerListener listener = A.Fake<ISchedulerListener>();

            await scheduler.ScheduleJob(jobDetail, jobTrigger);
            // add listener after scheduled
            scheduler.ListenerManager.AddSchedulerListener(listener);

            // act
            await scheduler.RescheduleJob(new TriggerKey(TriggerName, TriggerGroup), jobTrigger);

            // assert
            // expect unschedule and schedule
            A.CallTo(() => listener.JobUnscheduled(new TriggerKey(TriggerName, TriggerGroup), A<CancellationToken>._)).MustHaveHappened();
            A.CallTo(() => listener.JobScheduled(jobTrigger, A<CancellationToken>._)).MustHaveHappened();
        }

        [Test]
        [Ignore("Flaky in CI")]
        public void CurrentlyExecutingJobs()
        {
            IReadOnlyCollection<IJobExecutionContext> executingJobs;

            var scheduler = CreateQuartzScheduler("A", "B", 5);

            executingJobs = scheduler.CurrentlyExecutingJobs;
            Assert.AreEqual(0, executingJobs.Count);

            scheduler.Start().GetAwaiter().GetResult();

            executingJobs = scheduler.CurrentlyExecutingJobs;
            Assert.AreEqual(0, executingJobs.Count);

            ScheduleJobs<DelayedJob>(scheduler, 3, true, false, 1, TimeSpan.FromMilliseconds(1), 1);
            ScheduleJobs<DelayedJob>(scheduler, 1, true, false, 1, TimeSpan.FromMilliseconds(1), 0);

            Thread.Sleep(150);

            executingJobs = scheduler.CurrentlyExecutingJobs;
            Assert.AreEqual(4, executingJobs.Count);

            Thread.Sleep(150);

            executingJobs = scheduler.CurrentlyExecutingJobs;
            Assert.AreEqual(3, executingJobs.Count);

            Thread.Sleep(300);

            executingJobs = scheduler.CurrentlyExecutingJobs;
            Assert.AreEqual(0, executingJobs.Count);

            scheduler.Shutdown(true).GetAwaiter().GetResult();
        }

        [Test]
        [Ignore("Flaky in CI")]
        public void NumJobsExecuted()
        {
            var scheduler = CreateQuartzScheduler("A", "B", 5);

            Assert.AreEqual(0, scheduler.NumJobsExecuted);

            scheduler.Start().GetAwaiter().GetResult();

            Assert.AreEqual(0, scheduler.NumJobsExecuted);

            ScheduleJobs<DelayedJob>(scheduler, 3, true, false, 1, TimeSpan.FromMilliseconds(1), 1);
            ScheduleJobs<DelayedJob>(scheduler, 1, true, false, 1, TimeSpan.FromMilliseconds(1), 0);

            Thread.Sleep(150);

            Assert.AreEqual(4, scheduler.NumJobsExecuted);

            Thread.Sleep(150);

            Assert.AreEqual(7, scheduler.NumJobsExecuted);

            Thread.Sleep(200);

            Assert.AreEqual(7, scheduler.NumJobsExecuted);

            scheduler.Shutdown(true).GetAwaiter().GetResult();
        }

        private static void ScheduleJobs<T>(QuartzScheduler scheduler,
                                            int jobCount,
                                            bool disableConcurrentExecution,
                                            bool persistJobDataAfterExecution,
                                            int triggersPerJob,
                                            TimeSpan repeatInterval,
                                            int repeatCount)
        {
            var triggersByJob = new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>>();

            for (var i = 0; i < jobCount; i++)
            {
                var job = CreateJobDetail(typeof(QuartzSchedulerTest).Name,
                                          typeof(T),
                                          disableConcurrentExecution,
                                          persistJobDataAfterExecution);

                var triggers = new ITrigger[triggersPerJob];
                for (var j = 0; j < triggersPerJob; j++)
                {
                    triggers[j] = CreateTrigger(job, repeatInterval, repeatCount);
                }

                triggersByJob.Add(job, triggers);
            }

            scheduler.ScheduleJobs(triggersByJob, false).GetAwaiter().GetResult();
        }


        private static QuartzScheduler CreateQuartzScheduler(string name, string instanceId, int threadCount)
        {
            var threadPool = new DefaultThreadPool { MaxConcurrency = threadCount };
            threadPool.Initialize();

            QuartzSchedulerResources res = new QuartzSchedulerResources
            {
                Name = name,
                InstanceId = instanceId,
                ThreadPool = threadPool,
                JobRunShellFactory = new StdJobRunShellFactory(),
                JobStore = new RAMJobStore(),
                IdleWaitTime = TimeSpan.FromMilliseconds(10),
                MaxBatchSize = threadCount,
                BatchTimeWindow = TimeSpan.FromMilliseconds(10)
            };

            var scheduler = new QuartzScheduler(res);
            scheduler.JobFactory = new SimpleJobFactory();
            return scheduler;
        }

        private static ITrigger CreateTrigger(IJobDetail job, TimeSpan repeatInterval, int repeatCount)
        {
            return TriggerBuilder.Create()
                                 .ForJob(job)
                                 .WithSimpleSchedule(
                                     sb => sb.WithRepeatCount(repeatCount)
                                             .WithInterval(repeatInterval)
                                             .WithMisfireHandlingInstruction(MisfireInstruction.IgnoreMisfirePolicy))
                                 .Build();
        }

        private static IJobDetail CreateJobDetail(string group,
                                                  Type jobType,
                                                  bool disableConcurrentExecution,
                                                  bool persistJobDataAfterExecution)
        {
            return JobBuilder.Create(jobType)
                             .WithIdentity(Guid.NewGuid().ToString(), group)
                             .DisallowConcurrentExecution(disableConcurrentExecution)
                             .PersistJobDataAfterExecution(persistJobDataAfterExecution)
                             .Build();
        }

        public class DelayedJob : IJob
        {
            private static TimeSpan _delay = TimeSpan.FromMilliseconds(200);

            public async Task Execute(IJobExecutionContext context)
            {
                await Task.Delay(_delay).ConfigureAwait(false);
            }
        }
    }
}