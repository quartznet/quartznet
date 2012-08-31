#region License
/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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
 * 
 */
#endregion

using System;

using NUnit.Framework;

using Quartz.Impl.Calendar;

namespace Quartz.Tests.Unit.Impl.Calendar
{
	/// <author>Marko Lahma (.NET)</author>
	[TestFixture]
	public class CronCalendarTest
	{
		[Test]
		public void TestTimeIncluded()
		{
			string expr = string.Format("0/15 * * * * ?");
			CronCalendar calendar = new CronCalendar(expr);
			string fault = "Time was not included when it was supposed to be";
			DateTime tst = DateTime.UtcNow.AddMinutes(2);
			tst = new DateTime(tst.Year, tst.Month, tst.Day, tst.Hour, tst.Minute, 30);
			Assert.IsTrue(calendar.IsTimeIncluded(tst), fault);

			calendar.SetCronExpressionString("0/25 * * * * ?");
			fault = "Time was not included as expected";
			Assert.IsFalse(calendar.IsTimeIncluded(tst), fault);
		}

		[Test]
		public void TestTimeIncluded_Range()
		{
			string expr = string.Format("* * 0-7,18-23 ? * *");
			CronCalendar calendar = new CronCalendar(expr);
			DateTime tst = DateTime.UtcNow;
			tst = new DateTime(tst.Year, tst.Month, tst.Day, 7, tst.Minute, tst.Second);
			Assert.IsTrue(calendar.IsTimeIncluded(tst), "Time was not included but it should be");

			tst = new DateTime(tst.Year, tst.Month, tst.Day, 10, tst.Minute, tst.Second);

			Assert.IsFalse(calendar.IsTimeIncluded(tst), "Time was included, but it shouldn't be");

			tst = new DateTime(tst.Year, tst.Month, tst.Day, 19, tst.Minute, tst.Second);
			Assert.IsTrue(calendar.IsTimeIncluded(tst), "Time was not included but it should be");
		}
		[Test]
		public void TestTimeIncluded_Range2()
		{
			string expr = string.Format("* * 0-20 ? * *");
			CronCalendar calendar = new CronCalendar(expr);
			DateTime tst = DateTime.UtcNow;
			tst = new DateTime(tst.Year, tst.Month, tst.Day, 10, tst.Minute, tst.Second);
			Assert.IsTrue(calendar.IsTimeIncluded(tst), "Time was not included but it should be");

			tst = new DateTime(tst.Year, tst.Month, tst.Day, 22, tst.Minute, tst.Second);

			Assert.IsFalse(calendar.IsTimeIncluded(tst), "Time was included, but it shouldn't be");
		}
		[Test]
		public void TestClone()
		{
			string expr = string.Format("0/15 * * * * ?");
			CronCalendar calendar = new CronCalendar(expr);
			CronCalendar clone = (CronCalendar)calendar.Clone();
			Assert.AreEqual(calendar.CronExpression, clone.CronExpression);
		}

	}
}
