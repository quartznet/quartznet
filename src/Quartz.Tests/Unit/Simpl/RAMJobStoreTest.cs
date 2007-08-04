/* 
 * Copyright 2004-2006 OpenSymphony 
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
 */
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
		protected void SetUp()
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
			DateTime d = DateTime.Now;
			Trigger trigger1 =
				new SimpleTrigger("trigger1", "triggerGroup1", fJobDetail.Name,
				                  fJobDetail.Group, d.AddSeconds(200),
				                  d.AddSeconds(200), 2, 2000);
			Trigger trigger2 =
				new SimpleTrigger("trigger2", "triggerGroup1", fJobDetail.Name,
				                  fJobDetail.Group, d.AddSeconds(-100),
				                  d.AddSeconds(20), 2, 2000);
			Trigger trigger3 =
				new SimpleTrigger("trigger1", "triggerGroup2", fJobDetail.Name,
				                  fJobDetail.Group, d.AddSeconds(100),
				                  d.AddSeconds(200), 2, 2000);

			trigger1.ComputeFirstFireTime(null);
			trigger2.ComputeFirstFireTime(null);
			trigger3.ComputeFirstFireTime(null);
			fJobStore.StoreTrigger(null, trigger1, false);
			fJobStore.StoreTrigger(null, trigger2, false);
			fJobStore.StoreTrigger(null, trigger3, false);

			Trigger t = fJobStore.AcquireNextTrigger(null, d.AddMilliseconds(10));
			Assert.IsNull(t);
			Assert.AreEqual(
				trigger2,
				fJobStore.AcquireNextTrigger(null, trigger1.GetNextFireTime().Value.AddSeconds(10)));
			Assert.AreEqual(
				trigger3,
				fJobStore.AcquireNextTrigger(null, trigger1.GetNextFireTime().Value.AddSeconds(10)));
			Assert.AreEqual(
				trigger1,
				fJobStore.AcquireNextTrigger(null, trigger1.GetNextFireTime().Value.AddSeconds(10)));
			Assert.IsNull(
				fJobStore.AcquireNextTrigger(null, trigger1.GetNextFireTime().Value.AddSeconds(10)));

			// because of trigger2
			Assert.AreEqual(1, fSignaler.fMisfireCount);

			// release trigger3
			fJobStore.ReleaseAcquiredTrigger(null, trigger3);
			Assert.AreEqual(
				trigger3,
				fJobStore.AcquireNextTrigger(null, trigger1.GetNextFireTime().Value.AddSeconds(10)));
		}

		[Test]
		public void TestTriggerStates()
		{
			Trigger trigger =
				new SimpleTrigger("trigger1", "triggerGroup1", fJobDetail.Name, fJobDetail.Group,
				                  DateTime.Now.AddSeconds(100), DateTime.Now.AddSeconds(200), 2, 2000);
			trigger.ComputeFirstFireTime(null);
			Assert.AreEqual(TriggerState.None, fJobStore.GetTriggerState(null, trigger.Name, trigger.Group));
			fJobStore.StoreTrigger(null, trigger, false);
			Assert.AreEqual(TriggerState.Normal, fJobStore.GetTriggerState(null, trigger.Name, trigger.Group));

			fJobStore.PauseTrigger(null, trigger.Name, trigger.Group);
			Assert.AreEqual(TriggerState.Paused , fJobStore.GetTriggerState(null, trigger.Name, trigger.Group));

			fJobStore.ResumeTrigger(null, trigger.Name, trigger.Group);
			Assert.AreEqual(TriggerState.Normal, fJobStore.GetTriggerState(null, trigger.Name, trigger.Group));

			trigger = fJobStore.AcquireNextTrigger(null,
			                                       trigger.GetNextFireTime().Value.AddSeconds(10));
			Assert.IsNotNull(trigger);
			fJobStore.ReleaseAcquiredTrigger(null, trigger);
			trigger = fJobStore.AcquireNextTrigger(null,
			                                       trigger.GetNextFireTime().Value.AddSeconds(10));
			Assert.IsNotNull(trigger);
			Assert.IsNull(fJobStore.AcquireNextTrigger(null,
			                                       trigger.GetNextFireTime().Value.AddSeconds(10)));
		}

        [Test]
        public void TestRemoveCalendarWhenTriggersPresent()
        {
            // QRTZNET-29

            Trigger trigger = new SimpleTrigger("trigger1", "triggerGroup1", fJobDetail.Name, fJobDetail.Group,
                                  DateTime.Now.AddSeconds(100), DateTime.Now.AddSeconds(200), 2, 2000);
            trigger.ComputeFirstFireTime(null);
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
            JobDetail detail = new JobDetail(jobName, jobGroup, typeof(NoOpJob));
            fJobStore.StoreJob(null, detail, false);

            string trName = "StoreTriggerReplacesTrigger";
            string trGroup = "StoreTriggerReplacesTriggerGroup";
            Trigger tr = new SimpleTrigger(trName, trGroup, DateTime.Now);
            tr.JobGroup = jobGroup;
            tr.JobName = jobName;
            tr.CalendarName = null;

            fJobStore.StoreTrigger(null, tr, false);
            Assert.AreEqual(tr, fJobStore.RetrieveTrigger(null, trName, trGroup));

            tr.CalendarName = "QQ";
            fJobStore.StoreTrigger(null, tr, true); 
            Assert.AreEqual(tr, fJobStore.RetrieveTrigger(null, trName, trGroup));
            Assert.AreEqual("QQ", fJobStore.RetrieveTrigger(null, trName, trGroup).CalendarName, "StoreJob doesn't replace triggers");

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

		    public void SignalSchedulingChange()
			{
			}
		}
	}
}
