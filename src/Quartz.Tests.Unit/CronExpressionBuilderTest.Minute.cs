using System;
using System.Collections.Generic;

using NUnit.Framework;

namespace Quartz.Tests.Unit
{
	[TestFixture]
	class CronExpressionBuilderTestMinutes
	{

		[Test]
		public void TestSpecificMinute()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.Minute(10);

			Assert.AreEqual("* 10 * ? * *", bldr.CronExpression);
		}

		[Test]
		public void TestMultipleMinutes()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			List<int> desiredMinutes = new List<int>();
			desiredMinutes.Add(17);
			desiredMinutes.Add(51);

			bldr.Minute(desiredMinutes);

			Assert.AreEqual("* 17,51 * ? * *", bldr.CronExpression);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestMultipleMinuteEmptyList()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			List<int> desiredMinutes = new List<int>();

			bldr.Minute(desiredMinutes);
		}

		[Test]
		public void TestIncrementsOfMinutes()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.MinuteIncrements(0, 10);

			Assert.AreEqual("* 0/10 * ? * *", bldr.CronExpression);
		}

		[Test]
		public void TestRangeOfMinutes()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.MinuteRange(20, 30);

			Assert.AreEqual("* 20-30 * ? * *", bldr.CronExpression);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestMultipleConfigurationsOfMinutes()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			bldr.MinuteRange(20, 30);
			bldr.MinuteIncrements(0, 20);
		}

		[Test]
		[ExpectedException(typeof(ArgumentException))]
		public void TestMultipleMinutesWithOneInvalid()
		{
			CronExpressionBuilder bldr = new CronExpressionBuilder();

			List<int> desiredMinutes = new List<int>();
			desiredMinutes.Add(17);
			desiredMinutes.Add(61);

			bldr.Minute(desiredMinutes);
		}
	}
}
