using System;
using System.Collections.Generic;

using NUnit.Framework;

namespace Quartz.Tests.Unit
{
	[TestFixture]
	class CronExpressionBuilderTestSeconds
	{

		[Test]
		public void TestEverySecond()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();
			Assert.AreEqual("* * * ? * *", bldr.CronExpression);
		}

		[Test]
		public void TestSpecificSecond()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.Second(10);

			Assert.AreEqual("10 * * ? * *", bldr.CronExpression);
		}

		[Test]
		public void TestMultipleSeconds()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			List<int> desiredSeconds = new List<int>();
			desiredSeconds.Add(17);
			desiredSeconds.Add(51);

			bldr.Second(desiredSeconds);

			Assert.AreEqual("17,51 * * ? * *", bldr.CronExpression);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestMultipleSecondEmptyList()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			List<int> desiredSeconds = new List<int>();

			bldr.Second(desiredSeconds);
		}

		[Test]
		public void TestIncrementsOfSeconds()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.SecondIncrements(0, 10);

			Assert.AreEqual("0/10 * * ? * *", bldr.CronExpression);
		}

		[Test]
		public void TestRangeOfSeconds()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.SecondRange(20, 30);

			Assert.AreEqual("20-30 * * ? * *", bldr.CronExpression);
		}

		[Test]
		[ExpectedException(typeof (ArgumentException))]
		public void TestMultipleConfigurationsOfSeconds()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.SecondRange(20, 30);
			bldr.SecondIncrements(0, 20);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestMultipleSecondsWithOneInvalid()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			List<int> desiredSeconds = new List<int>();
			desiredSeconds.Add(17);
			desiredSeconds.Add(61);

			bldr.Second(desiredSeconds);
		}
	}
}
