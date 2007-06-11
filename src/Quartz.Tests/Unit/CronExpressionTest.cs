/* 
 * Copyright 2004-2006 OpenSymphony 
 * 
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not 
 * use this file except in compliance with the License. You may obtain a copy 
 * of the License at 
 * 
 *   http://www.apache.org/licenses/LICENSE-2.0 
 *   
 * Unless required by applicable law or agreed to in writing, software 
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT 
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the 
 * License for the specific language governing permissions and limitations 
 * under the License.
 */
using System;

using Nullables;

using NUnit.Framework;

namespace Quartz.Tests.Unit
{
	[TestFixture]
	public class CronExpressionTest
	{
		private static string[] VERSIONS = new String[] {"1.5.2"};

		private static TimeZone EST_TIME_ZONE = TimeZone.CurrentTimeZone;

		/// <summary>
		/// Get the object to serialize when generating serialized file for future
		/// tests, and against which to validate deserialized object.
		/// </summary>
		/// <returns></returns>
		protected object GetTargetObject()
		{
			CronExpression cronExpression = new CronExpression("0 15 10 * * ? 2005");
			//cronExpression.TimeZone = EST_TIME_ZONE);

			return cronExpression;
		}

		/// <summary>
		/// Get the Quartz versions for which we should verify
		/// serialization backwards compatibility.
		/// </summary>
		/// <returns></returns>
		protected string[] GetVersions()
		{
			return VERSIONS;
		}

		/// <summary>
		/// Verify that the target object and the object we just deserialized 
		/// match.
		/// </summary>
		/// <param name="target"></param>
		/// <param name="deserialized"></param>
		protected void VerifyMatch(object target, object deserialized)
		{
			CronExpression targetCronExpression = (CronExpression) target;
			CronExpression deserializedCronExpression = (CronExpression) deserialized;

			Assert.IsNotNull(deserializedCronExpression);
			Assert.AreEqual(targetCronExpression.CronExpressionString, deserializedCronExpression.CronExpressionString);
			//Assert.AreEqual(targetCronExpression.getTimeZone(), deserializedCronExpression.getTimeZone());
		}

		/// <summary>
		/// Test method for 'CronExpression.IsSatisfiedBy(DateTime)'.
		/// </summary>
		[Test]
		public void TestIsSatisfiedBy()
		{
			CronExpression cronExpression = new CronExpression("0 15 10 * * ? 2005");

			DateTime cal = new DateTime(2005, 6, 1, 10, 15, 0);
			Assert.IsTrue(cronExpression.IsSatisfiedBy(cal));

			cal = cal.AddYears(1);
			Assert.IsFalse(cronExpression.IsSatisfiedBy(cal));

			cal = new DateTime(2005, 6, 1, 10, 16, 0);
			Assert.IsFalse(cronExpression.IsSatisfiedBy(cal));

			cal = new DateTime(2005, 6, 1, 10, 14, 0);
			Assert.IsFalse(cronExpression.IsSatisfiedBy(cal));

			cronExpression = new CronExpression("0 15 10 ? * MON-FRI");
			
			// weekends
			cal = new DateTime(2007, 6, 9, 10, 15, 0);
			Assert.IsFalse(cronExpression.IsSatisfiedBy(cal));
			Assert.IsFalse(cronExpression.IsSatisfiedBy(cal.AddDays(1)));
		}

		[Test]
		public void TestCronExpressionPassingMidnight()
		{
			CronExpression cronExpression = new CronExpression("0 15 00 * * ?");
			CronTrigger trigger = new CronTrigger("trigger", "group");
			DateTime cal = new DateTime(2005, 6, 1, 23, 15, 0);
			trigger.CronExpression = cronExpression;
			trigger.StartTime = cal;
		
			trigger.ComputeFirstFireTime(null);

			DateTime nextExpectedFireTime = new DateTime(2005, 6, 2, 0, 15, 0);
			NullableDateTime nextFireTime = trigger.GetNextFireTime();
			Assert.AreEqual(nextExpectedFireTime, nextFireTime.Value);
		}
	}
}