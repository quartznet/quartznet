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
using System.Text;
using System.Threading;

using NUnit.Framework;

using Quartz.Impl;

namespace Quartz.Tests.Unit
{
	/// <summary>
	/// Test Trigger priority support.
	/// </summary>
	[TestFixture]
	public class PriorityTest
	{
		private static StringBuilder result;

        [SetUp]
        public void Setup()
        {
            result = new StringBuilder();
        }


		[Test]
		public void TestSameDefaultPriority()
		{
			IScheduler sched = new StdSchedulerFactory().GetScheduler();

			DateTime n = DateTime.UtcNow;
			DateTime cal = new DateTime(n.Year, n.Month, n.Day, n.Hour, n.Minute, 1, n.Millisecond);

			Trigger trig1 = new SimpleTrigger("T1", null, cal);
			Trigger trig2 = new SimpleTrigger("T2", null, cal);

			JobDetail jobDetail = new JobDetail("JD", null, typeof (TestJob));

			sched.ScheduleJob(jobDetail, trig1);

			trig2.JobName = jobDetail.Name;
			sched.ScheduleJob(trig2);

			sched.Start();

			Thread.Sleep(2000);

			Assert.AreEqual("T1T2", result.ToString());

			sched.Shutdown();
		}

		public void TestDifferentPriority()
		{
			IScheduler sched = new StdSchedulerFactory().GetScheduler();

			DateTime n = DateTime.UtcNow;
			DateTime cal = new DateTime(n.Year, n.Month, n.Day, n.Hour, n.Minute, 1, n.Millisecond);

			Trigger trig1 = new SimpleTrigger("T1", null, cal);
			trig1.Priority = 5;

			Trigger trig2 = new SimpleTrigger("T2", null, cal);
			trig2.Priority = 10;

			JobDetail jobDetail = new JobDetail("JD", null, typeof (TestJob));

			sched.ScheduleJob(jobDetail, trig1);

			trig2.JobName = jobDetail.Name;
			sched.ScheduleJob(trig2);

			sched.Start();

			Thread.Sleep(2000);

			Assert.AreEqual("T2T1", result.ToString());

			sched.Shutdown();
		}

		class TestJob : IStatefulJob
		{
			public void Execute(JobExecutionContext context)
			{
				result.Append(context.Trigger.Name);
			}
		}
	}
}