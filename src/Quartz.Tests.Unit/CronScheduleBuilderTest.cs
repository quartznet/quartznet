using System;

using NUnit.Framework;

namespace Quartz.Tests.Unit
{
    [TestFixture]
    public class CronScheduleBuilderTest
    {
        [Test]
        public void TestAtHourAndMinuteOnGivenDaysOfWeek()
        {
            var trigger = (ICronTrigger) TriggerBuilder.Create()
                                             .WithIdentity("test")
                                             .WithSchedule(CronScheduleBuilder.AtHourAndMinuteOnGivenDaysOfWeek(10, 0, DayOfWeek.Monday, DayOfWeek.Thursday, DayOfWeek.Friday))
                                             .Build();

            Assert.AreEqual("0 0 10 ? * 2,5,6", trigger.CronExpressionString);

            trigger = (ICronTrigger) TriggerBuilder.Create().WithIdentity("test")
                                         .WithSchedule(CronScheduleBuilder.AtHourAndMinuteOnGivenDaysOfWeek(10, 0, DayOfWeek.Wednesday))
                                         .Build();
            
            Assert.AreEqual("0 0 10 ? * 4", trigger.CronExpressionString);
        }
    }
}