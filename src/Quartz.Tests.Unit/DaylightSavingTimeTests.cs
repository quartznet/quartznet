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

using System;
using System.Linq;

using NUnit.Framework;

using Quartz.Spi;

using TimeZoneConverter;

namespace Quartz.Tests.Unit;

[TestFixture]
public class DaylightSavingTimeTest
{
    private Func<DateTimeOffset> OriginalUtcNow;

    [OneTimeSetUp]
    public void Init()
    {
        OriginalUtcNow = SystemTime.UtcNow;
        SystemTime.UtcNow = () => new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }

    [OneTimeTearDown]
    public void Dispose()
    {
        SystemTime.UtcNow = OriginalUtcNow;
    }

    [Test]
    public void ShouldHandleDstSpringForwardTransition()
    {
        TimeZoneInfo tz = TZConvert.GetTimeZoneInfo("Pacific Standard Time");

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(2, 30).InTimeZone(tz))
            .ForJob("job1", "group1")
            .Build();

        DateTimeOffset midnight = new DateTimeOffset(2016, 3, 13, 0, 0, 0, TimeSpan.FromHours(-8));
        DateTimeOffset? fireTime = trigger.GetFireTimeAfter(midnight);

        // It should fire at the equivalent valid local time.  2:30-8 does not exist, so it should run at 3:30-7.
        DateTimeOffset expectedTime = new DateTimeOffset(2016, 3, 13, 3, 30, 0, TimeSpan.FromHours(-7));

        // We should definitely have a value
        Assert.NotNull(fireTime);

        // fireTime always is in UTC, but DateTimeOffset comparison normalized to UTC anyway.
        // Conversion here is for clarity of interpreting errors if the test fails.
        DateTimeOffset convertedFireTime = TimeZoneInfo.ConvertTime(fireTime.Value, tz);
        Assert.AreEqual(expectedTime, convertedFireTime);
    }

    [Test]
    public void ShouldHandleDstFallBackTransition()
    {
        TimeZoneInfo tz = TZConvert.GetTimeZoneInfo("Pacific Standard Time");

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(1, 30).InTimeZone(tz))
            .ForJob("job1", "group1")
            .Build();

        DateTimeOffset midnight = new DateTimeOffset(2016, 11, 6, 0, 0, 0, TimeSpan.FromHours(-7));
        DateTimeOffset? fireTime = trigger.GetFireTimeAfter(midnight);

        // It should fire at the first instance, which is 1:30-7 - the DAYLIGHT time, not the standard time.
        DateTimeOffset expectedTime = new DateTimeOffset(2016, 11, 6, 1, 30, 0, TimeSpan.FromHours(-7));

        // We should definitely have a value
        Assert.NotNull(fireTime);

        // fireTime always is in UTC, but DateTimeOffset comparison normalized to UTC anyway.
        // Conversion here is for clarity of interpreting errors if the test fails.
        DateTimeOffset convertedFireTime = TimeZoneInfo.ConvertTime(fireTime.Value, tz);
        Assert.AreEqual(expectedTime, convertedFireTime);
    }

    [Test]
    public void ShouldHandleDstFallBackTransition_AndNotRunTwiceOnTheSameDay()
    {
        TimeZoneInfo tz = TZConvert.GetTimeZoneInfo("Pacific Standard Time");

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(1, 30).InTimeZone(tz))
            .ForJob("job1", "group1")
            .Build();

        DateTimeOffset firstRun = new DateTimeOffset(2016, 11, 6, 1, 30, 0, TimeSpan.FromHours(-7));
        DateTimeOffset? fireTime = trigger.GetFireTimeAfter(firstRun);

        // It should not fire again at 1:30-8 on the same day, but should instead fire at 1:30-8 the following day.
        DateTimeOffset expectedTime = new DateTimeOffset(2016, 11, 7, 1, 30, 0, TimeSpan.FromHours(-8));

        // We should definitely have a value
        Assert.NotNull(fireTime);

        // fireTime always is in UTC, but DateTimeOffset comparison normalized to UTC anyway.
        // Conversion here is for clarity of interpreting errors if the test fails.
        DateTimeOffset convertedFireTime = TimeZoneInfo.ConvertTime(fireTime.Value, tz);
        Assert.AreEqual(expectedTime, convertedFireTime);
    }

    [Test]
    public void Can_GetNextFireTime_InDST_To_Issue_2475()
    {
        // Test starting during DST (first occurrence of 2:00 AM)
        var tz = TZConvert.GetTimeZoneInfo("Central European Standard Time"); //UTC+1  +2 in DST
        var startTime = new DateTimeOffset(2023, 10, 29, 2, 0, 0, TimeSpan.FromHours(2));

        Assert.IsTrue(tz.IsDaylightSavingTime(startTime), "Should be in DST");

        var trigger = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .WithSchedule(CronScheduleBuilder.CronSchedule("0 0/1 * ? * * *")
                .InTimeZone(tz)
            )
            .StartAt(startTime)
            .ForJob("job1", "group1")
            .Build();

        var nextFireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger)trigger, null, 20);

        // Fire times are returned in UTC
        // 2:00 AM CEST (UTC+2) = 00:00 UTC
        Assert.Multiple(() =>
        {
            Assert.That(nextFireTimes[0], Is.EqualTo(new DateTimeOffset(2023, 10, 29, 0, 00, 0, TimeSpan.Zero)));
            Assert.That(nextFireTimes[1], Is.EqualTo(new DateTimeOffset(2023, 10, 29, 0, 01, 0, TimeSpan.Zero)));
            Assert.That(nextFireTimes[2], Is.EqualTo(new DateTimeOffset(2023, 10, 29, 0, 02, 0, TimeSpan.Zero)));

            // Verify times advance monotonically
            Assert.That(nextFireTimes[0].UtcDateTime, Is.GreaterThanOrEqualTo(startTime.UtcDateTime));
        });
    }

    [Test]
    public void Can_GetNextFireTime_Transition_To_ST_Issue_2475()
    {
        // Test starting during standard time (second occurrence of 2:00 AM after DST ends)
        var tz = TZConvert.GetTimeZoneInfo("Central European Standard Time"); //UTC+1  +2 in DST
        var startTime = new DateTimeOffset(2023, 10, 29, 2, 0, 0, TimeSpan.FromHours(1));

        Assert.IsFalse(tz.IsDaylightSavingTime(startTime), "Should be in standard time");

        var trigger = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .WithSchedule(CronScheduleBuilder.CronSchedule("0 0/1 * ? * * *")
                .InTimeZone(tz)
            )
            .StartAt(startTime)
            .ForJob("job1", "group1")
            .Build();

        var nextFireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger)trigger, null, 20);

        // Fire times are returned in UTC
        // 2:00 AM CET (UTC+1, second occurrence) = 01:00 UTC
        // The fix ensures we don't go backwards to the first occurrence (00:00 UTC)
        Assert.Multiple(() =>
        {
            Assert.That(nextFireTimes[0], Is.EqualTo(new DateTimeOffset(2023, 10, 29, 1, 00, 0, TimeSpan.Zero)));
            Assert.That(nextFireTimes[1], Is.EqualTo(new DateTimeOffset(2023, 10, 29, 1, 01, 0, TimeSpan.Zero)));
            Assert.That(nextFireTimes[2], Is.EqualTo(new DateTimeOffset(2023, 10, 29, 1, 02, 0, TimeSpan.Zero)));

            // Critical: Verify no backwards time travel
            Assert.That(nextFireTimes[0].UtcDateTime, Is.GreaterThanOrEqualTo(startTime.UtcDateTime),
                "First fire time must not be before start time");

            // Verify monotonic progression
            for (int i = 1; i < 3; i++)
            {
                Assert.That(nextFireTimes[i], Is.GreaterThan(nextFireTimes[i - 1]),
                    $"Fire times must advance monotonically: [{i - 1}]={nextFireTimes[i - 1]}, [{i}]={nextFireTimes[i]}");
            }
        });
    }

    [Test]
    public void SpringForward_JobExecutingEveryMinute_SkipsMissingHour()
    {
        // March 13, 2016: 2:00 AM PST -> 3:00 AM PDT (clocks jump forward, 2:00-3:00 doesn't exist)
        var tz = TZConvert.GetTimeZoneInfo("Pacific Standard Time");
        var startTime = new DateTimeOffset(2016, 3, 13, 1, 58, 0, TimeSpan.FromHours(-8));

        var trigger = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .WithSchedule(CronScheduleBuilder.CronSchedule("0 * * ? * * *") // Every minute
                .InTimeZone(tz))
            .StartAt(startTime)
            .ForJob("job1", "group1")
            .Build();

        var nextFireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger)trigger, null, 10);

        // Convert to local time for easier verification
        var localTimes = nextFireTimes.Select(ft => TimeZoneInfo.ConvertTime(ft, tz)).ToList();

        Assert.Multiple(() =>
        {
            // Should fire at 1:58, 1:59 in PST
            Assert.That(localTimes[0].Hour, Is.EqualTo(1));
            Assert.That(localTimes[0].Minute, Is.EqualTo(58));
            Assert.That(localTimes[1].Hour, Is.EqualTo(1));
            Assert.That(localTimes[1].Minute, Is.EqualTo(59));

            // Should skip 2:00-2:59 (missing hour) and continue at 3:00 PDT
            Assert.That(localTimes[2].Hour, Is.EqualTo(3));
            Assert.That(localTimes[2].Minute, Is.EqualTo(0));
            Assert.That(localTimes[3].Hour, Is.EqualTo(3));
            Assert.That(localTimes[3].Minute, Is.EqualTo(1));

            // Verify UTC times are monotonically increasing
            for (int i = 1; i < nextFireTimes.Count; i++)
            {
                Assert.That(nextFireTimes[i].UtcDateTime, Is.GreaterThan(nextFireTimes[i - 1].UtcDateTime),
                    $"Fire times must advance: [{i - 1}]={nextFireTimes[i - 1].UtcDateTime:HH:mm:ss}, [{i}]={nextFireTimes[i].UtcDateTime:HH:mm:ss}");
            }

            // Verify all times are on or after start time
            Assert.That(nextFireTimes[0].UtcDateTime, Is.GreaterThanOrEqualTo(startTime.UtcDateTime));
        });
    }

    [Test]
    public void FallBack_JobExecutingEveryMinute_SkipsOverlappingHour()
    {
        // November 6, 2016: At 2:00 AM PDT, clocks fall back to 1:00 AM PST
        // When using cron patterns based on wall-clock time (hour:minute), the overlapping hour is skipped
        // because incrementing minute from 59 to 60 wraps to hour 2, which occurs after the transition
        var tz = TZConvert.GetTimeZoneInfo("Pacific Standard Time");

        // Start at 1:58 AM PDT (before the transition)
        var startTime = new DateTimeOffset(2016, 11, 6, 1, 58, 0, TimeSpan.FromHours(-7));

        var trigger = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .WithSchedule(CronScheduleBuilder.CronSchedule("0 * * ? * * *") // Every minute (wall-clock based)
                .InTimeZone(tz))
            .StartAt(startTime)
            .ForJob("job1", "group1")
            .Build();

        var nextFireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger)trigger, null, 10);
        var localTimes = nextFireTimes.Select(ft => TimeZoneInfo.ConvertTime(ft, tz)).ToList();

        Assert.Multiple(() =>
        {
            // Should fire at:
            // 1:58 AM PDT (08:58 UTC)
            // 1:59 AM PDT (08:59 UTC)
            // 2:00 AM PST (10:00 UTC) - skips the overlapping 1:xx hour because cron increments to hour 2
            // 2:01 AM PST (10:01 UTC)

            Assert.That(localTimes[0].Hour, Is.EqualTo(1));
            Assert.That(localTimes[0].Minute, Is.EqualTo(58));

            Assert.That(localTimes[1].Hour, Is.EqualTo(1));
            Assert.That(localTimes[1].Minute, Is.EqualTo(59));

            // After 1:59, cron increments to 2:00 (which is after the fall-back)
            Assert.That(localTimes[2].Hour, Is.EqualTo(2));
            Assert.That(localTimes[2].Minute, Is.EqualTo(0));

            Assert.That(localTimes[3].Hour, Is.EqualTo(2));
            Assert.That(localTimes[3].Minute, Is.EqualTo(1));

            // Verify UTC times are strictly increasing
            for (int i = 1; i < nextFireTimes.Count; i++)
            {
                Assert.That(nextFireTimes[i].UtcDateTime, Is.GreaterThan(nextFireTimes[i - 1].UtcDateTime),
                    $"UTC times must strictly increase: [{i - 1}]={nextFireTimes[i - 1].UtcDateTime:HH:mm:ss}, [{i}]={nextFireTimes[i].UtcDateTime:HH:mm:ss}");
            }

            // Verify first fire time is on or after start time
            Assert.That(nextFireTimes[0].UtcDateTime, Is.GreaterThanOrEqualTo(startTime.UtcDateTime));
        });
    }

    [Test]
    public void FallBack_JobEvery5Minutes_SkipsOverlappingHour()
    {
        // November 6, 2016: At 2:00 AM PDT, clocks fall back to 1:00 AM PST
        // Cron patterns based on wall-clock time skip the overlapping hour
        var tz = TZConvert.GetTimeZoneInfo("Pacific Standard Time");

        // Start at 1:20 AM PDT (before the transition)
        var startTime = new DateTimeOffset(2016, 11, 6, 1, 20, 0, TimeSpan.FromHours(-7));

        var trigger = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .WithSchedule(CronScheduleBuilder.CronSchedule("0 */5 * ? * * *") // Every 5 minutes
                .InTimeZone(tz))
            .StartAt(startTime)
            .ForJob("job1", "group1")
            .Build();

        var nextFireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger)trigger, null, 10);
        var localTimes = nextFireTimes.Select(ft => TimeZoneInfo.ConvertTime(ft, tz)).ToList();
        var utcTimes = nextFireTimes.Select(ft => ft.UtcDateTime).ToList();

        Assert.Multiple(() =>
        {
            // Should fire at:
            // 1:20 PDT (08:20 UTC)
            // 1:25 PDT (08:25 UTC)
            // 1:30 PDT (08:30 UTC)
            // 1:35 PDT (08:35 UTC)
            // 1:40 PDT (08:40 UTC)
            // 1:45 PDT (08:45 UTC)
            // 1:50 PDT (08:50 UTC)
            // 1:55 PDT (08:55 UTC)
            // 2:00 PST (10:00 UTC) - skips to hour 2 after transition
            // 2:05 PST (10:05 UTC)

            Assert.That(localTimes[0].Minute, Is.EqualTo(20));
            Assert.That(localTimes[1].Minute, Is.EqualTo(25));
            Assert.That(localTimes[7].Hour, Is.EqualTo(1));
            Assert.That(localTimes[7].Minute, Is.EqualTo(55));

            // After 1:55, next 5-minute mark is 2:00 (post-transition)
            Assert.That(localTimes[8].Hour, Is.EqualTo(2));
            Assert.That(localTimes[8].Minute, Is.EqualTo(0));

            // Verify all UTC times increase monotonically
            for (int i = 1; i < utcTimes.Count; i++)
            {
                Assert.That(utcTimes[i], Is.GreaterThan(utcTimes[i - 1]),
                    $"UTC time must increase: [{i - 1}]={utcTimes[i - 1]:HH:mm:ss} -> [{i}]={utcTimes[i]:HH:mm:ss}");
            }

            // Verify 5-minute intervals in UTC (except across the transition)
            for (int i = 1; i < 8; i++) // Before transition
            {
                var diff = (utcTimes[i] - utcTimes[i - 1]).TotalMinutes;
                Assert.That(diff, Is.EqualTo(5.0).Within(0.001),
                    $"Should be 5 minutes apart: [{i - 1}]={utcTimes[i - 1]:HH:mm:ss} -> [{i}]={utcTimes[i]:HH:mm:ss}");
            }

            // Across transition: 1:55 PDT (08:55 UTC) -> 2:00 PST (10:00 UTC) = 65 minutes
            var transitionDiff = (utcTimes[8] - utcTimes[7]).TotalMinutes;
            Assert.That(transitionDiff, Is.EqualTo(65.0).Within(0.001),
                "Transition from 1:55 PDT to 2:00 PST skips the overlapping hour");
        });
    }

    [Test]
    public void SpringForward_JobScheduledInMissingHour_AdvancesToNextValidTime()
    {
        // March 13, 2016: At 2:00 AM, clocks spring forward to 3:00 AM
        var tz = TZConvert.GetTimeZoneInfo("Pacific Standard Time");

        // Schedule job for 2:15 AM, which doesn't exist
        var trigger = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .WithSchedule(CronScheduleBuilder.CronSchedule("0 15 2 ? * * *") // 2:15 AM daily
                .InTimeZone(tz))
            .ForJob("job1", "group1")
            .Build();

        var midnight = new DateTimeOffset(2016, 3, 13, 0, 0, 0, TimeSpan.FromHours(-8));
        var fireTime = trigger.GetFireTimeAfter(midnight);

        Assert.That(fireTime, Is.Not.Null);

        var localTime = TimeZoneInfo.ConvertTime(fireTime.Value, tz);

        // Since 2:15 AM doesn't exist, it should advance to 3:15 AM PDT
        Assert.Multiple(() =>
        {
            Assert.That(localTime.Hour, Is.EqualTo(3));
            Assert.That(localTime.Minute, Is.EqualTo(15));
            Assert.That(localTime.Offset, Is.EqualTo(TimeSpan.FromHours(-7)), "Should be in PDT (UTC-7)");
        });
    }

    [Test]
    public void FallBack_CronEverySecond_MaintainsMonotonicProgression()
    {
        // October 29, 2023: 3:00 AM CEST -> 2:00 AM CET (clocks fall back)
        var tz = TZConvert.GetTimeZoneInfo("Central European Standard Time");

        // Start at 2:59:58 AM CEST (just before the transition)
        var startTime = new DateTimeOffset(2023, 10, 29, 0, 59, 58, TimeSpan.Zero); // UTC

        var trigger = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .WithSchedule(CronScheduleBuilder.CronSchedule("* * * ? * * *") // Every second
                .InTimeZone(tz))
            .StartAt(startTime)
            .ForJob("job1", "group1")
            .Build();

        var nextFireTimes = TriggerUtils.ComputeFireTimes((IOperableTrigger)trigger, null, 10);

        Assert.Multiple(() =>
        {
            // Verify strict monotonic increase in UTC
            for (int i = 1; i < nextFireTimes.Count; i++)
            {
                var diff = (nextFireTimes[i] - nextFireTimes[i - 1]).TotalSeconds;
                Assert.That(diff, Is.EqualTo(1.0).Within(0.001),
                    $"Each fire should be 1 second after previous: [{i - 1}]={nextFireTimes[i - 1]:HH:mm:ss} -> [{i}]={nextFireTimes[i]:HH:mm:ss}");
            }

            // Verify no time goes backwards
            for (int i = 1; i < nextFireTimes.Count; i++)
            {
                Assert.That(nextFireTimes[i].UtcDateTime, Is.GreaterThan(nextFireTimes[i - 1].UtcDateTime));
            }
        });
    }
}