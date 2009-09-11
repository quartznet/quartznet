using System;
using System.Threading;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Job;

using Rhino.Mocks;

namespace Quartz.Tests.Unit.Core
{
    [TestFixture]
    public class QuartzSchedulerTest
    {
        [Test]
        public void TestInvalidCalendarScheduling()
        {
            const string expectedError = "Calendar not found: FOOBAR";

            ISchedulerFactory sf = new StdSchedulerFactory();
            IScheduler sched = sf.GetScheduler();

            DateTime runTime = DateTime.Now.AddMinutes(10);

            // define the job and tie it to our HelloJob class
            JobDetail job = new JobDetail("job1", "group1", typeof(NoOpJob));

            // Trigger the job to run on the next round minute
            SimpleTrigger trigger = new SimpleTrigger("trigger1", "group1", runTime);

            // set invalid calendar
            trigger.CalendarName = "FOOBAR";

            try
            {
                sched.ScheduleJob(job, trigger);
                Assert.Fail("No error for non-existing calendar");
            }
            catch (SchedulerException ex)
            {
                Assert.AreEqual(expectedError, ex.Message);
            }

            try
            {
                sched.ScheduleJob(trigger);
                Assert.Fail("No error for non-existing calendar");
            }
            catch (SchedulerException ex)
            {
                Assert.AreEqual(expectedError, ex.Message);
            }
            
            sched.Shutdown(false);
        }

        [Test]
        public void TestStartDelayed()
        {
            ISchedulerFactory sf = new StdSchedulerFactory();
            IScheduler sched = sf.GetScheduler();
            sched.StartDelayed(TimeSpan.FromSeconds(2));
            Assert.IsFalse(sched.IsStarted);
            Thread.Sleep(TimeSpan.FromSeconds(3));
            Assert.IsTrue(sched.IsStarted);
        }

        [Test]
        public void TestRescheduleJob_SchedulerListenersCalledOnReschedule()
        {
            const string TriggerName = "triggerName";
            const string TriggerGroup = "triggerGroup";
            const string JobName = "jobName";
            const string JobGroup = "jobGroup";

            ISchedulerFactory sf = new StdSchedulerFactory();
            IScheduler scheduler = sf.GetScheduler();
            DateTime startTimeUtc = DateTime.UtcNow.AddSeconds(2);
            JobDetail jobDetail = new JobDetail(JobName, JobGroup, typeof(NoOpJob));
            SimpleTrigger jobTrigger = new SimpleTrigger(TriggerName, TriggerGroup, JobName, JobGroup, startTimeUtc, null, 1, TimeSpan.FromMilliseconds(1000));

            ISchedulerListener listener = MockRepository.GenerateMock<ISchedulerListener>();

            scheduler.ScheduleJob(jobDetail, jobTrigger);
            // add listener after scheduled
            scheduler.AddSchedulerListener(listener);

            // act
            scheduler.RescheduleJob(TriggerName, TriggerGroup, jobTrigger);

            // assert
            // expect unschedule and schedule
            listener.AssertWasCalled(l => l.JobUnscheduled(TriggerName, TriggerGroup));
            listener.AssertWasCalled(l => l.JobScheduled(jobTrigger));

        }
    }
}
