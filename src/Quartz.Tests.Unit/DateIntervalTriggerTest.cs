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
            DateTimeOffset startDate = new DateTimeOffset(2005, 6, 1, 9, 30, 17, TimeSpan.Zero);

            DateIntervalTrigger yearlyTrigger = new DateIntervalTrigger();
            yearlyTrigger.StartTimeUtc = startDate;
            yearlyTrigger.RepeatIntervalUnit = DateIntervalTrigger.IntervalUnit.Year;
            yearlyTrigger.RepeatInterval = 2; // every two years;

            DateTimeOffset targetDate = new DateTimeOffset(2009, 6, 1, 9, 30, 17, TimeSpan.Zero);

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 4);
            DateTimeOffset secondTime = fireTimes[2]; // get the third fire time

            Assert.AreEqual(targetDate, secondTime, "Year increment result not as expected.");
        }


        [Test]
        public void TestMonthlyIntervalGetFireTimeAfter()
        {
            DateTimeOffset startCalendar = new DateTimeOffset(2005, 6, 1, 9, 30, 17, TimeSpan.Zero);

            DateIntervalTrigger yearlyTrigger = new DateIntervalTrigger();
            yearlyTrigger.StartTimeUtc = startCalendar;
            yearlyTrigger.RepeatIntervalUnit = DateIntervalTrigger.IntervalUnit.Month;
            yearlyTrigger.RepeatInterval = 5; // every five months

            DateTimeOffset targetCalendar = new DateTimeOffset(2005, 6, 1, 9, 30, 17, TimeSpan.Zero);
            targetCalendar = targetCalendar.AddMonths(25); // jump 25 five months (5 intervals)

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 6);
            DateTimeOffset fifthTime = fireTimes[5]; // get the sixth fire time

            Assert.AreEqual(targetCalendar, fifthTime, "Month increment result not as expected.");
        }


        [Test]
        public void TestWeeklyIntervalGetFireTimeAfter()
        {
            DateTimeOffset startCalendar = new DateTimeOffset(2005, 6, 1, 9, 30, 17, TimeSpan.Zero);

            DateIntervalTrigger yearlyTrigger = new DateIntervalTrigger();
            yearlyTrigger.StartTimeUtc = startCalendar;
            yearlyTrigger.RepeatIntervalUnit = DateIntervalTrigger.IntervalUnit.Week;
            yearlyTrigger.RepeatInterval = 6; // every six weeks

            DateTimeOffset targetCalendar = new DateTimeOffset(2005, 6, 1, 9, 30, 17, TimeSpan.Zero);
            targetCalendar = targetCalendar.AddDays(7 * 6 * 4); // jump 24 weeks (4 intervals)

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 7);
            DateTimeOffset fifthTime = fireTimes[4]; // get the fifth fire time

            Console.Out.WriteLine("targetCalendar:" + targetCalendar.ToLocalTime());
            Console.Out.WriteLine("fifthTimee" + fifthTime.ToLocalTime());

            Assert.AreEqual(targetCalendar, fifthTime, "Week increment result not as expected.");
        }

        [Test]
        public void TestDailyIntervalGetFireTimeAfter()
        {
            DateTimeOffset startCalendar = new DateTimeOffset(2005, 6, 1, 9, 30, 17, TimeSpan.Zero);
            
            DateIntervalTrigger dailyTrigger = new DateIntervalTrigger();
            dailyTrigger.StartTimeUtc = startCalendar;
            dailyTrigger.RepeatIntervalUnit = DateIntervalTrigger.IntervalUnit.Day;
            dailyTrigger.RepeatInterval = 90; // every ninety days

            DateTimeOffset targetCalendar = new DateTimeOffset(2005, 6, 1, 9, 30, 17, TimeSpan.Zero);
            targetCalendar = targetCalendar.AddDays(360); // jump 360 days (4 intervals)

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(dailyTrigger, null, 6);
            DateTimeOffset fifthTime = fireTimes[4]; // get the fifth fire time

            Assert.AreEqual(targetCalendar, fifthTime, "Day increment result not as expected.");
        }

        [Test]
        public void TestHourlyIntervalGetFireTimeAfter()
        {
            DateTimeOffset startCalendar = new DateTimeOffset(2005, 6, 1, 9, 30, 17, TimeSpan.Zero);

            DateIntervalTrigger yearlyTrigger = new DateIntervalTrigger();
            yearlyTrigger.StartTimeUtc = startCalendar;
            yearlyTrigger.RepeatIntervalUnit = DateIntervalTrigger.IntervalUnit.Hour;
            yearlyTrigger.RepeatInterval = 100; // every 100 hours

            DateTimeOffset targetCalendar = new DateTimeOffset(2005, 6, 1, 9, 30, 17, TimeSpan.Zero);
            targetCalendar = targetCalendar.AddHours(400); // jump 400 hours (4 intervals)

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 6);
            DateTimeOffset fifthTime = fireTimes[4]; // get the fifth fire time

            Assert.AreEqual(targetCalendar, fifthTime, "Hour increment result not as expected.");
        }

        [Test]
        public void TestMinutelyIntervalGetFireTimeAfter()
        {
            DateTimeOffset startCalendar = new DateTimeOffset(2005, 6, 1, 9, 30, 17, TimeSpan.Zero);

            DateIntervalTrigger yearlyTrigger = new DateIntervalTrigger();
            yearlyTrigger.StartTimeUtc = startCalendar;
            yearlyTrigger.RepeatIntervalUnit = DateIntervalTrigger.IntervalUnit.Minute;
            yearlyTrigger.RepeatInterval = 100; // every 100 minutes

            DateTimeOffset targetCalendar = new DateTimeOffset(2005, 6, 1, 9, 30, 17, TimeSpan.Zero);
            targetCalendar = targetCalendar.AddMinutes(400); // jump 400 minutes (4 intervals)

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 6);
            DateTimeOffset fifthTime = fireTimes[4]; // get the fifth fire time

            Assert.AreEqual(targetCalendar, fifthTime, "Minutes increment result not as expected.");
        }

        [Test]
        public void TestSecondlyIntervalGetFireTimeAfter()
        {
            DateTimeOffset startCalendar = new DateTimeOffset(2005, 6, 1, 9, 30, 17, TimeSpan.Zero);

            DateIntervalTrigger yearlyTrigger = new DateIntervalTrigger();
            yearlyTrigger.StartTimeUtc = startCalendar;
            yearlyTrigger.RepeatIntervalUnit = DateIntervalTrigger.IntervalUnit.Second;
            yearlyTrigger.RepeatInterval = 100; // every 100 seconds

            DateTimeOffset targetCalendar = new DateTimeOffset(2005, 6, 1, 9, 30, 17, TimeSpan.Zero);
            targetCalendar = targetCalendar.AddSeconds(400); // jump 400 seconds (4 intervals)

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(yearlyTrigger, null, 6);
            DateTimeOffset fifthTime = fireTimes[4]; // get the third fire time

            Assert.AreEqual(targetCalendar, fifthTime, "Seconds increment result not as expected.");
        }

        [Test]
        public void TestDaylightSavingsTransitions()
        {
            // Pick a day before a daylight savings transition...

            DateTimeOffset startCalendar = new DateTimeOffset(2010, 3, 12, 9, 30, 17, TimeSpan.Zero);

            DateIntervalTrigger dailyTrigger = new DateIntervalTrigger();
            dailyTrigger.StartTimeUtc = startCalendar;
            dailyTrigger.RepeatIntervalUnit = DateIntervalTrigger.IntervalUnit.Day;
            dailyTrigger.RepeatInterval = 5; // every 5 days

            DateTimeOffset targetCalendar = startCalendar;
            targetCalendar = targetCalendar.AddDays(10); // jump 10 days (2 intervals)

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(dailyTrigger, null, 6);
            DateTimeOffset testTime = fireTimes[2]; // get the third fire time

            Assert.AreEqual(targetCalendar, testTime, "Day increment result not as expected over spring daylight savings transition.");

            // Pick a day before a daylight savings transition...

            startCalendar = new DateTimeOffset(2010, 10, 31, 9, 30, 17, TimeSpan.Zero);

            dailyTrigger = new DateIntervalTrigger();
            dailyTrigger.StartTimeUtc = startCalendar;
            dailyTrigger.RepeatIntervalUnit = DateIntervalTrigger.IntervalUnit.Day;
            dailyTrigger.RepeatInterval = 5; // every 5 days

            targetCalendar = startCalendar;
            targetCalendar = targetCalendar.AddDays(15); // jump 15 days (3 intervals)

            fireTimes = TriggerUtils.ComputeFireTimes(dailyTrigger, null, 6);
            testTime = fireTimes[3]; // get the fourth fire time

            Assert.AreEqual(targetCalendar, testTime, "Day increment result not as expected over fall daylight savings transition.");
        }

        [Test]
        public void TestFinalFireTimes()
        {
            DateTimeOffset startCalendar = new DateTimeOffset(2010, 3, 12, 9, 0, 0, TimeSpan.Zero);

            DateIntervalTrigger dailyTrigger = new DateIntervalTrigger();
            dailyTrigger.StartTimeUtc = startCalendar;
            dailyTrigger.RepeatIntervalUnit = DateIntervalTrigger.IntervalUnit.Day;
            dailyTrigger.RepeatInterval = 5; // every 5 days

            DateTimeOffset endCalendar = startCalendar;
            endCalendar = endCalendar.AddDays(10); // jump 10 days (2 intervals)
            dailyTrigger.EndTimeUtc = endCalendar;

            DateTimeOffset? testTime = dailyTrigger.FinalFireTimeUtc;

            Assert.AreEqual(endCalendar, testTime, "Final fire time not computed correctly for day interval.");


            startCalendar = new DateTimeOffset(2010, 3, 12, 9, 0, 0, TimeSpan.Zero);

            dailyTrigger = new DateIntervalTrigger();
            dailyTrigger.StartTimeUtc = startCalendar;
            dailyTrigger.RepeatIntervalUnit = DateIntervalTrigger.IntervalUnit.Minute;
            dailyTrigger.RepeatInterval = 5; // every 5 minutes

            endCalendar = startCalendar;
            endCalendar = endCalendar.AddDays(15); // jump 15 days 
            endCalendar = endCalendar.AddMinutes(-2); // back up two minutes
            dailyTrigger.EndTimeUtc = endCalendar;

            testTime = dailyTrigger.FinalFireTimeUtc;

            Assert.IsTrue(endCalendar > testTime, "Final fire time not computed correctly for minutely interval.");

            endCalendar = endCalendar.AddMinutes(-3); // back up three more minutes

            Assert.AreEqual(endCalendar, testTime, "Final fire time not computed correctly for minutely interval.");
        }
    }
}