using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using Common.Logging;

using NUnit.Framework;

using Quartz.Impl;
using Quartz.Simpl;

namespace Quartz.Tests.Integration
{
    /// <summary>
    /// This is a manual test requiring the tester to manually change the system clock at the right time or put the device to sleep for the right time 
    /// Not inheriting from <see cref="IntegrationTest" /> so that this manual test would not be run when integration category tests are run
    /// </summary>
    [TestFixture, Explicit]
    public class ScheduledFireTimeTest
    {
        private IScheduler sched;
        private ScheduledFireTimeTestJob scheduledFireTimeTestJob;

        [SetUp]
        public void SetUp()
        {
            ISchedulerFactory sf = new StdSchedulerFactory();
            sched = sf.GetScheduler();            
            scheduledFireTimeTestJob = new ScheduledFireTimeTestJob();
            var jobFactory = new FixedJobFactory(scheduledFireTimeTestJob);            
            sched.JobFactory = jobFactory;            
        }

        [Test]
        public void TestScheduledFireTimeUtc()
        {
            var now = DateTimeOffset.Now;
            var startTime = now.AddSeconds(120);
            var trigger = TriggerBuilder.Create().WithIdentity("DailyTrigger")
                .WithSchedule(
                    DailyTimeIntervalScheduleBuilder.Create()
                        .WithMisfireHandlingInstructionFireAndProceed()
                        .StartingDailyAt(TimeOfDay.HourMinuteAndSecondOfDay(startTime.Hour, startTime.Minute, startTime.Second))
                        .OnEveryDay()
                        .WithIntervalInHours(1)
                        .WithRepeatCount(9999)
                        .InTimeZone(TimeZoneInfo.Local)
                ).Build();
            var jobDetail = JobBuilder.Create().WithIdentity("DailyJob", "DailyGroup").Build();
            sched.ScheduleJob(jobDetail, trigger);
            sched.Start();            
            MessageBox.Show("You have about a minute to cause a misfire now. Please either: " + Environment.NewLine
                + "A. Push the computer clock 1 hour forward into the future" + Environment.NewLine
                + "or B. Put the computer to sleep for about three minutes and then resume" + Environment.NewLine
                + "When you have carried out A or B, press OK", "Manual Test", MessageBoxButtons.OK);
            if (scheduledFireTimeTestJob.ScheduledFireTimeUtc.HasValue)
            {
                var scheduledTimeUtc = scheduledFireTimeTestJob.ScheduledFireTimeUtc.Value;
                var fireTimeUtc = scheduledFireTimeTestJob.FireTimeUtc;
                var howLongAgo = fireTimeUtc - scheduledTimeUtc;                
                Console.WriteLine("Event Scheduled for {0} Ticked at {1} with Difference of {2}", scheduledTimeUtc, fireTimeUtc, howLongAgo);
                Assert.Greater(howLongAgo, TimeSpan.FromSeconds(30), "Scheduled Fire Time Wrong - It Should Have Been Earlier Because of the Misfire");
            }
            else
            {
                Assert.Fail("Failed To Get ScheduledFireTimeUtc at {0}", DateTimeOffset.UtcNow);
            }
        }

        [TearDown]
        public void TearDown()
        {            
            sched.Shutdown();            
        }

    }
}
