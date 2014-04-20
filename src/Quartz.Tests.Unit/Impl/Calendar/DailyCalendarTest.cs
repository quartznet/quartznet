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
using Quartz.Util;

namespace Quartz.Tests.Unit.Impl.Calendar
{
	/// <summary>
	/// Unit test for DailyCalendar.
	/// </summary>
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
	public class DailyCalendarTest : SerializationTestSupport
	{
		private static readonly string[] Versions = new string[] {"0.6.0"};

		[Test]
		public void TestStringStartEndTimes()
		{
			DailyCalendar dailyCalendar = new DailyCalendar("1:20", "14:50");
			Assert.IsTrue(dailyCalendar.ToString().IndexOf("01:20:00:000 - 14:50:00:000") > 0);

			dailyCalendar = new DailyCalendar("1:20:1:456", "14:50:15:2");
			Assert.IsTrue(dailyCalendar.ToString().IndexOf("01:20:01:456 - 14:50:15:002") > 0);
		}

        [Test]
        public void TestStartEndTimes()
        {
            // Grafit found a copy-paste problem from ending time, it was the same as starting time

            DateTime d = DateTime.Now;
            DailyCalendar dailyCalendar = new DailyCalendar("1:20", "14:50");
            DateTime expectedStartTime = new DateTime(d.Year, d.Month, d.Day, 1, 20, 0);
            DateTime expectedEndTime = new DateTime(d.Year, d.Month, d.Day, 14, 50, 0);

            Assert.AreEqual(expectedStartTime, dailyCalendar.GetTimeRangeStartingTimeUtc(d).DateTime);
            Assert.AreEqual(expectedEndTime, dailyCalendar.GetTimeRangeEndingTimeUtc(d).DateTime);
        }


		[Test]
		public void TestStringInvertTimeRange()
		{
			DailyCalendar dailyCalendar = new DailyCalendar("1:20", "14:50");
			dailyCalendar.InvertTimeRange = true;
			Assert.IsTrue(dailyCalendar.ToString().IndexOf("inverted: True") > 0);

			dailyCalendar.InvertTimeRange = false;
			Assert.IsTrue(dailyCalendar.ToString().IndexOf("inverted: False") > 0);
		}

        [Test]
        public void TestTimeZone()
        {
            TimeZoneInfo tz = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");

            DailyCalendar dailyCalendar = new DailyCalendar("12:00:00", "14:00:00");
            dailyCalendar.InvertTimeRange = true; //inclusive calendar
            dailyCalendar.TimeZone = tz;
            
            // 11/2/2012 17:00 (utc) is 11/2/2012 13:00 (est)
            DateTimeOffset timeToCheck = new DateTimeOffset(2012, 11, 2, 17, 0, 0, TimeSpan.FromHours(0));
            Assert.IsTrue(dailyCalendar.IsTimeIncluded(timeToCheck));
        }


		/// <summary>
		/// Get the object to serialize when generating serialized file for future
		/// tests, and against which to validate deserialized object.
		/// </summary>
		/// <returns></returns>
		protected override object GetTargetObject()
		{
			DailyCalendar c = new DailyCalendar("01:20:01:456", "14:50:15:002");
			c.Description = "description";
			c.InvertTimeRange = true;

			return c;
		}

		/// <summary>
		/// Get the Quartz versions for which we should verify
		/// serialization backwards compatibility.
		/// </summary>
		/// <returns></returns>
		protected override string[] GetVersions()
		{
			return Versions;
		}


		/// <summary>
		/// Verify that the target object and the object we just deserialized 
		/// match.
		/// </summary>
		/// <param name="target"></param>
		/// <param name="deserialized"></param>
		protected override void VerifyMatch(object target, object deserialized)
		{
			DailyCalendar targetCalendar = (DailyCalendar) target;
			DailyCalendar deserializedCalendar = (DailyCalendar) deserialized;

			Assert.IsNotNull(deserializedCalendar);
			Assert.AreEqual(targetCalendar.Description, deserializedCalendar.Description);
			Assert.IsTrue(deserializedCalendar.InvertTimeRange);
			//Assert.IsNull(deserializedCalendar.TimeZone);
			Assert.IsTrue(deserializedCalendar.ToString().IndexOf("01:20:01:456 - 14:50:15:002") > 0);
		}
	}
}