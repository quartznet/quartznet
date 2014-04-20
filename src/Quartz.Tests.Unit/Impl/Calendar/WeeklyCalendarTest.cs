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
    public class WeeklyCalendarTest : SerializationTestSupport
    {
        private WeeklyCalendar cal;

        private static string[] VERSIONS = new string[] { "1.5.1" };

        //private static final TimeZone EST_TIME_ZONE = TimeZone.getTimeZone("America/New_York"); 

        [SetUp]
        public void Setup()
        {
            cal = new WeeklyCalendar();
            cal.TimeZone = TimeZoneInfo.Utc; //assume utc if not specified.
        }

        [Test]
        public void TestAddAndRemoveExclusion()
        {
            cal.SetDayExcluded(DayOfWeek.Monday, true);
            Assert.IsTrue(cal.IsDayExcluded(DayOfWeek.Monday));
            cal.SetDayExcluded(DayOfWeek.Monday, false);
            Assert.IsFalse(cal.IsDayExcluded(DayOfWeek.Monday));
        }

        [Test]
        public void TestWeekDayExclusion()
        {
            // this is friday
            DateTimeOffset excluded = new DateTimeOffset(2007, 8, 3, 0, 0, 0, TimeSpan.Zero);
            cal.SetDayExcluded(DayOfWeek.Friday, true);
            // next monday should be next possible
            Assert.AreEqual(excluded.AddDays(3), cal.GetNextIncludedTimeUtc(excluded));
        }

        
        [Test]
        public void TestDaylightSavingTransition()
        {
            cal.TimeZone = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");
            cal.SetDayExcluded(DayOfWeek.Monday, false); //Monday only
            cal.SetDayExcluded(DayOfWeek.Tuesday, true);
            cal.SetDayExcluded(DayOfWeek.Wednesday, true);
            cal.SetDayExcluded(DayOfWeek.Thursday, true);
            cal.SetDayExcluded(DayOfWeek.Friday, true);
            cal.SetDayExcluded(DayOfWeek.Saturday, true);
            cal.SetDayExcluded(DayOfWeek.Sunday, true);

            //11/5/2012 12:00:00 AM -04:00 will translate into 11/4/2012 11:00:00 PM -05:00, which is a Sunday, not monday
            DateTimeOffset date = new DateTimeOffset(2012, 11, 5, 0, 0, 0, TimeSpan.FromHours(-4)); 
            Assert.IsFalse(cal.IsTimeIncluded(date));

            date = cal.GetNextIncludedTimeUtc(date);
            DateTimeOffset expected = new DateTimeOffset(2012, 11, 5, 0, 0, 0, TimeSpan.FromHours(-5));

            Assert.AreEqual(expected, date);
        }


    
        /// <summary>
        /// Get the object to serialize when generating serialized file for future
        /// tests, and against which to validate deserialized object.
        /// </summary>
        /// <returns></returns>
        protected override object GetTargetObject()
        {
            AnnualCalendar c = new AnnualCalendar();
            c.Description = "description";
            DateTime date = new DateTime(2005, 1, 20, 10, 5, 15);
            c.SetDayExcluded(date, true);
            return c;
        }

        /// <summary>
        /// Get the Quartz versions for which we should verify
        /// serialization backwards compatibility.
        /// </summary>
        /// <returns></returns>
        protected override string[] GetVersions()
        {
            return VERSIONS;
        }

        /// <summary>
        /// Verify that the target object and the object we just deserialized 
        /// match.
        /// </summary>
        /// <param name="target"></param>
        /// <param name="deserialized"></param>
        protected override void VerifyMatch(object target, object deserialized)
        {
            AnnualCalendar targetCalendar = (AnnualCalendar)target;
            AnnualCalendar deserializedCalendar = (AnnualCalendar)deserialized;

            Assert.IsNotNull(deserializedCalendar);
            Assert.AreEqual(targetCalendar.Description, deserializedCalendar.Description);
            Assert.AreEqual(targetCalendar.DaysExcluded, deserializedCalendar.DaysExcluded);
            //Assert.IsNull(deserializedCalendar.getTimeZone());
        }
    }
}
