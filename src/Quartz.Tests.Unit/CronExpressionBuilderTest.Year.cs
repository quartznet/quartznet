using System;
using System.Collections.Generic;

using NUnit.Framework;

namespace Quartz.Tests.Unit
{
	[TestFixture]
	class CronExpressionBuilderTestYear
	{

		[Test]
		public void TestSpecificYear()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.Year(2015);

			Assert.AreEqual("* * * ? * * 2015", bldr.CronExpression);
		}

		[Test]
		public void TestMultipleYears()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			List<int> desiredYears = new List<int>();
			desiredYears.Add(2015);
			desiredYears.Add(2016);

			bldr.Year(desiredYears);

			Assert.AreEqual("* * * ? * * 2015,2016", bldr.CronExpression);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestMultipleYearEmptyList()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			List<int> desiredYears = new List<int>();

			bldr.Year(desiredYears);
		}

		[Test]
		public void TestIncrementsOfYears()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.YearIncrements(2013, 5);

			Assert.AreEqual("* * * ? * * 2013/5", bldr.CronExpression);
		}

		[Test]
		public void TestRangeOfYears()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.YearRange(2015, 2018);

			Assert.AreEqual("* * * ? * * 2015-2018", bldr.CronExpression);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestMultipleConfigurationsOfYears()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.YearRange(2011, 2017);
			bldr.YearIncrements(2017, 3);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestMultipleYearsWithOneInvalid()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			List<int> desiredYears = new List<int>();
			desiredYears.Add(101);
			desiredYears.Add(1981);

			bldr.Year(desiredYears);
		}
	}
}
