#region License

/* 
 * Copyright 2001-2009 Terracotta, Inc. 
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

#endregion

using System;
using System.Collections.Generic;

using NUnit.Framework;

namespace Quartz.Tests.Unit
{
    /// <summary>
    /// Unit tests for DateIntervalTrigger.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    [TestFixture]
    public class DateIntervalTriggerTest
    {
        [Test]
        public void TestYearlyIntervalGetFireTimeAfter()
        {
            DateTime startDate = new DateTime(2005, 6, 1, 9, 30, 17, DateTimeKind.Utc);

            DateIntervalTrigger yearlyTrigger = new DateIntervalTrigger();
            yearlyTrigger.StartTimeUtc = startDate;
            yearlyTrigger.RepeatIntervalUnit = DateIntervalTrigger.IntervalUnit.Year;
            yearlyTrigger.RepeatInterval = 2; // every two years;

            DateTime targetDate = new DateTime(2009, 6, 1, 9, 30, 17, DateTimeKind.Utc);

            IList<DateTime> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 4);
            DateTime secondTime = fireTimes[2]; // get the third fire time

            Assert.AreEqual(targetDate, secondTime, "Year increment result not as expected.");
        }


        [Test]
        public void TestMonthlyIntervalGetFireTimeAfter()
        {
            DateTime startCalendar = new DateTime(2005, 6, 1, 9, 30, 17, DateTimeKind.Utc);

            DateIntervalTrigger yearlyTrigger = new DateIntervalTrigger();
            yearlyTrigger.StartTimeUtc = startCalendar;
            yearlyTrigger.RepeatIntervalUnit = DateIntervalTrigger.IntervalUnit.Month;
            yearlyTrigger.RepeatInterval = 5; // every five months

            DateTime targetCalendar = new DateTime(2005, 6, 1, 9, 30, 17, DateTimeKind.Utc);
            targetCalendar = targetCalendar.AddMonths(25); // jump 25 five months (5 intervals)

            IList<DateTime> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 6);
            DateTime fifthTime = fireTimes[5]; // get the sixth fire time

            Assert.AreEqual(targetCalendar, fifthTime, "Month increment result not as expected.");
        }


        [Test]
        public void TestWeeklyIntervalGetFireTimeAfter()
        {
            DateTime startCalendar = new DateTime(2005, 6, 1, 9, 30, 17, DateTimeKind.Utc);

            DateIntervalTrigger yearlyTrigger = new DateIntervalTrigger();
            yearlyTrigger.StartTimeUtc = startCalendar;
            yearlyTrigger.RepeatIntervalUnit = DateIntervalTrigger.IntervalUnit.Week;
            yearlyTrigger.RepeatInterval = 6; // every six weeks

            DateTime targetCalendar = new DateTime(2005, 6, 1, 9, 30, 17, DateTimeKind.Utc);
            targetCalendar = targetCalendar.AddDays(7 * 6 * 4); // jump 24 weeks (4 intervals)

            IList<DateTime> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 7);
            DateTime fifthTime = fireTimes[4]; // get the fifth fire time

            Console.Out.WriteLine("targetCalendar:" + targetCalendar.ToLocalTime());
            Console.Out.WriteLine("fifthTimee" + fifthTime.ToLocalTime());

            Assert.AreEqual(targetCalendar, fifthTime, "Week increment result not as expected.");
        }

        [Test]
        public void TestDailyIntervalGetFireTimeAfter()
        {
            DateTime startCalendar = new DateTime(2005, 6, 1, 9, 30, 17, DateTimeKind.Utc);
            
            DateIntervalTrigger yearlyTrigger = new DateIntervalTrigger();
            yearlyTrigger.StartTimeUtc = startCalendar;
            yearlyTrigger.RepeatIntervalUnit = DateIntervalTrigger.IntervalUnit.Day;
            yearlyTrigger.RepeatInterval = 90; // every ninety days

            DateTime targetCalendar = new DateTime(2005, 6, 1, 9, 30, 17, DateTimeKind.Utc);
            targetCalendar = targetCalendar.AddDays(360); // jump 360 days (4 intervals)

            IList<DateTime> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 6);
            DateTime fifthTime = fireTimes[4]; // get the fifth fire time

            Assert.AreEqual(targetCalendar, fifthTime, "Day increment result not as expected.");
        }

        [Test]
        public void TestHourlyIntervalGetFireTimeAfter()
        {
            DateTime startCalendar = new DateTime(2005, 6, 1, 9, 30, 17, DateTimeKind.Utc);

            DateIntervalTrigger yearlyTrigger = new DateIntervalTrigger();
            yearlyTrigger.StartTimeUtc = startCalendar;
            yearlyTrigger.RepeatIntervalUnit = DateIntervalTrigger.IntervalUnit.Hour;
            yearlyTrigger.RepeatInterval = 100; // every 100 hours

            DateTime targetCalendar = new DateTime(2005, 6, 1, 9, 30, 17, DateTimeKind.Utc);
            targetCalendar = targetCalendar.AddHours(400); // jump 400 hours (4 intervals)

            IList<DateTime> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 6);
            DateTime fifthTime = fireTimes[4]; // get the fifth fire time

            Assert.AreEqual(targetCalendar, fifthTime, "Hour increment result not as expected.");
        }

        [Test]
        public void TestMinutelyIntervalGetFireTimeAfter()
        {
            DateTime startCalendar = new DateTime(2005, 6, 1, 9, 30, 17, DateTimeKind.Utc);

            DateIntervalTrigger yearlyTrigger = new DateIntervalTrigger();
            yearlyTrigger.StartTimeUtc = startCalendar;
            yearlyTrigger.RepeatIntervalUnit = DateIntervalTrigger.IntervalUnit.Minute;
            yearlyTrigger.RepeatInterval = 100; // every 100 minutes

            DateTime targetCalendar = new DateTime(2005, 6, 1, 9, 30, 17, DateTimeKind.Utc);
            targetCalendar = targetCalendar.AddMinutes(400); // jump 400 minutes (4 intervals)

            IList<DateTime> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 6);
            DateTime fifthTime = fireTimes[4]; // get the fifth fire time

            Assert.AreEqual(targetCalendar, fifthTime, "Minutes increment result not as expected.");
        }

        [Test]
        public void TestSecondlyIntervalGetFireTimeAfter()
        {
            DateTime startCalendar = new DateTime(2005, 6, 1, 9, 30, 17, DateTimeKind.Utc);

            DateIntervalTrigger yearlyTrigger = new DateIntervalTrigger();
            yearlyTrigger.StartTimeUtc = startCalendar;
            yearlyTrigger.RepeatIntervalUnit = DateIntervalTrigger.IntervalUnit.Second;
            yearlyTrigger.RepeatInterval = 100; // every 100 seconds

            DateTime targetCalendar = new DateTime(2005, 6, 1, 9, 30, 17, DateTimeKind.Utc);
            targetCalendar = targetCalendar.AddSeconds(400); // jump 400 seconds (4 intervals)

            IList<DateTime> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 6);
            DateTime fifthTime = fireTimes[4]; // get the third fire time

            Assert.AreEqual(targetCalendar, fifthTime, "Seconds increment result not as expected.");
        }
    }
}