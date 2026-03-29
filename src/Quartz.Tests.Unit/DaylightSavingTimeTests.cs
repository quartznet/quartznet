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

using TimeZoneConverter;

namespace Quartz.Tests.Unit;

[NonParallelizable]
public class DaylightSavingTimeTest
{
    private sealed class TestTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset utcNow;

        public TestTimeProvider(DateTimeOffset utcNow)
        {
            this.utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    [Test]
    public void ShouldHandleDstSpringForwardTransition()
    {
        TimeZoneInfo tz = TZConvert.GetTimeZoneInfo("Pacific Standard Time");

        ITrigger trigger = TriggerBuilder.Create(new TestTimeProvider(new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero)))
            .WithIdentity("trigger1", "group1")
            .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(2, 30).InTimeZone(tz))
            .ForJob("job1", "group1")
            .Build();

        DateTimeOffset midnight = new DateTimeOffset(2016, 3, 13, 0, 0, 0, TimeSpan.FromHours(-8));
        DateTimeOffset? fireTime = trigger.GetFireTimeAfter(midnight);

        // It should fire at the equivalent valid local time.  2:30-8 does not exist, so it should run at 3:30-7.
        DateTimeOffset expectedTime = new DateTimeOffset(2016, 3, 13, 3, 30, 0, TimeSpan.FromHours(-7));

        // We should definitely have a value
        Assert.That(fireTime, Is.Not.Null);

        // fireTime always is in UTC, but DateTimeOffset comparison normalized to UTC anyway.
        // Conversion here is for clarity of interpreting errors if the test fails.
        DateTimeOffset convertedFireTime = TimeZoneInfo.ConvertTime(fireTime.Value, tz);
        Assert.That(convertedFireTime, Is.EqualTo(expectedTime));
    }

    [Test]
    public void ShouldHandleDstFallBackTransition()
    {
        TimeZoneInfo tz = TZConvert.GetTimeZoneInfo("Pacific Standard Time");

        ITrigger trigger = TriggerBuilder.Create(new TestTimeProvider(new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero)))
            .WithIdentity("trigger1", "group1")
            .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(1, 30).InTimeZone(tz))
            .ForJob("job1", "group1")
            .Build();

        DateTimeOffset midnight = new DateTimeOffset(2016, 11, 6, 0, 0, 0, TimeSpan.FromHours(-7));
        DateTimeOffset? fireTime = trigger.GetFireTimeAfter(midnight);

        // It should fire at the first instance, which is 1:30-7 - the DAYLIGHT time, not the standard time.
        DateTimeOffset expectedTime = new DateTimeOffset(2016, 11, 6, 1, 30, 0, TimeSpan.FromHours(-7));

        // We should definitely have a value
        Assert.That(fireTime, Is.Not.Null);

        // fireTime always is in UTC, but DateTimeOffset comparison normalized to UTC anyway.
        // Conversion here is for clarity of interpreting errors if the test fails.
        DateTimeOffset convertedFireTime = TimeZoneInfo.ConvertTime(fireTime.Value, tz);
        Assert.That(convertedFireTime, Is.EqualTo(expectedTime));
    }

    [Test]
    public void ShouldHandleDstFallBackTransition_AndNotRunTwiceOnTheSameDay()
    {
        TimeZoneInfo tz = TZConvert.GetTimeZoneInfo("Pacific Standard Time");

        ITrigger trigger = TriggerBuilder.Create(new TestTimeProvider(new DateTimeOffset(2016, 1, 1, 0, 0, 0, TimeSpan.Zero)))
            .WithIdentity("trigger1", "group1")
            .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(1, 30).InTimeZone(tz))
            .ForJob("job1", "group1")
            .Build();

        DateTimeOffset firstRun = new DateTimeOffset(2016, 11, 6, 1, 30, 0, TimeSpan.FromHours(-7));
        DateTimeOffset? fireTime = trigger.GetFireTimeAfter(firstRun);

        // It should not fire again at 1:30-8 on the same day, but should instead fire at 1:30-8 the following day.
        DateTimeOffset expectedTime = new DateTimeOffset(2016, 11, 7, 1, 30, 0, TimeSpan.FromHours(-8));

        // We should definitely have a value
        Assert.That(fireTime, Is.Not.Null);

        // fireTime always is in UTC, but DateTimeOffset comparison normalized to UTC anyway.
        // Conversion here is for clarity of interpreting errors if the test fails.
        DateTimeOffset convertedFireTime = TimeZoneInfo.ConvertTime(fireTime.Value, tz);
        Assert.That(convertedFireTime, Is.EqualTo(expectedTime));
    }

    [Test]
    public void ShouldNotFireInfinitelyAfterDstFallBackTransition()
    {
        // Amsterdam: CEST (UTC+2) → CET (UTC+1) on October 29, 2023 at 3:00 AM CEST (= 1:00 AM UTC).
        // During the first hour after the fall-back, every-minute cron triggers would previously
        // compute a NextFireTimeUtc that was 1 hour too early (in the past), causing infinite refires.
        TimeZoneInfo tz = TZConvert.GetTimeZoneInfo("W. Europe Standard Time");

        ITrigger trigger = TriggerBuilder.Create(new TestTimeProvider(new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero)))
            .WithIdentity("trigger1", "group1")
            .WithSchedule(CronScheduleBuilder.CronSchedule("0 * * * * ?").InTimeZone(tz))
            .ForJob("job1", "group1")
            .Build();

        // Simulates a fire at 01:00:49 UTC (= 02:00:49 CET, which is after the DST fall-back).
        DateTimeOffset fireTime = new DateTimeOffset(2023, 10, 29, 1, 0, 49, TimeSpan.Zero);
        DateTimeOffset? nextFireTime = trigger.GetFireTimeAfter(fireTime);

        // Next fire should be 01:01:00 UTC (= 02:01:00 CET), not 00:01:00 UTC (= 02:01:00 CEST).
        DateTimeOffset expectedUtc = new DateTimeOffset(2023, 10, 29, 1, 1, 0, TimeSpan.Zero);

        Assert.That(nextFireTime, Is.Not.Null);
        Assert.That(nextFireTime!.Value.ToUniversalTime(), Is.EqualTo(expectedUtc));
    }

    /// <summary>
    /// Regression test for GitHub issue #2156.
    /// IsSatisfiedBy should return true for times during the DST fall-back transition hour
    /// when the cron expression matches and the CronExpression TimeZone covers that period.
    /// </summary>
    [Test]
    public void IsSatisfiedBy_ShouldReturnTrue_DuringDstFallBack_Issue2156()
    {
        // Amsterdam: CEST (UTC+2) → CET (UTC+1) on October 29, 2023 at 3:00 AM CEST (= 1:00 AM UTC).
        // The local hour 02:00-02:59 occurs twice: first in CEST, then in CET.
        TimeZoneInfo tz = TZConvert.GetTimeZoneInfo("W. Europe Standard Time");
        var cron = new CronExpression("* * * ? 10 * *") { TimeZone = tz };

        // Exact moment of fall-back: 01:00:00 UTC = 02:00:00 CET (second occurrence)
        var atFallBack = new DateTimeOffset(2023, 10, 29, 1, 0, 0, TimeSpan.Zero);
        Assert.That(cron.IsSatisfiedBy(atFallBack), Is.True,
            "Should match at the exact fall-back moment (still October)");

        // One minute into the second occurrence: 01:01:00 UTC = 02:01:00 CET
        var afterFallBack = new DateTimeOffset(2023, 10, 29, 1, 1, 0, TimeSpan.Zero);
        Assert.That(cron.IsSatisfiedBy(afterFallBack), Is.True,
            "Should match one minute after fall-back (still October)");

        // One minute before fall-back: 00:59:00 UTC = 02:59:00 CEST (first occurrence)
        var beforeFallBack = new DateTimeOffset(2023, 10, 29, 0, 59, 0, TimeSpan.Zero);
        Assert.That(cron.IsSatisfiedBy(beforeFallBack), Is.True,
            "Should match one minute before fall-back (still October)");
    }

    /// <summary>
    /// Regression test for GitHub issue #2156 — original reproduction case.
    /// When CronExpression uses UTC timezone, IsSatisfiedBy must return correct results
    /// during DST transition periods.
    /// </summary>
    [Test]
    public void IsSatisfiedBy_ShouldReturnTrue_DuringDstFallBack_WithUtcTimeZone_Issue2156()
    {
        // Use UTC timezone to avoid any DST confusion — the cron expression should match
        // every second in October regardless of local system timezone settings.
        var cron = new CronExpression("* * * ? 10 * *") { TimeZone = TimeZoneInfo.Utc };

        // October 29, 2023 01:00:00 UTC — in UTC this is unambiguously October
        var atFallBack = new DateTimeOffset(2023, 10, 29, 1, 0, 0, TimeSpan.Zero);
        Assert.That(cron.IsSatisfiedBy(atFallBack), Is.True, "01:00 UTC on Oct 29 is still October in UTC");

        // Last second of October in UTC
        var lastSecond = new DateTimeOffset(2023, 10, 31, 23, 59, 59, TimeSpan.Zero);
        Assert.That(cron.IsSatisfiedBy(lastSecond), Is.True, "Last second of October should match");

        // First second of November — should NOT match
        var firstNov = new DateTimeOffset(2023, 11, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.That(cron.IsSatisfiedBy(firstNov), Is.False, "November should not match October cron");
    }

    /// <summary>
    /// Verify GetTimeAfter produces strictly increasing results across the entire
    /// DST fall-back transition for a high-frequency (every-minute) cron trigger.
    /// </summary>
    [Test]
    public void GetTimeAfter_ShouldProduceStrictlyIncreasingTimes_AcrossDstFallBack()
    {
        TimeZoneInfo tz = TZConvert.GetTimeZoneInfo("W. Europe Standard Time");
        var cron = new CronExpression("0 * * ? * * *") { TimeZone = tz };

        // Start 30 minutes before the fall-back: 00:30:00 UTC = 02:30:00 CEST
        DateTimeOffset current = new DateTimeOffset(2023, 10, 29, 0, 30, 0, TimeSpan.Zero);

        // Walk through 90 minutes — crosses the full fall-back transition
        for (int i = 0; i < 90; i++)
        {
            var next = cron.GetTimeAfter(current);
            Assert.That(next, Is.Not.Null, $"GetTimeAfter returned null at iteration {i}, current={current}");
            Assert.That(next!.Value, Is.GreaterThan(current),
                $"Iteration {i}: time did not advance! current={current}, next={next.Value}");
            current = next.Value;
        }
    }

    /// <summary>
    /// Verify GetTimeAfter produces strictly increasing results across the entire
    /// DST spring-forward transition for a high-frequency (every-minute) cron trigger.
    /// </summary>
    [Test]
    public void GetTimeAfter_ShouldProduceStrictlyIncreasingTimes_AcrossDstSpringForward()
    {
        TimeZoneInfo tz = TZConvert.GetTimeZoneInfo("Central Standard Time");
        var cron = new CronExpression("0 * * ? * * *") { TimeZone = tz };

        // Start 30 minutes before spring-forward: 2024-03-10 07:30:00 UTC = 01:30:00 CST
        // Spring-forward: 02:00 CST → 03:00 CDT (08:00 UTC)
        DateTimeOffset current = new DateTimeOffset(2024, 3, 10, 7, 30, 0, TimeSpan.Zero);

        // Walk through 90 minutes — crosses the full spring-forward transition
        for (int i = 0; i < 90; i++)
        {
            var next = cron.GetTimeAfter(current);
            Assert.That(next, Is.Not.Null, $"GetTimeAfter returned null at iteration {i}, current={current}");
            Assert.That(next!.Value, Is.GreaterThan(current),
                $"Iteration {i}: time did not advance! current={current}, next={next.Value}");
            current = next.Value;
        }
    }

    /// <summary>
    /// Reproduction of exact scenario from GitHub issue #2156.
    /// IsSatisfiedBy iterating every minute through October should not produce false negatives
    /// during the DST fall-back transition hour.
    /// </summary>
    [Test]
    public void IsSatisfiedBy_EveryMinuteInOctober_ShouldNotReturnFalseDuringDstFallBack_Issue2156()
    {
        TimeZoneInfo tz = TZConvert.GetTimeZoneInfo("W. Europe Standard Time");
        var cron = new CronExpression("* * * ? 10 * *") { TimeZone = tz };

        // Walk through October 28-30 minute by minute across the CET fall-back
        // Fall-back: Oct 29, 2023 at 03:00 CEST → 02:00 CET (= 01:00 UTC)
        var start = new DateTimeOffset(2023, 10, 28, 22, 0, 0, TimeSpan.Zero); // Oct 29 00:00 CET
        var end = new DateTimeOffset(2023, 10, 29, 4, 0, 0, TimeSpan.Zero);    // Oct 29 05:00 CET

        var current = start;
        while (current < end)
        {
            Assert.That(cron.IsSatisfiedBy(current), Is.True,
                $"IsSatisfiedBy returned false during October at {current} (UTC)");
            current = current.AddMinutes(1);
        }
    }
}