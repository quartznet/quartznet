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
    public class AnnualCalendarTest : SerializationTestSupport
    {
        private AnnualCalendar cal;

        private static readonly string[] Versions = new string[] {"0.6.0"};

        [SetUp]
        public void Setup()
        {
            cal = new AnnualCalendar();
        }

        [Test]
        public void TestDayExclusion()
        {
            // we're local by default
            DateTime d = new DateTime(2005, 1, 1);
            cal.SetDayExcluded(d, true);
            Assert.IsFalse(cal.IsTimeIncluded(d.ToUniversalTime()), "Time was included when it was supposed not to be");
            Assert.IsTrue(cal.IsDayExcluded(d), "Day was not excluded when it was supposed to be excluded");
            Assert.AreEqual(1, cal.DaysExcluded.Count);
            Assert.AreEqual(d.Day, cal.DaysExcluded[0].Day);
            Assert.AreEqual(d.Month, cal.DaysExcluded[0].Month);
        }

        [Test]
        public void TestDayInclusionAfterExclusion()
        {
            DateTime d = new DateTime(2005, 1, 1);
            cal.SetDayExcluded(d, true);
            cal.SetDayExcluded(d, false);
            cal.SetDayExcluded(d, false);
            Assert.IsTrue(cal.IsTimeIncluded(d), "Time was not included when it was supposed to be");
            Assert.IsFalse(cal.IsDayExcluded(d), "Day was excluded when it was supposed to be included");
        }

        [Test]
        public void TestDayExclusionDifferentYears()
        {
            string errMessage = "Day was not excluded when it was supposed to be excluded";
            DateTime d = new DateTime(2005, 1, 1);
            cal.SetDayExcluded(d, true);
            Assert.IsTrue(cal.IsDayExcluded(d), errMessage);
            Assert.IsTrue(cal.IsDayExcluded(d.AddYears(-2)), errMessage);
            Assert.IsTrue(cal.IsDayExcluded(d.AddYears(2)), errMessage);
            Assert.IsTrue(cal.IsDayExcluded(d.AddYears(100)), errMessage);
        }

        [Test]
        public void TestExclusionAndNextIncludedTime()
        {
            cal.DaysExcluded = null;
            DateTimeOffset test = DateTimeOffset.UtcNow.Date;
            Assert.AreEqual(test, cal.GetNextIncludedTimeUtc(test), "Did not get today as date when nothing was excluded");

            cal.SetDayExcluded(test, true);
            Assert.AreEqual(test.AddDays(1), cal.GetNextIncludedTimeUtc(test), "Did not get next day when current day excluded");
        }

        /// <summary>
        /// QUARTZ-679 Test if the annualCalendar works over years.
        /// </summary>
        [Test]
        public void TestDaysExcludedOverTime()
        {
            AnnualCalendar annualCalendar = new AnnualCalendar();

            DateTime day = new DateTime(2005, 6, 23);
            annualCalendar.SetDayExcluded(day, true);

            day = new DateTime(2008, 2, 1);
            annualCalendar.SetDayExcluded(day, true);

            Assert.IsTrue(annualCalendar.IsDayExcluded(day), "The day 1 February is expected to be excluded but it is not");
        }

        /// <summary>
        /// Part 2 of the tests of QUARTZ-679
        /// </summary>
        [Test]
        public void TestRemoveInTheFuture()
        {
            AnnualCalendar annualCalendar = new AnnualCalendar();

            DateTime day = new DateTime(2005, 6, 23);
            annualCalendar.SetDayExcluded(day, true);

            // Trying to remove the 23th of June
            day = new DateTime(2008, 6, 23);
            annualCalendar.SetDayExcluded(day, false);

            Assert.IsFalse(annualCalendar.IsDayExcluded(day), "The day 23 June is not expected to be excluded but it is");
        }

        [Test]
        public void TestAnnualCalendarTimeZone()
        {
            TimeZoneInfo tz = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");
            AnnualCalendar c = new AnnualCalendar();
            c.TimeZone = tz;

            DateTimeOffset excludedDay = new DateTimeOffset(2012, 11, 4, 0, 0, 0, TimeSpan.Zero);
            c.SetDayExcluded(excludedDay, true);

            // 11/5/2012 12:00:00 AM -04:00  translate into 11/4/2012 11:00:00 PM -05:00 (EST)
            DateTimeOffset date = new DateTimeOffset(2012, 11, 5, 0, 0, 0, TimeSpan.FromHours(-4));

            Assert.IsFalse(c.IsTimeIncluded(date), "date was expected to not be included.");
            Assert.IsTrue(c.IsTimeIncluded(date.AddDays(1)));

            DateTimeOffset expectedNextAvailable = new DateTimeOffset(2012, 11, 5, 0, 0, 0, TimeSpan.FromHours(-5));
            DateTimeOffset actualNextAvailable = c.GetNextIncludedTimeUtc(date);
            Assert.AreEqual(expectedNextAvailable, actualNextAvailable);
        }

        [Test]
        public void BaseCalendarShouldNotAffectSettingInternalDataStructures()
        {
            var dayToExclude = new DateTime(2015, 1, 1);

            AnnualCalendar a = new AnnualCalendar();
            a.SetDayExcluded(dayToExclude, true);

            AnnualCalendar b = new AnnualCalendar(a);
            b.SetDayExcluded(dayToExclude, true);

            b.CalendarBase = null;

            Assert.That(b.IsDayExcluded(dayToExclude), "day was no longer excluded after base calendar was detached");
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
            AnnualCalendar targetCalendar = (AnnualCalendar) target;
            AnnualCalendar deserializedCalendar = (AnnualCalendar) deserialized;

            Assert.IsNotNull(deserializedCalendar);
            Assert.AreEqual(targetCalendar.Description, deserializedCalendar.Description);
            Assert.AreEqual(targetCalendar.DaysExcluded, deserializedCalendar.DaysExcluded);
            //Assert.IsNull(deserializedCalendar.getTimeZone());
        }
    }
}