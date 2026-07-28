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
using Quartz.Spi;

namespace Quartz.Tests.Unit.Impl.Triggers;

/// <summary>
/// Daylight saving time coverage for <see cref="DailyTimeIntervalTriggerImpl" />, pinning the
/// behaviour that <see cref="DailyTimeIntervalTriggerImplTest" /> only covers for spring-forward.
/// </summary>
/// <remarks>
/// The trigger resolves each day's window start and end from wall-clock times (preferring the
/// daylight occurrence for an ambiguous local time and the pre-transition offset for one that does
/// not exist), but steps through the window by adding the interval in UTC ticks. Every expectation
/// below follows from that: a day yields <c>floor(windowLengthInSeconds / intervalInSeconds) + 1</c>
/// fires, where the window length is measured between the two resolved instants, not in wall-clock
/// hours.
/// </remarks>
public class DailyTimeIntervalTriggerDstTests
{
    /// <summary>
    /// Fall-back mirror of <c>DailyTimeIntervalTriggerImplTest.TestSpringForwardFireTimesAreStrictlyIncreasing</c>.
    /// </summary>
    /// <remarks>
    /// On 2018-10-28 in Central European time the window runs from local 00:00 +02:00
    /// (2018-10-27 22:00 UTC) to local 23:59:59 +01:00 (2018-10-28 22:59:59 UTC), which is
    /// 89999 elapsed seconds - a 25 hour day. Every expected count is
    /// <c>floor(89999 / interval) + 1</c>, so a sub-hour interval fires through both passes of the
    /// ambiguous 02:00-02:59 hour and the day yields one interval's worth of extra fires.
    /// The 24 hour row is the exception: it is the only interval that trips the
    /// <c>RepeatIntervalSpan &gt;= 1 day</c> fall-back correction, which pushes the second fire of the
    /// day (local 23:00, exactly 24 UTC hours after the window start) onto the next local date.
    /// </remarks>
    [Test]
    [Category("windowstimezoneid")]
    [TestCase(IntervalUnit.Second, 900, 100)]
    [TestCase(IntervalUnit.Minute, 1, 1500)]
    [TestCase(IntervalUnit.Minute, 5, 300)]
    [TestCase(IntervalUnit.Minute, 15, 100)]
    [TestCase(IntervalUnit.Minute, 30, 50)]
    [TestCase(IntervalUnit.Minute, 45, 34)]
    [TestCase(IntervalUnit.Hour, 1, 25)]
    [TestCase(IntervalUnit.Hour, 2, 13)]
    [TestCase(IntervalUnit.Hour, 4, 7)]
    [TestCase(IntervalUnit.Hour, 24, 1)]
    public void FallBackFireTimesAreStrictlyIncreasing(IntervalUnit unit, int interval, int expectedFireCountOnFallBackDay)
    {
        TimeZoneInfo timeZone = TestTimeZones.CentralEuropean;
        TestTimeZones.AssumeAmbiguousLocalTime(timeZone, new DateTime(2018, 10, 28, 2, 30, 0));

        IOperableTrigger trigger = (IOperableTrigger) DailyTimeIntervalScheduleBuilder.Create()
            .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(0, 0))
            .EndingDailyAt(TimeOfDay.HourMinuteAndSecondOfDay(23, 59, 59))
            .OnEveryDay()
            .WithInterval(interval, unit)
            .InTimeZone(timeZone)
            .Build();

        // Oct 27 22:00 UTC is Oct 28 00:00 CEST, the start of the fall-back day
        trigger.StartTimeUtc = new DateTimeOffset(2018, 10, 27, 22, 0, 0, TimeSpan.Zero);
        trigger.ComputeFirstFireTimeUtc(null);

        // walk the fall-back day and the ordinary day after it; Walk fails the test if the trigger
        // ever moves backwards, repeats itself or runs away
        List<DateTimeOffset> local = TestTimeZones
            .Walk(after => trigger.GetFireTimeAfter(after),
                trigger.StartTimeUtc.AddSeconds(-1),
                new DateTimeOffset(2018, 10, 29, 23, 0, 0, TimeSpan.Zero))
            .Select(t => TimeZoneInfo.ConvertTime(t, timeZone))
            .ToList();

        local[0].TimeOfDay.Should().Be(TimeSpan.Zero, "the first fire of the day is startTimeOfDay");
        local.Count(t => t.Date == new DateTime(2018, 10, 28)).Should().Be(expectedFireCountOnFallBackDay);
        local.Select(t => t.Date).Distinct().Should().HaveCountGreaterThan(1, "the trigger must advance past the fall-back day");
    }

    /// <summary>
    /// <see cref="IDailyTimeIntervalTrigger.RepeatCount" /> caps the fires <em>per local day</em>
    /// ("setting to N means fire N+1 times per day"), and the cap counts interval steps rather than
    /// wall-clock time.
    /// </summary>
    /// <remarks>
    /// So the 25 hour fall-back day gets exactly as many fires as an ordinary day - the extra hour
    /// buys nothing. It only shifts where the run ends on the clock: 30 steps of 30 minutes is
    /// 15 UTC hours from the window start, which is local 15:00 on an ordinary day but local 14:00
    /// on the fall-back day, because one of those 15 hours was spent repeating 02:00-02:59.
    /// </remarks>
    [Test]
    [Category("windowstimezoneid")]
    public void RepeatCountLimitsFires_AcrossFallBackDay()
    {
        TimeZoneInfo timeZone = TestTimeZones.CentralEuropean;
        TestTimeZones.AssumeAmbiguousLocalTime(timeZone, new DateTime(2018, 10, 28, 2, 30, 0));

        IOperableTrigger trigger = (IOperableTrigger) DailyTimeIntervalScheduleBuilder.Create()
            .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(0, 0))
            .EndingDailyAt(TimeOfDay.HourMinuteAndSecondOfDay(23, 59, 59))
            .OnEveryDay()
            .WithIntervalInMinutes(30)
            .WithRepeatCount(30)
            .InTimeZone(timeZone)
            .Build();

        // Oct 26 22:00 UTC is Oct 27 00:00 CEST, the start of the day before the fall-back day
        trigger.StartTimeUtc = new DateTimeOffset(2018, 10, 26, 22, 0, 0, TimeSpan.Zero);
        trigger.ComputeFirstFireTimeUtc(null);

        List<DateTimeOffset> local = TestTimeZones
            .Walk(after => trigger.GetFireTimeAfter(after),
                trigger.StartTimeUtc.AddSeconds(-1),
                new DateTimeOffset(2018, 10, 29, 23, 0, 0, TimeSpan.Zero))
            .Select(t => TimeZoneInfo.ConvertTime(t, timeZone))
            .ToList();

        List<DateTimeOffset> dayBefore = local.Where(t => t.Date == new DateTime(2018, 10, 27)).ToList();
        List<DateTimeOffset> fallBackDay = local.Where(t => t.Date == new DateTime(2018, 10, 28)).ToList();
        List<DateTimeOffset> dayAfter = local.Where(t => t.Date == new DateTime(2018, 10, 29)).ToList();

        dayBefore.Should().HaveCount(31, "repeatCount 30 means 31 fires per day");
        fallBackDay.Should().HaveCount(31, "the 25 hour day gets no extra fires, the cap counts interval steps");
        dayAfter.Should().HaveCount(31);

        dayBefore[^1].Should().Be(TestTimeZones.Local("2018-10-27 15:00 +02:00"));
        fallBackDay[^1].Should().Be(TestTimeZones.Local("2018-10-28 14:00 +01:00"), "an hour of the run was spent repeating 02:00-02:59");
        dayAfter[^1].Should().Be(TestTimeZones.Local("2018-10-29 15:00 +01:00"));

        fallBackDay.Where(t => t.Hour == 2).Should().HaveCount(4, "the ambiguous hour occurs twice and holds two 30 minute slots each time");

        // FinalFireTimeUtc is derived from EndTimeUtc only; a per-day repeat count never ends the trigger
        trigger.FinalFireTimeUtc.Should().BeNull("without an end time the trigger repeats its daily run forever");
    }

    /// <summary>
    /// An <c>endTimeOfDay</c> that lands inside the spring-forward gap still bounds the day, but it
    /// bounds it as an <em>instant</em>, not as a wall-clock time.
    /// </summary>
    /// <remarks>
    /// On 2018-03-25 the local times 02:00-02:59 do not exist. The gap end resolves with the
    /// pre-transition offset, so local 02:30 becomes 02:30 +01:00 = 01:30 UTC. The third hourly slot
    /// falls on 01:00 UTC, which is still before that bound, and 01:00 UTC is the transition instant
    /// itself - so the trigger fires at local 03:00 +02:00, half an hour past the wall-clock
    /// endTimeOfDay it was given. That is the only slot that behaves that way: the fourth is
    /// 02:00 UTC, past the bound, and the day ends cleanly. There is no loop and no wedge.
    /// </remarks>
    [Test]
    [Category("windowstimezoneid")]
    public void EndTimeOfDayInsideSpringForwardGap_DayEndsCleanly()
    {
        TimeZoneInfo timeZone = TestTimeZones.CentralEuropean;
        TestTimeZones.AssumeInvalidLocalTime(timeZone, new DateTime(2018, 3, 25, 2, 0, 0));
        TestTimeZones.AssumeInvalidLocalTime(timeZone, new DateTime(2018, 3, 25, 2, 30, 0));

        IOperableTrigger trigger = (IOperableTrigger) DailyTimeIntervalScheduleBuilder.Create()
            .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(0, 0))
            .EndingDailyAt(TimeOfDay.HourAndMinuteOfDay(2, 30))
            .OnEveryDay()
            .WithIntervalInHours(1)
            .InTimeZone(timeZone)
            .Build();

        // Mar 24 23:00 UTC is Mar 25 00:00 CET, the start of the spring-forward day
        trigger.StartTimeUtc = new DateTimeOffset(2018, 3, 24, 23, 0, 0, TimeSpan.Zero);
        trigger.ComputeFirstFireTimeUtc(null);

        List<DateTimeOffset> times = TestTimeZones.Walk(
            after => trigger.GetFireTimeAfter(after),
            trigger.StartTimeUtc.AddSeconds(-1),
            new DateTimeOffset(2018, 3, 26, 12, 0, 0, TimeSpan.Zero));

        times.Should().Equal(
        [
            TestTimeZones.Local("2018-03-25 00:00 +01:00"),
            TestTimeZones.Local("2018-03-25 01:00 +01:00"),
            // 02:00 does not exist, and 03:00 +02:00 is the same instant the skipped 02:00 +01:00
            // slot would have been - still inside the resolved end of day, so it fires
            TestTimeZones.Local("2018-03-25 03:00 +02:00"),
            // the ordinary day that follows honours endTimeOfDay as written
            TestTimeZones.Local("2018-03-26 00:00 +02:00"),
            TestTimeZones.Local("2018-03-26 01:00 +02:00"),
            TestTimeZones.Local("2018-03-26 02:00 +02:00")
        ]);
    }

    /// <summary>
    /// Chile moves the clock at midnight, so the transition sits on the edge of the local day rather
    /// than in the middle of it, and the two directions come out asymmetric.
    /// </summary>
    /// <remarks>
    /// Spring forward (2019-09-08): 00:00 does not exist, the day starts at 01:00 -03:00 and is
    /// 23 hours long, so a 30 minute trigger fires 46 times and never at local hour 0.
    /// <para>
    /// Fall back (Saturday 2019-04-06): the repeated hour is 23:00-23:59, i.e. the last hour of the
    /// local day. Unlike the Central European case - where the repeated hour is in the middle of the
    /// day and the day really does yield 25 hours of fires - <c>endTimeOfDay</c> 23:59:59 is itself
    /// ambiguous here, and it resolves to the <em>daylight</em> (first) occurrence. The window
    /// therefore closes at 2019-04-07 02:59:59 UTC, one hour before the local day ends: 48 fires,
    /// and the second pass of 23:00-23:59 is skipped entirely even though those instants still map
    /// to local date 2019-04-06.
    /// </para>
    /// </remarks>
    [Test]
    [Category("windowstimezoneid")]
    public void MidnightGapZone_SubHourInterval_Santiago()
    {
        TimeZoneInfo timeZone = TestTimeZones.Santiago;
        TestTimeZones.AssumeInvalidLocalTime(timeZone, new DateTime(2019, 9, 8, 0, 0, 0));
        TestTimeZones.AssumeAmbiguousLocalTime(timeZone, new DateTime(2019, 4, 6, 23, 30, 0));

        // Sep 8 04:00 UTC is Sep 8 01:00 -03:00, the first instant of the spring-forward day
        List<DateTimeOffset> springForwardDay = LocalFiresOnDate(
            CreateHalfHourlyTrigger(timeZone, new DateTimeOffset(2019, 9, 8, 4, 0, 0, TimeSpan.Zero)),
            timeZone,
            new DateTime(2019, 9, 8),
            new DateTimeOffset(2019, 9, 9, 12, 0, 0, TimeSpan.Zero));

        springForwardDay.Should().HaveCount(46, "the local day is 23 hours long, so 22:59:59 / 30 min + 1");
        springForwardDay.Should().NotContain(t => t.Hour == 0, "00:00-00:59 does not exist on the spring-forward day");
        springForwardDay[0].Should().Be(TestTimeZones.Local("2019-09-08 01:00 -03:00"));
        springForwardDay[^1].Should().Be(TestTimeZones.Local("2019-09-08 23:30 -03:00"));

        // Apr 6 03:00 UTC is Apr 6 00:00 -03:00, the start of the fall-back Saturday
        List<DateTimeOffset> fallBackDay = LocalFiresOnDate(
            CreateHalfHourlyTrigger(timeZone, new DateTimeOffset(2019, 4, 6, 3, 0, 0, TimeSpan.Zero)),
            timeZone,
            new DateTime(2019, 4, 6),
            new DateTimeOffset(2019, 4, 7, 12, 0, 0, TimeSpan.Zero));

        fallBackDay.Should().HaveCount(48, "endTimeOfDay 23:59:59 is ambiguous and resolves to the first occurrence, closing the window after 24 hours");
        fallBackDay[0].Should().Be(TestTimeZones.Local("2019-04-06 00:00 -03:00"));
        fallBackDay[^1].Should().Be(TestTimeZones.Local("2019-04-06 23:30 -03:00"));
        fallBackDay.Where(t => t.Hour == 23).Should().HaveCount(2, "only the first pass of the repeated last hour is inside the window");
        fallBackDay.Should().OnlyContain(t => t.Offset == TimeSpan.FromHours(-3), "the standard-time pass of the day is never reached");
    }

    /// <summary>
    /// Regression test for the offset resolution in <c>AdvanceToNextDayOfWeekIfNecessary</c>: when
    /// the day-of-week walk crosses a spring-forward transition, the advanced day's start-of-day
    /// must resolve through the wall-clock policy before it is compared against
    /// <see cref="ITrigger.EndTimeUtc" />.
    /// </summary>
    /// <remarks>
    /// On Sunday 2018-03-25 the local start-of-day 02:30 does not exist; the wall-clock policy
    /// resolves it to 02:30 +01:00 = 01:30 UTC. Resolving the offset from the walked instant instead
    /// yields 02:30 +02:00 = 00:30 UTC — one transition delta too early — and an EndTimeUtc between
    /// the two instants lets the trigger fire once past its configured end.
    /// </remarks>
    [Test]
    [Category("windowstimezoneid")]
    public void AdvanceAcrossSpringForwardGap_RespectsEndTimeUtc()
    {
        TimeZoneInfo timeZone = TestTimeZones.CentralEuropean;
        TestTimeZones.AssumeInvalidLocalTime(timeZone, new DateTime(2018, 3, 25, 2, 30, 0));

        IOperableTrigger trigger = (IOperableTrigger) DailyTimeIntervalScheduleBuilder.Create()
            .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(2, 30))
            .EndingDailyAt(TimeOfDay.HourAndMinuteOfDay(3, 30))
            .OnDaysOfTheWeek(DayOfWeek.Saturday, DayOfWeek.Sunday)
            .WithIntervalInHours(1)
            .InTimeZone(timeZone)
            .Build();

        // Friday 2018-03-23 22:00 UTC, before the Saturday window opens
        trigger.StartTimeUtc = new DateTimeOffset(2018, 3, 23, 22, 0, 0, TimeSpan.Zero);
        // between the mis-resolved Sunday window start (00:30 UTC) and the correctly resolved one (01:30 UTC)
        trigger.EndTimeUtc = new DateTimeOffset(2018, 3, 25, 1, 0, 0, TimeSpan.Zero);
        trigger.ComputeFirstFireTimeUtc(null);

        DateTimeOffset? saturdayFirst = trigger.GetFireTimeAfter(trigger.StartTimeUtc);
        saturdayFirst.Should().Be(TestTimeZones.Local("2018-03-24 02:30 +01:00"));

        DateTimeOffset? saturdayLast = trigger.GetFireTimeAfter(saturdayFirst);
        saturdayLast.Should().Be(TestTimeZones.Local("2018-03-24 03:30 +01:00"));

        DateTimeOffset? afterSaturday = trigger.GetFireTimeAfter(saturdayLast);
        afterSaturday.Should().BeNull("Sunday's window start resolves to 01:30 UTC, which is past EndTimeUtc 01:00 UTC");
    }

    /// <summary>
    /// A trigger that only runs on Sundays advances six days at a time through
    /// <c>AdvanceToNextDayOfWeekIfNecessary</c>, so every transition Sunday exercises the day-of-week
    /// walk landing directly on a gap or an ambiguous window start.
    /// </summary>
    [Test]
    [Category("windowstimezoneid")]
    [TestCase("CentralEuropean", "2018-03-25 02:30", true, 2, 30, "2018-03-25 03:30 +02:00", "2018-04-01 02:30 +02:00")]
    [TestCase("Eastern", "2024-03-10 02:30", true, 2, 30, "2024-03-10 03:30 -04:00", "2024-03-17 02:30 -04:00")]
    [TestCase("Eastern", "2024-11-03 01:30", false, 1, 30, "2024-11-03 01:30 -04:00", "2024-11-10 01:30 -05:00")]
    [TestCase("Santiago", "2019-09-08 00:00", true, 0, 0, "2019-09-08 01:00 -03:00", "2019-09-15 00:00 -03:00")]
    public void SundayOnlyTrigger_TransitionSunday_FiresAtResolvedTime(
        string zoneKey,
        string premiseLocal,
        bool premiseIsGap,
        int startHour,
        int startMinute,
        string expectedTransitionSundayFire,
        string expectedNextSundayFire)
    {
        TimeZoneInfo timeZone = ResolveZone(zoneKey);
        DateTime premise = DateTime.Parse(premiseLocal, System.Globalization.CultureInfo.InvariantCulture);
        if (premiseIsGap)
        {
            TestTimeZones.AssumeInvalidLocalTime(timeZone, premise);
        }
        else
        {
            TestTimeZones.AssumeAmbiguousLocalTime(timeZone, premise);
        }

        IOperableTrigger trigger = (IOperableTrigger) DailyTimeIntervalScheduleBuilder.Create()
            .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(startHour, startMinute))
            .EndingDailyAt(TimeOfDay.HourAndMinuteOfDay(startHour + 2, startMinute))
            .OnDaysOfTheWeek(DayOfWeek.Sunday)
            .WithIntervalInHours(1)
            .InTimeZone(timeZone)
            .Build();

        DateTime transitionSunday = premise.Date;
        trigger.StartTimeUtc = new DateTimeOffset(transitionSunday.AddDays(-3), TimeSpan.Zero).AddHours(12);
        trigger.ComputeFirstFireTimeUtc(null);

        List<DateTimeOffset> local = TestTimeZones
            .Walk(after => trigger.GetFireTimeAfter(after),
                trigger.StartTimeUtc,
                new DateTimeOffset(transitionSunday.AddDays(8), TimeSpan.Zero).AddHours(12))
            .Select(t => TimeZoneInfo.ConvertTime(t, timeZone))
            .ToList();

        local[0].Should().Be(TestTimeZones.Local(expectedTransitionSundayFire));
        local.Should().OnlyContain(t => t.DayOfWeek == DayOfWeek.Sunday);

        DateTimeOffset firstOnNextSunday = local.First(t => t.Date == transitionSunday.AddDays(7));
        firstOnNextSunday.Should().Be(TestTimeZones.Local(expectedNextSundayFire));
    }

    private static TimeZoneInfo ResolveZone(string zoneKey)
    {
        switch (zoneKey)
        {
            case "Eastern":
                return TestTimeZones.Eastern;
            case "CentralEuropean":
                return TestTimeZones.CentralEuropean;
            case "Santiago":
                return TestTimeZones.Santiago;
            default:
                throw new ArgumentOutOfRangeException(nameof(zoneKey), zoneKey, "unknown test zone");
        }
    }

    private static IOperableTrigger CreateHalfHourlyTrigger(TimeZoneInfo timeZone, DateTimeOffset startTimeUtc)
    {
        IOperableTrigger trigger = (IOperableTrigger) DailyTimeIntervalScheduleBuilder.Create()
            .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(0, 0))
            .EndingDailyAt(TimeOfDay.HourMinuteAndSecondOfDay(23, 59, 59))
            .OnEveryDay()
            .WithIntervalInMinutes(30)
            .InTimeZone(timeZone)
            .Build();

        trigger.StartTimeUtc = startTimeUtc;
        trigger.ComputeFirstFireTimeUtc(null);

        return trigger;
    }

    private static List<DateTimeOffset> LocalFiresOnDate(
        IOperableTrigger trigger,
        TimeZoneInfo timeZone,
        DateTime localDate,
        DateTimeOffset untilExclusive)
    {
        return TestTimeZones
            .Walk(after => trigger.GetFireTimeAfter(after), trigger.StartTimeUtc.AddSeconds(-1), untilExclusive)
            .Select(t => TimeZoneInfo.ConvertTime(t, timeZone))
            .Where(t => t.Date == localDate)
            .ToList();
    }
}
