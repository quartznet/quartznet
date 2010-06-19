#region License

/* 
 * Copyright 2001-2009 Terracotta, Inc. 
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
using NUnit.Framework;
using Quartz.Impl.Calendar;
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
        private JobDetail fJobDetail;
        private SampleSignaler fSignaler;

        [SetUp]
        public void SetUp()
        {
            fJobStore = new RAMJobStore();
            fSignaler = new SampleSignaler();
            fJobStore.Initialize(null, fSignaler);

            fJobDetail = new JobDetail("job1", "jobGroup1", typeof (NoOpJob));
            fJobDetail.Durable = true;
            fJobStore.StoreJob(null, fJobDetail, false);
        }

        [Test]
        public void TestAcquireNextTrigger()
        {
            DateTime d = DateTime.UtcNow;
            Trigger trigger1 =
                new SimpleTrigger("trigger1", "triggerGroup1", fJobDetail.Name,
                                  fJobDetail.Group, d.AddSeconds(200),
                                  d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
            Trigger trigger2 =
                new SimpleTrigger("trigger2", "triggerGroup1", fJobDetail.Name,
                                  fJobDetail.Group, d.AddSeconds(-100),
                                  d.AddSeconds(20), 2, TimeSpan.FromSeconds(2));
            Trigger trigger3 =
                new SimpleTrigger("trigger1", "triggerGroup2", fJobDetail.Name,
                                  fJobDetail.Group, d.AddSeconds(100),
                                  d.AddSeconds(200), 2, TimeSpan.FromSeconds(2));

            trigger1.ComputeFirstFireTimeUtc(null);
            trigger2.ComputeFirstFireTimeUtc(null);
            trigger3.ComputeFirstFireTimeUtc(null);
            fJobStore.StoreTrigger(null, trigger1, false);
            fJobStore.StoreTrigger(null, trigger2, false);
            fJobStore.StoreTrigger(null, trigger3, false);

            Assert.AreEqual(0, fJobStore.AcquireNextTriggers(null, d.AddMilliseconds(10), 1, TimeSpan.FromMilliseconds(1)).Count);
            Assert.AreEqual(
                trigger2,
                fJobStore.AcquireNextTriggers(null, trigger1.GetNextFireTimeUtc().Value.AddSeconds(10), 1,
                                              TimeSpan.FromMilliseconds(1))[0]);
            Assert.AreEqual(
                trigger3,
                fJobStore.AcquireNextTriggers(null, trigger1.GetNextFireTimeUtc().Value.AddSeconds(10), 1,
                                              TimeSpan.FromMilliseconds(1))[0]);
            Assert.AreEqual(
                trigger1,
                fJobStore.AcquireNextTriggers(null, trigger1.GetNextFireTimeUtc().Value.AddSeconds(10), 1,
                                              TimeSpan.FromMilliseconds(1))[0]);
            Assert.AreEqual(0,
                            fJobStore.AcquireNextTriggers(null, trigger1.GetNextFireTimeUtc().Value.AddSeconds(10), 1,
                                                          TimeSpan.FromMilliseconds(1)).Count);

            // because of trigger2
            Assert.AreEqual(1, fSignaler.fMisfireCount);

            // release trigger3
            fJobStore.ReleaseAcquiredTrigger(null, trigger3);
            Assert.AreEqual(
                trigger3,
                fJobStore.AcquireNextTriggers(null, trigger1.GetNextFireTimeUtc().Value.AddSeconds(10), 1,
                                              TimeSpan.FromMilliseconds(1))[0]);
        }

        [Test]
        public void TestAcquireNextTriggerBatch()
        {
            Trigger trigger1 =
                new SimpleTrigger("trigger1", "triggerGroup1", this.fJobDetail.Name,
                                  this.fJobDetail.Group, DateTime.UtcNow.AddSeconds(200),
                                  DateTime.UtcNow.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
            Trigger trigger2 =
                new SimpleTrigger("trigger2", "triggerGroup1", this.fJobDetail.Name,
                                  this.fJobDetail.Group, DateTime.UtcNow.AddMilliseconds(200100),
                                  DateTime.UtcNow.AddMilliseconds(200100), 2, TimeSpan.FromSeconds(2));
            Trigger trigger3 =
                new SimpleTrigger("trigger3", "triggerGroup1", this.fJobDetail.Name,
                                  this.fJobDetail.Group, DateTime.UtcNow.AddMilliseconds(200200),
                                  DateTime.UtcNow.AddMilliseconds(200200), 2, TimeSpan.FromSeconds(2));
            Trigger trigger4 =
                new SimpleTrigger("trigger4", "triggerGroup1", this.fJobDetail.Name,
                                  this.fJobDetail.Group, DateTime.UtcNow.AddMilliseconds(200300),
                                  DateTime.UtcNow.AddMilliseconds(200300), 2, TimeSpan.FromSeconds(2));

            Trigger trigger10 =
                new SimpleTrigger("trigger10", "triggerGroup2", this.fJobDetail.Name,
                                  this.fJobDetail.Group, DateTime.UtcNow.AddSeconds(500),
                                  DateTime.UtcNow.AddSeconds(700), 2, TimeSpan.FromSeconds(2));

            trigger1.ComputeFirstFireTimeUtc(null);
            trigger2.ComputeFirstFireTimeUtc(null);
            trigger3.ComputeFirstFireTimeUtc(null);
            trigger4.ComputeFirstFireTimeUtc(null);
            trigger10.ComputeFirstFireTimeUtc(null);
            this.fJobStore.StoreTrigger(null, trigger1, false);
            this.fJobStore.StoreTrigger(null, trigger2, false);
            this.fJobStore.StoreTrigger(null, trigger3, false);
            this.fJobStore.StoreTrigger(null, trigger4, false);
            this.fJobStore.StoreTrigger(null, trigger10, false);

            Assert.AreEqual(3,
                            this.fJobStore.AcquireNextTriggers(null, trigger1.GetNextFireTimeUtc().Value.AddSeconds(10),
                                                               3, TimeSpan.FromSeconds(1)).Count);
            this.fJobStore.ReleaseAcquiredTrigger(null, trigger1);
            this.fJobStore.ReleaseAcquiredTrigger(null, trigger2);
            this.fJobStore.ReleaseAcquiredTrigger(null, trigger3);

            Assert.AreEqual(4,
                            this.fJobStore.AcquireNextTriggers(null, trigger1.GetNextFireTimeUtc().Value.AddSeconds(10),
                                                               4, TimeSpan.FromSeconds(1)).Count);
            this.fJobStore.ReleaseAcquiredTrigger(null, trigger1);
            this.fJobStore.ReleaseAcquiredTrigger(null, trigger2);
            this.fJobStore.ReleaseAcquiredTrigger(null, trigger3);
            this.fJobStore.ReleaseAcquiredTrigger(null, trigger4);

            Assert.AreEqual(4,
                            this.fJobStore.AcquireNextTriggers(null, trigger1.GetNextFireTimeUtc().Value.AddSeconds(10),
                                                               5, TimeSpan.FromSeconds(1)).Count);
            this.fJobStore.ReleaseAcquiredTrigger(null, trigger1);
            this.fJobStore.ReleaseAcquiredTrigger(null, trigger2);
            this.fJobStore.ReleaseAcquiredTrigger(null, trigger3);
            this.fJobStore.ReleaseAcquiredTrigger(null, trigger4);

            Assert.AreEqual(1,
                            this.fJobStore.AcquireNextTriggers(null, trigger1.GetNextFireTimeUtc().Value.AddSeconds(0),
                                                               5, TimeSpan.Zero).Count);
            this.fJobStore.ReleaseAcquiredTrigger(null, trigger1);

            Assert.AreEqual(2,
                            this.fJobStore.AcquireNextTriggers(null,
                                                               trigger1.GetNextFireTimeUtc().Value.AddMilliseconds(150),
                                                               5, TimeSpan.Zero).Count);
            this.fJobStore.ReleaseAcquiredTrigger(null, trigger1);
            this.fJobStore.ReleaseAcquiredTrigger(null, trigger2);
        }

        [Test]
        public void TestTriggerStates()
        {
            Trigger trigger =
                new SimpleTrigger("trigger1", "triggerGroup1", fJobDetail.Name, fJobDetail.Group,
                                  DateTime.Now.AddSeconds(100), DateTime.Now.AddSeconds(200), 2, TimeSpan.FromSeconds(2));
            trigger.ComputeFirstFireTimeUtc(null);
            Assert.AreEqual(TriggerState.None, fJobStore.GetTriggerState(null, trigger.Name, trigger.Group));
            fJobStore.StoreTrigger(null, trigger, false);
            Assert.AreEqual(TriggerState.Normal, fJobStore.GetTriggerState(null, trigger.Name, trigger.Group));

            fJobStore.PauseTrigger(null, trigger.Name, trigger.Group);
            Assert.AreEqual(TriggerState.Paused, fJobStore.GetTriggerState(null, trigger.Name, trigger.Group));

            fJobStore.ResumeTrigger(null, trigger.Name, trigger.Group);
            Assert.AreEqual(TriggerState.Normal, fJobStore.GetTriggerState(null, trigger.Name, trigger.Group));

            trigger = fJobStore.AcquireNextTriggers(null,
                                                    trigger.GetNextFireTimeUtc().Value.AddSeconds(10), 1,
                                                    TimeSpan.FromMilliseconds(1))[0];
            Assert.IsNotNull(trigger);
            fJobStore.ReleaseAcquiredTrigger(null, trigger);
            trigger = fJobStore.AcquireNextTriggers(null,
                                                    trigger.GetNextFireTimeUtc().Value.AddSeconds(10), 1,
                                                    TimeSpan.FromMilliseconds(1))[0];
            Assert.IsNotNull(trigger);
            Assert.AreEqual(0, fJobStore.AcquireNextTriggers(null,
                                                             trigger.GetNextFireTimeUtc().Value.AddSeconds(10), 1,
                                                             TimeSpan.FromMilliseconds(1)).Count);
        }

        [Test]
        public void TestRemoveCalendarWhenTriggersPresent()
        {
            // QRTZNET-29

            Trigger trigger = new SimpleTrigger("trigger1", "triggerGroup1", fJobDetail.Name, fJobDetail.Group,
                                                DateTime.Now.AddSeconds(100), DateTime.Now.AddSeconds(200), 2,
                                                TimeSpan.FromSeconds(2));
            trigger.ComputeFirstFireTimeUtc(null);
            ICalendar cal = new MonthlyCalendar();
            fJobStore.StoreTrigger(null, trigger, false);
            fJobStore.StoreCalendar(null, "cal", cal, false, true);

            fJobStore.RemoveCalendar(null, "cal");
        }

        [Test]
        public void TestStoreTriggerReplacesTrigger()
        {
            string jobName = "StoreTriggerReplacesTrigger";
            string jobGroup = "StoreTriggerReplacesTriggerGroup";
            JobDetail detail = new JobDetail(jobName, jobGroup, typeof (NoOpJob));
            fJobStore.StoreJob(null, detail, false);

            string trName = "StoreTriggerReplacesTrigger";
            string trGroup = "StoreTriggerReplacesTriggerGroup";
            Trigger tr = new SimpleTrigger(trName, trGroup, DateTime.Now);
            tr.JobGroup = jobGroup;
            tr.JobName = jobName;
            tr.CalendarName = null;

            fJobStore.StoreTrigger(null, tr, false);
            Assert.AreEqual(tr, fJobStore.RetrieveTrigger(null, trName, trGroup));

            tr.CalendarName = "NonExistingCalendar";
            fJobStore.StoreTrigger(null, tr, true);
            Assert.AreEqual(tr, fJobStore.RetrieveTrigger(null, trName, trGroup));
            Assert.AreEqual(tr.CalendarName, fJobStore.RetrieveTrigger(null, trName, trGroup).CalendarName,
                            "StoreJob doesn't replace triggers");

            bool exeptionRaised = false;
            try
            {
                fJobStore.StoreTrigger(null, tr, false);
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
            JobDetail detail = new JobDetail(jobName1, jobGroup, typeof (NoOpJob));
            detail.Durable = true;
            fJobStore.StoreJob(null, detail, false);
            fJobStore.PauseJobGroup(null, jobGroup);

            detail = new JobDetail(jobName2, jobGroup, typeof (NoOpJob));
            detail.Durable = true;
            fJobStore.StoreJob(null, detail, false);

            string trName = "PauseJobGroupPausesNewJobTrigger";
            string trGroup = "PauseJobGroupPausesNewJobTriggerGroup";
            Trigger tr = new SimpleTrigger(trName, trGroup, DateTime.UtcNow);
            tr.JobGroup = jobGroup;
            tr.JobName = jobName2;
            fJobStore.StoreTrigger(null, tr, false);
            Assert.AreEqual(TriggerState.Paused, fJobStore.GetTriggerState(null, tr.Name, tr.Group));
        }

        [Test]
        public void TestRetrieveJob_NoJobFound()
        {
            RAMJobStore store = new RAMJobStore();
            JobDetail job = store.RetrieveJob(null, "not", "existing");
            Assert.IsNull(job);
        }

        [Test]
        public void TestRetrieveTrigger_NoTriggerFound()
        {
            RAMJobStore store = new RAMJobStore();
            Trigger trigger = store.RetrieveTrigger(null, "not", "existing");
            Assert.IsNull(trigger);
        }

        public class SampleSignaler : ISchedulerSignaler
        {
            internal int fMisfireCount = 0;

            public void NotifyTriggerListenersMisfired(Trigger trigger)
            {
                fMisfireCount++;
            }

            public void NotifySchedulerListenersFinalized(Trigger trigger)
            {
            }

            public void SignalSchedulingChange(DateTime? candidateNewNextFireTimeUtc)
            {
            }
        }
    }
}