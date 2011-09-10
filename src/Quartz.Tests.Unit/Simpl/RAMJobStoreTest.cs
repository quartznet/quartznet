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
using System.Collections.Generic;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Matchers;
using Quartz.Impl.Triggers;
using Quartz.Job;
using Quartz.Simpl;
using Quartz.Spi;

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
            fJobStore.Initialize(null, fSignaler);

            fJobDetail = new JobDetailImpl("job1", "jobGroup1", typeof (NoOpJob));
            fJobDetail.Durable = true;
            fJobStore.StoreJob(fJobDetail, false);
        }

        [Test]
        public void TestAcquireNextTrigger()
        {
            DateTimeOffset d = DateTimeOffset.UtcNow;
            IOperableTrigger trigger1 = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, d.AddSeconds(200), d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
            IOperableTrigger trigger2 = new SimpleTriggerImpl("trigger2", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, d.AddSeconds(-100), d.AddSeconds(20), 2, TimeSpan.FromSeconds(2));
            IOperableTrigger trigger3 = new SimpleTriggerImpl("trigger1", "triggerGroup2", fJobDetail.Name, fJobDetail.Group, d.AddSeconds(100), d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));

            trigger1.ComputeFirstFireTimeUtc(null);
            trigger2.ComputeFirstFireTimeUtc(null);
            trigger3.ComputeFirstFireTimeUtc(null);
            fJobStore.StoreTrigger(trigger1, false);
            fJobStore.StoreTrigger(trigger2, false);
            fJobStore.StoreTrigger(trigger3, false);

            Assert.AreEqual(0, fJobStore.AcquireNextTriggers(d.AddMilliseconds(10), 1, TimeSpan.FromMilliseconds(1)).Count);
            Assert.AreEqual(trigger2, fJobStore.AcquireNextTriggers(trigger1.GetNextFireTimeUtc().Value.AddSeconds(10), 1, TimeSpan.FromMilliseconds(1))[0]);
            Assert.AreEqual(trigger3, fJobStore.AcquireNextTriggers(trigger1.GetNextFireTimeUtc().Value.AddSeconds(10), 1, TimeSpan.FromMilliseconds(1))[0]);
            Assert.AreEqual(trigger1, fJobStore.AcquireNextTriggers(trigger1.GetNextFireTimeUtc().Value.AddSeconds(10), 1, TimeSpan.FromMilliseconds(1))[0]);
            Assert.AreEqual(0, fJobStore.AcquireNextTriggers(trigger1.GetNextFireTimeUtc().Value.AddSeconds(10), 1, TimeSpan.FromMilliseconds(1)).Count);

            // because of trigger2
            Assert.AreEqual(1, fSignaler.fMisfireCount);

            // release trigger3
            fJobStore.ReleaseAcquiredTrigger(trigger3);
            Assert.AreEqual(trigger3, fJobStore.AcquireNextTriggers(trigger1.GetNextFireTimeUtc().Value.AddSeconds(10), 1, TimeSpan.FromMilliseconds(1))[0]);
        }

        [Test]
        public void TestAcquireNextTriggerBatch()
        {
            IOperableTrigger trigger1 = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, DateTimeOffset.UtcNow.AddSeconds(200), DateTimeOffset.UtcNow.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
            IOperableTrigger trigger2 = new SimpleTriggerImpl("trigger2", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, DateTimeOffset.UtcNow.AddMilliseconds(200100), DateTimeOffset.UtcNow.AddMilliseconds(200100), 2, TimeSpan.FromSeconds(2));
            IOperableTrigger trigger3 = new SimpleTriggerImpl("trigger3", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, DateTimeOffset.UtcNow.AddMilliseconds(200200), DateTimeOffset.UtcNow.AddMilliseconds(200200), 2, TimeSpan.FromSeconds(2));
            IOperableTrigger trigger4 = new SimpleTriggerImpl("trigger4", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, DateTimeOffset.UtcNow.AddMilliseconds(200300), DateTimeOffset.UtcNow.AddMilliseconds(200300), 2, TimeSpan.FromSeconds(2));

            IOperableTrigger trigger10 = new SimpleTriggerImpl("trigger10", "triggerGroup2", fJobDetail.Name, fJobDetail.Group, DateTimeOffset.UtcNow.AddSeconds(500), DateTimeOffset.UtcNow.AddSeconds(700), 2, TimeSpan.FromSeconds(2));

            trigger1.ComputeFirstFireTimeUtc(null);
            trigger2.ComputeFirstFireTimeUtc(null);
            trigger3.ComputeFirstFireTimeUtc(null);
            trigger4.ComputeFirstFireTimeUtc(null);
            trigger10.ComputeFirstFireTimeUtc(null);
            fJobStore.StoreTrigger(trigger1, false);
            fJobStore.StoreTrigger(trigger2, false);
            fJobStore.StoreTrigger(trigger3, false);
            fJobStore.StoreTrigger(trigger4, false);
            fJobStore.StoreTrigger(trigger10, false);

            Assert.AreEqual(3, this.fJobStore.AcquireNextTriggers(trigger1.GetNextFireTimeUtc().Value.AddSeconds(10), 3, TimeSpan.FromSeconds(1)).Count);
            fJobStore.ReleaseAcquiredTrigger(trigger1);
            fJobStore.ReleaseAcquiredTrigger(trigger2);
            fJobStore.ReleaseAcquiredTrigger(trigger3);

            Assert.AreEqual(4, this.fJobStore.AcquireNextTriggers(trigger1.GetNextFireTimeUtc().Value.AddSeconds(10), 4, TimeSpan.FromSeconds(1)).Count);
            fJobStore.ReleaseAcquiredTrigger(trigger1);
            fJobStore.ReleaseAcquiredTrigger(trigger2);
            fJobStore.ReleaseAcquiredTrigger(trigger3);
            fJobStore.ReleaseAcquiredTrigger(trigger4);

            Assert.AreEqual(4, this.fJobStore.AcquireNextTriggers(trigger1.GetNextFireTimeUtc().Value.AddSeconds(10), 5, TimeSpan.FromSeconds(1)).Count);
            fJobStore.ReleaseAcquiredTrigger(trigger1);
            fJobStore.ReleaseAcquiredTrigger(trigger2);
            fJobStore.ReleaseAcquiredTrigger(trigger3);
            fJobStore.ReleaseAcquiredTrigger(trigger4);

            Assert.AreEqual(1, this.fJobStore.AcquireNextTriggers(trigger1.GetNextFireTimeUtc().Value.AddSeconds(0), 5, TimeSpan.Zero).Count);
            fJobStore.ReleaseAcquiredTrigger(trigger1);

            Assert.AreEqual(2, this.fJobStore.AcquireNextTriggers(trigger1.GetNextFireTimeUtc().Value.AddMilliseconds(150), 5, TimeSpan.Zero).Count);
            fJobStore.ReleaseAcquiredTrigger(trigger1);
            fJobStore.ReleaseAcquiredTrigger(trigger2);
        }

        [Test]
        public void TestTriggerStates()
        {
            IOperableTrigger trigger = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, DateTimeOffset.Now.AddSeconds(100), DateTimeOffset.Now.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
            trigger.ComputeFirstFireTimeUtc(null);
            Assert.AreEqual(TriggerState.None, fJobStore.GetTriggerState(trigger.Key));
            fJobStore.StoreTrigger(trigger, false);
            Assert.AreEqual(TriggerState.Normal, fJobStore.GetTriggerState(trigger.Key));

            fJobStore.PauseTrigger(trigger.Key);
            Assert.AreEqual(TriggerState.Paused, fJobStore.GetTriggerState(trigger.Key));

            fJobStore.ResumeTrigger(trigger.Key);
            Assert.AreEqual(TriggerState.Normal, fJobStore.GetTriggerState(trigger.Key));

            trigger = fJobStore.AcquireNextTriggers(trigger.GetNextFireTimeUtc().Value.AddSeconds(10), 1, TimeSpan.FromMilliseconds(1))[0];
            Assert.IsNotNull(trigger);
            fJobStore.ReleaseAcquiredTrigger(trigger);
            trigger = fJobStore.AcquireNextTriggers(trigger.GetNextFireTimeUtc().Value.AddSeconds(10), 1, TimeSpan.FromMilliseconds(1))[0];
            Assert.IsNotNull(trigger);
            Assert.AreEqual(0, fJobStore.AcquireNextTriggers(trigger.GetNextFireTimeUtc().Value.AddSeconds(10), 1, TimeSpan.FromMilliseconds(1)).Count);
        }

        [Test]
        public void TestRemoveCalendarWhenTriggersPresent()
        {
            // QRTZNET-29

            IOperableTrigger trigger = new SimpleTriggerImpl("trigger1", "triggerGroup1", fJobDetail.Name, fJobDetail.Group, DateTimeOffset.Now.AddSeconds(100), DateTimeOffset.Now.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
            trigger.ComputeFirstFireTimeUtc(null);
            ICalendar cal = new MonthlyCalendar();
            fJobStore.StoreTrigger(trigger, false);
            fJobStore.StoreCalendar("cal", cal, false, true);

            fJobStore.RemoveCalendar("cal");
        }

        [Test]
        public void TestStoreTriggerReplacesTrigger()
        {
            string jobName = "StoreTriggerReplacesTrigger";
            string jobGroup = "StoreTriggerReplacesTriggerGroup";
            JobDetailImpl detail = new JobDetailImpl(jobName, jobGroup, typeof (NoOpJob));
            fJobStore.StoreJob(detail, false);

            string trName = "StoreTriggerReplacesTrigger";
            string trGroup = "StoreTriggerReplacesTriggerGroup";
            IOperableTrigger tr = new SimpleTriggerImpl(trName, trGroup, DateTimeOffset.Now);
            tr.JobKey = new JobKey(jobName, jobGroup);
            tr.CalendarName = null;

            fJobStore.StoreTrigger(tr, false);
            Assert.AreEqual(tr, fJobStore.RetrieveTrigger(new TriggerKey(trName, trGroup)));

            tr.CalendarName = "NonExistingCalendar";
            fJobStore.StoreTrigger(tr, true);
            Assert.AreEqual(tr, fJobStore.RetrieveTrigger(new TriggerKey(trName, trGroup)));
            Assert.AreEqual(tr.CalendarName, fJobStore.RetrieveTrigger(new TriggerKey(trName, trGroup)).CalendarName, "StoreJob doesn't replace triggers");

            bool exeptionRaised = false;
            try
            {
                fJobStore.StoreTrigger(tr, false);
            }
            catch (ObjectAlreadyExistsException)
            {
                exeptionRaised = true;
            }
            Assert.IsTrue(exeptionRaised, "an attempt to store duplicate trigger succeeded");
        }

        [Test]
        public void PauseJobGroupPausesNewJob()
        {
            string jobName1 = "PauseJobGroupPausesNewJob";
            string jobName2 = "PauseJobGroupPausesNewJob2";
            string jobGroup = "PauseJobGroupPausesNewJobGroup";
            JobDetailImpl detail = new JobDetailImpl(jobName1, jobGroup, typeof (NoOpJob));
            detail.Durable = true;
            fJobStore.StoreJob(detail, false);
            fJobStore.PauseJobs(GroupMatcher<JobKey>.GroupEquals(jobGroup));

            detail = new JobDetailImpl(jobName2, jobGroup, typeof (NoOpJob));
            detail.Durable = true;
            fJobStore.StoreJob(detail, false);

            string trName = "PauseJobGroupPausesNewJobTrigger";
            string trGroup = "PauseJobGroupPausesNewJobTriggerGroup";
            IOperableTrigger tr = new SimpleTriggerImpl(trName, trGroup, DateTimeOffset.UtcNow);
            tr.JobKey = new JobKey(jobName2, jobGroup);
            fJobStore.StoreTrigger(tr, false);
            Assert.AreEqual(TriggerState.Paused, fJobStore.GetTriggerState(tr.Key));
        }

        [Test]
        public void TestRetrieveJob_NoJobFound()
        {
            RAMJobStore store = new RAMJobStore();
            IJobDetail job = store.RetrieveJob(new JobKey("not", "existing"));
            Assert.IsNull(job);
        }

        [Test]
        public void TestRetrieveTrigger_NoTriggerFound()
        {
            RAMJobStore store = new RAMJobStore();
            IOperableTrigger trigger = store.RetrieveTrigger(new TriggerKey("not", "existing"));
            Assert.IsNull(trigger);
        }

        [Test]
        public void testStoreAndRetrieveJobs()
        {
            RAMJobStore store = new RAMJobStore();

            // Store jobs.
            for (int i = 0; i < 10; i++)
            {
                IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job" + i).Build();
                store.StoreJob(job, false);
            }
            // Retrieve jobs.
            for (int i = 0; i < 10; i++)
            {
                JobKey jobKey = JobKey.Create("job" + i);
                IJobDetail storedJob = store.RetrieveJob(jobKey);
                Assert.AreEqual(jobKey, storedJob.Key);
            }
        }

        [Test]
        public void TestStoreAndRetriveTriggers()
        {
            RAMJobStore store = new RAMJobStore();

            // Store jobs and triggers.
            for (int i = 0; i < 10; i++)
            {
                IJobDetail job = JobBuilder.Create<NoOpJob>().WithIdentity("job" + i).Build();
                store.StoreJob(job, true);
                SimpleScheduleBuilder schedule = SimpleScheduleBuilder.Create();
                ITrigger trigger = TriggerBuilder.Create().WithIdentity("job" + i).WithSchedule(schedule).ForJob(job).Build();
                store.StoreTrigger((IOperableTrigger) trigger, true);
            }
            // Retrieve job and trigger.
            for (int i = 0; i < 10; i++)
            {
                JobKey jobKey = JobKey.Create("job" + i);
                IJobDetail storedJob = store.RetrieveJob(jobKey);
                Assert.AreEqual(jobKey, storedJob.Key);

                TriggerKey triggerKey = new TriggerKey("job" + i);
                ITrigger storedTrigger = store.RetrieveTrigger(triggerKey);
                Assert.AreEqual(triggerKey, storedTrigger.Key);
            }
        }

        [Test]
        public void TestAcquireTriggers()
        {
            ISchedulerSignaler schedSignaler = new SampleSignaler();
            ITypeLoadHelper loadHelper = new SimpleTypeLoadHelper();
            loadHelper.Initialize();

            RAMJobStore store = new RAMJobStore();
            store.Initialize(loadHelper, schedSignaler);

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

                store.StoreJobAndTrigger(job, trigger);
            }

            // Test acquire one trigger at a time
            for (int i = 0; i < 10; i++)
            {
                DateTimeOffset noLaterThan = startTime0.AddMinutes(i);
                int maxCount = 1;
                TimeSpan timeWindow = TimeSpan.Zero;
                IList<IOperableTrigger> triggers = store.AcquireNextTriggers(noLaterThan, maxCount, timeWindow);
                Assert.AreEqual(1, triggers.Count);
                Assert.AreEqual("job" + i, triggers[0].Key.Name);

                // Let's remove the trigger now.
                store.RemoveJob(triggers[0].JobKey);
            }
        }

        [Test]
        public void TestAcquireTriggersInBatch()
        {
            ISchedulerSignaler schedSignaler = new SampleSignaler();
            ITypeLoadHelper loadHelper = new SimpleTypeLoadHelper();
            loadHelper.Initialize();

            RAMJobStore store = new RAMJobStore();
            store.Initialize(loadHelper, schedSignaler);

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

                store.StoreJobAndTrigger(job, trigger);
            }

            // Test acquire batch of triggers at a time
            DateTimeOffset noLaterThan = startTime0.AddMinutes(10);
            int maxCount = 7;
            TimeSpan timeWindow = TimeSpan.Zero;
            IList<IOperableTrigger> triggers = store.AcquireNextTriggers(noLaterThan, maxCount, timeWindow);
            Assert.AreEqual(7, triggers.Count);
            for (int i = 0; i < 7; i++)
            {
                Assert.AreEqual("job" + i, triggers[i].Key.Name);
            }
        }

        public class SampleSignaler : ISchedulerSignaler
        {
            internal int fMisfireCount = 0;

            public void NotifyTriggerListenersMisfired(ITrigger trigger)
            {
                fMisfireCount++;
            }

            public void NotifySchedulerListenersFinalized(ITrigger trigger)
            {
            }

            public void SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc)
            {
            }

            public void NotifySchedulerListenersJobDeleted(JobKey jobKey)
            {
            }
        }
    }
}