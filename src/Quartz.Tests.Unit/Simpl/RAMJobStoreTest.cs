#region License

/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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
using System.Threading.Tasks;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using Quartz.Job;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Tests.Unit.Simpl
{
    /// <summary>
    ///  Unit test for RAMJobStore.  These tests were submitted by Johannes Zillmann
    /// as part of issue QUARTZ-306.
    /// </summary>
    [TestFixture]
    public class RAMJobStoreTest
    {
        private IJobStore fJobStore;
        private JobDetailImpl fJobDetail;
        private SampleSignaler fSignaler;

        [SetUp]
        public void SetUp()
        {
            fJobStore = new RAMJobStore();
            fSignaler = new SampleSignaler();
            fJobStore.InitializeAsync(null, fSignaler);
            fJobStore.SchedulerStartedAsync();

            fJobDetail = new JobDetailImpl("job1", "jobGroup1", typeof (NoOpJob));
            fJobDetail.Durable = true;
            fJobStore.StoreJobAsync(fJobDetail, false);
        }

        [Test]
        public async Task TestAcquireNextTrigger()
        {
            DateTimeOffset d = DateBuilder.EvenMinuteDateAfterNow();
            IOperableTrigger trigger1 = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, d.AddSeconds(200), d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
            IOperableTrigger trigger2 = new SimpleTriggerImpl("trigger2", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, d.AddSeconds(50), d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
            IOperableTrigger trigger3 = new SimpleTriggerImpl("trigger1", "triggerGroup2", fJobDetail.Name, fJobDetail.Group, d.AddSeconds(100), d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));

            trigger1.ComputeFirstFireTimeUtc(null);
            trigger2.ComputeFirstFireTimeUtc(null);
            trigger3.ComputeFirstFireTimeUtc(null);
            await fJobStore.StoreTriggerAsync(trigger1, false);
            await fJobStore.StoreTriggerAsync(trigger2, false);
            await fJobStore.StoreTriggerAsync(trigger3, false);

            DateTimeOffset firstFireTime = trigger1.GetNextFireTimeUtc().Value;

            Assert.AreEqual(0, (await fJobStore.AcquireNextTriggersAsync(d.AddMilliseconds(10), 1, TimeSpan.Zero)).Count);
            Assert.AreEqual(trigger2, (await fJobStore.AcquireNextTriggersAsync(firstFireTime.AddSeconds(10), 1, TimeSpan.Zero))[0]);
            Assert.AreEqual(trigger3, (await fJobStore.AcquireNextTriggersAsync(firstFireTime.AddSeconds(10), 1, TimeSpan.Zero))[0]);
            Assert.AreEqual(trigger1, (await fJobStore.AcquireNextTriggersAsync(firstFireTime.AddSeconds(10), 1, TimeSpan.Zero))[0]);
            Assert.AreEqual(0, (await fJobStore.AcquireNextTriggersAsync(firstFireTime.AddSeconds(10), 1, TimeSpan.Zero)).Count);


            // release trigger3
            await fJobStore.ReleaseAcquiredTriggerAsync(trigger3);
            Assert.AreEqual(trigger3, (await fJobStore.AcquireNextTriggersAsync(firstFireTime.AddSeconds(10), 1, TimeSpan.FromMilliseconds(1)))[0]);
        }

        [Test]
        public async Task TestAcquireNextTriggerBatch()
        {
            DateTimeOffset d = DateBuilder.EvenMinuteDateAfterNow();
            
            IOperableTrigger early = new SimpleTriggerImpl("early", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, d, d.AddMilliseconds(5), 2, TimeSpan.FromSeconds(2));
            IOperableTrigger trigger1 = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, d.AddMilliseconds(200000), d.AddMilliseconds(200005), 2, TimeSpan.FromSeconds(2));
            IOperableTrigger trigger2 = new SimpleTriggerImpl("trigger2", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, d.AddMilliseconds(200100), d.AddMilliseconds(200105), 2, TimeSpan.FromSeconds(2));
            IOperableTrigger trigger3 = new SimpleTriggerImpl("trigger3", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, d.AddMilliseconds(200200), d.AddMilliseconds(200205), 2, TimeSpan.FromSeconds(2));
            IOperableTrigger trigger4 = new SimpleTriggerImpl("trigger4", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, d.AddMilliseconds(200300), d.AddMilliseconds(200305), 2, TimeSpan.FromSeconds(2));
            IOperableTrigger trigger10 = new SimpleTriggerImpl("trigger10", "triggerGroup2", fJobDetail.Name, fJobDetail.Group, d.AddMilliseconds(500000), d.AddMilliseconds(700000), 2, TimeSpan.FromSeconds(2));

            early.ComputeFirstFireTimeUtc(null);
            trigger1.ComputeFirstFireTimeUtc(null);
            trigger2.ComputeFirstFireTimeUtc(null);
            trigger3.ComputeFirstFireTimeUtc(null);
            trigger4.ComputeFirstFireTimeUtc(null);
            trigger10.ComputeFirstFireTimeUtc(null);
            await fJobStore.StoreTriggerAsync(early, false);
            await fJobStore.StoreTriggerAsync(trigger1, false);
            await fJobStore.StoreTriggerAsync(trigger2, false);
            await fJobStore.StoreTriggerAsync(trigger3, false);
            await fJobStore.StoreTriggerAsync(trigger4, false);
            await fJobStore.StoreTriggerAsync(trigger10, false);

            DateTimeOffset firstFireTime = trigger1.GetNextFireTimeUtc().Value;

            var acquiredTriggers = await fJobStore.AcquireNextTriggersAsync(firstFireTime.AddSeconds(10), 4, TimeSpan.FromSeconds(1));
            Assert.AreEqual(4, acquiredTriggers.Count);
            Assert.AreEqual(early.Key, acquiredTriggers[0].Key);
            Assert.AreEqual(trigger1.Key, acquiredTriggers[1].Key);
            Assert.AreEqual(trigger2.Key, acquiredTriggers[2].Key);
            Assert.AreEqual(trigger3.Key, acquiredTriggers[3].Key);
            await fJobStore.ReleaseAcquiredTriggerAsync(early);
      		await fJobStore.ReleaseAcquiredTriggerAsync(trigger1);
        	await fJobStore.ReleaseAcquiredTriggerAsync(trigger2);
        	await fJobStore.ReleaseAcquiredTriggerAsync(trigger3);
			
            acquiredTriggers = await fJobStore.AcquireNextTriggersAsync(firstFireTime.AddSeconds(10), 5, TimeSpan.FromMilliseconds(1000));
            Assert.AreEqual(5, acquiredTriggers.Count);
            Assert.AreEqual(early.Key, acquiredTriggers[0].Key);
            Assert.AreEqual(trigger1.Key, acquiredTriggers[1].Key);
            Assert.AreEqual(trigger2.Key, acquiredTriggers[2].Key);
            Assert.AreEqual(trigger3.Key, acquiredTriggers[3].Key);
            Assert.AreEqual(trigger4.Key, acquiredTriggers[4].Key);
            await fJobStore.ReleaseAcquiredTriggerAsync(early);
            await fJobStore.ReleaseAcquiredTriggerAsync(trigger1);
            await fJobStore.ReleaseAcquiredTriggerAsync(trigger2);
            await fJobStore.ReleaseAcquiredTriggerAsync(trigger3);
            await fJobStore.ReleaseAcquiredTriggerAsync(trigger4);

            acquiredTriggers = await fJobStore.AcquireNextTriggersAsync(firstFireTime.AddSeconds(10), 6, TimeSpan.FromSeconds(1));
            Assert.AreEqual(5, acquiredTriggers.Count);
            Assert.AreEqual(early.Key, acquiredTriggers[0].Key);
            Assert.AreEqual(trigger1.Key, acquiredTriggers[1].Key);
            Assert.AreEqual(trigger2.Key, acquiredTriggers[2].Key);
            Assert.AreEqual(trigger3.Key, acquiredTriggers[3].Key);
            Assert.AreEqual(trigger4.Key, acquiredTriggers[4].Key);
            await fJobStore.ReleaseAcquiredTriggerAsync(early);
            await fJobStore.ReleaseAcquiredTriggerAsync(trigger1);
            await fJobStore.ReleaseAcquiredTriggerAsync(trigger2);
            await fJobStore.ReleaseAcquiredTriggerAsync(trigger3);
            await fJobStore.ReleaseAcquiredTriggerAsync(trigger4);

            acquiredTriggers = await fJobStore.AcquireNextTriggersAsync(firstFireTime.AddMilliseconds(1), 5, TimeSpan.Zero);
            Assert.AreEqual(2, acquiredTriggers.Count);
            await fJobStore.ReleaseAcquiredTriggerAsync(early);
            await fJobStore.ReleaseAcquiredTriggerAsync(trigger1);

            acquiredTriggers = await fJobStore.AcquireNextTriggersAsync(firstFireTime.AddMilliseconds(250), 5, TimeSpan.FromMilliseconds(199));
            Assert.AreEqual(5, acquiredTriggers.Count);
            await fJobStore.ReleaseAcquiredTriggerAsync(early); 
            await fJobStore.ReleaseAcquiredTriggerAsync(trigger1);
            await fJobStore.ReleaseAcquiredTriggerAsync(trigger2);
            await fJobStore.ReleaseAcquiredTriggerAsync(trigger3);
            await fJobStore.ReleaseAcquiredTriggerAsync(trigger4);

            acquiredTriggers = await fJobStore.AcquireNextTriggersAsync(firstFireTime.AddMilliseconds(150), 5, TimeSpan.FromMilliseconds(50L));
            Assert.AreEqual(4, acquiredTriggers.Count);
            await fJobStore.ReleaseAcquiredTriggerAsync(early);
            await fJobStore.ReleaseAcquiredTriggerAsync(trigger1);
            await fJobStore.ReleaseAcquiredTriggerAsync(trigger2);
            await fJobStore.ReleaseAcquiredTriggerAsync(trigger3);
        }

        [Test]
        public async Task TestTriggerStates()
        {
            IOperableTrigger trigger = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, DateTimeOffset.Now.AddSeconds(100), DateTimeOffset.Now.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
            trigger.ComputeFirstFireTimeUtc(null);
            Assert.AreEqual(TriggerState.None, await fJobStore.GetTriggerStateAsync(trigger.Key));
            await fJobStore.StoreTriggerAsync(trigger, false);
            Assert.AreEqual(TriggerState.Normal, await fJobStore.GetTriggerStateAsync(trigger.Key));

            await fJobStore.PauseTriggerAsync(trigger.Key);
            Assert.AreEqual(TriggerState.Paused, await fJobStore.GetTriggerStateAsync(trigger.Key));

            await fJobStore.ResumeTriggerAsync(trigger.Key);
            Assert.AreEqual(TriggerState.Normal, await fJobStore.GetTriggerStateAsync(trigger.Key));

            trigger = (await fJobStore.AcquireNextTriggersAsync(trigger.GetNextFireTimeUtc().Value.AddSeconds(10), 1, TimeSpan.FromMilliseconds(1)))[0];
            Assert.IsNotNull(trigger);
            await fJobStore.ReleaseAcquiredTriggerAsync(trigger);
            trigger = (await fJobStore.AcquireNextTriggersAsync(trigger.GetNextFireTimeUtc().Value.AddSeconds(10), 1, TimeSpan.FromMilliseconds(1)))[0];
            Assert.IsNotNull(trigger);
            Assert.AreEqual(0, (await fJobStore.AcquireNextTriggersAsync(trigger.GetNextFireTimeUtc().Value.AddSeconds(10), 1, TimeSpan.FromMilliseconds(1))).Count);
        }

        [Test]
        public void TestRemoveCalendarWhenTriggersPresent()
        {
            // QRTZNET-29

            IOperableTrigger trigger = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, DateTimeOffset.Now.AddSeconds(100), DateTimeOffset.Now.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
            trigger.ComputeFirstFireTimeUtc(null);
            ICalendar cal = new MonthlyCalendar();
            fJobStore.StoreTriggerAsync(trigger, false);
            fJobStore.StoreCalendarAsync("cal", cal, false, true);

            fJobStore.RemoveCalendarAsync("cal");
        }

        [Test]
        public async Task TestStoreTriggerReplacesTrigger()
        {
            string jobName = "StoreTriggerReplacesTrigger";
            string jobGroup = "StoreTriggerReplacesTriggerGroup";
            JobDetailImpl detail = new JobDetailImpl(jobName, jobGroup, typeof (NoOpJob));
            await fJobStore.StoreJobAsync(detail, false);

            string trName = "StoreTriggerReplacesTrigger";
            string trGroup = "StoreTriggerReplacesTriggerGroup";
            IOperableTrigger tr = new SimpleTriggerImpl(trName, trGroup, DateTimeOffset.Now);
            tr.JobKey = new JobKey(jobName, jobGroup);
            tr.CalendarName = null;

            await fJobStore.StoreTriggerAsync(tr, false);
            Assert.AreEqual(tr, await fJobStore.RetrieveTriggerAsync(new TriggerKey(trName, trGroup)));

            tr.CalendarName = "NonExistingCalendar";
            await fJobStore.StoreTriggerAsync(tr, true);
            Assert.AreEqual(tr, await fJobStore.RetrieveTriggerAsync(new TriggerKey(trName, trGroup)));
            var trigger = await fJobStore.RetrieveTriggerAsync(new TriggerKey(trName, trGroup));
            Assert.AreEqual(tr.CalendarName, trigger.CalendarName, "StoreJob doesn't replace triggers");

            bool exceptionRaised = false;
            try
            {
                await fJobStore.StoreTriggerAsync(tr, false);
            }
            catch (ObjectAlreadyExistsException)
            {
                exceptionRaised = true;
            }
            Assert.IsTrue(exceptionRaised, "an attempt to store duplicate trigger succeeded");
        }

        [Test]
        public async Task PauseJobGroupPausesNewJob()
        {
            string jobName1 = "PauseJobGroupPausesNewJob";
            string jobName2 = "PauseJobGroupPausesNewJob2";
            string jobGroup = "PauseJobGroupPausesNewJobGroup";
            JobDetailImpl detail = new JobDetailImpl(jobName1, jobGroup, typeof (NoOpJob));
            detail.Durable = true;
            await fJobStore.StoreJobAsync(detail, false);
            await fJobStore.PauseJobsAsync(GroupMatcher<JobKey>.GroupEquals(jobGroup));

            detail = new JobDetailImpl(jobName2, jobGroup, typeof (NoOpJob));
            detail.Durable = true;
            await fJobStore.StoreJobAsync(detail, false);

            string trName = "PauseJobGroupPausesNewJobTrigger";
            string trGroup = "PauseJobGroupPausesNewJobTriggerGroup";
            IOperableTrigger tr = new SimpleTriggerImpl(trName, trGroup, DateTimeOffset.UtcNow);
            tr.JobKey = new JobKey(jobName2, jobGroup);
            await fJobStore.StoreTriggerAsync(tr, false);
            Assert.AreEqual(TriggerState.Paused, await fJobStore.GetTriggerStateAsync(tr.Key));
        }

        [Test]
        public async Task TestRetrieveJob_NoJobFound()
        {
            RAMJobStore store = new RAMJobStore();
            IJobDetail job = await store.RetrieveJobAsync(new JobKey("not", "existing"));
            Assert.IsNull(job);
        }

        [Test]
        public async Task TestRetrieveTrigger_NoTriggerFound()
        {
            RAMJobStore store = new RAMJobStore();
            IOperableTrigger trigger = await store.RetrieveTriggerAsync(new TriggerKey("not", "existing"));
            Assert.IsNull(trigger);
        }

        [Test]
        public async Task testStoreAndRetrieveJobs()
        {
            RAMJobStore store = new RAMJobStore();

            // Store jobs.
            for (int i = 0; i < 10; i++)
            {
                IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job" + i).Build();
                await store.StoreJobAsync(job, false);
            }
            // Retrieve jobs.
            for (int i = 0; i < 10; i++)
            {
                JobKey jobKey = JobKey.Create("job" + i);
                IJobDetail storedJob = await store.RetrieveJobAsync(jobKey);
                Assert.AreEqual(jobKey, storedJob.Key);
            }
        }

        [Test]
        public async Task TestStoreAndRetrieveTriggers()
        {
            RAMJobStore store = new RAMJobStore();

            // Store jobs and triggers.
            for (int i = 0; i < 10; i++)
            {
                IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job" + i).Build();
                await store.StoreJobAsync(job, true);
                SimpleScheduleBuilder schedule = SimpleScheduleBuilder.Create();
                ITrigger trigger = TriggerBuilder.Create().WithIdentity("job" + i).WithSchedule(schedule).ForJob(job).Build();
                await store.StoreTriggerAsync((IOperableTrigger)trigger, true);
            }
            // Retrieve job and trigger.
            for (int i = 0; i < 10; i++)
            {
                JobKey jobKey = JobKey.Create("job" + i);
                IJobDetail storedJob = await store.RetrieveJobAsync(jobKey);
                Assert.AreEqual(jobKey, storedJob.Key);

                TriggerKey triggerKey = new TriggerKey("job" + i);
                ITrigger storedTrigger = await store.RetrieveTriggerAsync(triggerKey);
                Assert.AreEqual(triggerKey, storedTrigger.Key);
            }
        }

        [Test]
        public async Task TestAcquireTriggers()
        {
            ISchedulerSignaler schedSignaler = new SampleSignaler();
            ITypeLoadHelper loadHelper = new SimpleTypeLoadHelper();
            loadHelper.Initialize();

            RAMJobStore store = new RAMJobStore();
            await store.InitializeAsync(loadHelper, schedSignaler);

            // Setup: Store jobs and triggers.
            DateTime startTime0 = DateTime.UtcNow.AddMinutes(1).ToUniversalTime(); // a min from now.
            for (int i = 0; i < 10; i++)
            {
                DateTime startTime = startTime0.AddMinutes(i*1); // a min apart
                IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job" + i).Build();
                SimpleScheduleBuilder schedule = SimpleScheduleBuilder.RepeatMinutelyForever(2);
                IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create().WithIdentity("job" + i).WithSchedule(schedule).ForJob(job).StartAt(startTime).Build();

                // Manually trigger the first fire time computation that scheduler would do. Otherwise 
                // the store.acquireNextTriggers() will not work properly.
                DateTimeOffset? fireTime = trigger.ComputeFirstFireTimeUtc(null);
                Assert.AreEqual(true, fireTime != null);

                await store.StoreJobAndTriggerAsync(job, trigger);
            }

            // Test acquire one trigger at a time
            for (int i = 0; i < 10; i++)
            {
                DateTimeOffset noLaterThan = startTime0.AddMinutes(i);
                int maxCount = 1;
                TimeSpan timeWindow = TimeSpan.Zero;
                var triggers = await store.AcquireNextTriggersAsync(noLaterThan, maxCount, timeWindow);
                Assert.AreEqual(1, triggers.Count);
                Assert.AreEqual("job" + i, triggers[0].Key.Name);

                // Let's remove the trigger now.
                await store.RemoveJobAsync(triggers[0].JobKey);
            }
        }

        [Test]
        public async Task TestAcquireTriggersInBatch()
        {
            ISchedulerSignaler schedSignaler = new SampleSignaler();
            ITypeLoadHelper loadHelper = new SimpleTypeLoadHelper();
            loadHelper.Initialize();

            RAMJobStore store = new RAMJobStore();
            await store.InitializeAsync(loadHelper, schedSignaler);

            // Setup: Store jobs and triggers.
            DateTimeOffset startTime0 = DateTimeOffset.UtcNow.AddMinutes(1); // a min from now.
            for (int i = 0; i < 10; i++)
            {
                DateTimeOffset startTime = startTime0.AddMinutes(i); // a min apart
                IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job" + i).Build();
                SimpleScheduleBuilder schedule = SimpleScheduleBuilder.RepeatMinutelyForever(2);
                IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create().WithIdentity("job" + i).WithSchedule(schedule).ForJob(job).StartAt(startTime).Build();

                // Manually trigger the first fire time computation that scheduler would do. Otherwise 
                // the store.acquireNextTriggers() will not work properly.
                DateTimeOffset? fireTime = trigger.ComputeFirstFireTimeUtc(null);
                Assert.AreEqual(true, fireTime != null);

                await store.StoreJobAndTriggerAsync(job, trigger);
            }

            // Test acquire batch of triggers at a time
            DateTimeOffset noLaterThan = startTime0.AddMinutes(10);
            int maxCount = 7;
            TimeSpan timeWindow = TimeSpan.FromMinutes(8);
            var triggers = await store.AcquireNextTriggersAsync(noLaterThan, maxCount, timeWindow);
            Assert.AreEqual(7, triggers.Count);
            for (int i = 0; i < 7; i++)
            {
                Assert.AreEqual("job" + i, triggers[i].Key.Name);
            }
        }

        public class SampleSignaler : ISchedulerSignaler
        {
            internal int fMisfireCount = 0;

            public Task NotifyTriggerListenersMisfiredAsync(ITrigger trigger)
            {
                fMisfireCount++;
                return TaskUtil.CompletedTask;
            }

            public Task NotifySchedulerListenersFinalizedAsync(ITrigger trigger)
            {
                return TaskUtil.CompletedTask;
            }

            public void SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc)
            {
            }

            public Task NotifySchedulerListenersErrorAsync(string message, SchedulerException jpe)
            {
                return TaskUtil.CompletedTask;
            }

            public Task NotifySchedulerListenersJobDeletedAsync(JobKey jobKey)
            {
                return TaskUtil.CompletedTask;
            }
        }
    }
}