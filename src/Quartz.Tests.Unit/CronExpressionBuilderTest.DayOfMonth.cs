using System;
using System.Collections.Generic;

using NUnit.Framework;

namespace Quartz.Tests.Unit
{
	[TestFixture]
	class CronExpressionBuilderTestDayOfMonth
	{

		[Test]
		public void TestSpecificDayOfMonth()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.DayOfMonth(10);

			Assert.AreEqual("* * * 10 * ?", bldr.CronExpression);
		}

		[Test]
		public void TestMultipleDaysOfMonth()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			List<int> desiredDays = new List<int>();
			desiredDays.Add(1);
			desiredDays.Add(5);

			bldr.DayOfMonth(desiredDays);

			Assert.AreEqual("* * * 1,5 * ?", bldr.CronExpression);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestMultipleDayOfMonthEmptyList()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			List<int> desiredDays = new List<int>();

			bldr.DayOfMonth(desiredDays);
		}

		[Test]
		public void TestIncrementsOfDaysOfMonth()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.DayOfMonthIncrements(1, 5);

			Assert.AreEqual("* * * 1/5 * ?", bldr.CronExpression);
		}

		[Test]
		public void TestRangeOfDaysOfMonth()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.DayOfMonthRange(20, 22);

			Assert.AreEqual("* * * 20-22 * ?", bldr.CronExpression);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestMultipleConfigurationsOfDaysOfMonth()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.DayOfMonthRange(20, 30);
			bldr.DayOfMonthIncrements(0, 20);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestMultipleDaysOfMonthWithOneInvalid()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			List<int> desiredDays = new List<int>();
			desiredDays.Add(17);
			desiredDays.Add(32);

			bldr.DayOfMonth(desiredDays);
		}

		[Test]
		public void TestLastDayOfMonth()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.LastDayOfMonth();

			Assert.AreEqual("* * * L * ?", bldr.CronExpression);
		}

		[Test]
		public void TestNearestWeekDayOfMonth()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.NearestWeekDayOfMonth(15);

			Assert.AreEqual("* * * 15W * ?", bldr.CronExpression);
		}

	}
}
