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

using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Job;
using Quartz.Spi;
using Quartz.Util;

using TimeZoneConverter;

namespace Quartz.Tests.Unit.Impl.Triggers;

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

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(48));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011)));
            Assert.That(fireTimes[47], Is.EqualTo(DateBuilder.DateOf(10, 24, 0, 16, 1, 2011)));
        });
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
        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, cronCal, 48);
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes.Count, Is.EqualTo(48));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011)));
            Assert.That(fireTimes[1], Is.EqualTo(DateBuilder.DateOf(13, 0, 0, 1, 1, 2011)));
            Assert.That(fireTimes[47], Is.EqualTo(DateBuilder.DateOf(23, 0, 0, 4, 1, 2011)));
        });
    }

    [Test]
    public void TestValidateTimeOfDayOrder()
    {
        Assert.Throws<ArgumentException>(() =>
            new DailyTimeIntervalTriggerImpl
            {
                StartTimeOfDay = new TimeOfDay(12, 0, 0),
                EndTimeOfDay = new TimeOfDay(8, 0, 0)
            }, "End time of day cannot be before start time of day");
    }

    [Test]
    public void TestValidateInterval()
    {
        var trigger = new DailyTimeIntervalTriggerImpl
        {
            Key = new TriggerKey("test", "test"),
            JobKey = JobKey.Create("test"),
            RepeatIntervalUnit = IntervalUnit.Hour,
            RepeatInterval = 25
        };

        Assert.Throws<SchedulerException>(trigger.Validate, "repeatInterval can not exceed 24 hours. Given 25 hours.");

        trigger.RepeatIntervalUnit = IntervalUnit.Minute;
        trigger.RepeatInterval = 60 * 25;
        Assert.Throws<SchedulerException>(trigger.Validate, "repeatInterval can not exceed 24 hours (86400 seconds). Given 90000");

        trigger.RepeatIntervalUnit = IntervalUnit.Second;
        trigger.RepeatInterval = 60 * 60 * 25;

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

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(48));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(0, 0, 0, 1, 1, 2011)));
            Assert.That(fireTimes[47], Is.EqualTo(DateBuilder.DateOf(23, 0, 0, 2, 1, 2011)));
        });
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

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(47));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(0, 0, 0, 1, 1, 2011)));
            Assert.That(fireTimes[46], Is.EqualTo(DateBuilder.DateOf(22, 0, 0, 2, 1, 2011)));
        });
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

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(48));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011)));
            Assert.That(fireTimes[47], Is.EqualTo(DateBuilder.DateOf(23, 0, 0, 3, 1, 2011)));
        });
    }

    [Test]
    public void TestStartTimeBeforeStartTimeOfDayOnInvalidDay()
    {
        DateTimeOffset startTime = dateOf(0, 0, 0, 1, 1, 2011); // Jan 1, 2011 was a saturday...
        TimeOfDay startTimeOfDay = new TimeOfDay(8, 0, 0);
        var trigger = new DailyTimeIntervalTriggerImpl();
        var daysOfWeek = new List<DayOfWeek>
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
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(48));
            Assert.That(fireTimes[0], Is.EqualTo(dateOf(8, 0, 0, 3, 1, 2011)));
            Assert.That(fireTimes[47], Is.EqualTo(dateOf(23, 0, 0, 5, 1, 2011)));
        });
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

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(48));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(10, 0, 0, 1, 1, 2011)));
            Assert.That(fireTimes[47], Is.EqualTo(DateBuilder.DateOf(9, 0, 0, 4, 1, 2011)));
        });
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

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(35));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(0, 0, 0, 1, 1, 2011)));
            Assert.That(fireTimes[17], Is.EqualTo(DateBuilder.DateOf(17, 0, 0, 1, 1, 2011)));
            Assert.That(fireTimes[34], Is.EqualTo(DateBuilder.DateOf(16, 0, 0, 2, 1, 2011)));
        });
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

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(36));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(0, 0, 0, 1, 1, 2011)));
            Assert.That(fireTimes[17], Is.EqualTo(DateBuilder.DateOf(17, 0, 0, 1, 1, 2011)));
            Assert.That(fireTimes[35], Is.EqualTo(DateBuilder.DateOf(17, 0, 0, 2, 1, 2011)));
        });
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

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(48));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011)));
            Assert.That(fireTimes[9], Is.EqualTo(DateBuilder.DateOf(17, 0, 0, 1, 1, 2011))); // The 10th hours is the end of day.
            Assert.That(fireTimes[47], Is.EqualTo(DateBuilder.DateOf(15, 0, 0, 5, 1, 2011)));
        });
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

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(30));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011)));
            Assert.That(fireTimes[9], Is.EqualTo(DateBuilder.DateOf(17, 0, 0, 1, 1, 2011))); // The 10th hours is the end of day.
            Assert.That(fireTimes[29], Is.EqualTo(DateBuilder.DateOf(17, 0, 0, 3, 1, 2011)));
        });
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

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(48));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(8, 23, 0, 1, 1, 2011)));
            Assert.That(fireTimes[47], Is.EqualTo(DateBuilder.DateOf(23, 23, 0, 3, 1, 2011)));
        });
    }

    [Test]
    public void TestAllDaysOfTheWeek()
    {
        IReadOnlyCollection<DayOfWeek> daysOfWeek = DailyTimeIntervalScheduleBuilder.AllDaysOfTheWeek;
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

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(48));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011)));
            Assert.That(fireTimes[9], Is.EqualTo(DateBuilder.DateOf(17, 0, 0, 1, 1, 2011))); // The 10th hours is the end of day.
            Assert.That(fireTimes[47], Is.EqualTo(DateBuilder.DateOf(15, 0, 0, 5, 1, 2011)));
        });
    }

    [Test]
    public void TestMonThroughFri()
    {
        IReadOnlyCollection<DayOfWeek> daysOfWeek = DailyTimeIntervalScheduleBuilder.MondayThroughFriday;
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

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
        
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(48));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(8, 0, 0, 3, 1, 2011)));
            Assert.That(fireTimes[0].LocalDateTime.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
            Assert.That(fireTimes[10], Is.EqualTo(DateBuilder.DateOf(8, 0, 0, 4, 1, 2011)));
            Assert.That(fireTimes[10].LocalDateTime.DayOfWeek, Is.EqualTo(DayOfWeek.Tuesday));
            Assert.That(fireTimes[47], Is.EqualTo(DateBuilder.DateOf(15, 0, 0, 7, 1, 2011)));
            Assert.That(fireTimes[47].LocalDateTime.DayOfWeek, Is.EqualTo(DayOfWeek.Friday));
        });
    }

    [Test]
    public void TestSatAndSun()
    {
        IReadOnlyCollection<DayOfWeek> daysOfWeek = DailyTimeIntervalScheduleBuilder.SaturdayAndSunday;
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

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(48));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011)));
            Assert.That(fireTimes[0].LocalDateTime.DayOfWeek, Is.EqualTo(DayOfWeek.Saturday));
            Assert.That(fireTimes[10], Is.EqualTo(DateBuilder.DateOf(8, 0, 0, 2, 1, 2011)));
            Assert.That(fireTimes[10].LocalDateTime.DayOfWeek, Is.EqualTo(DayOfWeek.Sunday));
            Assert.That(fireTimes[47], Is.EqualTo(DateBuilder.DateOf(15, 0, 0, 15, 1, 2011)));
            Assert.That(fireTimes[47].LocalDateTime.DayOfWeek, Is.EqualTo(DayOfWeek.Saturday));
        });
    }

    [TestCase(-14)]
    [TestCase(0)]
    [TestCase(10)]
    public void TestMonOnly(int tzOffsetHours)
    {
        var daysOfWeek = new HashSet<DayOfWeek>
        {
            DayOfWeek.Monday
        };
        DateTimeOffset startTime = new DateTimeOffset(2011, 1, 1, 0, 0, 0, TimeSpan.FromHours(tzOffsetHours)); // SAT(7)
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

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
        Assert.Multiple(() =>{
            Assert.That(fireTimes, Has.Count.EqualTo(48));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(8, 0, 0, 3, 1, 2011)));
            Assert.That(fireTimes[0].LocalDateTime.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
            Assert.That(fireTimes[10], Is.EqualTo(DateBuilder.DateOf(8, 0, 0, 10, 1, 2011)));
            Assert.That(fireTimes[10].LocalDateTime.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
            Assert.That(fireTimes[47], Is.EqualTo(DateBuilder.DateOf(15, 0, 0, 31, 1, 2011)));
            Assert.That(fireTimes[47].LocalDateTime.DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
        });
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

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(18));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011)));
            Assert.That(fireTimes[5], Is.EqualTo(DateBuilder.DateOf(9, 55, 0, 1, 1, 2011)));
            Assert.That(fireTimes[17], Is.EqualTo(DateBuilder.DateOf(9, 55, 0, 3, 1, 2011)));
        });
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

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(48));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(8, 1, 15, 1, 1, 2011)));
            Assert.That(fireTimes[47], Is.EqualTo(DateBuilder.DateOf(12, 1, 15, 10, 1, 2011)));
        });
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

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(48));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(8, 0, 2, 1, 1, 2011)));
            Assert.That(fireTimes[47], Is.EqualTo(DateBuilder.DateOf(8, 56, 26, 1, 1, 2011)));
        });
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
            RepeatInterval = 72,
            RepeatCount = DailyTimeIntervalTriggerImpl.RepeatIndefinitely
        };

        // Setting this (which is default) should make the trigger just as normal one.

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(48));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011)));
            Assert.That(fireTimes[47], Is.EqualTo(DateBuilder.DateOf(10, 24, 0, 16, 1, 2011)));
        });
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

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(8));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011)));
            Assert.That(fireTimes[7], Is.EqualTo(DateBuilder.DateOf(9, 12, 0, 3, 1, 2011)));
        });
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

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 48);
        Assert.Multiple(() =>
        {
            Assert.That(fireTimes, Has.Count.EqualTo(1));
            Assert.That(fireTimes[0], Is.EqualTo(DateBuilder.DateOf(8, 0, 0, 1, 1, 2011)));
        });
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

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 8);

        DateTimeOffset expected0 = new DateTimeOffset(2012, 3, 10, 8, 0, 0, 0, TimeSpan.FromHours(-5));
        DateTimeOffset expected1 = new DateTimeOffset(2012, 3, 10, 9, 0, 0, 0, TimeSpan.FromHours(-5));
        DateTimeOffset expected2 = new DateTimeOffset(2012, 3, 10, 10, 0, 0, 0, TimeSpan.FromHours(-5));
        DateTimeOffset expected3 = new DateTimeOffset(2012, 3, 10, 11, 0, 0, 0, TimeSpan.FromHours(-5));

        DateTimeOffset expected4 = new DateTimeOffset(2012, 3, 11, 8, 0, 0, 0, TimeSpan.FromHours(-4));
        DateTimeOffset expected5 = new DateTimeOffset(2012, 3, 11, 9, 0, 0, 0, TimeSpan.FromHours(-4));
        DateTimeOffset expected6 = new DateTimeOffset(2012, 3, 11, 10, 0, 0, 0, TimeSpan.FromHours(-4));
        DateTimeOffset expected7 = new DateTimeOffset(2012, 3, 11, 11, 0, 0, 0, TimeSpan.FromHours(-4));

        Assert.Multiple(() =>
        {
            Assert.That(fireTimes[0], Is.EqualTo(expected0));
            Assert.That(fireTimes[1], Is.EqualTo(expected1));
            Assert.That(fireTimes[2], Is.EqualTo(expected2));
            Assert.That(fireTimes[3], Is.EqualTo(expected3));
            Assert.That(fireTimes[4], Is.EqualTo(expected4));
            Assert.That(fireTimes[5], Is.EqualTo(expected5));
            Assert.That(fireTimes[6], Is.EqualTo(expected6));
            Assert.That(fireTimes[7], Is.EqualTo(expected7));
        });
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

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 8);

        DateTimeOffset expected0 = new DateTimeOffset(2012, 11, 3, 8, 0, 0, 0, TimeSpan.FromHours(-4));
        DateTimeOffset expected1 = new DateTimeOffset(2012, 11, 3, 9, 0, 0, 0, TimeSpan.FromHours(-4));
        DateTimeOffset expected2 = new DateTimeOffset(2012, 11, 3, 10, 0, 0, 0, TimeSpan.FromHours(-4));
        DateTimeOffset expected3 = new DateTimeOffset(2012, 11, 3, 11, 0, 0, 0, TimeSpan.FromHours(-4));

        DateTimeOffset expected4 = new DateTimeOffset(2012, 11, 4, 8, 0, 0, 0, TimeSpan.FromHours(-5));
        DateTimeOffset expected5 = new DateTimeOffset(2012, 11, 4, 9, 0, 0, 0, TimeSpan.FromHours(-5));
        DateTimeOffset expected6 = new DateTimeOffset(2012, 11, 4, 10, 0, 0, 0, TimeSpan.FromHours(-5));
        DateTimeOffset expected7 = new DateTimeOffset(2012, 11, 4, 11, 0, 0, 0, TimeSpan.FromHours(-5));

        Assert.Multiple(() =>
        {
            Assert.That(fireTimes[0], Is.EqualTo(expected0));
            Assert.That(fireTimes[1], Is.EqualTo(expected1));
            Assert.That(fireTimes[2], Is.EqualTo(expected2));
            Assert.That(fireTimes[3], Is.EqualTo(expected3));
            Assert.That(fireTimes[4], Is.EqualTo(expected4));
            Assert.That(fireTimes[5], Is.EqualTo(expected5));
            Assert.That(fireTimes[6], Is.EqualTo(expected6));
            Assert.That(fireTimes[7], Is.EqualTo(expected7));
        });
    }

    [Test]
    public void DayOfWeekPropertyShouldNotAffectOtherTriggers()
    {
        //make 2 trigger exactly the same
        DailyTimeIntervalTriggerImpl trigger1 = new DailyTimeIntervalTriggerImpl
        {
            RepeatInterval = 1,
            RepeatIntervalUnit = IntervalUnit.Hour
        };

        DailyTimeIntervalTriggerImpl trigger2 = new DailyTimeIntervalTriggerImpl
        {
            RepeatInterval = 1,
            RepeatIntervalUnit = IntervalUnit.Hour
        };

        //make an adjustment to only one trigger.
        //I only want mondays now
        trigger1.DaysOfWeek = new List<DayOfWeek>
        {
            DayOfWeek.Monday
        };

        //check trigger 2 DOW
        //this fails because the reference collection only contains MONDAY b/c it was cleared.
        Assert.Multiple(() =>
        {
            Assert.That(trigger2.DaysOfWeek, Does.Contain(DayOfWeek.Monday));
            Assert.That(trigger2.DaysOfWeek, Does.Contain(DayOfWeek.Tuesday));
            Assert.That(trigger2.DaysOfWeek, Does.Contain(DayOfWeek.Wednesday));
            Assert.That(trigger2.DaysOfWeek, Does.Contain(DayOfWeek.Thursday));
            Assert.That(trigger2.DaysOfWeek, Does.Contain(DayOfWeek.Friday));
            Assert.That(trigger2.DaysOfWeek, Does.Contain(DayOfWeek.Saturday));
            Assert.That(trigger2.DaysOfWeek, Does.Contain(DayOfWeek.Sunday));
        });
    }

    [Test]
    public void ValidateShouldSucceedWithValidIntervalUnitHourConfiguration()
    {
        var trigger = new DailyTimeIntervalTriggerImpl
        {
            Key = new TriggerKey("name", "group"),
            JobKey = new JobKey("jobname", "jobgroup"),
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

        Assert.Multiple(() =>
        {
            Assert.That(trigger.GetFireTimeAfter(dateOf(0, 0, 0, 1, 1, 2011)), Is.EqualTo(dateOf(8, 0, 0, 1, 1, 2011)));
            Assert.That(trigger.GetFireTimeAfter(dateOf(7, 0, 0, 1, 1, 2011)), Is.EqualTo(dateOf(8, 0, 0, 1, 1, 2011)));
            Assert.That(trigger.GetFireTimeAfter(dateOf(7, 59, 59, 1, 1, 2011)), Is.EqualTo(dateOf(8, 0, 0, 1, 1, 2011)));
            Assert.That(trigger.GetFireTimeAfter(dateOf(8, 0, 0, 1, 1, 2011)), Is.EqualTo(dateOf(9, 0, 0, 1, 1, 2011)));
            Assert.That(trigger.GetFireTimeAfter(dateOf(9, 0, 0, 1, 1, 2011)), Is.EqualTo(dateOf(10, 0, 0, 1, 1, 2011)));
            Assert.That(trigger.GetFireTimeAfter(dateOf(12, 59, 59, 1, 1, 2011)), Is.EqualTo(dateOf(13, 0, 0, 1, 1, 2011)));
            Assert.That(trigger.GetFireTimeAfter(dateOf(13, 0, 0, 1, 1, 2011)), Is.EqualTo(dateOf(8, 0, 0, 2, 1, 2011)));
        });
    }

    private DateTimeOffset dateOf(int hour, int minute, int second, int dayOfMonth, int month, int year)
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
        Assert.Multiple(() =>
        {
            Assert.That(trigger.GetFireTimeAfter(dateOf(0, 0, 0, 1, 1, 2011)), Is.EqualTo(dateOf(8, 0, 0, 1, 1, 2012)));
            Assert.That(trigger.GetFireTimeAfter(dateOf(7, 0, 0, 1, 1, 2011)), Is.EqualTo(dateOf(8, 0, 0, 1, 1, 2012)));
            Assert.That(trigger.GetFireTimeAfter(dateOf(7, 59, 59, 1, 1, 2011)), Is.EqualTo(dateOf(8, 0, 0, 1, 1, 2012)));
            Assert.That(trigger.GetFireTimeAfter(dateOf(8, 0, 0, 1, 1, 2011)), Is.EqualTo(dateOf(8, 0, 0, 1, 1, 2012)));
            Assert.That(trigger.GetFireTimeAfter(dateOf(9, 0, 0, 1, 1, 2011)), Is.EqualTo(dateOf(8, 0, 0, 1, 1, 2012)));
            Assert.That(trigger.GetFireTimeAfter(dateOf(12, 59, 59, 1, 1, 2011)), Is.EqualTo(dateOf(8, 0, 0, 1, 1, 2012)));
            Assert.That(trigger.GetFireTimeAfter(dateOf(13, 0, 0, 1, 1, 2011)), Is.EqualTo(dateOf(8, 0, 0, 1, 1, 2012)));

            // Now try some test times at or after startTime
            Assert.That(trigger.GetFireTimeAfter(dateOf(0, 0, 0, 1, 1, 2012)), Is.EqualTo(dateOf(8, 0, 0, 1, 1, 2012)));
            Assert.That(trigger.GetFireTimeAfter(dateOf(13, 0, 0, 1, 1, 2012)), Is.EqualTo(dateOf(8, 0, 0, 2, 1, 2012)));
        });
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

        Assert.That(trigger.GetFireTimeAfter(new DateTime(2012, 1, 1)), Is.EqualTo(dateOf(8, 0, 0, 1, 1, 2012)));
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

        Assert.Multiple(() =>
        {
            Assert.That(trigger.Key, Is.Not.Null);
            Assert.That(trigger.Key.Name, Is.EqualTo("triggerName"));
            Assert.That(trigger.Key.Group, Is.EqualTo("triggerGroup"));
            Assert.That(trigger.JobKey, Is.Not.Null);
            Assert.That(trigger.JobKey.Name, Is.EqualTo("jobName"));
            Assert.That(trigger.JobKey.Group, Is.EqualTo("jobGroup"));
            Assert.That(trigger.StartTimeUtc, Is.EqualTo(dateOf(8, 0, 0, 1, 1, 2012)));
            Assert.That(trigger.EndTimeUtc, Is.EqualTo(null));
            Assert.That(trigger.StartTimeOfDay, Is.EqualTo(new TimeOfDay(8, 0, 0)));
            Assert.That(trigger.EndTimeOfDay, Is.EqualTo(new TimeOfDay(17, 0, 0)));
            Assert.That(trigger.RepeatIntervalUnit, Is.EqualTo(IntervalUnit.Hour));
            Assert.That(trigger.RepeatInterval, Is.EqualTo(1));
        });

        trigger = new DailyTimeIntervalTriggerImpl(
            "triggerName", "triggerGroup",
            dateOf(8, 0, 0, 1, 1, 2012), null,
            new TimeOfDay(8, 0, 0), new TimeOfDay(17, 0, 0),
            IntervalUnit.Hour, 1);

        Assert.Multiple(() =>
        {
            Assert.That(trigger.Key, Is.Not.Null);
            Assert.That(trigger.Key.Name, Is.EqualTo("triggerName"));
            Assert.That(trigger.Key.Group, Is.EqualTo("triggerGroup"));
            Assert.That(trigger.JobKey, Is.Null);
            Assert.That(trigger.StartTimeUtc, Is.EqualTo(dateOf(8, 0, 0, 1, 1, 2012)));
            Assert.That(trigger.EndTimeUtc, Is.EqualTo(null));
            Assert.That(trigger.StartTimeOfDay, Is.EqualTo(new TimeOfDay(8, 0, 0)));
            Assert.That(trigger.EndTimeOfDay, Is.EqualTo(new TimeOfDay(17, 0, 0)));
            Assert.That(trigger.RepeatIntervalUnit, Is.EqualTo(IntervalUnit.Hour));
            Assert.That(trigger.RepeatInterval, Is.EqualTo(1));
        });
    }

    [Test]
    [Category("windowstimezoneid")]
    public void TestDayLightSaving()
    {
        var timeZoneInfo = TZConvert.GetTimeZoneInfo("GMT Standard Time");

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
    public void TestDayLightSaving2()
    {
        var timeZoneInfo = TZConvert.GetTimeZoneInfo("Central Standard Time");

        var trigger = DailyTimeIntervalScheduleBuilder.Create()
            .OnEveryDay()
            .InTimeZone(timeZoneInfo)
            .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(0, 0))
            .WithIntervalInHours(4)
            .Build();

        var first = trigger.GetFireTimeAfter(new DateTimeOffset(2017, 3, 12, 9, 0, 0, TimeSpan.Zero));
        Assert.That(first, !Is.EqualTo(new DateTimeOffset(2017, 3, 12, 9, 0, 0, TimeSpan.Zero)));
    }

    [Test]
    public void TestDayLightSaving3()
    {
        var timeZoneInfo = TZConvert.GetTimeZoneInfo("Eastern Standard Time");
        //UTC: 2020/3/7/ 00:00  EST: 2020/3/6 19:00
        var startTime = new DateTimeOffset(2020, 3, 7, 0, 0, 0, TimeSpan.Zero);
        var trigger = DailyTimeIntervalScheduleBuilder.Create()
            .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(17, 00))
            .EndingDailyAt(TimeOfDay.HourAndMinuteOfDay(19, 30))
            .OnDaysOfTheWeek(new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
            .WithIntervalInHours(2)
            .InTimeZone(timeZoneInfo)
            .Build();

        var first = trigger.GetFireTimeAfter(startTime);
        //UTC: 2020/3/9/ 21:00  EST: 2020/3/9 17:00
        Assert.That(first, Is.EqualTo(new DateTimeOffset(2020, 3, 9, 21, 0, 0, TimeSpan.Zero)));
    }

    [Test]
    public void TestDayLightSaving4()
    {
        var timeZoneInfo = TZConvert.GetTimeZoneInfo("Eastern Standard Time");
        //UTC: 2019/11/1/ 23:00  EST: 2019/11/1 19:00
        var startTime = new DateTimeOffset(2019, 11, 1, 23, 0, 0, TimeSpan.Zero);
        var trigger = DailyTimeIntervalScheduleBuilder.Create()
            .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(17, 00))
            .EndingDailyAt(TimeOfDay.HourAndMinuteOfDay(19, 30))
            .OnDaysOfTheWeek(new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
            .WithIntervalInHours(2)
            .InTimeZone(timeZoneInfo)
            .Build();

        var first = trigger.GetFireTimeAfter(startTime);
        //UTC: 2019/11/4/ 22:00  EST: 2019/11/1 17:00
        Assert.That(first, Is.EqualTo(new DateTimeOffset(2019, 11, 4, 22, 0, 0, TimeSpan.Zero)));
    }

    [Test]
    [Explicit]
    public void TestPassingMidnight()
    {
        IOperableTrigger trigger = (IOperableTrigger) DailyTimeIntervalScheduleBuilder.Create()
            .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(16, 0))
            .EndingDailyAt(TimeOfDay.HourMinuteAndSecondOfDay(23, 59, 59))
            .OnEveryDay()
            .WithIntervalInMinutes(30)
            .Build();

        trigger.StartTimeUtc = new DateTimeOffset(2015, 1, 11, 23, 57, 0, 0, TimeSpan.Zero);

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, null, 100);
        foreach (var fireTime in fireTimes)
        {
            // Console.WriteLine(fireTime.LocalDateTime);
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
        trigger.DaysOfWeek = new List<DayOfWeek>
        {
            DayOfWeek.Thursday
        };
        trigger.MisfireInstruction = MisfireInstruction.DailyTimeIntervalTrigger.FireOnceNow;
        trigger.TimeZone = TimeZoneInfo.Utc;

        var scheduleBuilder = trigger.GetScheduleBuilder();

        var cloned = (DailyTimeIntervalTriggerImpl) scheduleBuilder.Build();
        Assert.Multiple(() =>
        {
            Assert.That(trigger.DaysOfWeek, Is.EqualTo(cloned.DaysOfWeek).AsCollection);
            Assert.That(cloned.RepeatCount, Is.EqualTo(trigger.RepeatCount));
            Assert.That(cloned.RepeatInterval, Is.EqualTo(trigger.RepeatInterval));
            Assert.That(cloned.RepeatIntervalUnit, Is.EqualTo(trigger.RepeatIntervalUnit));
            Assert.That(cloned.StartTimeOfDay, Is.EqualTo(trigger.StartTimeOfDay));
            Assert.That(cloned.EndTimeOfDay, Is.EqualTo(trigger.EndTimeOfDay));
            Assert.That(cloned.TimeZone, Is.EqualTo(trigger.TimeZone));
        });
    }

    [Test(Description = "https://github.com/quartznet/quartznet/issues/382")]
    public void ShouldAllowAlteringToLaterTimeInDay()
    {
        IJobDetail job = JobBuilder.Create<NoOpJob>()
            .WithIdentity("DummyJob", "DEFAULT")
            .Build();

        ITrigger myTrigger = TriggerBuilder.Create()
            .ForJob(job)
            .WithIdentity("MyIdentity", "DEFAULT")
            .WithDailyTimeIntervalSchedule(s => s
                .StartingDailyAt(new TimeOfDay(12, 0, 0))
                .EndingDailyAt(new TimeOfDay(15, 0, 0))
                .OnEveryDay()
            )
            .Build();

        ((DailyTimeIntervalTriggerImpl) myTrigger).EndTimeOfDay = new TimeOfDay(16, 0, 0);
    }

    [Test]
    public void TestInfinitiveLoop()
    {
        var trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithDailyTimeIntervalSchedule(x => x
                .InTimeZone(TZConvert.GetTimeZoneInfo("GTB Standard Time"))
                .StartingDailyAt(new TimeOfDay(0, 0, 0))
                .EndingDailyAt(new TimeOfDay(22, 0, 0))
                .WithInterval(15, IntervalUnit.Minute)
                .WithMisfireHandlingInstructionDoNothing()
            )
            .Build();

        var from = new DateTimeOffset(2018, 3, 25, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2018, 3, 27, 0, 0, 0, TimeSpan.Zero);
        var times = TriggerUtils.ComputeFireTimesBetween(trigger, null, from, to);
        Assert.That(times, Has.Count.LessThan(200));
    }
}