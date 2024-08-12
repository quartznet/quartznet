#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
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

using System.Collections.Specialized;

using FluentAssertions;
using FluentAssertions.Execution;

using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Job;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Tests.Unit;

/// <summary>
/// Unit test for DailyTimeIntervalScheduleBuilder.
/// </summary>
/// <author>Zemian Deng saltnlight5@gmail.com</author>
/// <author>Nuno Maia (.NET)</author>
[TestFixture]
public class DailyTimeIntervalScheduleBuilderTest
{
    [Test]
    public async Task TestScheduleActualTrigger()
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
        };

        var factory = new StdSchedulerFactory(properties);
        IScheduler scheduler = await factory.GetScheduler();
        IJobDetail job = JobBuilder.Create(typeof(NoOpJob)).Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("test")
            .WithDailyTimeIntervalSchedule(x => x.WithIntervalInSeconds(3))
            .Build();

        await scheduler.ScheduleJob(job, trigger); //We are not verify anything other than just run through the scheduler.
        await scheduler.Shutdown();
    }

    [Test]
    public async Task TestScheduleInMiddleOfDailyInterval()
    {
        DateTimeOffset currTime = DateTimeOffset.UtcNow;

        // this test won't work out well in the early hours, where 'backing up' would give previous day,
        // or where daylight savings transitions could occur and confuse the assertions...
        if (currTime.Hour < 3)
        {
            return;
        }

        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType
        };
        ISchedulerFactory sf = new StdSchedulerFactory(properties);
        IScheduler scheduler = await sf.GetScheduler();

        IJobDetail job = JobBuilder.Create<NoOpJob>().Build();
        ITrigger trigger = TriggerBuilder.Create().WithIdentity("test")
            .WithDailyTimeIntervalSchedule(x => x
                .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(2, 15))
                .WithIntervalInMinutes(5))
            .StartAt(currTime)
            .Build();

        await scheduler.ScheduleJob(job, trigger);

        trigger = await scheduler.GetTrigger(trigger.Key);
        var nextFireTime = trigger.GetNextFireTimeUtc();

        Assert.That(nextFireTime, Is.Not.Null);
        Assert.That(nextFireTime, Is.GreaterThan(currTime));

        DateTimeOffset startTime = DateBuilder.TodayAt(2, 15, 0);

        job = JobBuilder.Create<NoOpJob>().Build();

        trigger = TriggerBuilder.Create().WithIdentity("test2")
            .WithDailyTimeIntervalSchedule(x => x
                .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(2, 15))
                .WithIntervalInMinutes(5))
            .StartAt(startTime)
            .Build();
        await scheduler.ScheduleJob(job, trigger);

        trigger = await scheduler.GetTrigger(trigger.Key);
        nextFireTime = trigger.GetNextFireTimeUtc();

        Assert.That(nextFireTime, Is.Not.Null);
        Assert.That(nextFireTime, Is.EqualTo(startTime));

        await scheduler.Shutdown();
    }

    [Test]
    public void TestHourlyTrigger()
    {
        IDailyTimeIntervalTrigger trigger = (IDailyTimeIntervalTrigger) TriggerBuilder.Create()
            .WithIdentity("test")
            .WithDailyTimeIntervalSchedule(x => x.WithIntervalInHours(3))
            .Build();
        Assert.Multiple(() =>
        {
            Assert.That(trigger.Key.Name, Is.EqualTo("test"));
            Assert.That(trigger.Key.Group, Is.EqualTo("DEFAULT"));
            Assert.That(trigger.RepeatIntervalUnit, Is.EqualTo(IntervalUnit.Hour));
        });
        //Assert.AreEqual(1, trigger.RepeatInterval);
        var fireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger) trigger, null, 48);
        Assert.That(fireTimes, Has.Count.EqualTo(48));
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

        Assert.Multiple(() =>
        {
            Assert.That(trigger.Key.Name, Is.EqualTo("test"));
            Assert.That(trigger.Key.Group, Is.EqualTo("group"));
            Assert.That(TimeProvider.System.GetUtcNow() >= trigger.StartTimeUtc, Is.EqualTo(true));
            Assert.That(null == trigger.EndTimeUtc, Is.EqualTo(true));
            Assert.That(trigger.RepeatIntervalUnit, Is.EqualTo(IntervalUnit.Minute));
            Assert.That(trigger.RepeatInterval, Is.EqualTo(72));
            Assert.That(trigger.StartTimeOfDay, Is.EqualTo(new TimeOfDay(8, 0)));
            Assert.That(trigger.EndTimeOfDay, Is.EqualTo(new TimeOfDay(17, 0)));
        });
        var fireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger) trigger, null, 48);
        Assert.That(fireTimes, Has.Count.EqualTo(48));
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
        Assert.Multiple(() =>
        {
            Assert.That(trigger.Key.Name, Is.EqualTo("test"));
            Assert.That(trigger.Key.Group, Is.EqualTo("test"));
            Assert.That(startTime == trigger.StartTimeUtc, Is.EqualTo(true));
            Assert.That(endTime == trigger.EndTimeUtc, Is.EqualTo(true));
            Assert.That(trigger.RepeatIntervalUnit, Is.EqualTo(IntervalUnit.Second));
            Assert.That(trigger.RepeatInterval, Is.EqualTo(121));
            Assert.That(trigger.StartTimeOfDay, Is.EqualTo(new TimeOfDay(10, 0, 0)));
            Assert.That(trigger.EndTimeOfDay, Is.EqualTo(new TimeOfDay(23, 59, 59)));
        });
        var fireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger) trigger, null, 48);
        Assert.That(fireTimes, Has.Count.EqualTo(48));
    }

    [Test]
    public void TestRepeatCountTrigger()
    {
        IDailyTimeIntervalTrigger trigger = (IDailyTimeIntervalTrigger) TriggerBuilder.Create()
            .WithIdentity("test")
            .WithDailyTimeIntervalSchedule(x => x.WithIntervalInHours(1).WithRepeatCount(9))
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(trigger.Key.Name, Is.EqualTo("test"));
            Assert.That(trigger.Key.Group, Is.EqualTo("DEFAULT"));
            Assert.That(trigger.RepeatIntervalUnit, Is.EqualTo(IntervalUnit.Hour));
            Assert.That(trigger.RepeatInterval, Is.EqualTo(1));
        });
        var fireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger) trigger, null, 48);
        Assert.That(fireTimes, Has.Count.EqualTo(10));
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
        Assert.Multiple(() =>
        {
            Assert.That(trigger.Key.Name, Is.EqualTo("test"));
            Assert.That(trigger.Key.Group, Is.EqualTo("DEFAULT"));
            Assert.That(trigger.RepeatIntervalUnit, Is.EqualTo(IntervalUnit.Minute));
        });
        var fireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger) trigger, null, 48);
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(48));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011)));
            Assert.That(fireTimes[47], Is.EqualTo(DateBuilder.DateOf(10, 45, 0, 4, 1, 2011)));
            Assert.That(trigger.EndTimeOfDay, Is.EqualTo(new TimeOfDay(10, 45)));
        });
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
            .ForJob("testJob", "testJobGroup")
            .Build();
        Assert.Multiple(() =>
        {
            Assert.That(trigger.Key.Name, Is.EqualTo("test"));
            Assert.That(trigger.Key.Group, Is.EqualTo("DEFAULT"));
            Assert.That(trigger.RepeatIntervalUnit, Is.EqualTo(IntervalUnit.Minute));
        });
        ((IOperableTrigger) trigger).Validate();
        var fireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger) trigger, null, 48);
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(48));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011)));
            Assert.That(fireTimes[47], Is.EqualTo(DateBuilder.DateOf(8, 0, 0, 17, 2, 2011)));
            Assert.That(trigger.EndTimeOfDay, Is.EqualTo(new TimeOfDay(8, 0)));
        });
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

        Assert.That(trigger.TimeZone, Is.EqualTo(est));
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
        trigger1.DaysOfWeek = new List<DayOfWeek>
        {
            DayOfWeek.Monday
        };

        //build same way as trigger1
        DailyTimeIntervalTriggerImpl trigger2 = (DailyTimeIntervalTriggerImpl) builder
            .WithInterval(1, IntervalUnit.Hour)
            .OnMondayThroughFriday()
            .Build();

        Assert.Multiple(() =>
        {
            //check trigger 2 DOW
            //this fails because the reference collection only contains MONDAY b/c it was cleared.
            Assert.That(trigger2.DaysOfWeek, Does.Contain(DayOfWeek.Monday));
            Assert.That(trigger2.DaysOfWeek, Does.Contain(DayOfWeek.Tuesday));
            Assert.That(trigger2.DaysOfWeek, Does.Contain(DayOfWeek.Wednesday));
            Assert.That(trigger2.DaysOfWeek, Does.Contain(DayOfWeek.Thursday));
            Assert.That(trigger2.DaysOfWeek, Does.Contain(DayOfWeek.Friday));

            Assert.That(trigger2.DaysOfWeek, Does.Not.Contain(DayOfWeek.Saturday));
            Assert.That(trigger2.DaysOfWeek, Does.Not.Contain(DayOfWeek.Sunday));
        });
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
        Assert.Multiple(() =>
        {
            Assert.That(times, Has.Count.EqualTo(2), "wrong occurrancy count");
            Assert.That(times[1].ToLocalTime().DateTime, Is.EqualTo(new DateTime(2015, 1, 1, 10, 0, 0)), "wrong occurrancy count");
        });
    }

    [Test]
    public void TriggerBuilderShouldHandleIgnoreMisfirePolicy()
    {
        var trigger1 = TriggerBuilder.Create()
            .WithDailyTimeIntervalSchedule(x => x
                .WithMisfireHandlingInstructionIgnoreMisfires()
            )
            .Build();

        var trigger2 = trigger1
            .GetTriggerBuilder()
            .Build();
        using (new AssertionScope())
        {
            trigger1.MisfireInstruction.Should().Be(MisfireInstruction.IgnoreMisfirePolicy);
            trigger2.MisfireInstruction.Should().Be(MisfireInstruction.IgnoreMisfirePolicy);
        }
    }
}