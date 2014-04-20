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
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class HolidayCalendarTest : SerializationTestSupport
    {
        private HolidayCalendar cal;

        private static readonly string[] versions = new[] { "1.5.1" };

        [SetUp]
        public void Setup()
        {
            cal = new HolidayCalendar();
        }

        [Test]
        public void TestAddAndRemoveExclusion()
        {
            cal.AddExcludedDate(new DateTime(2007, 10, 20, 12, 40, 22));
            cal.RemoveExcludedDate(new DateTime(2007, 10, 20, 2, 0, 0));
            Assert.IsTrue(cal.ExcludedDates.Count == 0);
        }

        [Test]
        public void TestDayExclusion()
        {
            // use end of day to get by with utc offsets
            DateTime excluded = new DateTime(2007, 12, 31);
            cal.AddExcludedDate(excluded);
            
            Assert.AreEqual(new DateTimeOffset(2008, 1, 1, 0,0,0, cal.TimeZone.BaseUtcOffset), cal.GetNextIncludedTimeUtc(excluded));
        }
    
        /// <summary>
        /// Get the object to serialize when generating serialized file for future
        /// tests, and against which to validate deserialized object.
        /// </summary>
        /// <returns></returns>
        protected override object GetTargetObject()
        {
            HolidayCalendar c = new HolidayCalendar();
            c.Description = "description";
            DateTime date = new DateTime(2005, 1, 20, 10, 5, 15);
            c.AddExcludedDate(date);
            return c;
        }

        [Test]
        public void TestTimeZone()
        {
            TimeZoneInfo tz = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");
            HolidayCalendar c = new HolidayCalendar();
            c.TimeZone = tz;

            DateTimeOffset excludedDay = new DateTimeOffset(2012, 11, 4, 0,0,0, TimeSpan.Zero);
            c.AddExcludedDate(excludedDay.DateTime);

            // 11/5/2012 12:00:00 AM -04:00  translate into 11/4/2012 11:00:00 PM -05:00 (EST)
            DateTimeOffset date = new DateTimeOffset(2012, 11, 5, 0, 0, 0, TimeSpan.FromHours(-4));

            Assert.IsFalse(c.IsTimeIncluded(date), "date was expected to not be included.");
            Assert.IsTrue(c.IsTimeIncluded(date.AddDays(1)));

            DateTimeOffset expectedNextAvailable = new DateTimeOffset(2012, 11, 5, 0, 0, 0, TimeSpan.FromHours(-5));
            DateTimeOffset actualNextAvailable = c.GetNextIncludedTimeUtc(date);
            Assert.AreEqual(expectedNextAvailable, actualNextAvailable);
        }
    

        /// <summary>
        /// Get the Quartz versions for which we should verify
        /// serialization backwards compatibility.
        /// </summary>
        /// <returns></returns>
        protected override string[] GetVersions()
        {
            return versions;
        }

        /// <summary>
        /// Verify that the target object and the object we just deserialized 
        /// match.
        /// </summary>
        /// <param name="target"></param>
        /// <param name="deserialized"></param>
        protected override void VerifyMatch(object target, object deserialized)
        {
            HolidayCalendar targetCalendar = (HolidayCalendar)target;
            HolidayCalendar deserializedCalendar = (HolidayCalendar)deserialized;

            Assert.IsNotNull(deserializedCalendar);
            Assert.AreEqual(targetCalendar.Description, deserializedCalendar.Description);
            Assert.AreEqual(targetCalendar.ExcludedDates, deserializedCalendar.ExcludedDates);
            //Assert.IsNull(deserializedCalendar.getTimeZone());
        }
    }
}
