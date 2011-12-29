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
using System.Collections.Generic;

using Quartz.Collection;

using NUnit.Framework;

namespace Quartz.Tests.Unit
{
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class CronExpressionTest : SerializationTestSupport
    {
        private static readonly string[] Versions = new string[] { "0.6.0" };

        private static readonly TimeZoneInfo TestTimeZone = TimeZoneInfo.Local;

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

            DateTimeOffset testDate = DateBuilder.DateOf(10, 15, 0, 1, 6, 2005);
            Assert.IsTrue(cronExpression.IsSatisfiedBy(testDate));

            testDate = testDate.AddYears(1);
            Assert.IsFalse(cronExpression.IsSatisfiedBy(testDate));

            testDate = DateBuilder.DateOf(10, 16, 0, 1, 6, 2005);
            Assert.IsFalse(cronExpression.IsSatisfiedBy(testDate));

            testDate = DateBuilder.DateOf(10, 14, 0, 1, 6, 2005);
            Assert.IsFalse(cronExpression.IsSatisfiedBy(testDate));

            cronExpression = new CronExpression("0 15 10 ? * MON-FRI");

            // weekends
            testDate = DateBuilder.DateOf(10, 15, 0, 9, 6, 2007);
            Assert.IsFalse(cronExpression.IsSatisfiedBy(testDate));
            Assert.IsFalse(cronExpression.IsSatisfiedBy(testDate.AddDays(1)));
        }

        [Test]
        public void TestLastDayOffset()
        {
            CronExpression cronExpression = new CronExpression("0 15 10 L-2 * ? 2010");

            DateTimeOffset testDate = DateBuilder.DateOf(10, 15, 0, 29, 10, 2010); // last day - 2
            Assert.IsTrue(cronExpression.IsSatisfiedBy(testDate));

            testDate = DateBuilder.DateOf(10, 15, 0, 28, 10, 2010);
            Assert.IsFalse(cronExpression.IsSatisfiedBy(testDate));

            cronExpression = new CronExpression("0 15 10 L-5W * ? 2010");

            testDate = DateBuilder.DateOf(10, 15, 0, 26, 10, 2010); // last day - 5
            Assert.IsTrue(cronExpression.IsSatisfiedBy(testDate));

            cronExpression = new CronExpression("0 15 10 L-1 * ? 2010");

            testDate = DateBuilder.DateOf(10, 15, 0, 30, 10, 2010); // last day - 1
            Assert.IsTrue(cronExpression.IsSatisfiedBy(testDate));

            cronExpression = new CronExpression("0 15 10 L-1W * ? 2010");

            testDate = DateBuilder.DateOf(10, 15, 0, 29, 10, 2010); // nearest weekday to last day - 1 (29th is a friday in 2010)
            Assert.IsTrue(cronExpression.IsSatisfiedBy(testDate));
        }

        [Test]
        public void TestCronExpressionPassingMidnight()
        {
            CronExpression cronExpression = new CronExpression("0 15 23 * * ?");
            DateTimeOffset cal = DateBuilder.DateOf(23, 16, 0, 1, 6, 2005);
            DateTimeOffset nextExpectedFireTime = DateBuilder.DateOf(23, 15, 0, 2, 6, 2005);
            Assert.AreEqual(nextExpectedFireTime, cronExpression.GetTimeAfter(cal).Value);
        }

        [Test]
        public void TestCronExpressionPassingYear()
        {
            DateTimeOffset start = DateBuilder.DateOf(23, 59, 59, 1, 12, 2007);

            CronExpression ce = new CronExpression("0 55 15 1 * ?");
            DateTimeOffset expected = DateBuilder.DateOf(15, 55, 0, 1, 1, 2008);
            DateTimeOffset d = ce.GetNextValidTimeAfter(start).Value;
            Assert.AreEqual(expected, d, "Got wrong date and time when passed year");
        }


        [Test]
        public void TestCronExpressionWeekdaysMonFri()
        {
            CronExpression cronExpression = new CronExpression("0 0 12 ? * MON-FRI");
            int[] arrJuneDaysThatShouldFire =
                new int[] { 1, 4, 5, 6, 7, 8, 11, 12, 13, 14, 15, 18, 19, 20, 22, 21, 25, 26, 27, 28, 29 };
            List<int> juneDays = new List<int>(arrJuneDaysThatShouldFire);

            TestCorrectWeekFireDays(cronExpression, juneDays);
        }

        [Test]
        public void TestCronExpressionWeekdaysFriday()
        {
            CronExpression cronExpression = new CronExpression("0 0 12 ? * FRI");
            int[] arrJuneDaysThatShouldFire =
                new int[] { 1, 8, 15, 22, 29 };
            List<int> juneDays = new List<int>(arrJuneDaysThatShouldFire);

            TestCorrectWeekFireDays(cronExpression, juneDays);
        }

        [Test]
        public void TestCronExpressionLastDayOfMonth()
        {
            CronExpression cronExpression = new CronExpression("0 0 12 L * ?");
            int[] arrJuneDaysThatShouldFire = new int[] { 30 };
            List<int> juneDays = new List<int>(arrJuneDaysThatShouldFire);

            TestCorrectWeekFireDays(cronExpression, juneDays);
        }

        [Test]
        public void TestHourShift()
        {
            // cronexpression that fires every 5 seconds
            CronExpression cronExpression = new CronExpression("0/5 * * * * ?");
            DateTimeOffset cal = DateBuilder.DateOf(1, 59, 55, 1, 6, 2005);
            DateTimeOffset nextExpectedFireTime = DateBuilder.DateOf(2, 0, 0, 1, 6, 2005);
            Assert.AreEqual(nextExpectedFireTime, cronExpression.GetTimeAfter(cal).Value);
        }

        [Test]
        public void TestMonthShift()
        {
            // QRTZNET-28
            CronExpression cronExpression = new CronExpression("* * 1 * * ?");
            DateTimeOffset cal = DateBuilder.DateOf(22, 59, 57, 31, 7, 2005);
            DateTimeOffset nextExpectedFireTime = DateBuilder.DateOf(1, 0, 0, 1, 8, 2005);
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

        private static void TestCorrectWeekFireDays(CronExpression cronExpression, IList<int> correctFireDays)
        {
            List<int> fireDays = new List<int>();

            DateTime cal = new DateTime(2007, 6, 1, 11, 0, 0).ToUniversalTime();
            for (int i = 0; i < DateTime.DaysInMonth(2007, 6); ++i)
            {
                DateTimeOffset? nextFireTime = cronExpression.GetTimeAfter(cal);
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
            Assert.IsTrue(correctFireDays.Count == 0, string.Format("CronExpression did not evaluate true for all expected days (count: {0}).", correctFireDays.Count));
        }

        [Test]
        [ExpectedException(ExpectedException = typeof(FormatException),
            ExpectedMessage = "Support for specifying both a day-of-week AND a day-of-month parameter is not implemented.")]
        public void TestFormatExceptionWildCardDayOfMonthAndDayOfWeek()
        {
            CronExpression cronExpression = new CronExpression("0 0 * * * *");
        }

        [Test]
        [ExpectedException(
            ExpectedException = typeof(FormatException),
            ExpectedMessage = "Support for specifying both a day-of-week AND a day-of-month parameter is not implemented.")]
        public void TestFormatExceptionSpecifiedDayOfMonthAndWildCardDayOfWeek()
        {
            CronExpression cronExpression = new CronExpression("0 0 * 4 * *");
        }

        [Test]
        [ExpectedException(
            ExpectedException = typeof(FormatException),
            ExpectedMessage = "Support for specifying both a day-of-week AND a day-of-month parameter is not implemented.")]
        public void TestFormatExceptionWildCardDayOfMonthAndSpecifiedDayOfWeek()
        {
            CronExpression cronExpression = new CronExpression("0 0 * * * 4");
        }

        [Test]
        public void TestNthWeekDayPassingMonth()
        {
            CronExpression ce = new CronExpression("0 30 10-13 ? * FRI#3");
            DateTimeOffset start = DateBuilder.DateOf(0, 0, 0, 19, 12, 2008);
            for (int i = 0; i < 200; ++i)
            {
                bool shouldFire = (start.Hour >= 10 && start.Hour <= 13 && start.Minute == 30 && (start.DayOfWeek == DayOfWeek.Wednesday || start.DayOfWeek == DayOfWeek.Friday));
                shouldFire = shouldFire && start.Day > 15 && start.Day < 28;

                bool satisfied = ce.IsSatisfiedBy(start);
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
            AssertParsesForField("0 0 14-6 ? * FRI-MON", 2);
            AssertParsesForField("0 0 14-6 ? * FRI-MON", 5);

            AssertParsesForField("55-3 56-2 6 ? * FRI", 0);
            AssertParsesForField("55-3 56-2 6 ? * FRI", 1);
        }

        private static void AssertParsesForField(string expression, int constant)
        {
            try
            {
                SimpleCronExpression cronExpression = new SimpleCronExpression(expression);
                Collection.ISet<int> set = cronExpression.GetSetPublic(constant);
                if (set.Count == 0)
                {
                    Assert.Fail("Empty field [" + constant + "] returned for " + expression);
                }
            }
            catch (FormatException pe)
            {
                Assert.Fail("Exception thrown during parsing: " + pe);
            }
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

        [Test]
        public void TestGetTimeAfter_QRTZNET149()
        {
            CronExpression expression = new CronExpression("0 0 0 29 * ?");
            DateTimeOffset? after = expression.GetNextValidTimeAfter(DateBuilder.DateOf(0, 0, 0, 30, 1, 2009));
            Assert.IsTrue(after.HasValue);
            Assert.AreEqual(DateBuilder.DateOf(0, 0, 0, 29, 3, 2009), after.Value);

            after = expression.GetNextValidTimeAfter(DateBuilder.DateOf(0, 0, 0, 30, 12, 2009));
            Assert.IsTrue(after.HasValue);
            Assert.AreEqual(DateBuilder.DateOf(0, 0, 0, 29, 1, 2010), after.Value);
        }

        [Test]
        public void TestQRTZNET152()
        {
            CronExpression expression = new CronExpression("0 5 13 5W 1-12 ?");
            DateTimeOffset test = DateBuilder.DateOf(0, 0, 0, 8, 3, 2009);
            DateTimeOffset d = expression.GetNextValidTimeAfter(test).Value;
            Assert.AreEqual(DateBuilder.DateOf(13, 5, 0, 6, 4, 2009), d);
            d = expression.GetNextValidTimeAfter(d).Value;
            Assert.AreEqual(DateBuilder.DateOf(13, 5, 0, 5, 5, 2009), d);
        }

        [Test]
        public void ShouldThrowExceptionIfWParameterMakesNoSense()
        {
            try
            {
                new CronExpression("0/5 * * 32W 1 ?");
                Assert.Fail("Expected ParseException did not fire for W with value larger than 31");
            }
            catch (FormatException pe)
            {
                Assert.IsTrue(pe.Message.StartsWith("The 'W' option does not make sense with values larger than"), "Incorrect ParseException thrown");
            }
        }

        private class SimpleCronExpression : CronExpression
        {
            public SimpleCronExpression(string cronExpression)
                : base(cronExpression)
            {
            }

            public ISortedSet<int> GetSetPublic(int constant)
            {
                return base.GetSet(constant);
            }
        }
    }
}
