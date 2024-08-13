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

using FluentAssertions;

using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Tests.Unit.Impl.Calendar;

/// <summary>
/// Unit test for DailyCalendar.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
[TestFixture(typeof(NewtonsoftJsonObjectSerializer))]
[TestFixture(typeof(SystemTextJsonObjectSerializer))]
public class DailyCalendarTest : SerializationTestSupport<DailyCalendar, ICalendar>
{
    public DailyCalendarTest(Type serializerType) : base(serializerType)
    {
    }

    [Test]
    public void TestStringStartEndTimes()
    {
        DailyCalendar dailyCalendar = new DailyCalendar("1:20", "14:50");
        var toString = dailyCalendar.ToString();
        Assert.That(toString, Does.Contain("01:20:00:000 - 14:50:00:000"));

        dailyCalendar = new DailyCalendar("1:20:1:456", "14:50:15:2");
        toString = dailyCalendar.ToString();
        Assert.That(toString, Does.Contain("01:20:01:456 - 14:50:15:002"));
    }

    [Test]
    public void TestStartEndTimes()
    {
        // Grafit found a copy-paste problem from ending time, it was the same as starting time

        DateTime d = DateTime.Now;
        DailyCalendar dailyCalendar = new DailyCalendar("1:20", "14:50");
        DateTime expectedStartTime = new DateTime(d.Year, d.Month, d.Day, 1, 20, 0);
        DateTime expectedEndTime = new DateTime(d.Year, d.Month, d.Day, 14, 50, 0);

        Assert.Multiple(() =>
        {
            Assert.That(dailyCalendar.GetTimeRangeStartingTimeUtc(d).DateTime, Is.EqualTo(expectedStartTime));
            Assert.That(dailyCalendar.GetTimeRangeEndingTimeUtc(d).DateTime, Is.EqualTo(expectedEndTime));
        });
    }

    [Test]
    public void TestStringInvertTimeRange()
    {
        DailyCalendar dailyCalendar = new DailyCalendar("1:20", "14:50")
        {
            InvertTimeRange = true
        };
        Assert.That(dailyCalendar.ToString().IndexOf("inverted: True"), Is.GreaterThan(0));

        dailyCalendar.InvertTimeRange = false;
        Assert.That(dailyCalendar.ToString().IndexOf("inverted: False"), Is.GreaterThan(0));
    }

    [Test]
    public void TestTimeZone()
    {
        TimeZoneInfo tz = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");

        DailyCalendar dailyCalendar = new DailyCalendar("12:00:00", "14:00:00")
        {
            InvertTimeRange = true, //inclusive calendar
            TimeZone = tz
        };

        // 11/2/2012 17:00 (utc) is 11/2/2012 13:00 (est)
        DateTimeOffset timeToCheck = new DateTimeOffset(2012, 11, 2, 17, 0, 0, TimeSpan.FromHours(0));
        Assert.That(dailyCalendar.IsTimeIncluded(timeToCheck), Is.True);
    }

    /// <summary>
    /// Ensure that the DailyCalendar use the same TimeZone offset for all the checks
    /// </summary>
    [Test]
    public void TestTimeZone2()
    {
        DailyCalendar dailyCalendar = new DailyCalendar("00:00:00", "04:00:00");
        dailyCalendar.TimeZone = TimeZoneInfo.Utc;

        var trigger = (IOperableTrigger)TriggerBuilder
            .Create()
            .WithIdentity("TestTimeZone2Trigger")
            .StartAt(DateBuilder.EvenMinuteDateAfterNow())
            .WithSimpleSchedule(s => s
                .WithIntervalInMinutes(1)
                .RepeatForever())
            .Build();

        var fireTimes = TriggerUtils.ComputeFireTimes(trigger, dailyCalendar, (int)TimeSpan.FromDays(1).TotalMinutes);

        var timeZoneOffset = TimeZoneInfo.Local.BaseUtcOffset;

        // Trigger need to fire during the period when the local timezone and utc timezone are on different day
        if (timeZoneOffset > TimeSpan.Zero)
        {
            // Trigger must fire between midnight and utc offset if positive offset.
            fireTimes.Should().Contain(t => t.Hour >= 0 && t.Hour <= timeZoneOffset.Hours);
        }
        else if (timeZoneOffset < TimeSpan.Zero)
        {
            // Trigger must fire between midnight minus utc offset and midnight if negative offset.
            fireTimes.Should().Contain(t => t.Hour >= 24 + timeZoneOffset.Hours && t.Hour <= 23);
        }
        else
        {
            // Trigger must not fire between midnight and utc offset if offset is UTC (zero)
            fireTimes.Should().NotContain(t => t.Hour >= 0 && t.Hour <= timeZoneOffset.Hours);
        }
    }

    [Test]
    public void ShouldAllowExactMidnight()
    {
        var calendar = new DailyCalendar("01:00", "05:00");

        var trigger = (CronTriggerImpl) TriggerBuilder.Create()
            .WithIdentity("TestJobTrigger", "group1")
            .StartNow()
            .WithCronSchedule("0 0 0 * * ? *")
            .ModifiedByCalendar("CustomCalendar")
            .Build();

        var fireTimeUtc = trigger.ComputeFirstFireTimeUtc(calendar);
        fireTimeUtc.Should().NotBeNull();
    }

    protected override DailyCalendar GetTargetObject()
    {
        DailyCalendar c = new DailyCalendar("01:20:01:456", "14:50:15:002");
        c.Description = "description";
        c.InvertTimeRange = true;
        return c;
    }

    protected override void VerifyMatch(DailyCalendar original, DailyCalendar deserialized)
    {
        Assert.Multiple(() =>
        {
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized.Description, Is.EqualTo(original.Description));
            Assert.That(deserialized.InvertTimeRange, Is.EqualTo(original.InvertTimeRange));
            Assert.That(deserialized.TimeZone, Is.EqualTo(original.TimeZone));
            Assert.That(deserialized.ToString(), Is.EqualTo(original.ToString()));
        });
    }
}