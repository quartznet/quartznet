using System;
using System.Collections.Generic;
using System.Text;

using NUnit.Framework;

using Quartz.Impl.Calendar;

namespace Quartz.Tests.Unit.Impl.Calendar
{
    [TestFixture]
    public class AnnualCalendarTest : SerializationTestSupport
    {
        private AnnualCalendar cal;

        private static string[] VERSIONS = new string[] { "1.5.1" };

        //private static final TimeZone EST_TIME_ZONE = TimeZone.getTimeZone("America/New_York"); 

        [SetUp]
        public void Setup()
        {
            cal = new AnnualCalendar();
        }
    
        [Test]
        public void TestDayExclusion()
        {
            DateTime d = new DateTime(2005, 1, 1);
            cal.SetDayExcluded(d, true);
            Assert.IsFalse(cal.IsTimeIncluded(d), "Time was included when it was supposed not to be");
            Assert.IsTrue(cal.IsDayExcluded(d), "Day was not excluded when it was supposed to be excluded");
            Assert.AreEqual(1, cal.DaysExcluded.Count);
            Assert.AreEqual(d, cal.DaysExcluded[0]);
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


        /// <summary>
        /// Get the object to serialize when generating serialized file for future
        /// tests, and against which to validate deserialized object.
        /// </summary>
        /// <returns></returns>
        protected override object GetTargetObject()
        {
            AnnualCalendar c = new AnnualCalendar();
            c.Description = "description";
            DateTime cal = new DateTime(2005, 1, 20, 10, 5, 15);
            c.SetDayExcluded(cal, true);
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
