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
using Quartz.Extensibility;

namespace Quartz.Tests.Unit;

/// <summary>
/// Misfire handling when the scheduler catches up while "now" sits on a daylight saving time
/// boundary: either at the instant a naive wall-clock computation would call a time that never
/// existed (spring forward), or during the hour that happens twice (fall back).
/// <para>
/// Every misfire instruction that reschedules reads the trigger's <see cref="TimeProvider"/> clock,
/// so each trigger is constructed with a fixed one. The tests pin the current behaviour - they are
/// a regression net, not a specification of what is ideal.
/// </para>
/// </summary>
public class TriggerDstMisfireTests
{
    private static readonly TimeZoneInfo Eastern = TestTimeZones.Eastern;

    /// <summary>
    /// Eastern springs forward on 2024-03-10: local 02:00 EST becomes 03:00 EDT at 07:00Z, so the
    /// wall clock 02:30 never happens on that date. This instant is the one that a naive
    /// "02:30 at the standard offset" computation would pick, and its real local time is 03:30 EDT.
    /// </summary>
    private static readonly DateTimeOffset NowInGapHour = new DateTimeOffset(2024, 3, 10, 7, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// Eastern falls back on 2024-11-03: local 02:00 EDT becomes 01:00 EST at 06:00Z, so the wall
    /// clock 01:30 happens twice - first at 05:30Z (daylight pass) and again at 06:30Z (standard
    /// pass). This is the second, standard-offset occurrence.
    /// </summary>
    private static readonly DateTimeOffset NowInAmbiguousHourStandardPass = new DateTimeOffset(2024, 11, 3, 6, 30, 0, TimeSpan.Zero);

    /// <summary>The first occurrence of the ambiguous 01:30 wall clock, still on daylight time.</summary>
    private static readonly DateTimeOffset NowInAmbiguousHourDaylightPass = new DateTimeOffset(2024, 11, 3, 5, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// States the transition premises of this fixture. A changed time zone database skips the tests
    /// instead of failing them.
    /// </summary>
    private static void AssumeEasternTransitions()
    {
        TestTimeZones.AssumeInvalidLocalTime(Eastern, new DateTime(2024, 3, 10, 2, 30, 0));
        TestTimeZones.AssumeAmbiguousLocalTime(Eastern, new DateTime(2024, 11, 3, 1, 30, 0));
    }

    // ---------------------------------------------------------------------------------------
    // Trigger factories. Every trigger starts well before the frozen "now", so the fire time that
    // ComputeFirstFireTimeUtc produces is badly past due by the time misfire handling runs.
    // ---------------------------------------------------------------------------------------

    private static CronTriggerImpl CreateCronTrigger(int misfireInstruction, DateTimeOffset frozenNow)
    {
        CronTriggerImpl trigger = new CronTriggerImpl(new FixedTimeProvider(frozenNow))
        {
            Key = new TriggerKey("test", "test"),
            CronExpressionString = "0 30 2 * * ?",
            TimeZone = Eastern,
            StartTimeUtc = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            MisfireInstructionCode = misfireInstruction
        };
        trigger.ComputeFirstFireTimeUtc(null);
        return trigger;
    }

    private static CalendarIntervalTriggerImpl CreateCalendarIntervalTrigger(int misfireInstruction, DateTimeOffset frozenNow)
    {
        CalendarIntervalTriggerImpl trigger = new CalendarIntervalTriggerImpl(new FixedTimeProvider(frozenNow))
        {
            Key = new TriggerKey("test", "test"),
            // 2024-01-15 02:30 EST, i.e. the same wall clock the Eastern transitions attack.
            StartTimeUtc = new DateTimeOffset(2024, 1, 15, 7, 30, 0, TimeSpan.Zero),
            RepeatInterval = 1,
            RepeatIntervalUnit = IntervalUnit.Day,
            TimeZone = Eastern,
            PreserveHourOfDayAcrossDaylightSavings = true,
            MisfireInstructionCode = misfireInstruction
        };
        trigger.ComputeFirstFireTimeUtc(null);
        return trigger;
    }

    private static DailyTimeIntervalTriggerImpl CreateDailyTimeIntervalTrigger(int misfireInstruction, DateTimeOffset frozenNow)
    {
        DailyTimeIntervalTriggerImpl trigger = new DailyTimeIntervalTriggerImpl(new FixedTimeProvider(frozenNow))
        {
            Key = new TriggerKey("test", "test"),
            StartTimeUtc = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            StartTimeOfDay = new TimeOnly(0, 0, 0),
            EndTimeOfDay = new TimeOnly(23, 59, 59),
            RepeatInterval = 15,
            RepeatIntervalUnit = IntervalUnit.Minute,
            TimeZone = Eastern,
            MisfireInstructionCode = misfireInstruction
        };
        trigger.ComputeFirstFireTimeUtc(null);
        return trigger;
    }

    private static SimpleTriggerImpl CreateSimpleTrigger(int misfireInstruction, DateTimeOffset frozenNow)
    {
        SimpleTriggerImpl trigger = new SimpleTriggerImpl(new FixedTimeProvider(frozenNow))
        {
            Key = new TriggerKey("test", "test"),
            StartTimeUtc = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            RepeatInterval = TimeSpan.FromHours(1),
            RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
            MisfireInstructionCode = misfireInstruction
        };
        trigger.ComputeFirstFireTimeUtc(null);
        return trigger;
    }

    // ---------------------------------------------------------------------------------------
    // Assertions shared by the per-type tests.
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// Runs misfire handling and returns the rescheduled fire time. The frozen "now" is the
    /// <see cref="FixedTimeProvider"/> the trigger was constructed with.
    /// </summary>
    private static DateTimeOffset? Misfire(IOperableTrigger trigger)
    {
        trigger.UpdateAfterMisfire(null);
        return trigger.NextFireTimeUtc;
    }

    /// <summary>
    /// Collects <paramref name="count"/> further fire times, asserting strict progress at each step
    /// so that a trigger that wedges on a DST boundary fails here rather than looping forever.
    /// </summary>
    private static List<DateTimeOffset> StepForward(ITrigger trigger, DateTimeOffset from, int count)
    {
        List<DateTimeOffset> fireTimes = new List<DateTimeOffset>(count);
        DateTimeOffset current = from;
        for (int i = 0; i < count; i++)
        {
            DateTimeOffset? next = trigger.GetFireTimeAfter(current);
            next.Should().NotBeNull("the trigger must keep producing fire times after {0:O}", current);
            next!.Value.Should().BeAfter(current, "fire times must strictly increase, step {0}", i + 1);
            fireTimes.Add(next.Value);
            current = next.Value;
        }

        return fireTimes;
    }

    /// <summary>
    /// States the invariant that a rescheduled fire time reads back as a wall clock the zone really
    /// has. This is a shape check only - a well formed instant can never normalise onto a skipped
    /// wall clock - so the pinned exact fire times below carry the weight of the DST assertions.
    /// </summary>
    private static void AssertLocalTimeExists(DateTimeOffset instant)
    {
        DateTime local = TimeZoneInfo.ConvertTime(instant, Eastern).DateTime;
        Eastern.IsInvalidTime(local).Should().BeFalse(
            "the fire time {0:O} maps to Eastern wall clock {1:yyyy-MM-dd HH:mm:ss}, which must exist", instant, local);
    }

    /// <summary>
    /// The two passes of an ambiguous hour share a wall clock. A trigger that fires on both would
    /// run the job twice for what the user reads as one scheduled time.
    /// </summary>
    private static void AssertNoRepeatedWallClock(IEnumerable<DateTimeOffset> fireTimes)
    {
        List<DateTime> localTimes = fireTimes.Select(t => TimeZoneInfo.ConvertTime(t, Eastern).DateTime).ToList();
        localTimes.Should().OnlyHaveUniqueItems("no wall clock may be fired twice across the ambiguous hour");
    }

    // ---------------------------------------------------------------------------------------
    // Spring forward: misfire handling runs at the instant whose would-be wall clock never existed.
    // ---------------------------------------------------------------------------------------

    [Test]
    public void Cron_DoNothing_MisfireNowInGapHour_NextFireIsStrictlyFutureAndValid()
    {
        AssumeEasternTransitions();

        CronTriggerImpl trigger = CreateCronTrigger(MisfireInstruction.CronTrigger.DoNothing, NowInGapHour);

        DateTimeOffset? nextFire = Misfire(trigger);

        nextFire.Should().NotBeNull();
        nextFire!.Value.Should().BeAfter(NowInGapHour, "DoNothing must skip all past-due fire times");
        nextFire.Value.Should().Be(new DateTimeOffset(2024, 3, 11, 6, 30, 0, TimeSpan.Zero),
            "02:30 does not exist on the spring forward day, so the next daily fire is 2024-03-11 02:30 EDT");
        AssertLocalTimeExists(nextFire.Value);

        StepForward(trigger, nextFire.Value, 2);
    }

    [Test]
    public void CalendarInterval_DoNothing_MisfireNowInGapHour_NextFireIsStrictlyFutureAndValid()
    {
        AssumeEasternTransitions();

        CalendarIntervalTriggerImpl trigger = CreateCalendarIntervalTrigger(MisfireInstruction.CalendarIntervalTrigger.DoNothing, NowInGapHour);

        DateTimeOffset? nextFire = Misfire(trigger);

        nextFire.Should().NotBeNull();
        nextFire!.Value.Should().BeAfter(NowInGapHour, "DoNothing must skip all past-due fire times");
        nextFire.Value.Should().Be(new DateTimeOffset(2024, 3, 11, 6, 30, 0, TimeSpan.Zero),
            "PreserveHourOfDayAcrossDaylightSavings keeps the 02:30 wall clock, now at the daylight offset");
        nextFire.Value.Offset.Should().Be(TimeSpan.FromHours(-4),
            "unlike the other three trigger types, CalendarIntervalTriggerImpl hands back values carrying " +
            "the trigger time zone's offset rather than UTC; equality is by instant, so this only matters " +
            "to callers that read Offset or DateTime off the result");
        AssertLocalTimeExists(nextFire.Value);

        StepForward(trigger, nextFire.Value, 2);
    }

    [Test]
    public void DailyTimeInterval_DoNothing_MisfireNowInGapHour_NextFireIsStrictlyFutureAndValid()
    {
        AssumeEasternTransitions();

        DailyTimeIntervalTriggerImpl trigger = CreateDailyTimeIntervalTrigger(MisfireInstruction.DailyTimeIntervalTrigger.DoNothing, NowInGapHour);

        DateTimeOffset? nextFire = Misfire(trigger);

        nextFire.Should().NotBeNull();
        nextFire!.Value.Should().BeAfter(NowInGapHour, "DoNothing must skip all past-due fire times");
        nextFire.Value.Should().Be(new DateTimeOffset(2024, 3, 10, 7, 45, 0, TimeSpan.Zero),
            "the quarter hour grid continues at 03:45 EDT, the first slot after the frozen now");
        AssertLocalTimeExists(nextFire.Value);

        StepForward(trigger, nextFire.Value, 2);
    }

    [Test]
    public void Simple_RescheduleNextWithRemainingCount_MisfireNowInGapHour_NextFireIsStrictlyFutureAndValid()
    {
        AssumeEasternTransitions();

        // SimpleTrigger has no DoNothing instruction; RescheduleNextWithRemainingCount is the
        // closest equivalent - it also picks the next scheduled time strictly after now instead of
        // firing immediately.
        SimpleTriggerImpl trigger = CreateSimpleTrigger(MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount, NowInGapHour);

        DateTimeOffset? nextFire = Misfire(trigger);

        nextFire.Should().NotBeNull();
        nextFire!.Value.Should().BeAfter(NowInGapHour, "the trigger must not fire immediately after misfire handling");
        nextFire.Value.Should().Be(new DateTimeOffset(2024, 3, 10, 8, 0, 0, TimeSpan.Zero),
            "SimpleTrigger counts absolute intervals from the start time, so DST does not shift the grid");
        AssertLocalTimeExists(nextFire.Value);

        StepForward(trigger, nextFire.Value, 2);
    }

    // ---------------------------------------------------------------------------------------
    // Fall back: misfire handling runs during the hour that happens twice.
    // ---------------------------------------------------------------------------------------

    [Test]
    public void Cron_FireOnceNow_MisfireNowInAmbiguousHour_ProgressesWithoutDuplicate()
    {
        AssumeEasternTransitions();

        CronTriggerImpl trigger = CreateCronTrigger(MisfireInstruction.CronTrigger.FireOnceNow, NowInAmbiguousHourStandardPass);

        DateTimeOffset? nextFire = Misfire(trigger);

        nextFire.Should().NotBeNull();
        nextFire!.Value.Should().Be(NowInAmbiguousHourStandardPass, "FireOnceNow schedules the trigger for exactly now");

        List<DateTimeOffset> onward = StepForward(trigger, nextFire.Value, 3);
        onward[0].Should().Be(new DateTimeOffset(2024, 11, 3, 7, 30, 0, TimeSpan.Zero),
            "the regular 02:30 fire on the fall back day happens once, at the standard offset");
        AssertNoRepeatedWallClock(onward.Prepend(nextFire.Value));
    }

    [Test]
    public void Cron_FireOnceNow_MisfireNowInAmbiguousHourDaylightPass_ProgressesWithoutDuplicate()
    {
        AssumeEasternTransitions();

        CronTriggerImpl trigger = CreateCronTrigger(MisfireInstruction.CronTrigger.FireOnceNow, NowInAmbiguousHourDaylightPass);

        DateTimeOffset? nextFire = Misfire(trigger);

        nextFire.Should().NotBeNull();
        nextFire!.Value.Should().Be(NowInAmbiguousHourDaylightPass, "FireOnceNow schedules the trigger for exactly now");

        List<DateTimeOffset> onward = StepForward(trigger, nextFire.Value, 3);
        onward[0].Should().Be(new DateTimeOffset(2024, 11, 3, 7, 30, 0, TimeSpan.Zero),
            "stepping out of the first pass of the repeated hour must not revisit it");
        AssertNoRepeatedWallClock(onward.Prepend(nextFire.Value));
    }

    [Test]
    public void CalendarInterval_FireOnceNow_MisfireNowInAmbiguousHour_ProgressesWithoutDuplicate()
    {
        AssumeEasternTransitions();

        CalendarIntervalTriggerImpl trigger = CreateCalendarIntervalTrigger(MisfireInstruction.CalendarIntervalTrigger.FireOnceNow, NowInAmbiguousHourStandardPass);

        DateTimeOffset? nextFire = Misfire(trigger);

        nextFire.Should().NotBeNull();
        nextFire!.Value.Should().Be(NowInAmbiguousHourStandardPass, "FireOnceNow schedules the trigger for exactly now");

        List<DateTimeOffset> onward = StepForward(trigger, nextFire.Value, 3);
        onward[0].Should().Be(new DateTimeOffset(2024, 11, 3, 7, 30, 0, TimeSpan.Zero),
            "the preserved 02:30 wall clock resumes at the standard offset");
        onward[0].Offset.Should().Be(TimeSpan.FromHours(-5),
            "the fire time that FireOnceNow stores is whatever the trigger clock returned, but the times " +
            "computed afterwards carry the trigger time zone's offset");
        AssertNoRepeatedWallClock(onward.Prepend(nextFire.Value));
    }

    [Test]
    public void DailyTimeInterval_FireOnceNow_MisfireNowInAmbiguousHour_ProgressesWithoutDuplicate()
    {
        AssumeEasternTransitions();

        DailyTimeIntervalTriggerImpl trigger = CreateDailyTimeIntervalTrigger(MisfireInstruction.DailyTimeIntervalTrigger.FireOnceNow, NowInAmbiguousHourStandardPass);

        DateTimeOffset? nextFire = Misfire(trigger);

        nextFire.Should().NotBeNull();
        nextFire!.Value.Should().Be(NowInAmbiguousHourStandardPass, "FireOnceNow schedules the trigger for exactly now");

        List<DateTimeOffset> onward = StepForward(trigger, nextFire.Value, 3);
        onward[0].Should().Be(new DateTimeOffset(2024, 11, 3, 6, 45, 0, TimeSpan.Zero),
            "the quarter hour grid continues inside the repeated hour at 01:45 EST");
        AssertNoRepeatedWallClock(onward.Prepend(nextFire.Value));
    }

    [Test]
    public void DailyTimeInterval_FireOnceNow_MisfireNowInAmbiguousHourDaylightPass_RepeatsTheAmbiguousWallClock()
    {
        AssumeEasternTransitions();

        // The counterpart of the test above, pinning the behaviour that makes the standard pass the
        // interesting case: when misfire handling lands on the *first* pass of the repeated hour,
        // the quarter hour grid walks straight through the fall back and serves 01:30 a second time.
        // Both fires are distinct instants an hour apart, so nothing here is a wedge - but a job
        // scheduled for 01:30 does run twice.
        DailyTimeIntervalTriggerImpl trigger = CreateDailyTimeIntervalTrigger(MisfireInstruction.DailyTimeIntervalTrigger.FireOnceNow, NowInAmbiguousHourDaylightPass);

        DateTimeOffset? nextFire = Misfire(trigger);

        nextFire.Should().NotBeNull();
        nextFire!.Value.Should().Be(NowInAmbiguousHourDaylightPass);

        List<DateTimeOffset> onward = StepForward(trigger, nextFire.Value, 4);
        onward[3].Should().Be(NowInAmbiguousHourStandardPass,
            "the fourth step is 01:30 again, this time at the standard offset");
        TimeZoneInfo.ConvertTime(onward[3], Eastern).DateTime.Should().Be(
            TimeZoneInfo.ConvertTime(nextFire.Value, Eastern).DateTime,
            "both fires read as the same 01:30 wall clock");
    }

    [Test]
    public void Simple_FireNow_MisfireNowInAmbiguousHour_ProgressesWithoutDuplicate()
    {
        AssumeEasternTransitions();

        // For a repeating SimpleTrigger, FireNow is rewritten to RescheduleNowWithRemainingRepeatCount,
        // which also fires now but additionally re-anchors StartTimeUtc to now.
        SimpleTriggerImpl trigger = CreateSimpleTrigger(MisfireInstruction.SimpleTrigger.FireNow, NowInAmbiguousHourStandardPass);

        DateTimeOffset? nextFire = Misfire(trigger);

        nextFire.Should().NotBeNull();
        nextFire!.Value.Should().Be(NowInAmbiguousHourStandardPass, "FireNow schedules the trigger for exactly now");
        trigger.StartTimeUtc.Should().Be(NowInAmbiguousHourStandardPass,
            "RescheduleNowWithRemainingRepeatCount re-anchors the interval grid to now");

        List<DateTimeOffset> onward = StepForward(trigger, nextFire.Value, 3);
        onward[0].Should().Be(new DateTimeOffset(2024, 11, 3, 7, 30, 0, TimeSpan.Zero),
            "the hourly interval is absolute, so the next fire is one real hour later");
        AssertNoRepeatedWallClock(onward.Prepend(nextFire.Value));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
