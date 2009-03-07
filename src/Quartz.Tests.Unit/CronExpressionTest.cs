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
using System.Collections;

using Quartz.Collection;

#if NET_20
using NullableDateTime = System.Nullable<System.DateTime>;
#else
using Nullables;
#endif

#if NET_35
using TimeZone = System.TimeZoneInfo;
#endif

using NUnit.Framework;

namespace Quartz.Tests.Unit
{
    [TestFixture]
    public class CronExpressionTest : SerializationTestSupport
    {
        private static readonly string[] Versions = new string[] { "0.6.0" };

#if !NET_35
        private static readonly TimeZone TestTimeZone = TimeZone.CurrentTimeZone;
#else
        private static readonly TimeZone TestTimeZone = TimeZoneInfo.Local;
#endif

        /// <summary>
        /// Get the object to serialize when generating serialized file for future
        /// tests, and against which to validate deserialized object.
        /// </summary>
        /// <returns></returns>
        protected override object GetTargetObject()
        {
            CronExpression cronExpression = new CronExpression("0 15 10 * * ? 2005");
            cronExpression.TimeZone = TestTimeZone;

            return cronExpression;
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
            CronExpression targetCronExpression = (CronExpression)target;
            CronExpression deserializedCronExpression = (CronExpression)deserialized;

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

            DateTime cal = new DateTime(2005, 6, 1, 10, 15, 0).ToUniversalTime();
            Assert.IsTrue(cronExpression.IsSatisfiedBy(cal));

            cal = cal.AddYears(1);
            Assert.IsFalse(cronExpression.IsSatisfiedBy(cal));

            cal = new DateTime(2005, 6, 1, 10, 16, 0).ToUniversalTime();
            Assert.IsFalse(cronExpression.IsSatisfiedBy(cal));

            cal = new DateTime(2005, 6, 1, 10, 14, 0).ToUniversalTime();
            Assert.IsFalse(cronExpression.IsSatisfiedBy(cal));

            cronExpression = new CronExpression("0 15 10 ? * MON-FRI");

            // weekends
            cal = new DateTime(2007, 6, 9, 10, 15, 0).ToUniversalTime();
            Assert.IsFalse(cronExpression.IsSatisfiedBy(cal));
            Assert.IsFalse(cronExpression.IsSatisfiedBy(cal.AddDays(1)));
        }

        [Test]
        public void TestCronExpressionPassingMidnight()
        {
            CronExpression cronExpression = new CronExpression("0 15 23 * * ?");
            DateTime cal = new DateTime(2005, 6, 1, 23, 16, 0).ToUniversalTime();
            DateTime nextExpectedFireTime = new DateTime(2005, 6, 2, 23, 15, 0).ToUniversalTime();
            Assert.AreEqual(nextExpectedFireTime, cronExpression.GetTimeAfter(cal).Value);
        }

        [Test]
        public void TestCronExpressionPassingYear()
        {
            DateTime start = new DateTime(2007, 12, 1, 23, 59, 59).ToUniversalTime();

            CronExpression ce = new CronExpression("0 55 15 1 * ?");
            DateTime d = ce.GetNextValidTimeAfter(start).Value.ToLocalTime();
            Assert.AreEqual(new DateTime(2008, 1, 1, 15, 55, 0), d, "Got wrong date and time when passed year");
        }


        [Test]
        public void TestCronExpressionWeekdaysMonFri()
        {
            CronExpression cronExpression = new CronExpression("0 0 12 ? * MON-FRI");
            int[] arrJuneDaysThatShouldFire =
                new int[] { 1, 4, 5, 6, 7, 8, 11, 12, 13, 14, 15, 18, 19, 20, 22, 21, 25, 26, 27, 28, 29 };
            ArrayList juneDays = new ArrayList(arrJuneDaysThatShouldFire);

            TestCorrectWeekFireDays(cronExpression, juneDays);
        }

        [Test]
        public void TestCronExpressionWeekdaysFriday()
        {
            CronExpression cronExpression = new CronExpression("0 0 12 ? * FRI");
            int[] arrJuneDaysThatShouldFire =
                new int[] { 1, 8, 15, 22, 29 };
            ArrayList juneDays = new ArrayList(arrJuneDaysThatShouldFire);

            TestCorrectWeekFireDays(cronExpression, juneDays);
        }

        [Test]
        public void TestCronExpressionLastDayOfMonth()
        {
            CronExpression cronExpression = new CronExpression("0 0 12 L * ?");
            int[] arrJuneDaysThatShouldFire = new int[] { 30 };
            ArrayList juneDays = new ArrayList(arrJuneDaysThatShouldFire);

            TestCorrectWeekFireDays(cronExpression, juneDays);
        }

        [Test]
        public void TestHourShift()
        {
            // cronexpression that fires every 5 seconds
            CronExpression cronExpression = new CronExpression("0/5 * * * * ?");
            DateTime cal = new DateTime(2005, 6, 1, 1, 59, 55);
            DateTime nextExpectedFireTime = new DateTime(2005, 6, 1, 2, 0, 0);
            Assert.AreEqual(nextExpectedFireTime, cronExpression.GetTimeAfter(cal).Value);
        }

        [Test]
        public void TestMonthShift()
        {
            // QRTZNET-28
            CronExpression cronExpression = new CronExpression("* * 1 * * ?");
            DateTime cal = new DateTime(2005, 7, 31, 22, 59, 57).ToUniversalTime();
            DateTime nextExpectedFireTime = new DateTime(2005, 8, 1, 1, 0, 0).ToUniversalTime();
            Assert.AreEqual(nextExpectedFireTime, cronExpression.GetTimeAfter(cal).Value);
        }

        [Test]
        public void TestYearChange()
        {
            // QRTZNET-85
            CronExpression cronExpression = new CronExpression("0 12 4 ? * 3");
            cronExpression.GetNextValidTimeAfter(new DateTime(2007, 12, 28));
        }

        [Test]
        public void TestCronExpressionParsingIncorrectDayOfWeek()
        {
            // test failed before because of improper trimming
            try
            {
                string expr = string.Format(" * * * * * {0}", DateTime.Now.Year);
                CronExpression ce = new CronExpression(expr);
                ce.IsSatisfiedBy(DateTime.UtcNow.AddMinutes(2));
                Assert.Fail("Accepted wrong format");
            }
            catch (FormatException fe)
            {
                Assert.AreEqual("Day-of-Week values must be between 1 and 7", fe.Message);
            }
        }

        [Test]
        public void TestCronExpressionWithExtraWhiteSpace()
        {
            // test failed before because of improper trimming
            string expr = " 30 *   * * * ?  ";
            CronExpression calendar = new CronExpression(expr);
            Assert.IsFalse(calendar.IsSatisfiedBy(DateTime.Now.AddMinutes(2)), "Time was included");
        }

        private static void TestCorrectWeekFireDays(CronExpression cronExpression, IList correctFireDays)
        {
            ArrayList fireDays = new ArrayList();

            DateTime cal = new DateTime(2007, 6, 1, 11, 0, 0).ToUniversalTime();
            for (int i = 0; i < DateTime.DaysInMonth(2007, 6); ++i)
            {
                NullableDateTime nextFireTime = cronExpression.GetTimeAfter(cal);
                if (!fireDays.Contains(nextFireTime.Value.Day) && nextFireTime.Value.Month == 6)
                {
                    // next fire day may be monday for several days..
                    fireDays.Add(nextFireTime.Value.Day);
                }
                cal = cal.AddDays(1);
            }
            // check rite dates fired
            for (int i = 0; i < fireDays.Count; ++i)
            {
                int idx = correctFireDays.IndexOf(fireDays[i]);
                Assert.Greater(idx, -1,
                               string.Format("CronExpression evaluated true for {0} even when it shouldn't have", fireDays[i]));
                correctFireDays.RemoveAt(idx);
            }

            // check that all fired
            Assert.IsEmpty(correctFireDays,
                           string.Format("CronExpression did not evaluate true for all expected days (count: {0}).", correctFireDays.Count));
        }

        [Test]
        [ExpectedException(
            typeof(FormatException),
            ExpectedMessage = "Support for specifying both a day-of-week AND a day-of-month parameter is not implemented.",
            UserMessage = "Expected FormatException did not fire for wildcard day-of-month and day-of-week")]
        public void TestFormatExceptionWildCardDayOfMonthAndDayOfWeek()
        {
            CronExpression cronExpression = new CronExpression("0 0 * * * *");
        }

        [Test]
        [ExpectedException(
            typeof(FormatException),
            ExpectedMessage = "Support for specifying both a day-of-week AND a day-of-month parameter is not implemented.",
            UserMessage = "Expected FormatException did not fire for specified day-of-month and wildcard day-of-week")]
        public void TestFormatExceptionSpecifiedDayOfMonthAndWildCardDayOfWeek()
        {
            CronExpression cronExpression = new CronExpression("0 0 * 4 * *");
        }

        [Test]
        [ExpectedException(
            typeof(FormatException),
            ExpectedMessage = "Support for specifying both a day-of-week AND a day-of-month parameter is not implemented.",
            UserMessage = "Expected FormatException did not fire for wildcard day-of-month and specified day-of-week")]
        public void TestFormatExceptionWildCardDayOfMonthAndSpecifiedDayOfWeek()
        {
            CronExpression cronExpression = new CronExpression("0 0 * * * 4");
        }

        [Test]
        public void TestNthWeekDayPassingMonth()
        {
            CronExpression ce = new CronExpression("0 30 10-13 ? * FRI#3");
            DateTime start = new DateTime(2008, 12, 19, 0, 0, 0);
            for (int i = 0; i < 200; ++i)
            {
                bool shouldFire = (start.Hour >= 10 && start.Hour <= 13 && start.Minute == 30 && (start.DayOfWeek == DayOfWeek.Wednesday || start.DayOfWeek == DayOfWeek.Friday));
                shouldFire = shouldFire && start.Day > 15 && start.Day < 28;

                bool satisfied = ce.IsSatisfiedBy(start.ToUniversalTime());
                Assert.AreEqual(shouldFire, satisfied);

                // cycle with half hour precision
                start = start.AddHours(0.5);
            }
        }

        [Test]
        public void TestNormal()
        {
            for (int i = 0; i < 6; i++)
            {
                AssertParsesForField("0 15 10 * * ? 2005", i);
            }
        }

        [Test]
        public void TestSecond()
        {
            AssertParsesForField("58-4 5 21 ? * MON-FRI", 0);
        }

        [Test]
        public void TestMinute()
        {
            AssertParsesForField("0 58-4 21 ? * MON-FRI", 1);
        }

        [Test]
        public void TestHour()
        {
            AssertParsesForField("0 0/5 21-3 ? * MON-FRI", 2);
        }

        [Test]
        public void TestDayOfWeekNumber()
        {
            AssertParsesForField("58 5 21 ? * 6-2", 5);
        }

        [Test]
        public void TestDayOfWeek()
        {
            AssertParsesForField("58 5 21 ? * FRI-TUE", 5);
        }

        [Test]
        public void TestDayOfMonth()
        {
            AssertParsesForField("58 5 21 28-5 1 ?", 3);
        }

        [Test]
        public void TestMonth()
        {
            AssertParsesForField("58 5 21 ? 11-2 FRI", 4);
        }

        [Test]
        public void TestAmbiguous()
        {
            Console.Error.WriteLine(AssertParsesForField("0 0 14-6 ? * FRI-MON", 2));
            Console.Error.WriteLine(AssertParsesForField("0 0 14-6 ? * FRI-MON", 5));

            Console.Error.WriteLine(AssertParsesForField("55-3 56-2 6 ? * FRI", 0));
            Console.Error.WriteLine(AssertParsesForField("55-3 56-2 6 ? * FRI", 1));
        }

        private static ISet AssertParsesForField(String expression, int constant)
        {
            try
            {
                TestCronExpression cronExpression = new TestCronExpression(expression);
                ISet set = cronExpression.GetSetPublic(constant);
                if (set.Count == 0)
                {
                    Assert.Fail("Empty field [" + constant + "] returned for " + expression);
                }
                return set;
            }
            catch (FormatException pe)
            {
                Assert.Fail("Exception thrown during parsing: " + pe);
            }
            return null;  // not reachable
        }

        [Test]
        public void TestQuartz640()
        {
            try
            {
                new CronExpression("0 43 9 1,5,29,L * ?");
                Assert.Fail("Expected FormatException did not fire for L combined with other days of the month");
            }
            catch (FormatException fe)
            {
                Assert.IsTrue(
                    fe.Message.StartsWith("Support for specifying 'L' and 'LW' with other days of the month is not implemented"),
                    "Incorrect FormatException thrown");
            }
            try
            {
                new CronExpression("0 43 9 ? * SAT,SUN,L");
                Assert.Fail("Expected FormatException did not fire for L combined with other days of the week");
            }
            catch (FormatException pe)
            {
                Assert.IsTrue(
                    pe.Message.StartsWith("Support for specifying 'L' with other days of the week is not implemented"),
                    "Incorrect FormatException thrown");
            }
            try
            {
                new CronExpression("0 43 9 ? * 6,7,L");
                Assert.Fail("Expected FormatException did not fire for L combined with other days of the week");
            }
            catch (FormatException pe)
            {
                Assert.IsTrue(
                    pe.Message.StartsWith("Support for specifying 'L' with other days of the week is not implemented"),
                    "Incorrect FormatException thrown");
            }
            try 
            { 
                new CronExpression("0 43 9 ? * 5L"); 
            }
            catch (FormatException) 
            { 
                Assert.Fail("Unexpected ParseException thrown for supported '5L' expression."); 
            } 
        }

        [Test(Description = "http://jira.opensymphony.com/browse/QRTZNET-149")]
        public void TestGetTimeAfter_QRTZNET149()
        {
            CronExpression expression = new CronExpression("0 0 0 29 * ?");
            NullableDateTime after = expression.GetNextValidTimeAfter(new DateTime(2009, 1, 30));
            Assert.IsTrue(after.HasValue);
            Assert.AreEqual(new DateTime(2009, 3, 29, 0, 0, 0).ToUniversalTime(), after.Value);

            after = expression.GetNextValidTimeAfter(new DateTime(2009, 12, 30));
            Assert.IsTrue(after.HasValue);
            Assert.AreEqual(new DateTime(2010, 1, 29, 0, 0, 0).ToUniversalTime(), after.Value);
        }

        [Test]
        public void TestQRTZNET152()
        {
            CronExpression expression = new CronExpression("0 5 13 5W 1-12 ?");
            DateTime d = expression.GetNextValidTimeAfter(new DateTime(2009, 3, 8).ToUniversalTime()).Value;
            Assert.AreEqual(new DateTime(2009, 4, 6, 13, 5, 0), d.ToLocalTime());
            d = expression.GetNextValidTimeAfter(d).Value;
            Assert.AreEqual(new DateTime(2009, 5, 5, 13, 5, 0), d.ToLocalTime());
        }

    }


    class TestCronExpression : CronExpression
    {
        public TestCronExpression(String cronExpression)
            : base(cronExpression)
        {
        }

        public ISet GetSetPublic(int constant)
        {
            return base.GetSet(constant);
        }
    }
}
