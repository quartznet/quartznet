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
using System.Collections.Generic;
using System.Linq;

using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit;

/// <summary>
/// Pins that <see cref="SimpleTriggerImpl" /> is daylight saving time agnostic.
/// </summary>
/// <remarks>
/// The trigger has no time zone at all: its fire times are <c>startTimeUtc + n * repeatInterval</c>
/// in ticks. That means an "hourly" simple trigger repeats every hour of real elapsed time, so a
/// local day that gains or loses an hour to a DST transition simply contains one more or one fewer
/// fire. This is deliberately different from <see cref="DailyTimeIntervalTriggerImpl" />, which
/// re-anchors its window to the local wall clock every day.
/// </remarks>
public class SimpleTriggerDstTests
{
    /// <summary>
    /// A 25 hour local day holds 25 hourly fires and a 23 hour local day holds 23, while the
    /// spacing between consecutive fires stays exactly one hour of real time throughout.
    /// </summary>
    [Test]
    [Category("windowstimezoneid")]
    public void HourlySimpleTrigger_FallBackLocalDayHas25Fires_SpringDay23()
    {
        TimeZoneInfo timeZone = TestTimeZones.Eastern;
        TestTimeZones.AssumeAmbiguousLocalTime(timeZone, new DateTime(2024, 11, 3, 1, 30, 0));
        TestTimeZones.AssumeInvalidLocalTime(timeZone, new DateTime(2024, 3, 10, 2, 30, 0));

        // Nov 1 04:00 UTC is Nov 1 00:00 EDT, so every fire lands on a whole local hour
        List<DateTimeOffset> acrossFallBack = WalkHourly(
            new DateTimeOffset(2024, 11, 1, 4, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 11, 5, 5, 0, 0, TimeSpan.Zero));

        AssertExactlyOneHourApart(acrossFallBack);
        acrossFallBack.Count(t => TimeZoneInfo.ConvertTime(t, timeZone).Date == new DateTime(2024, 11, 3))
            .Should().Be(25, "the fall-back day is 25 hours of real time long");

        // Mar 8 05:00 UTC is Mar 8 00:00 EST
        List<DateTimeOffset> acrossSpringForward = WalkHourly(
            new DateTimeOffset(2024, 3, 8, 5, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2024, 3, 12, 4, 0, 0, TimeSpan.Zero));

        AssertExactlyOneHourApart(acrossSpringForward);
        acrossSpringForward.Count(t => TimeZoneInfo.ConvertTime(t, timeZone).Date == new DateTime(2024, 3, 10))
            .Should().Be(23, "the spring-forward day is 23 hours of real time long");
    }

    /// <summary>
    /// <see cref="SimpleTriggerImpl.FinalFireTimeUtc" /> is <c>startTimeUtc + repeatCount * interval</c>
    /// in ticks even when the run spans a transition, so it drifts against the wall clock on purpose.
    /// </summary>
    [Test]
    [Category("windowstimezoneid")]
    public void FinalFireTimeUtc_IsPureTickArithmetic_AcrossTransition()
    {
        TimeZoneInfo timeZone = TestTimeZones.Eastern;
        TestTimeZones.AssumeInvalidLocalTime(timeZone, new DateTime(2024, 3, 10, 2, 30, 0));

        DateTimeOffset startTimeUtc = new DateTimeOffset(2024, 3, 8, 5, 0, 0, TimeSpan.Zero);
        TimeSpan interval = TimeSpan.FromHours(1);
        int repeatCount = 100;

        SimpleTriggerImpl trigger = CreateTrigger(startTimeUtc, repeatCount, interval);

        trigger.FinalFireTimeUtc.Should().Be(startTimeUtc.AddTicks(repeatCount * interval.Ticks));
        trigger.FinalFireTimeUtc.Should().Be(new DateTimeOffset(2024, 3, 12, 9, 0, 0, TimeSpan.Zero));

        // 100 hours of real time after local Mar 8 00:00 is local Mar 12 05:00, not 04:00: the run
        // crossed the spring-forward gap and the trigger did not compensate for it
        TimeZoneInfo.ConvertTime(trigger.FinalFireTimeUtc!.Value, timeZone).DateTime
            .Should().Be(new DateTime(2024, 3, 12, 5, 0, 0));

        trigger.GetFireTimeAfter(trigger.FinalFireTimeUtc).Should().BeNull("the final fire time is the last one");
    }

    private static List<DateTimeOffset> WalkHourly(DateTimeOffset startTimeUtc, DateTimeOffset untilExclusive)
    {
        SimpleTriggerImpl trigger = CreateTrigger(startTimeUtc, SimpleTriggerImpl.RepeatIndefinitely, TimeSpan.FromHours(1));

        // GetFireTimeAfter(startTimeUtc) already skips to the second fire, so start one tick earlier
        return TestTimeZones.Walk(after => trigger.GetFireTimeAfter(after), startTimeUtc.AddTicks(-1), untilExclusive);
    }

    private static SimpleTriggerImpl CreateTrigger(DateTimeOffset startTimeUtc, int repeatCount, TimeSpan repeatInterval)
    {
        return new SimpleTriggerImpl("dstTrigger", "dstGroup", "dstJob", "dstJobGroup", startTimeUtc, null, repeatCount, repeatInterval);
    }

    private static void AssertExactlyOneHourApart(List<DateTimeOffset> fireTimes)
    {
        fireTimes.Should().NotBeEmpty();

        for (int i = 1; i < fireTimes.Count; i++)
        {
            (fireTimes[i] - fireTimes[i - 1]).Should().Be(TimeSpan.FromHours(1), $"fire {i} must follow fire {i - 1} by exactly one hour of real time");
        }
    }
}
