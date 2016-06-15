using System;
using System.Collections.Generic;

using NUnit.Framework;

namespace Quartz.Tests.Unit
{
	[TestFixture]
	class CronExpressionBuilderTestMonth
	{

		[Test]
		public void TestSpecificMonth()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.Month(2);

			Assert.AreEqual("* * * ? 2 *", bldr.CronExpression);
		}

		[Test]
		public void TestMultipleMonths()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			List<int> desiredMonths = new List<int>();
			desiredMonths.Add(3);
			desiredMonths.Add(12);

			bldr.Month(desiredMonths);

			Assert.AreEqual("* * * ? 3,12 *", bldr.CronExpression);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestMultipleMonthEmptyList()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			List<int> desiredMonths = new List<int>();

			bldr.Month(desiredMonths);
		}

		[Test]
		public void TestIncrementsOfMonths()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.MonthIncrements(3, 5);

			Assert.AreEqual("* * * ? 3/5 *", bldr.CronExpression);
		}

		[Test]
		public void TestRangeOfMonths()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.MonthRange(2, 8);

			Assert.AreEqual("* * * ? 2-8 *", bldr.CronExpression);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestMultipleConfigurationsOfMonths()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.MonthRange(1, 7);
			bldr.MonthIncrements(7, 3);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestMultipleMonthsWithOneInvalid()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			List<int> desiredMonths = new List<int>();
			desiredMonths.Add(13);
			desiredMonths.Add(2);

			bldr.Month(desiredMonths);
		}
	}
}
