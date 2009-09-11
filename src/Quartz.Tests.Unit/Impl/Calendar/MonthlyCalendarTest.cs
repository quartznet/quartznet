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
    public class MonthlyCalendarTest : SerializationTestSupport
    {
        private MonthlyCalendar cal;

        private static string[] VERSIONS = new string[] { "1.5.1" };

        //private static final TimeZone EST_TIME_ZONE = TimeZone.getTimeZone("America/New_York"); 

        [SetUp]
        public void Setup()
        {
            cal = new MonthlyCalendar();
        }

        [Test]
        public void TestAddAndRemoveExclusion()
        {
            cal.SetDayExcluded(15, true);
            Assert.IsTrue(cal.IsDayExcluded(15));
            cal.SetDayExcluded(15, false);
            Assert.IsFalse(cal.IsDayExcluded(15));
        }

        [Test]
        public void TestMonthDayExclusion()
        {
            DateTime excluded = new DateTime(2007, 8, 3);
            cal.SetDayExcluded(3, true);
            Assert.AreEqual(excluded.AddDays(1), cal.GetNextIncludedTimeUtc(excluded));
        }

        [Test]
        public void TestForInfiniteLoop()
        {
            MonthlyCalendar monthlyCalendar = new MonthlyCalendar();

            for (int i = 1; i < 9; i++)
            {
                monthlyCalendar.SetDayExcluded(i, true);
            }

            DateTime d = new DateTime(2007, 11, 8, 12, 0, 0);

            monthlyCalendar.GetNextIncludedTimeUtc(d.ToUniversalTime());
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
