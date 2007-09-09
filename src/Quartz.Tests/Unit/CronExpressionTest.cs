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

#if NET_20
using NullableDateTime = System.Nullable<System.DateTime>;
#else
using Nullables;
#endif

using NUnit.Framework;

namespace Quartz.Tests.Unit
{
    [TestFixture]
    public class CronExpressionTest
    {
        private static readonly string[] VERSIONS = new String[] {"1.5.2"};

        private static TimeZone EST_TIME_ZONE = TimeZone.CurrentTimeZone;

        /// <summary>
        /// Get the object to serialize when generating serialized file for future
        /// tests, and against which to validate deserialized object.
        /// </summary>
        /// <returns></returns>
        protected object GetTargetObject()
        {
            CronExpression cronExpression = new CronExpression("0 15 10 * * ? 2005");
            //cronExpression.TimeZone = EST_TIME_ZONE);

            return cronExpression;
        }

        /// <summary>
        /// Get the Quartz versions for which we should verify
        /// serialization backwards compatibility.
        /// </summary>
        /// <returns></returns>
        protected string[] GetVersions()
        {
            return VERSIONS;
        }

        /// <summary>
        /// Verify that the target object and the object we just deserialized 
        /// match.
        /// </summary>
        /// <param name="target"></param>
        /// <param name="deserialized"></param>
        protected void VerifyMatch(object target, object deserialized)
        {
            CronExpression targetCronExpression = (CronExpression) target;
            CronExpression deserializedCronExpression = (CronExpression) deserialized;

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
        public void TestCronExpressionWeekdaysMonFri()
        {
            CronExpression cronExpression = new CronExpression("0 0 12 ? * MON-FRI");
            int[] arrJuneDaysThatShouldFire =
                new int[] {1, 4, 5, 6, 7, 8, 11, 12, 13, 14, 15, 18, 19, 20, 22, 21, 25, 26, 27, 28, 29};
            ArrayList juneDays = new ArrayList(arrJuneDaysThatShouldFire);

            TestCorrectWeekFireDays(cronExpression, juneDays);
        }

        [Test]
        public void TestCronExpressionWeekdaysFriday()
        {
            CronExpression cronExpression = new CronExpression("0 0 12 ? * FRI");
            int[] arrJuneDaysThatShouldFire =
                new int[] {1, 8, 15, 22, 29};
            ArrayList juneDays = new ArrayList(arrJuneDaysThatShouldFire);

            TestCorrectWeekFireDays(cronExpression, juneDays);
        }

        [Test]
        public void TestCronExpressionLastDayOfMonth()
        {
            CronExpression cronExpression = new CronExpression("0 0 12 L * ?");
            int[] arrJuneDaysThatShouldFire = new int[] {30};
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
    }
}
