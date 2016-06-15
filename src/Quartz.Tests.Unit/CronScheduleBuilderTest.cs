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

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestAtHourAndMinuteOnGivenDaysOfWeekInvalidHour()
		{
			var trigger = (ICronTrigger)TriggerBuilder.Create()
											 .WithIdentity("test")
											 .WithSchedule(CronScheduleBuilder.AtHourAndMinuteOnGivenDaysOfWeek(25, 0, DayOfWeek.Monday, DayOfWeek.Thursday, DayOfWeek.Friday))
											 .Build();
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestAtHourAndMinuteOnGivenDaysOfWeekInvalidMinute()
		{
			var trigger = (ICronTrigger)TriggerBuilder.Create()
											 .WithIdentity("test")
											 .WithSchedule(CronScheduleBuilder.AtHourAndMinuteOnGivenDaysOfWeek(13, 68, DayOfWeek.Monday, DayOfWeek.Thursday, DayOfWeek.Friday))
											 .Build();
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestAtHourAndMinuteOnGivenDaysOfWeekInvalidDaysOfWeek()
		{
			var trigger = (ICronTrigger)TriggerBuilder.Create()
											 .WithIdentity("test")
											 .WithSchedule(CronScheduleBuilder.AtHourAndMinuteOnGivenDaysOfWeek(13, 25, null))
											 .Build();
		}

	    [Test]
	    public void DailyAtHourAndMinute()
	    {
			var trigger = (ICronTrigger) TriggerBuilder.Create()
                                             .WithIdentity("test")
											 .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(10, 20))
                                             .Build();

            Assert.AreEqual("0 20 10 ? * *", trigger.CronExpressionString);
	    }

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void DailyAtHourAndMinuteInvalidHour()
		{
			var trigger = (ICronTrigger)TriggerBuilder.Create()
											 .WithIdentity("test")
											 .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(26, 23))
											 .Build();
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void DailyAtHourAndMinuteInvalidMinute()
		{
			var trigger = (ICronTrigger)TriggerBuilder.Create()
											 .WithIdentity("test")
											 .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(11, 78))
											 .Build();
		}

		[Test]
		public void WeeklyOnDayAndHourAndMinute()
		{
			var trigger = (ICronTrigger)TriggerBuilder.Create()
											 .WithIdentity("test")
											 .WithSchedule(CronScheduleBuilder.WeeklyOnDayAndHourAndMinute(DayOfWeek.Saturday, 11, 41))
											 .Build();

			Assert.AreEqual("0 41 11 ? * 7", trigger.CronExpressionString);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void WeeklyOnDayAndHourAndMinuteInvalidHour()
		{
			var trigger = (ICronTrigger)TriggerBuilder.Create()
											 .WithIdentity("test")
											 .WithSchedule(CronScheduleBuilder.WeeklyOnDayAndHourAndMinute(DayOfWeek.Monday, 25, 2))
											 .Build();
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void WeeklyOnDayAndHourAndMinuteInvalidMinute()
		{
			var trigger = (ICronTrigger)TriggerBuilder.Create()
											 .WithIdentity("test")
											 .WithSchedule(CronScheduleBuilder.WeeklyOnDayAndHourAndMinute(DayOfWeek.Monday, 2, 62))
											 .Build();
		}

		[Test]
		public void MonthlyOnDayAndHourAndMinute()
		{
			var trigger = (ICronTrigger)TriggerBuilder.Create()
											 .WithIdentity("test")
											 .WithSchedule(CronScheduleBuilder.MonthlyOnDayAndHourAndMinute(6, 18, 30))
											 .Build();

			Assert.AreEqual("0 30 18 6 * ?", trigger.CronExpressionString);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void MonthlyOnDayAndHourAndMinuteInvalidDayOfMonth()
		{
			var trigger = (ICronTrigger)TriggerBuilder.Create()
											 .WithIdentity("test")
											 .WithSchedule(CronScheduleBuilder.MonthlyOnDayAndHourAndMinute(32, 18, 30))
											 .Build();
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void MonthlyOnDayAndHourAndMinuteInvalidHour()
		{
			var trigger = (ICronTrigger)TriggerBuilder.Create()
											 .WithIdentity("test")
											 .WithSchedule(CronScheduleBuilder.MonthlyOnDayAndHourAndMinute(18, 25, 1))
											 .Build();
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void MonthlyOnDayAndHourAndMinuteInvalidMinute()
		{
			var trigger = (ICronTrigger)TriggerBuilder.Create()
											 .WithIdentity("test")
											 .WithSchedule(CronScheduleBuilder.MonthlyOnDayAndHourAndMinute(16, 19, 61))
											 .Build();
		}
    }
}