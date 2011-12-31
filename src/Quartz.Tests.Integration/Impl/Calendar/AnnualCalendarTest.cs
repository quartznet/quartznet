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
using System.Threading;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Impl.Calendar;

namespace Quartz.Tests.Integration.Impl.Calendar
{
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class AnnualCalendarTest : IntegrationTest
    {
        [SetUp]
        public void SetUp()
        {
            ISchedulerFactory sf = new StdSchedulerFactory();
            sched = sf.GetScheduler();      
        }

        [Test]
        public void TestTriggerFireExclusion()
        {
            sched.Start();
            TestJob.JobHasFired = false;
            IJobDetail jobDetail = JobBuilder.Create<TestJob>()
                .WithIdentity("name", "group")
                .Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("trigName", "trigGroup")
                .ModifiedByCalendar("calendar")
                .WithCronSchedule("0/15 * * * * ?")
                .Build();

            AnnualCalendar calendar = new AnnualCalendar();
            calendar.SetDayExcluded(DateTime.Now, true);
            sched.AddCalendar("calendar", calendar, true, true);

            sched.ScheduleJob(jobDetail, trigger);

            ITrigger triggerreplace = TriggerBuilder.Create()
                .WithIdentity("foo", "trigGroup")
                .ForJob(jobDetail)
                .ModifiedByCalendar("calendar")
                .WithCronSchedule("0/15 * * * * ?")
                .Build();

            sched.RescheduleJob(new TriggerKey("trigName", "trigGroup"), triggerreplace);
            Thread.Sleep(TimeSpan.FromSeconds(20));
            Assert.IsFalse(TestJob.JobHasFired, "task must not be neglected - it is forbidden by the calendar");

            calendar.SetDayExcluded(DateTime.Now, false);
            sched.AddCalendar("calendar", calendar, true, true);
            Thread.Sleep(TimeSpan.FromSeconds(20));
            Assert.IsTrue(TestJob.JobHasFired, "task must be neglected - it is permitted by the calendar");

            sched.DeleteJob(new JobKey("name", "group"));
            sched.DeleteCalendar("calendar");

            sched.Shutdown();
        }
    }
}
