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
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;

using FakeItEasy;

using NUnit.Framework;

using Quartz.Core;
using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Job;
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
            var versionInfo = FileVersionInfo.GetVersionInfo(Assembly.GetAssembly(typeof(QuartzScheduler)).Location);
            Assert.AreEqual(versionInfo.FileMajorPart.ToString(CultureInfo.InvariantCulture), QuartzScheduler.VersionMajor);
            Assert.AreEqual(versionInfo.FileMinorPart.ToString(CultureInfo.InvariantCulture), QuartzScheduler.VersionMinor);
            Assert.AreEqual(versionInfo.FileBuildPart.ToString(CultureInfo.InvariantCulture), QuartzScheduler.VersionIteration);
        }

        [Test]
        public async Task TestInvalidCalendarScheduling()
        {
            const string ExpectedError = "Calendar not found: FOOBAR";

            ISchedulerFactory sf = new StdSchedulerFactory();
            IScheduler sched = await sf.GetScheduler();

            DateTime runTime = DateTime.Now.AddMinutes(10);

            // define the job and tie it to our HelloJob class
            JobDetailImpl job = new JobDetailImpl("job1", "group1", typeof(NoOpJob));

            // Trigger the job to run on the next round minute
            IOperableTrigger trigger = new SimpleTriggerImpl("trigger1", "group1", runTime);

            // set invalid calendar
            trigger.CalendarName = "FOOBAR";

            try
            {
                await sched.ScheduleJobAsync(job, trigger);
                Assert.Fail("No error for non-existing calendar");
            }
            catch (SchedulerException ex)
            {
                Assert.AreEqual(ExpectedError, ex.Message);
            }

            try
            {
                await sched.ScheduleJobAsync(trigger);
                Assert.Fail("No error for non-existing calendar");
            }
            catch (SchedulerException ex)
            {
                Assert.AreEqual(ExpectedError, ex.Message);
            }
            
            await sched.ShutdownAsync(false);
        }

        [Test]
        public async Task TestStartDelayed()
        {
            ISchedulerFactory sf = new StdSchedulerFactory();
            IScheduler sched = await sf.GetScheduler();
            var task = sched.StartDelayedAsync(TimeSpan.FromSeconds(2));
            Assert.IsFalse(sched.IsStarted);
            await Task.Delay(TimeSpan.FromSeconds(3));
            Assert.IsTrue(sched.IsStarted);
        }

        [Test]
        public async Task TestRescheduleJob_SchedulerListenersCalledOnReschedule()
        {
            const string TriggerName = "triggerName";
            const string TriggerGroup = "triggerGroup";
            const string JobName = "jobName";
            const string JobGroup = "jobGroup";

            ISchedulerFactory sf = new StdSchedulerFactory();
            IScheduler scheduler = await sf.GetScheduler();
            DateTime startTimeUtc = DateTime.UtcNow.AddSeconds(2);
            JobDetailImpl jobDetail = new JobDetailImpl(JobName, JobGroup, typeof(NoOpJob));
            SimpleTriggerImpl jobTrigger = new SimpleTriggerImpl(TriggerName, TriggerGroup, JobName, JobGroup, startTimeUtc, null, 1, TimeSpan.FromMilliseconds(1000));

            ISchedulerListener listener = A.Fake<ISchedulerListener>();

            await scheduler.ScheduleJobAsync(jobDetail, jobTrigger);
            // add listener after scheduled
            scheduler.ListenerManager.AddSchedulerListener(listener);

            // act
            await scheduler.RescheduleJobAsync(new TriggerKey(TriggerName, TriggerGroup), jobTrigger);

            // assert
            // expect unschedule and schedule
            A.CallTo(() => listener.JobUnscheduledAsync(new TriggerKey(TriggerName, TriggerGroup))).MustHaveHappened();
            A.CallTo(() => listener.JobScheduledAsync(jobTrigger)).MustHaveHappened();

        }
    }
}
