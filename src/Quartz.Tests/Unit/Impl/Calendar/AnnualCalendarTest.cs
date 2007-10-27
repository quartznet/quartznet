/* 
* Copyright 2004-2005 OpenSymphony 
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

using System;

using NUnit.Framework;

using Quartz.Impl.Calendar;

namespace Quartz.Tests.Unit.Impl.Calendar
{
    [TestFixture]
    public class AnnualCalendarTest : SerializationTestSupport
    {
        private AnnualCalendar cal;

        private static string[] VERSIONS = new string[] { "1.5.1" };

        private static readonly TimeZone TEST_TIME_ZONE = TimeZone.CurrentTimeZone; 

        [SetUp]
        public void Setup()
        {
            cal = new AnnualCalendar();
            cal.TimeZone = TEST_TIME_ZONE;
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
            Assert.AreEqual(d.Day, ((DateTime) cal.DaysExcluded[0]).Day);
            Assert.AreEqual(d.Month, ((DateTime)cal.DaysExcluded[0]).Month);
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
            DateTime test = DateTime.UtcNow.Date;
            Assert.AreEqual(test, cal.GetNextIncludedTimeUtc(test), "Did not get today as date when nothing was excluded");

            cal.SetDayExcluded(test, true);
            Assert.AreEqual(test.AddDays(1), cal.GetNextIncludedTimeUtc(test), "Did not get next day when current day excluded");

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
            ///Assert.IsNull(deserializedCalendar.getTimeZone());
        }
    }
}
