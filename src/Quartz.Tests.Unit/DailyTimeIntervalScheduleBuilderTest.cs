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

using Quartz.Spi;
using Quartz.Job;
using Quartz.Impl;
using Quartz.Impl.Triggers;

using NUnit.Framework;

using Quartz.Util;

namespace Quartz.Tests.Unit
{
    /// <summary>
    /// Unit test for DailyTimeIntervalScheduleBuilder.
    /// </summary>
    /// <author>Zemian Deng saltnlight5@gmail.com</author>
    /// <author>Nuno Maia (.NET)</author>
    [TestFixture]
    public class DailyTimeIntervalScheduleBuilderTest
    {
        [Test]
        public void TestScheduleActualTrigger()
        {
            IScheduler scheduler = StdSchedulerFactory.GetDefaultScheduler();
            IJobDetail job = JobBuilder.Create(typeof (NoOpJob)).Build();

            ITrigger trigger = TriggerBuilder.Create()
                .WithIdentity("test")
                .WithDailyTimeIntervalSchedule(x => x.WithIntervalInSeconds(3))
                .Build();

            scheduler.ScheduleJob(job, trigger); //We are not verify anything other than just run through the scheduler.
            scheduler.Shutdown();
        }

        [Test]
        public void TestScheduleInMiddleOfDailyInterval()
        {
            DateTimeOffset currTime = DateTimeOffset.UtcNow;

            // this test won't work out well in the early hours, where 'backing up' would give previous day,
            // or where daylight savings transitions could occur and confuse the assertions...
            if (currTime.Hour < 3)
            {
                return;
            }

            IScheduler scheduler = StdSchedulerFactory.GetDefaultScheduler();
            IJobDetail job = JobBuilder.Create<NoOpJob>().Build();
            ITrigger trigger = TriggerBuilder.Create().WithIdentity("test")
                .WithDailyTimeIntervalSchedule(x => x
                    .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(2, 15))
                    .WithIntervalInMinutes(5))
                .StartAt(currTime)
                .Build();

            scheduler.ScheduleJob(job, trigger);

            trigger = scheduler.GetTrigger(trigger.Key);

            Console.WriteLine("testScheduleInMiddleOfDailyInterval: currTime = " + currTime);
            Console.WriteLine("testScheduleInMiddleOfDailyInterval: computed first fire time = " + trigger.GetNextFireTimeUtc());

            Assert.That(trigger.GetNextFireTimeUtc() > currTime, "First fire time is not after now!");

            DateTimeOffset startTime = DateBuilder.TodayAt(2, 15, 0);

            job = JobBuilder.Create<NoOpJob>().Build();

            trigger = TriggerBuilder.Create().WithIdentity("test2")
                .WithDailyTimeIntervalSchedule(x => x
                    .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(2, 15))
                    .WithIntervalInMinutes(5))
                .StartAt(startTime)
                .Build();
            scheduler.ScheduleJob(job, trigger);

            trigger = scheduler.GetTrigger(trigger.Key);

            Console.WriteLine("testScheduleInMiddleOfDailyInterval: startTime = " + startTime);
            Console.WriteLine("testScheduleInMiddleOfDailyInterval: computed first fire time = " + trigger.GetNextFireTimeUtc());

            Assert.That(trigger.GetNextFireTimeUtc() == startTime);

            scheduler.Shutdown();
        }

        [Test]
        public void TestHourlyTrigger()
        {
            IDailyTimeIntervalTrigger trigger = (IDailyTimeIntervalTrigger) TriggerBuilder.Create()
                .WithIdentity("test")
                .WithDailyTimeIntervalSchedule(x => x.WithIntervalInHours(3))
                .Build();
            Assert.AreEqual("test", trigger.Key.Name);
            Assert.AreEqual("DEFAULT", trigger.Key.Group);
            Assert.AreEqual(IntervalUnit.Hour, trigger.RepeatIntervalUnit);
            //Assert.AreEqual(1, trigger.RepeatInterval);
            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger) trigger, null, 48);
            Assert.AreEqual(48, fireTimes.Count);
        }

        [Test]
        public void TestMinutelyTriggerWithTimeOfDay()
        {
            IDailyTimeIntervalTrigger trigger = (IDailyTimeIntervalTrigger) TriggerBuilder.Create()
                .WithIdentity("test", "group")
                .WithDailyTimeIntervalSchedule(x =>
                    x.WithIntervalInMinutes(72)
                        .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(8, 0))
                        .EndingDailyAt(TimeOfDay.HourAndMinuteOfDay(17, 0))
                        .OnMondayThroughFriday())
                .Build();

            Assert.AreEqual("test", trigger.Key.Name);
            Assert.AreEqual("group", trigger.Key.Group);
            Assert.AreEqual(true, SystemTime.UtcNow() >= trigger.StartTimeUtc);
            Assert.AreEqual(true, null == trigger.EndTimeUtc);
            Assert.AreEqual(IntervalUnit.Minute, trigger.RepeatIntervalUnit);
            Assert.AreEqual(72, trigger.RepeatInterval);
            Assert.AreEqual(new TimeOfDay(8, 0), trigger.StartTimeOfDay);
            Assert.AreEqual(new TimeOfDay(17, 0), trigger.EndTimeOfDay);
            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger) trigger, null, 48);
            Assert.AreEqual(48, fireTimes.Count);
        }

        [Test]
        public void TestSecondlyTriggerWithStartAndEndTime()
        {
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
            DateTimeOffset endTime = DateBuilder.DateOf(0, 0, 0, 2, 1, 2011);
            IDailyTimeIntervalTrigger trigger = (IDailyTimeIntervalTrigger) TriggerBuilder.Create()
                .WithIdentity("test", "test")
                .WithDailyTimeIntervalSchedule(x =>
                    x.WithIntervalInSeconds(121)
                        .StartingDailyAt(TimeOfDay.HourMinuteAndSecondOfDay(10, 0, 0))
                        .EndingDailyAt(TimeOfDay.HourMinuteAndSecondOfDay(23, 59, 59))
                        .OnSaturdayAndSunday())
                .StartAt(startTime)
                .EndAt(endTime)
                .Build();
            Assert.AreEqual("test", trigger.Key.Name);
            Assert.AreEqual("test", trigger.Key.Group);
            Assert.AreEqual(true, startTime == trigger.StartTimeUtc);
            Assert.AreEqual(true, endTime == trigger.EndTimeUtc);
            Assert.AreEqual(IntervalUnit.Second, trigger.RepeatIntervalUnit);
            Assert.AreEqual(121, trigger.RepeatInterval);
            Assert.AreEqual(new TimeOfDay(10, 0, 0), trigger.StartTimeOfDay);
            Assert.AreEqual(new TimeOfDay(23, 59, 59), trigger.EndTimeOfDay);
            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger) trigger, null, 48);
            Assert.AreEqual(48, fireTimes.Count);
        }

        [Test]
        public void TestRepeatCountTrigger()
        {
            IDailyTimeIntervalTrigger trigger = (IDailyTimeIntervalTrigger) TriggerBuilder.Create()
                .WithIdentity("test")
                .WithDailyTimeIntervalSchedule(x => x.WithIntervalInHours(1).WithRepeatCount(9))
                .Build();

            Assert.AreEqual("test", trigger.Key.Name);
            Assert.AreEqual("DEFAULT", trigger.Key.Group);
            Assert.AreEqual(IntervalUnit.Hour, trigger.RepeatIntervalUnit);
            Assert.AreEqual(1, trigger.RepeatInterval);
            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger) trigger, null, 48);
            Assert.AreEqual(10, fireTimes.Count);
        }

        [Test]
        public void TestEndingAtAfterCount()
        {
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
            IDailyTimeIntervalTrigger trigger = (IDailyTimeIntervalTrigger) TriggerBuilder.Create()
                .WithIdentity("test")
                .WithDailyTimeIntervalSchedule(x =>
                    x.WithIntervalInMinutes(15)
                        .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(8, 0))
                        .EndingDailyAfterCount(12))
                .StartAt(startTime)
                .Build();
            Assert.AreEqual("test", trigger.Key.Name);
            Assert.AreEqual("DEFAULT", trigger.Key.Group);
            Assert.AreEqual(IntervalUnit.Minute, trigger.RepeatIntervalUnit);
            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger) trigger, null, 48);
            Assert.AreEqual(48, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DateBuilder.DateOf(10, 45, 0, 4, 1, 2011), fireTimes[47]);
            Assert.AreEqual(new TimeOfDay(10, 45), trigger.EndTimeOfDay);
        }

        [Test]
        public void TestEndingAtAfterCountOf1()
        {
            DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
            IDailyTimeIntervalTrigger trigger = (IDailyTimeIntervalTrigger) TriggerBuilder.Create()
                .WithIdentity("test")
                .WithDailyTimeIntervalSchedule(x => x.WithIntervalInMinutes(15)
                    .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(8, 0))
                    .EndingDailyAfterCount(1))
                .StartAt(startTime)
                .Build();
            Assert.AreEqual("test", trigger.Key.Name);
            Assert.AreEqual("DEFAULT", trigger.Key.Group);
            Assert.AreEqual(IntervalUnit.Minute, trigger.RepeatIntervalUnit);
            IList<DateTimeOffset> fireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger) trigger, null, 48);
            Assert.AreEqual(48, fireTimes.Count);
            Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011), fireTimes[0]);
            Assert.AreEqual(DateBuilder.DateOf(8, 0, 0, 17, 2, 2011), fireTimes[47]);
            Assert.AreEqual(new TimeOfDay(8, 0), trigger.EndTimeOfDay);
        }

        [Test]
        public void TestEndingAtAfterCountOf0()
        {
            try
            {
                DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
                TriggerBuilder.Create()
                    .WithIdentity("test")
                    .WithDailyTimeIntervalSchedule(x =>
                        x.WithIntervalInMinutes(15)
                            .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(8, 0))
                            .EndingDailyAfterCount(0))
                    .StartAt(startTime)
                    .Build();
                Assert.Fail("We should not accept endingDailyAfterCount(0)");
            }
            catch (ArgumentException)
            {
                // Expected.
            }

            try
            {
                DateTimeOffset startTime = DateBuilder.DateOf(0, 0, 0, 1, 1, 2011);
                TriggerBuilder.Create()
                    .WithIdentity("test")
                    .WithDailyTimeIntervalSchedule(x =>
                        x.WithIntervalInMinutes(15)
                            .EndingDailyAfterCount(1))
                    .StartAt(startTime)
                    .Build();
                Assert.Fail("We should not accept endingDailyAfterCount(x) without first setting startingDailyAt.");
            }
            catch (ArgumentException)
            {
                // Expected.
            }
        }

        [Test]
        public void TestEndingAtAfterCountEndTimeOfDayValidation()
        {
            DailyTimeIntervalTriggerImpl trigger = (DailyTimeIntervalTriggerImpl) TriggerBuilder.Create()
                .WithIdentity("testTrigger")
                .ForJob("testJob")
                .WithDailyTimeIntervalSchedule(x =>
                    x.StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(8, 0))
                        .EndingDailyAfterCount(1))
                .Build();
            Assert.DoesNotThrow(trigger.Validate, "We should accept EndTimeOfDay specified by EndingDailyAfterCount(x).");
        }

        [Test]
        public void TestCanSetTimeZone()
        {
            TimeZoneInfo est = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");

            IDailyTimeIntervalTrigger trigger = (IDailyTimeIntervalTrigger) TriggerBuilder.Create()
                .WithDailyTimeIntervalSchedule(x => x.WithIntervalInHours(1)
                    .InTimeZone(est))
                .Build();

            Assert.AreEqual(est, trigger.TimeZone);
        }

        [Test]
        public void DayOfWeekPropertyShouldNotAffectOtherTriggers()
        {
            DailyTimeIntervalScheduleBuilder builder = DailyTimeIntervalScheduleBuilder.Create();

            DailyTimeIntervalTriggerImpl trigger1 = (DailyTimeIntervalTriggerImpl) builder
                .WithInterval(1, IntervalUnit.Hour)
                .OnMondayThroughFriday()
                .Build();

            //make an adjustment to this one trigger. 
            //I only want mondays now
            trigger1.DaysOfWeek.Clear();
            trigger1.DaysOfWeek.Add(DayOfWeek.Monday);

            //build same way as trigger1
            DailyTimeIntervalTriggerImpl trigger2 = (DailyTimeIntervalTriggerImpl) builder
                .WithInterval(1, IntervalUnit.Hour)
                .OnMondayThroughFriday()
                .Build();

            //check trigger 2 DOW
            //this fails because the reference collection only contains MONDAY b/c it was cleared.
            Assert.IsTrue(trigger2.DaysOfWeek.Contains(DayOfWeek.Monday));
            Assert.IsTrue(trigger2.DaysOfWeek.Contains(DayOfWeek.Tuesday));
            Assert.IsTrue(trigger2.DaysOfWeek.Contains(DayOfWeek.Wednesday));
            Assert.IsTrue(trigger2.DaysOfWeek.Contains(DayOfWeek.Thursday));
            Assert.IsTrue(trigger2.DaysOfWeek.Contains(DayOfWeek.Friday));

            Assert.IsFalse(trigger2.DaysOfWeek.Contains(DayOfWeek.Saturday));
            Assert.IsFalse(trigger2.DaysOfWeek.Contains(DayOfWeek.Sunday));
        }

        [Test]
        public void TestEndingDailyAfterCount()
        {
            var startDate = new DateTime(2015, 1, 1).ToUniversalTime();
            DailyTimeIntervalTriggerImpl trigger = (DailyTimeIntervalTriggerImpl) TriggerBuilder.Create()
                .WithDailyTimeIntervalSchedule(x => x
                    .StartingDailyAt(new TimeOfDay(9, 0, 0))
                    .WithIntervalInHours(1)
                    .EndingDailyAfterCount(2))
                .StartAt(startDate)
                .Build();

            var times = TriggerUtils.ComputeFireTimesBetween(trigger, null, startDate, new DateTime(2015, 1, 2));
            Assert.That(times.Count, Is.EqualTo(2), "wrong occurrancy count");
            Assert.That(times[1].ToLocalTime().DateTime, Is.EqualTo(new DateTime(2015, 1, 1, 10, 0, 0)), "wrong occurrancy count");
        }
    }
}