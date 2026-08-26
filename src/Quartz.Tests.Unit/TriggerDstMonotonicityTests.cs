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

using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit;

/// <summary>
/// Property tests over daylight saving time transitions: whatever a trigger decides to do with a
/// wall clock that vanishes or repeats, walking its fire times must make monotonic progress and
/// must not stall. Each case walks one trigger kind across one transition and asserts that every
/// step strictly increases, that no step jumps further than the schedule plus the transition delta
/// could explain, and that the walk actually gets past the transition instant.
/// <para>
/// These tests never touch <see cref="SystemTime"/>, so the fixture is parallel safe.
/// </para>
/// </summary>
public class TriggerDstMonotonicityTests
{
    private const int StepCount = 150;

    /// <summary>
    /// A window that starts two hours before a transition instant, so a walk of 150 steps of any of
    /// the schedules below is guaranteed to cross it.
    /// </summary>
    private sealed class DstWindow
    {
        public DstWindow(TimeZoneInfo zone, DateTimeOffset start, Action assumePremise)
        {
            Zone = zone;
            Start = start;
            AssumePremise = assumePremise;
        }

        public TimeZoneInfo Zone { get; }

        public DateTimeOffset Start { get; }

        public DateTimeOffset Transition => Start.AddHours(2);

        public Action AssumePremise { get; }
    }

    /// <summary>
    /// Resolved lazily per test case: <see cref="TestTimeZones.LordHowe"/> ignores the test when the
    /// zone is missing, which must not happen while building a shared static table.
    /// </summary>
    private static DstWindow ResolveWindow(string windowKey)
    {
        switch (windowKey)
        {
            case "EasternSpring":
                // 02:00 EST -> 03:00 EDT at 2024-03-10 07:00Z.
                return new DstWindow(
                    TestTimeZones.Eastern,
                    new DateTimeOffset(2024, 3, 10, 5, 0, 0, TimeSpan.Zero),
                    () => TestTimeZones.AssumeInvalidLocalTime(TestTimeZones.Eastern, new DateTime(2024, 3, 10, 2, 30, 0)));

            case "EasternFall":
                // 02:00 EDT -> 01:00 EST at 2024-11-03 06:00Z.
                return new DstWindow(
                    TestTimeZones.Eastern,
                    new DateTimeOffset(2024, 11, 3, 4, 0, 0, TimeSpan.Zero),
                    () => TestTimeZones.AssumeAmbiguousLocalTime(TestTimeZones.Eastern, new DateTime(2024, 11, 3, 1, 30, 0)));

            case "CentralEuropeanFall":
                // 03:00 CEST -> 02:00 CET at 2018-10-28 01:00Z.
                return new DstWindow(
                    TestTimeZones.CentralEuropean,
                    new DateTimeOffset(2018, 10, 27, 23, 0, 0, TimeSpan.Zero),
                    () => TestTimeZones.AssumeAmbiguousLocalTime(TestTimeZones.CentralEuropean, new DateTime(2018, 10, 28, 2, 30, 0)));

            case "SantiagoSpring":
                // Midnight transition: 2019-09-08 never has a 00:00 local, the day starts at 01:00.
                return new DstWindow(
                    TestTimeZones.Santiago,
                    new DateTimeOffset(2019, 9, 8, 2, 0, 0, TimeSpan.Zero),
                    () => TestTimeZones.AssumeInvalidLocalTime(TestTimeZones.Santiago, new DateTime(2019, 9, 8, 0, 30, 0)));

            case "LordHoweSpring":
                // Half hour delta: 02:00 -> 02:30 local at 2019-10-05 15:30Z.
                return new DstWindow(
                    TestTimeZones.LordHowe,
                    new DateTimeOffset(2019, 10, 5, 13, 30, 0, TimeSpan.Zero),
                    () => TestTimeZones.AssumeInvalidLocalTime(TestTimeZones.LordHowe, new DateTime(2019, 10, 6, 2, 15, 0)));

            case "LordHoweFall":
                // Half hour delta: 02:00 -> 01:30 local at 2019-04-06 15:00Z.
                return new DstWindow(
                    TestTimeZones.LordHowe,
                    new DateTimeOffset(2019, 4, 6, 13, 0, 0, TimeSpan.Zero),
                    () => TestTimeZones.AssumeAmbiguousLocalTime(TestTimeZones.LordHowe, new DateTime(2019, 4, 7, 1, 45, 0)));

            case "AmmanSpring":
                // Midnight transition, the other way round from Santiago's: 2017-03-31 has no 00:00
                // local, the day starts at 01:00. Jordan abolished DST in 2022, so this is frozen
                // history rather than a rule that can move under the test.
                return new DstWindow(
                    TestTimeZones.Amman,
                    new DateTimeOffset(2017, 3, 30, 20, 0, 0, TimeSpan.Zero),
                    () => TestTimeZones.AssumeInvalidLocalTime(TestTimeZones.Amman, new DateTime(2017, 3, 31, 0, 30, 0)));

            case "AmmanFall":
                // The repeated hour is the first hour of the local day, 00:00-00:59 on 2017-10-27,
                // which no other window here covers.
                return new DstWindow(
                    TestTimeZones.Amman,
                    new DateTimeOffset(2017, 10, 26, 20, 0, 0, TimeSpan.Zero),
                    () => TestTimeZones.AssumeAmbiguousLocalTime(TestTimeZones.Amman, new DateTime(2017, 10, 27, 0, 30, 0)));

            case "SydneyFall":
                // 03:00 AEDT -> 02:00 AEST at 2024-04-06 16:00Z.
                return new DstWindow(
                    TestTimeZones.Sydney,
                    new DateTimeOffset(2024, 4, 6, 14, 0, 0, TimeSpan.Zero),
                    () => TestTimeZones.AssumeAmbiguousLocalTime(TestTimeZones.Sydney, new DateTime(2024, 4, 7, 2, 30, 0)));

            default:
                throw new ArgumentOutOfRangeException(nameof(windowKey), windowKey, "unknown DST window");
        }
    }

    private static ITrigger CreateTrigger(string triggerKind, TimeZoneInfo zone, DateTimeOffset windowStart)
    {
        switch (triggerKind)
        {
            case "CronMinutely":
                return new CronTriggerImpl
                {
                    Key = new TriggerKey(triggerKind, "dst"),
                    CronExpressionString = "0 * * * * ?",
                    TimeZone = zone,
                    StartTimeUtc = windowStart
                };

            case "CronHourly":
                return new CronTriggerImpl
                {
                    Key = new TriggerKey(triggerKind, "dst"),
                    CronExpressionString = "0 0 * * * ?",
                    TimeZone = zone,
                    StartTimeUtc = windowStart
                };

            case "CalendarIntervalHourly":
                return new CalendarIntervalTriggerImpl
                {
                    Key = new TriggerKey(triggerKind, "dst"),
                    StartTimeUtc = windowStart,
                    RepeatInterval = 1,
                    RepeatIntervalUnit = IntervalUnit.Hour,
                    TimeZone = zone
                };

            case "CalendarIntervalDailyPreserve":
                return new CalendarIntervalTriggerImpl
                {
                    Key = new TriggerKey(triggerKind, "dst"),
                    StartTimeUtc = AnchorAtLocalHalfPastTwo(zone, windowStart),
                    RepeatInterval = 1,
                    RepeatIntervalUnit = IntervalUnit.Day,
                    TimeZone = zone,
                    PreserveHourOfDayAcrossDaylightSavings = true
                };

            case "DailyTimeIntervalQuarterHour":
                return new DailyTimeIntervalTriggerImpl
                {
                    Key = new TriggerKey(triggerKind, "dst"),
                    StartTimeUtc = windowStart,
                    StartTimeOfDay = new TimeOnly(0, 0, 0),
                    EndTimeOfDay = new TimeOnly(23, 59, 59),
                    RepeatInterval = 15,
                    RepeatIntervalUnit = IntervalUnit.Minute,
                    TimeZone = zone
                };

            case "SimpleHalfHour":
                return new SimpleTriggerImpl
                {
                    Key = new TriggerKey(triggerKind, "dst"),
                    StartTimeUtc = windowStart,
                    RepeatInterval = TimeSpan.FromMinutes(30),
                    RepeatCount = SimpleTriggerImpl.RepeatIndefinitely
                };

            case "RecurrenceHourly":
                return new RecurrenceTriggerImpl
                {
                    Key = new TriggerKey(triggerKind, "dst"),
                    RecurrenceRule = "FREQ=HOURLY;INTERVAL=1",
                    TimeZone = zone,
                    StartTimeUtc = windowStart
                };

            default:
                throw new ArgumentOutOfRangeException(nameof(triggerKind), triggerKind, "unknown trigger kind");
        }
    }

    /// <summary>
    /// Anchors a daily schedule at 02:30 local a few days before the window, so that the daily fire
    /// lands squarely on the wall clock the transition attacks.
    /// </summary>
    private static DateTimeOffset AnchorAtLocalHalfPastTwo(TimeZoneInfo zone, DateTimeOffset windowStart)
    {
        DateTime localWindowStart = TimeZoneInfo.ConvertTime(windowStart, zone).DateTime;
        DateTime anchorLocal = localWindowStart.Date.AddDays(-3).AddHours(2).AddMinutes(30);
        return new DateTimeOffset(anchorLocal, zone.GetUtcOffset(anchorLocal));
    }

    [TestCase("CronMinutely", "EasternSpring", "2024-03-10 05:00 +00:00", 90)]
    [TestCase("CronMinutely", "EasternFall", "2024-11-03 04:00 +00:00", 90)]
    [TestCase("CronMinutely", "CentralEuropeanFall", "2018-10-27 23:00 +00:00", 90)]
    [TestCase("CronMinutely", "SantiagoSpring", "2019-09-08 02:00 +00:00", 90)]
    [TestCase("CronMinutely", "LordHoweSpring", "2019-10-05 13:30 +00:00", 90)]
    [TestCase("CronMinutely", "LordHoweFall", "2019-04-06 13:00 +00:00", 90)]
    [TestCase("CronMinutely", "SydneyFall", "2024-04-06 14:00 +00:00", 90)]
    [TestCase("CronMinutely", "AmmanSpring", "2017-03-30 20:00 +00:00", 90)]
    [TestCase("CronMinutely", "AmmanFall", "2017-10-26 20:00 +00:00", 90)]
    [TestCase("CronHourly", "EasternSpring", "2024-03-10 05:00 +00:00", 150)]
    [TestCase("CronHourly", "EasternFall", "2024-11-03 04:00 +00:00", 150)]
    [TestCase("CronHourly", "CentralEuropeanFall", "2018-10-27 23:00 +00:00", 150)]
    [TestCase("CronHourly", "SantiagoSpring", "2019-09-08 02:00 +00:00", 150)]
    [TestCase("CronHourly", "LordHoweSpring", "2019-10-05 13:30 +00:00", 150)]
    [TestCase("CronHourly", "LordHoweFall", "2019-04-06 13:00 +00:00", 150)]
    [TestCase("CronHourly", "SydneyFall", "2024-04-06 14:00 +00:00", 150)]
    [TestCase("CronHourly", "AmmanSpring", "2017-03-30 20:00 +00:00", 150)]
    [TestCase("CronHourly", "AmmanFall", "2017-10-26 20:00 +00:00", 150)]
    [TestCase("CalendarIntervalHourly", "EasternSpring", "2024-03-10 05:00 +00:00", 90)]
    [TestCase("CalendarIntervalHourly", "EasternFall", "2024-11-03 04:00 +00:00", 90)]
    [TestCase("CalendarIntervalHourly", "CentralEuropeanFall", "2018-10-27 23:00 +00:00", 90)]
    [TestCase("CalendarIntervalHourly", "SantiagoSpring", "2019-09-08 02:00 +00:00", 90)]
    [TestCase("CalendarIntervalHourly", "LordHoweSpring", "2019-10-05 13:30 +00:00", 90)]
    [TestCase("CalendarIntervalHourly", "LordHoweFall", "2019-04-06 13:00 +00:00", 90)]
    [TestCase("CalendarIntervalHourly", "SydneyFall", "2024-04-06 14:00 +00:00", 90)]
    [TestCase("CalendarIntervalHourly", "AmmanSpring", "2017-03-30 20:00 +00:00", 90)]
    [TestCase("CalendarIntervalHourly", "AmmanFall", "2017-10-26 20:00 +00:00", 90)]
    // A daily schedule that preserves its wall clock spans the 25 hour fall back day, so the bound
    // is 26 hours. Observed maximum across these seven windows is exactly 25 hours (Central European
    // and Sydney fall back); the spare hour absorbs a zone whose delta is larger than one hour.
    [TestCase("CalendarIntervalDailyPreserve", "EasternSpring", "2024-03-10 05:00 +00:00", 26 * 60)]
    [TestCase("CalendarIntervalDailyPreserve", "EasternFall", "2024-11-03 04:00 +00:00", 26 * 60)]
    [TestCase("CalendarIntervalDailyPreserve", "CentralEuropeanFall", "2018-10-27 23:00 +00:00", 26 * 60)]
    [TestCase("CalendarIntervalDailyPreserve", "SantiagoSpring", "2019-09-08 02:00 +00:00", 26 * 60)]
    [TestCase("CalendarIntervalDailyPreserve", "LordHoweSpring", "2019-10-05 13:30 +00:00", 26 * 60)]
    [TestCase("CalendarIntervalDailyPreserve", "LordHoweFall", "2019-04-06 13:00 +00:00", 26 * 60)]
    [TestCase("CalendarIntervalDailyPreserve", "SydneyFall", "2024-04-06 14:00 +00:00", 26 * 60)]
    [TestCase("CalendarIntervalDailyPreserve", "AmmanSpring", "2017-03-30 20:00 +00:00", 26 * 60)]
    [TestCase("CalendarIntervalDailyPreserve", "AmmanFall", "2017-10-26 20:00 +00:00", 26 * 60)]
    [TestCase("DailyTimeIntervalQuarterHour", "EasternSpring", "2024-03-10 05:00 +00:00", 90)]
    [TestCase("DailyTimeIntervalQuarterHour", "EasternFall", "2024-11-03 04:00 +00:00", 90)]
    [TestCase("DailyTimeIntervalQuarterHour", "CentralEuropeanFall", "2018-10-27 23:00 +00:00", 90)]
    [TestCase("DailyTimeIntervalQuarterHour", "SantiagoSpring", "2019-09-08 02:00 +00:00", 90)]
    [TestCase("DailyTimeIntervalQuarterHour", "LordHoweSpring", "2019-10-05 13:30 +00:00", 90)]
    [TestCase("DailyTimeIntervalQuarterHour", "LordHoweFall", "2019-04-06 13:00 +00:00", 90)]
    [TestCase("DailyTimeIntervalQuarterHour", "SydneyFall", "2024-04-06 14:00 +00:00", 90)]
    [TestCase("DailyTimeIntervalQuarterHour", "AmmanSpring", "2017-03-30 20:00 +00:00", 90)]
    [TestCase("DailyTimeIntervalQuarterHour", "AmmanFall", "2017-10-26 20:00 +00:00", 90)]
    [TestCase("SimpleHalfHour", "EasternSpring", "2024-03-10 05:00 +00:00", 31)]
    [TestCase("SimpleHalfHour", "EasternFall", "2024-11-03 04:00 +00:00", 31)]
    [TestCase("SimpleHalfHour", "CentralEuropeanFall", "2018-10-27 23:00 +00:00", 31)]
    [TestCase("SimpleHalfHour", "SantiagoSpring", "2019-09-08 02:00 +00:00", 31)]
    [TestCase("SimpleHalfHour", "LordHoweSpring", "2019-10-05 13:30 +00:00", 31)]
    [TestCase("SimpleHalfHour", "LordHoweFall", "2019-04-06 13:00 +00:00", 31)]
    [TestCase("SimpleHalfHour", "SydneyFall", "2024-04-06 14:00 +00:00", 31)]
    [TestCase("SimpleHalfHour", "AmmanSpring", "2017-03-30 20:00 +00:00", 31)]
    [TestCase("SimpleHalfHour", "AmmanFall", "2017-10-26 20:00 +00:00", 31)]
    [TestCase("RecurrenceHourly", "EasternSpring", "2024-03-10 05:00 +00:00", 150)]
    [TestCase("RecurrenceHourly", "EasternFall", "2024-11-03 04:00 +00:00", 150)]
    [TestCase("RecurrenceHourly", "CentralEuropeanFall", "2018-10-27 23:00 +00:00", 150)]
    [TestCase("RecurrenceHourly", "SantiagoSpring", "2019-09-08 02:00 +00:00", 150)]
    [TestCase("RecurrenceHourly", "LordHoweSpring", "2019-10-05 13:30 +00:00", 150)]
    [TestCase("RecurrenceHourly", "LordHoweFall", "2019-04-06 13:00 +00:00", 150)]
    [TestCase("RecurrenceHourly", "SydneyFall", "2024-04-06 14:00 +00:00", 150)]
    [TestCase("RecurrenceHourly", "AmmanSpring", "2017-03-30 20:00 +00:00", 150)]
    [TestCase("RecurrenceHourly", "AmmanFall", "2017-10-26 20:00 +00:00", 150)]
    public void FireTimesProgressMonotonicallyAcrossTransition(
        string triggerKind,
        string windowKey,
        string windowStart,
        int maxStepMinutes)
    {
        DstWindow window = ResolveWindow(windowKey);
        DateTimeOffset start = TestTimeZones.Local(windowStart);
        start.Should().Be(window.Start, "the test case row and the window table must agree on the window start");
        window.AssumePremise();

        ITrigger trigger = CreateTrigger(triggerKind, window.Zone, start);
        TimeSpan maxStep = TimeSpan.FromMinutes(maxStepMinutes);

        List<DateTimeOffset> fireTimes = new List<DateTimeOffset>(StepCount);
        DateTimeOffset current = start;
        for (int i = 0; i < StepCount; i++)
        {
            DateTimeOffset? next = trigger.GetFireTimeAfter(current);

            next.Should().NotBeNull(
                "{0} must keep producing fire times in {1}, but stopped after {2:O} (step {3})",
                triggerKind, window.Zone.Id, current, i + 1);

            next.Value.Should().BeAfter(current,
                "{0} fire times must strictly increase in {1} (step {2})", triggerKind, window.Zone.Id, i + 1);

            (next.Value - current).Should().BeLessThanOrEqualTo(maxStep,
                "{0} must not skip a whole schedule period around the {1} transition: {2:O} -> {3:O} (step {4})",
                triggerKind, windowKey, current, next.Value, i + 1);

            fireTimes.Add(next.Value);
            current = next.Value;
        }

        fireTimes[fireTimes.Count - 1].Should().BeAfter(window.Transition,
            "{0} steps of {1} must carry the walk past the {2} transition", StepCount, triggerKind, windowKey);
    }
}
