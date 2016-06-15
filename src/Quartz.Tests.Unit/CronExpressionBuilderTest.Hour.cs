using System;
using System.Collections.Generic;

using NUnit.Framework;

namespace Quartz.Tests.Unit
{
	[TestFixture]
	class CronExpressionBuilderTestHours
	{

		[Test]
		public void TestSpecificHour()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.Hour(10);

			Assert.AreEqual("* * 10 ? * *", bldr.CronExpression);
		}

		[Test]
		public void TestMultipleHours()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			List<int> desiredHours = new List<int>();
			desiredHours.Add(1);
			desiredHours.Add(5);

			bldr.Hour(desiredHours);

			Assert.AreEqual("* * 1,5 ? * *", bldr.CronExpression);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestMultipleHourEmptyList()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			List<int> desiredHours = new List<int>();

			bldr.Hour(desiredHours);
		}

		[Test]
		public void TestIncrementsOfHours()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.HourIncrements(0, 10);

			Assert.AreEqual("* * 0/10 ? * *", bldr.CronExpression);
		}

		[Test]
		public void TestRangeOfHours()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.HourRange(20, 22);

			Assert.AreEqual("* * 20-22 ? * *", bldr.CronExpression);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestMultipleConfigurationsOfHours()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.HourRange(20, 30);
			bldr.HourIncrements(0, 20);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestMultipleHoursWithOneInvalid()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			List<int> desiredHours = new List<int>();
			desiredHours.Add(17);
			desiredHours.Add(24);

			bldr.Hour(desiredHours);
		}
	}
}
