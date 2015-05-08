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
using System.Linq;

using NUnit.Framework;

using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Tests.Unit.Impl.Triggers
{
    /// <summary>
    /// Unit test for <see cref="DailyTimeIntervalTriggerImpl"/>.
    /// </summary>
    /// <author>Zemian Deng saltnlight5@gmail.com</author>
    /// <author>Nuno Maia (.NET)</author>
    [TestFixture]
    public class DailyTimeIntervalTriggerImplTest
    {
        [Test]
        public void TestNormalExample()
        {
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
            TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
            TimeOfDay endTimeOfDay = new TimeOfDay(11, 0, 0);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                StartTimeOfDay = startTimeOfDay,
                EndTimeOfDay = endTimeOfDay,
                RepeatIntervalUnit = IntervalUnit.Minute,
                RepeatInterval = 72 // this interval will give three firings per day (8:00, 9:12, and 10:24)
            };

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.AreEqual(48, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DateBuilder.DateOf(10, 24, 0, 16, 1, 2011), fireTimes[47]);
        }

        [Test]
        public void TestQuartzCalendarExclusion()
        {
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                StartTimeOfDay = new TimeOfDay(8, 0),
                RepeatIntervalUnit = IntervalUnit.Minute,
                RepeatInterval = 60
            };

            CronCalendar cronCal = new CronCalendar("* * 9-12 * * ?"); // exclude 9-12		
            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, cronCal, 48);
            Assert.AreEqual(48, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DateBuilder.DateOf(13, 0, 0, 1, 1, 2011), fireTimes[1]);
            Assert.AreEqual(DateBuilder.DateOf(23, 0, 0, 4, 1, 2011), fireTimes[47]);
        }

        [Test]
        public void TestValidateTimeOfDayOrder()
        {
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeOfDay = new TimeOfDay(12, 0, 0),
                EndTimeOfDay = new TimeOfDay(8, 0, 0)
            };
            try
            {
                trigger.Validate();
                Assert.Fail("Trigger should be invalidate when time of day is not in order.");
            }
            catch (SchedulerException)
            {
                // expected.
            }
        }

        [Test]
        public void TestValidateInterval()
        {
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                Name = "test",
                Group = "test",
                JobKey = JobKey.Create("test"),
                RepeatIntervalUnit = IntervalUnit.Hour,
                RepeatInterval = 25
            };

            Assert.Throws<SchedulerException>(trigger.Validate, "repeatInterval can not exceed 24 hours. Given 25 hours.");

            trigger.RepeatIntervalUnit = IntervalUnit.Minute;
            trigger.RepeatInterval = 60*25;
            Assert.Throws<SchedulerException>(trigger.Validate, "repeatInterval can not exceed 24 hours (86400 seconds). Given 90000");

            trigger.RepeatIntervalUnit = IntervalUnit.Second;
            trigger.RepeatInterval = 60*60*25;

            Assert.Throws<SchedulerException>(trigger.Validate, "repeatInterval can not exceed 24 hours (86400 seconds). Given 90000");

            Assert.Throws<ArgumentException>(delegate { trigger.RepeatIntervalUnit = IntervalUnit.Day; }, "Invalid repeat IntervalUnit (must be Second, Minute or Hour)");

            trigger.RepeatIntervalUnit = IntervalUnit.Second;
            trigger.RepeatInterval = 0;
            Assert.Throws<SchedulerException>(trigger.Validate, "Repeat Interval cannot be zero.");
        }

        [Test]
        public void TestStartTimeWithoutStartTimeOfDay()
        {
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                RepeatIntervalUnit = IntervalUnit.Minute,
                RepeatInterval = 60
            };

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.AreEqual(48, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(0, 0, 0, 1, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DateBuilder.DateOf(23, 0, 0, 2, 1, 2011), fireTimes[47]);
        }

        [Test]
        public void TestEndTimeWithoutEndTimeOfDay()
        {
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
            DateTimeOffset endTime = DateBuilder.DateOf(22, 0, 0, 2, 1, 2011);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                EndTimeUtc = endTime.ToUniversalTime(),
                RepeatIntervalUnit = IntervalUnit.Minute,
                RepeatInterval = 60
            };

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.AreEqual(47, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(0, 0, 0, 1, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DateBuilder.DateOf(22, 0, 0, 2, 1, 2011), fireTimes[46]);
        }

        [Test]
        public void TestStartTimeBeforeStartTimeOfDay()
        {
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
            TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                StartTimeOfDay = startTimeOfDay,
                RepeatIntervalUnit = IntervalUnit.Minute,
                RepeatInterval = 60
            };

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.AreEqual(48, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DateBuilder.DateOf(23, 0, 0, 3, 1, 2011), fireTimes[47]);
        }

        [Test]
        public void TestStartTimeBeforeStartTimeOfDayOnInvalidDay()
        {
            DateTimeOffset startTime = dateOf(0, 0, 0, 1, 1, 2011); // Jan 1, 2011 was a saturday...
            TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
            var trigger = new DailyTimeIntervalTriggerImpl();
            var daysOfWeek = new Collection.HashSet<DayOfWeek>
            {
                DayOfWeek.Monday,
                DayOfWeek.Tuesday,
                DayOfWeek.Wednesday,
                DayOfWeek.Thursday,
                DayOfWeek.Friday
            };
            trigger.DaysOfWeek = daysOfWeek;
            trigger.StartTimeUtc = startTime.ToUniversalTime();
            trigger.StartTimeOfDay = startTimeOfDay;
            trigger.RepeatIntervalUnit = IntervalUnit.Minute;
            trigger.RepeatInterval = 60;

            Assert.That(trigger.GetFireTimeAfter(dateOf(6, 0, 0, 22, 5, 2010)), Is.EqualTo(dateOf(8, 0, 0, 3, 1, 2011)));

            var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.That(fireTimes.Count, Is.EqualTo(48));
            Assert.That(fireTimes[0], Is.EqualTo(dateOf(8, 0, 0, 3, 1, 2011)));
            Assert.That(fireTimes[47], Is.EqualTo(dateOf(23, 0, 0, 5, 1, 2011)));
        }

        [Test]
        public void TestStartTimeAfterStartTimeOfDay()
        {
            DateTimeOffset startTime = DateBuilder.DateOf(9, 23, 0, 1, 1, 2011);
            TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                StartTimeOfDay = startTimeOfDay,
                RepeatIntervalUnit = IntervalUnit.Minute,
                RepeatInterval = 60
            };

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.AreEqual(48, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(10, 0, 0, 1, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DateBuilder.DateOf(9, 0, 0, 4, 1, 2011), fireTimes[47]);
        }

        [Test]
        public void TestEndTimeBeforeEndTimeOfDay()
        {
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
            DateTimeOffset endTime = DateBuilder.DateOf(16, 0, 0, 2, 1, 2011);
            TimeOfDay endTimeOfDay = new TimeOfDay(17, 0, 0);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                EndTimeUtc = endTime.ToUniversalTime(),
                EndTimeOfDay = endTimeOfDay,
                RepeatIntervalUnit = IntervalUnit.Minute,
                RepeatInterval = 60
            };

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.AreEqual(35, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(0, 0, 0, 1, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DateBuilder.DateOf(17, 0, 0, 1, 1, 2011), fireTimes[17]);
            Assert.AreEqual(DateBuilder.DateOf(16, 0, 0, 2, 1, 2011), fireTimes[34]);
        }

        [Test]
        public void TestEndTimeAfterEndTimeOfDay()
        {
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
            DateTimeOffset endTime = DateBuilder.DateOf(18, 0, 0, 2, 1, 2011);
            TimeOfDay endTimeOfDay = new TimeOfDay(17, 0, 0);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                EndTimeUtc = endTime.ToUniversalTime(),
                EndTimeOfDay = endTimeOfDay,
                RepeatIntervalUnit = IntervalUnit.Minute,
                RepeatInterval = 60
            };

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.AreEqual(36, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(0, 0, 0, 1, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DateBuilder.DateOf(17, 0, 0, 1, 1, 2011), fireTimes[17]);
            Assert.AreEqual(DateBuilder.DateOf(17, 0, 0, 2, 1, 2011), fireTimes[35]);
        }

        [Test]
        public void TestTimeOfDayWithStartTime()
        {
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
            TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
            TimeOfDay endTimeOfDay = new TimeOfDay(17, 0, 0);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                StartTimeOfDay = startTimeOfDay,
                EndTimeOfDay = endTimeOfDay,
                RepeatIntervalUnit = IntervalUnit.Minute,
                RepeatInterval = 60
            };

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.AreEqual(48, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DateBuilder.DateOf(17, 0, 0, 1, 1, 2011), fireTimes[9]); // The 10th hours is the end of day.
            Assert.AreEqual(DateBuilder.DateOf(15, 0, 0, 5, 1, 2011), fireTimes[47]);
        }

        [Test]
        public void TestTimeOfDayWithEndTime()
        {
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
            DateTimeOffset endTime = DateBuilder.DateOf(0, 0, 0, 4, 1, 2011);
            TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
            TimeOfDay endTimeOfDay = new TimeOfDay(17, 0, 0);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                EndTimeUtc = endTime.ToUniversalTime(),
                StartTimeOfDay = startTimeOfDay,
                EndTimeOfDay = endTimeOfDay,
                RepeatIntervalUnit = IntervalUnit.Minute,
                RepeatInterval = 60
            };

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.AreEqual(30, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DateBuilder.DateOf(17, 0, 0, 1, 1, 2011), fireTimes[9]); // The 10th hours is the end of day.
            Assert.AreEqual(DateBuilder.DateOf(17, 0, 0, 3, 1, 2011), fireTimes[29]);
        }

        [Test]
        public void TestTimeOfDayWithEndTime2()
        {
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
            TimeOfDay startTimeOfDay = new TimeOfDay(8, 23, 0);
            TimeOfDay endTimeOfDay = new TimeOfDay(23, 59, 59); // edge case when endTime is last second of day, which is default too.
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                StartTimeOfDay = startTimeOfDay,
                EndTimeOfDay = endTimeOfDay,
                RepeatIntervalUnit = IntervalUnit.Minute,
                RepeatInterval = 60
            };

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.AreEqual(48, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(8, 23, 0, 1, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DateBuilder.DateOf(23, 23, 0, 3, 1, 2011), fireTimes[47]);
        }

        [Test]
        public void TestAllDaysOfTheWeek()
        {
            Collection.ISet<DayOfWeek> daysOfWeek = DailyTimeIntervalScheduleBuilder.AllDaysOfTheWeek;
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011); // SAT
            TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
            TimeOfDay endTimeOfDay = new TimeOfDay(17, 0, 0);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                StartTimeOfDay = startTimeOfDay,
                EndTimeOfDay = endTimeOfDay,
                DaysOfWeek = daysOfWeek,
                RepeatIntervalUnit = IntervalUnit.Minute,
                RepeatInterval = 60
            };

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.AreEqual(48, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DateBuilder.DateOf(17, 0, 0, 1, 1, 2011), fireTimes[9]); // The 10th hours is the end of day.
            Assert.AreEqual(DateBuilder.DateOf(15, 0, 0, 5, 1, 2011), fireTimes[47]);
        }

        [Test]
        public void TestMonThroughFri()
        {
            Collection.ISet<DayOfWeek> daysOfWeek = DailyTimeIntervalScheduleBuilder.MondayThroughFriday;
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011); // SAT(7)
            TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
            TimeOfDay endTimeOfDay = new TimeOfDay(17, 0, 0);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                StartTimeOfDay = startTimeOfDay,
                EndTimeOfDay = endTimeOfDay,
                DaysOfWeek = daysOfWeek,
                RepeatIntervalUnit = IntervalUnit.Minute,
                RepeatInterval = 60
            };

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.AreEqual(48, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 3, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DayOfWeek.Monday, fireTimes[0].DayOfWeek);
            Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 4, 1, 2011), fireTimes[10]);
            Assert.AreEqual(DayOfWeek.Tuesday, fireTimes[10].DayOfWeek);
            Assert.AreEqual(DateBuilder.DateOf(15, 0, 0, 7, 1, 2011), fireTimes[47]);
            Assert.AreEqual(DayOfWeek.Friday, fireTimes[47].DayOfWeek);
        }

        [Test]
        public void TestSatAndSun()
        {
            Collection.ISet<DayOfWeek> daysOfWeek = DailyTimeIntervalScheduleBuilder.SaturdayAndSunday;
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011); // SAT(7)
            TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
            TimeOfDay endTimeOfDay = new TimeOfDay(17, 0, 0);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                StartTimeOfDay = startTimeOfDay,
                EndTimeOfDay = endTimeOfDay,
                DaysOfWeek = daysOfWeek,
                RepeatIntervalUnit = IntervalUnit.Minute,
                RepeatInterval = 60
            };

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.AreEqual(48, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DayOfWeek.Saturday, fireTimes[0].DayOfWeek);
            Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 2, 1, 2011), fireTimes[10]);
            Assert.AreEqual(DayOfWeek.Sunday, fireTimes[10].DayOfWeek);
            Assert.AreEqual(DateBuilder.DateOf(15, 0, 0, 15, 1, 2011), fireTimes[47]);
            Assert.AreEqual(DayOfWeek.Saturday, fireTimes[47].DayOfWeek);
        }

        [Test]
        public void TestMonOnly()
        {
            Collection.ISet<DayOfWeek> daysOfWeek = new Collection.HashSet<DayOfWeek>();
            daysOfWeek.Add(DayOfWeek.Monday);
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011); // SAT(7)
            TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
            TimeOfDay endTimeOfDay = new TimeOfDay(17, 0, 0);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                StartTimeOfDay = startTimeOfDay,
                EndTimeOfDay = endTimeOfDay,
                DaysOfWeek = daysOfWeek,
                RepeatIntervalUnit = IntervalUnit.Minute,
                RepeatInterval = 60
            };

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.AreEqual(48, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 3, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DayOfWeek.Monday, fireTimes[0].DayOfWeek);
            Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 10, 1, 2011), fireTimes[10]);
            Assert.AreEqual(DayOfWeek.Monday, fireTimes[10].DayOfWeek);
            Assert.AreEqual(DateBuilder.DateOf(15, 0, 0, 31, 1, 2011), fireTimes[47]);
            Assert.AreEqual(DayOfWeek.Monday, fireTimes[47].DayOfWeek);
        }

        [Test]
        public void TestTimeOfDayWithEndTimeOddInterval()
        {
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
            DateTimeOffset endTime = DateBuilder.DateOf(0, 0, 0, 4, 1, 2011);
            TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
            TimeOfDay endTimeOfDay = new TimeOfDay(10, 0, 0);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                EndTimeUtc = endTime.ToUniversalTime(),
                StartTimeOfDay = startTimeOfDay,
                EndTimeOfDay = endTimeOfDay,
                RepeatIntervalUnit = IntervalUnit.Minute,
                RepeatInterval = 23
            };

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.AreEqual(18, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DateBuilder.DateOf(9, 55, 0, 1, 1, 2011), fireTimes[5]);
            Assert.AreEqual(DateBuilder.DateOf(9, 55, 0, 3, 1, 2011), fireTimes[17]);
        }

        [Test]
        public void TestHourInterval()
        {
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
            DateTimeOffset endTime = DateBuilder.DateOf(13, 0, 0, 15, 1, 2011);
            TimeOfDay startTimeOfDay = new TimeOfDay(8, 1, 15);
            TimeOfDay endTimeOfDay = new TimeOfDay(16, 1, 15);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                StartTimeOfDay = startTimeOfDay,
                EndTimeUtc = endTime.ToUniversalTime(),
                EndTimeOfDay = endTimeOfDay,
                RepeatIntervalUnit = IntervalUnit.Hour,
                RepeatInterval = 2
            };

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.AreEqual(48, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(8, 1, 15, 1, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DateBuilder.DateOf(12, 1, 15, 10, 1, 2011), fireTimes[47]);
        }

        [Test]
        public void TestSecondInterval()
        {
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
            TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 2);
            TimeOfDay endTimeOfDay = new TimeOfDay(13, 30, 0);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                StartTimeOfDay = startTimeOfDay,
                EndTimeOfDay = endTimeOfDay,
                RepeatIntervalUnit = IntervalUnit.Second,
                RepeatInterval = 72
            };

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.AreEqual(48, fireTimes.Count);
            Assert.AreEqual((DateBuilder.DateOf(8, 0, 2, 1, 1, 2011)), fireTimes[0]);
            Assert.AreEqual((DateBuilder.DateOf(8, 56, 26, 1, 1, 2011)), fireTimes[47]);
        }

        [Test]
        public void TestRepeatCountInf()
        {
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
            TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
            TimeOfDay endTimeOfDay = new TimeOfDay(11, 0, 0);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                StartTimeOfDay = startTimeOfDay,
                EndTimeOfDay = endTimeOfDay,
                RepeatIntervalUnit = IntervalUnit.Minute,
                RepeatInterval = (72),
                RepeatCount = DailyTimeIntervalTriggerImpl.RepeatIndefinitely
            };

            // Setting this (which is default) should make the trigger just as normal one.

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.AreEqual(48, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DateBuilder.DateOf(10, 24, 0, 16, 1, 2011), fireTimes[47]);
        }

        [Test]
        public void TestRepeatCount()
        {
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
            TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
            TimeOfDay endTimeOfDay = new TimeOfDay(11, 0, 0);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                StartTimeOfDay = startTimeOfDay,
                EndTimeOfDay = endTimeOfDay,
                RepeatIntervalUnit = IntervalUnit.Minute,
                RepeatInterval = 72,
                RepeatCount = 7
            };

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.AreEqual(8, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DateBuilder.DateOf(9, 12, 0, 3, 1, 2011), fireTimes[7]);
        }

        [Test]
        public void TestRepeatCount0()
        {
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
            TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
            TimeOfDay endTimeOfDay = new TimeOfDay(11, 0, 0);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                StartTimeOfDay = startTimeOfDay,
                EndTimeOfDay = endTimeOfDay,
                RepeatIntervalUnit = IntervalUnit.Minute,
                RepeatInterval = 72,
                RepeatCount = 0
            };

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
            Assert.AreEqual(1, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011), fireTimes[0]);
        }

        [Test]
        public void TestFollowsTimeZone1()
        {
            TimeZoneInfo est = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");

            DateTimeOffset startTime = new DateTimeOffset(2012, 3, 9, 23, 0, 0, TimeSpan.FromHours(-5));

            TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
            TimeOfDay endTimeOfDay = new TimeOfDay(11, 0, 0);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                StartTimeOfDay = startTimeOfDay,
                EndTimeOfDay = endTimeOfDay,
                RepeatIntervalUnit = IntervalUnit.Hour,
                RepeatInterval = 1,
                TimeZone = est
            };

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 8);

            DateTimeOffset expected0 = new DateTimeOffset(2012, 3, 10, 8, 0, 0, 0, TimeSpan.FromHours(-5));
            DateTimeOffset expected1 = new DateTimeOffset(2012, 3, 10, 9, 0, 0, 0, TimeSpan.FromHours(-5));
            DateTimeOffset expected2 = new DateTimeOffset(2012, 3, 10, 10, 0, 0, 0, TimeSpan.FromHours(-5));
            DateTimeOffset expected3 = new DateTimeOffset(2012, 3, 10, 11, 0, 0, 0, TimeSpan.FromHours(-5));

            DateTimeOffset expected4 = new DateTimeOffset(2012, 3, 11, 8, 0, 0, 0, TimeSpan.FromHours(-4));
            DateTimeOffset expected5 = new DateTimeOffset(2012, 3, 11, 9, 0, 0, 0, TimeSpan.FromHours(-4));
            DateTimeOffset expected6 = new DateTimeOffset(2012, 3, 11, 10, 0, 0, 0, TimeSpan.FromHours(-4));
            DateTimeOffset expected7 = new DateTimeOffset(2012, 3, 11, 11, 0, 0, 0, TimeSpan.FromHours(-4));

            Assert.AreEqual(expected0, fireTimes[0]);
            Assert.AreEqual(expected1, fireTimes[1]);
            Assert.AreEqual(expected2, fireTimes[2]);
            Assert.AreEqual(expected3, fireTimes[3]);
            Assert.AreEqual(expected4, fireTimes[4]);
            Assert.AreEqual(expected5, fireTimes[5]);
            Assert.AreEqual(expected6, fireTimes[6]);
            Assert.AreEqual(expected7, fireTimes[7]);
        }

        [Test]
        public void TestFollowsTimeZone2()
        {
            TimeZoneInfo est = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");

            DateTimeOffset startTime = new DateTimeOffset(2012, 11, 2, 12, 0, 0, TimeSpan.FromHours(-4));

            TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
            TimeOfDay endTimeOfDay = new TimeOfDay(11, 0, 0);
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                StartTimeUtc = startTime.ToUniversalTime(),
                StartTimeOfDay = startTimeOfDay,
                EndTimeOfDay = endTimeOfDay,
                RepeatIntervalUnit = IntervalUnit.Hour,
                RepeatInterval = 1,
                TimeZone = est
            };

            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 8);

            DateTimeOffset expected0 = new DateTimeOffset(2012, 11, 3, 8, 0, 0, 0, TimeSpan.FromHours(-4));
            DateTimeOffset expected1 = new DateTimeOffset(2012, 11, 3, 9, 0, 0, 0, TimeSpan.FromHours(-4));
            DateTimeOffset expected2 = new DateTimeOffset(2012, 11, 3, 10, 0, 0, 0, TimeSpan.FromHours(-4));
            DateTimeOffset expected3 = new DateTimeOffset(2012, 11, 3, 11, 0, 0, 0, TimeSpan.FromHours(-4));

            DateTimeOffset expected4 = new DateTimeOffset(2012, 11, 4, 8, 0, 0, 0, TimeSpan.FromHours(-5));
            DateTimeOffset expected5 = new DateTimeOffset(2012, 11, 4, 9, 0, 0, 0, TimeSpan.FromHours(-5));
            DateTimeOffset expected6 = new DateTimeOffset(2012, 11, 4, 10, 0, 0, 0, TimeSpan.FromHours(-5));
            DateTimeOffset expected7 = new DateTimeOffset(2012, 11, 4, 11, 0, 0, 0, TimeSpan.FromHours(-5));

            Assert.AreEqual(expected0, fireTimes[0]);
            Assert.AreEqual(expected1, fireTimes[1]);
            Assert.AreEqual(expected2, fireTimes[2]);
            Assert.AreEqual(expected3, fireTimes[3]);
            Assert.AreEqual(expected4, fireTimes[4]);
            Assert.AreEqual(expected5, fireTimes[5]);
            Assert.AreEqual(expected6, fireTimes[6]);
            Assert.AreEqual(expected7, fireTimes[7]);
        }

        [Test]
        public void DayOfWeekPropertyShouldNotAffectOtherTriggers()
        {
            //make 2 trigger exactly the same
            DailyTimeIntervalTriggerImpl trigger1 = new DailyTimeIntervalTriggerImpl()
            {
                RepeatInterval = 1,
                RepeatIntervalUnit = IntervalUnit.Hour
            };

            DailyTimeIntervalTriggerImpl trigger2 = new DailyTimeIntervalTriggerImpl()
            {
                RepeatInterval = 1,
                RepeatIntervalUnit = IntervalUnit.Hour
            };

            //make an adjustment to only one trigger. 
            //I only want mondays now
            trigger1.DaysOfWeek.Clear();
            trigger1.DaysOfWeek.Add(DayOfWeek.Monday);

            //check trigger 2 DOW
            //this fails because the reference collection only contains MONDAY b/c it was cleared.
            Assert.IsTrue(trigger2.DaysOfWeek.Contains(DayOfWeek.Monday));
            Assert.IsTrue(trigger2.DaysOfWeek.Contains(DayOfWeek.Tuesday));
            Assert.IsTrue(trigger2.DaysOfWeek.Contains(DayOfWeek.Wednesday));
            Assert.IsTrue(trigger2.DaysOfWeek.Contains(DayOfWeek.Thursday));
            Assert.IsTrue(trigger2.DaysOfWeek.Contains(DayOfWeek.Friday));
            Assert.IsTrue(trigger2.DaysOfWeek.Contains(DayOfWeek.Saturday));
            Assert.IsTrue(trigger2.DaysOfWeek.Contains(DayOfWeek.Sunday));
        }

        [Test]
        public void ValidateShouldSucceedWithValidIntervalUnitHourConfiguration()
        {
            var trigger = new DailyTimeIntervalTriggerImpl
            {
                Name = "name",
                Group = "group",
                JobName = "jobname",
                JobGroup = "jobgroup",
                RepeatIntervalUnit = IntervalUnit.Hour
            };
            trigger.Validate();
        }

        [Test]
        public void TestGetFireTime()
        {
            DateTime startTime = new DateTime(2011, 1, 1);
            TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
            TimeOfDay endTimeOfDay = new TimeOfDay(13, 0, 0);
            var trigger = new DailyTimeIntervalTriggerImpl();
            trigger.StartTimeUtc = startTime;
            trigger.StartTimeOfDay = startTimeOfDay;
            trigger.EndTimeOfDay = endTimeOfDay;
            trigger.RepeatIntervalUnit = IntervalUnit.Hour;
            trigger.RepeatInterval = 1;

            Assert.AreEqual(dateOf(8, 0, 0, 1, 1, 2011), trigger.GetFireTimeAfter(dateOf(0, 0, 0, 1, 1, 2011)));
            Assert.AreEqual(dateOf(8, 0, 0, 1, 1, 2011), trigger.GetFireTimeAfter(dateOf(7, 0, 0, 1, 1, 2011)));
            Assert.AreEqual(dateOf(8, 0, 0, 1, 1, 2011), trigger.GetFireTimeAfter(dateOf(7, 59, 59, 1, 1, 2011)));
            Assert.AreEqual(dateOf(9, 0, 0, 1, 1, 2011), trigger.GetFireTimeAfter(dateOf(8, 0, 0, 1, 1, 2011)));
            Assert.AreEqual(dateOf(10, 0, 0, 1, 1, 2011), trigger.GetFireTimeAfter(dateOf(9, 0, 0, 1, 1, 2011)));
            Assert.AreEqual(dateOf(13, 0, 0, 1, 1, 2011), trigger.GetFireTimeAfter(dateOf(12, 59, 59, 1, 1, 2011)));
            Assert.AreEqual(dateOf(8, 0, 0, 2, 1, 2011), trigger.GetFireTimeAfter(dateOf(13, 0, 0, 1, 1, 2011)));
        }

        public DateTimeOffset dateOf(int hour, int minute, int second, int dayOfMonth, int month, int year)
        {
            return new DateTime(year, month, dayOfMonth, hour, minute, second);
        }

        [Test]
        public void TestGetFireTimeWithDateBeforeStartTime()
        {
            DateTime startTime = new DateTime(2012, 1, 1);
            TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
            TimeOfDay endTimeOfDay = new TimeOfDay(13, 0, 0);
            DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl();
            trigger.StartTimeUtc = startTime;
            trigger.StartTimeOfDay = startTimeOfDay;
            trigger.EndTimeOfDay = endTimeOfDay;
            trigger.RepeatIntervalUnit = IntervalUnit.Hour;
            trigger.RepeatInterval = 1;

            // NOTE that if you pass a date past the startTime, you will get the startTime back!
            Assert.AreEqual(dateOf(8, 0, 0, 1, 1, 2012), trigger.GetFireTimeAfter(dateOf(0, 0, 0, 1, 1, 2011)));
            Assert.AreEqual(dateOf(8, 0, 0, 1, 1, 2012), trigger.GetFireTimeAfter(dateOf(7, 0, 0, 1, 1, 2011)));
            Assert.AreEqual(dateOf(8, 0, 0, 1, 1, 2012), trigger.GetFireTimeAfter(dateOf(7, 59, 59, 1, 1, 2011)));
            Assert.AreEqual(dateOf(8, 0, 0, 1, 1, 2012), trigger.GetFireTimeAfter(dateOf(8, 0, 0, 1, 1, 2011)));
            Assert.AreEqual(dateOf(8, 0, 0, 1, 1, 2012), trigger.GetFireTimeAfter(dateOf(9, 0, 0, 1, 1, 2011)));
            Assert.AreEqual(dateOf(8, 0, 0, 1, 1, 2012), trigger.GetFireTimeAfter(dateOf(12, 59, 59, 1, 1, 2011)));
            Assert.AreEqual(dateOf(8, 0, 0, 1, 1, 2012), trigger.GetFireTimeAfter(dateOf(13, 0, 0, 1, 1, 2011)));

            // Now try some test times at or after startTime
            Assert.AreEqual(dateOf(8, 0, 0, 1, 1, 2012), trigger.GetFireTimeAfter(dateOf(0, 0, 0, 1, 1, 2012)));
            Assert.AreEqual(dateOf(8, 0, 0, 2, 1, 2012), trigger.GetFireTimeAfter(dateOf(13, 0, 0, 1, 1, 2012)));
        }

        [Test]
        public void TestGetFireTimeWhenStartTimeAndTimeOfDayIsSame()
        {
            // A test case for QTZ-369
            DateTime startTime = new DateTime(2012, 1, 1);
            TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
            TimeOfDay endTimeOfDay = new TimeOfDay(13, 0, 0);
            var trigger = new DailyTimeIntervalTriggerImpl();
            trigger.StartTimeUtc = startTime;
            trigger.StartTimeOfDay = startTimeOfDay;
            trigger.EndTimeOfDay = endTimeOfDay;
            trigger.RepeatIntervalUnit = IntervalUnit.Hour;
            trigger.RepeatInterval = 1;

            Assert.AreEqual(dateOf(8, 0, 0, 1, 1, 2012), trigger.GetFireTimeAfter(new DateTime(2012, 1, 1)));
        }

        [Test]
        public void TestExtraConstructors()
        {
            // A test case for QTZ-389 - some extra constructors didn't set all parameters
            DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl(
                "triggerName", "triggerGroup", "jobName", "jobGroup",
                dateOf(8, 0, 0, 1, 1, 2012), null,
                new TimeOfDay(8, 0, 0), new TimeOfDay(17, 0, 0),
                IntervalUnit.Hour, 1);

            Assert.AreEqual("triggerName", trigger.Name);
            Assert.AreEqual("triggerGroup", trigger.Group);
            Assert.AreEqual("jobName", trigger.JobName);
            Assert.AreEqual("jobGroup", trigger.JobGroup);
            Assert.AreEqual(dateOf(8, 0, 0, 1, 1, 2012), trigger.StartTimeUtc);
            Assert.AreEqual(null, trigger.EndTimeUtc);
            Assert.AreEqual(new TimeOfDay(8, 0, 0), trigger.StartTimeOfDay);
            Assert.AreEqual(new TimeOfDay(17, 0, 0), trigger.EndTimeOfDay);
            Assert.AreEqual(IntervalUnit.Hour, trigger.RepeatIntervalUnit);
            Assert.AreEqual(1, trigger.RepeatInterval);

            trigger = new DailyTimeIntervalTriggerImpl(
                "triggerName", "triggerGroup",
                dateOf(8, 0, 0, 1, 1, 2012), null,
                new TimeOfDay(8, 0, 0), new TimeOfDay(17, 0, 0),
                IntervalUnit.Hour, 1);

            Assert.AreEqual("triggerName", trigger.Name);
            Assert.AreEqual("triggerGroup", trigger.Group);
            Assert.AreEqual(null, trigger.JobName);
            Assert.AreEqual("DEFAULT", trigger.JobGroup);
            Assert.AreEqual(dateOf(8, 0, 0, 1, 1, 2012), trigger.StartTimeUtc);
            Assert.AreEqual(null, trigger.EndTimeUtc);
            Assert.AreEqual(new TimeOfDay(8, 0, 0), trigger.StartTimeOfDay);
            Assert.AreEqual(new TimeOfDay(17, 0, 0), trigger.EndTimeOfDay);
            Assert.AreEqual(IntervalUnit.Hour, trigger.RepeatIntervalUnit);
            Assert.AreEqual(1, trigger.RepeatInterval);
        }

        [Test]
        public void TestDayLightSaving()
        {
            var timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");

            var trigger = DailyTimeIntervalScheduleBuilder.Create()
                .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(22, 15))
                .OnEveryDay()
                .WithIntervalInHours(24)
                .WithRepeatCount(9999)
                .InTimeZone(timeZoneInfo)
                .Build();

            var first = trigger.GetFireTimeAfter(new DateTimeOffset(2014, 10, 25, 0, 0, 0, TimeSpan.Zero));
            Assert.That(first, Is.EqualTo(new DateTimeOffset(2014, 10, 25, 22, 15, 0, TimeSpan.FromHours(1))));

            var second = trigger.GetFireTimeAfter(first);
            Assert.That(second, Is.EqualTo(new DateTimeOffset(2014, 10, 26, 22, 15, 0, TimeSpan.FromHours(0))));

            var third = trigger.GetFireTimeAfter(second);
            Assert.That(third, Is.EqualTo(new DateTimeOffset(2014, 10, 27, 22, 15, 0, TimeSpan.FromHours(0))));
        }

        [Test]
        public void TestPassingMidnight()
        {
            IOperableTrigger trigger = (IOperableTrigger) DailyTimeIntervalScheduleBuilder.Create()
                .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(16, 00))
                .EndingDailyAt(TimeOfDay.HourAndMinuteOfDay(00, 00))
                .OnEveryDay()
                .WithIntervalInMinutes(30)
                .Build();

            trigger.StartTimeUtc = new DateTimeOffset(2015, 1, 11, 23, 57, 0, 0, TimeSpan.Zero);

            var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 100);
            foreach (var fireTime in fireTimes)
            {
                Console.WriteLine(fireTime.LocalDateTime);
            }
        }

        [Test]
        public void ShouldGetScheduleBuilderWithSameSettingsAsTrigger()
        {
            var startTime = DateTimeOffset.UtcNow;
            var endTime = DateTimeOffset.UtcNow.AddDays(1);
            var startTimeOfDay = new TimeOfDay(1, 2, 3);
            var endTimeOfDay = new TimeOfDay(3, 2, 1);
            var trigger = new DailyTimeIntervalTriggerImpl("name", "group", startTime, endTime, startTimeOfDay, endTimeOfDay, IntervalUnit.Hour, 10);
            trigger.RepeatCount = 12;
            trigger.DaysOfWeek = new Collection.HashSet<DayOfWeek>
            {
                DayOfWeek.Thursday
            };
            trigger.MisfireInstruction = MisfireInstruction.DailyTimeIntervalTrigger.FireOnceNow;
            trigger.TimeZone = TimeZoneInfo.Utc;

            var scheduleBuilder = trigger.GetScheduleBuilder();

            var cloned = (DailyTimeIntervalTriggerImpl) scheduleBuilder.Build();
            CollectionAssert.AreEqual(cloned.DaysOfWeek, trigger.DaysOfWeek);
            Assert.That(cloned.RepeatCount, Is.EqualTo(trigger.RepeatCount));
            Assert.That(cloned.RepeatInterval, Is.EqualTo(trigger.RepeatInterval));
            Assert.That(cloned.RepeatIntervalUnit, Is.EqualTo(trigger.RepeatIntervalUnit));
            Assert.That(cloned.StartTimeOfDay, Is.EqualTo(trigger.StartTimeOfDay));
            Assert.That(cloned.EndTimeOfDay, Is.EqualTo(trigger.EndTimeOfDay));
            Assert.That(cloned.TimeZone, Is.EqualTo(trigger.TimeZone));
        }
    }
}