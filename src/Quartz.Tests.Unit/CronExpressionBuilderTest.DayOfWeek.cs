using System;
using System.Collections.Generic;

using NUnit.Framework;

namespace Quartz.Tests.Unit
{
	[TestFixture]
	class CronExpressionBuilderTestDayOfWeek
	{

		[Test]
		public void TestSpecificDayOfWeek()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.DayOfWeek(DayOfWeek.Thursday);

			Assert.AreEqual("* * * ? * 5", bldr.CronExpression);
		}

		[Test]
		public void TestMultipleDaysOfWeek()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			List<DayOfWeek> desiredDays = new List<DayOfWeek>();
			desiredDays.Add(DayOfWeek.Sunday);
			desiredDays.Add(DayOfWeek.Wednesday);

			bldr.DayOfWeek(desiredDays);

			Assert.AreEqual("* * * ? * 1,4", bldr.CronExpression);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestMultipleDayOfWeekEmptyList()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			List<DayOfWeek> desiredDays = new List<DayOfWeek>();

			bldr.DayOfWeek(desiredDays);
		}

		[Test]
		public void TestIncrementsOfDaysOfWeek()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.DayOfWeekIncrements(DayOfWeek.Sunday, 2);

			Assert.AreEqual("* * * ? * 1/2", bldr.CronExpression);
		}

		[Test]
		public void TestWeekdays()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.Weekdays();

			Assert.AreEqual("* * * ? * 2-6", bldr.CronExpression);
		}

		[Test]
		public void TestRangeOfDaysOfWeek()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.DayOfWeekRange(DayOfWeek.Thursday, DayOfWeek.Sunday);

			Assert.AreEqual("* * * ? * 5-1", bldr.CronExpression);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestMultipleConfigurationsOfDays()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.DayOfWeekRange(DayOfWeek.Friday, DayOfWeek.Sunday);
			bldr.DayOfWeekIncrements(0, 20);
		}

		[Test]
		public void TestNthDayOfWeekOfMonth()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.NthDayOfWeekOfMonth(DayOfWeek.Sunday, 3);

			Assert.AreEqual("* * * ? * 1#3", bldr.CronExpression );
		}

		[Test]
		public void TestLastDayOfWeekInMonth()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.LastDayOfWeekInMonth(DayOfWeek.Thursday);

			Assert.AreEqual("* * * ? * 5L", bldr.CronExpression);

		}

		[Test]
		public void TestLastDayOfWeek()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.LastDayOfWeek();

			Assert.AreEqual("* * * ? * L", bldr.CronExpression);
		}
	}
}
