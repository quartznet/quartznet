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

using Microsoft.Extensions.Time.Testing;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit;

/// <summary>
/// <see cref="ITrigger.EndTimeUtc" /> means one thing for every shipped trigger type: it is the last
/// instant at which a trigger may fire, so a fire time exactly equal to it is produced, and nothing
/// past it is.
/// </summary>
/// <remarks>
/// One table over all five types, because the types disagreed — see #3458.
/// </remarks>
public class TriggerEndTimeBoundaryTest
{
    /// <summary>
    /// Every schedule in the table fires hourly from here, so the third repeat lands exactly on
    /// <see cref="End" /> and the boundary is a fire time the schedule genuinely produces.
    /// </summary>
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset End = Start.AddHours(3);

    /// <summary>
    /// An end time that falls between two fire times, where "the trigger fires at the end time"
    /// cannot be what stops the schedule.
    /// </summary>
    private static readonly DateTimeOffset MisalignedEnd = Start.AddHours(2).AddMinutes(30);

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>
    /// The five shipped trigger types, each building the same hourly schedule, with an end time the
    /// caller chooses so the same schedule can be walked with and without one.
    /// </summary>
    public static IEnumerable<TestCaseData> TriggerTypes()
    {
        yield return Case("Simple", (clock, endTimeUtc) => Builder(clock, endTimeUtc)
            .WithSchedule(SimpleScheduleBuilder.Create().WithInterval(Interval).RepeatForever()));

        yield return Case("Cron", (clock, endTimeUtc) => Builder(clock, endTimeUtc)
            .WithSchedule(CronScheduleBuilder.Create("0 0 * * * ?").InTimeZone(TimeZoneInfo.Utc)));

        yield return Case("CalendarInterval", (clock, endTimeUtc) => Builder(clock, endTimeUtc)
            .WithSchedule(CalendarIntervalScheduleBuilder.Create().WithInterval(1, IntervalUnit.Hour).InTimeZone(TimeZoneInfo.Utc)));

        yield return Case("DailyTimeInterval", (clock, endTimeUtc) => Builder(clock, endTimeUtc)
            .WithSchedule(DailyTimeIntervalScheduleBuilder.Create()
                .WithInterval(1, IntervalUnit.Hour)
                .OnEveryDay()
                .InTimeZone(TimeZoneInfo.Utc)));

        yield return Case("Recurrence", (clock, endTimeUtc) => Builder(clock, endTimeUtc)
            .WithSchedule(RecurrenceScheduleBuilder.Create("FREQ=HOURLY").InTimeZone(TimeZoneInfo.Utc)));
    }

    [TestCaseSource(nameof(TriggerTypes))]
    public void GetFireTimeAfter_ProducesTheFireTimeThatLandsOnTheEndTime(TriggerFactory factory)
    {
        IOperableTrigger trigger = Build(factory, End);

        trigger.GetFireTimeAfter(End - Interval).Should().Be(End,
            "the end time is the last instant at which a trigger may fire, so a fire time equal to it still fires");
    }

    [TestCaseSource(nameof(TriggerTypes))]
    public void GetFireTimeAfter_ProducesNothingPastTheEndTime(TriggerFactory factory)
    {
        IOperableTrigger trigger = Build(factory, End);

        trigger.GetFireTimeAfter(End).Should().BeNull(
            "the end time is the last instant at which a trigger may fire, so nothing after it fires");
    }

    [TestCaseSource(nameof(TriggerTypes))]
    public void TheWalkEndsOnTheEndTime(TriggerFactory factory)
    {
        IOperableTrigger trigger = Build(factory, End);

        List<DateTimeOffset> fireTimes = TriggerFireTimes.Compute(trigger, calendar: null, numberOfTimes: 10);

        fireTimes.Should().Equal(
            [Start, Start.AddHours(1), Start.AddHours(2), End],
            "the schedule runs out at the end time, having fired on it");
    }

    [TestCaseSource(nameof(TriggerTypes))]
    public void FinalFireTimeIsTheFireTimeOnTheEndTime(TriggerFactory factory)
    {
        IOperableTrigger trigger = Build(factory, End);

        trigger.FinalFireTimeUtc.Should().Be(End,
            "the fire time that lands on the end time is the last one, so it is the final one");
    }

    [TestCaseSource(nameof(TriggerTypes))]
    public void NothingFiresPastAnEndTimeThatIsNoFireTime(TriggerFactory factory)
    {
        IOperableTrigger trigger = Build(factory, MisalignedEnd);

        trigger.GetFireTimeAfter(Start.AddHours(2)).Should().BeNull(
            "the next repeat falls past the end time, and an end time between two fire times ends the schedule at the earlier one");

        TriggerFireTimes.Compute(trigger, calendar: null, numberOfTimes: 10).Should().Equal(
            [Start, Start.AddHours(1), Start.AddHours(2)],
            "an end time is a bound on firing, not merely on how far the schedule is walked");

        trigger.FinalFireTimeUtc.Should().BeOnOrBefore(MisalignedEnd,
            "a trigger's last fire cannot be past the last instant at which it may fire");
    }

    /// <summary>
    /// The daily-time-interval trigger reports the last instant at which it may fire rather than a
    /// fire time its schedule produces, which the other four types do. What it may not report is a
    /// time past the end time.
    /// </summary>
    [Test]
    public void DailyTimeIntervalFinalFireTimeStopsAtTheEndTimeRatherThanTheDailyWindow()
    {
        IOperableTrigger trigger = Build(
            (clock, endTimeUtc) => Builder(clock, endTimeUtc)
                .WithSchedule(DailyTimeIntervalScheduleBuilder.Create()
                    .WithInterval(1, IntervalUnit.Hour)
                    .OnEveryDay()
                    .InTimeZone(TimeZoneInfo.Utc)),
            MisalignedEnd);

        trigger.FinalFireTimeUtc.Should().Be(MisalignedEnd,
            "the daily window closing at 23:59:59 is no reason to report a final fire past the end time");
    }

    /// <summary>
    /// The other reading of the same rule: where the daily window closes before the end time, the
    /// window is the last instant at which the trigger may fire.
    /// </summary>
    [Test]
    public void DailyTimeIntervalFinalFireTimeStopsAtTheDailyWindowWhenThatComesFirst()
    {
        DateTimeOffset endTimeUtc = Start.AddHours(20);

        IOperableTrigger trigger = Build(
            (clock, endAt) => Builder(clock, endAt)
                .WithSchedule(DailyTimeIntervalScheduleBuilder.Create()
                    .WithInterval(1, IntervalUnit.Hour)
                    .OnEveryDay()
                    .StartingDailyAt(new TimeOnly(8, 0))
                    .EndingDailyAt(new TimeOnly(17, 0))
                    .InTimeZone(TimeZoneInfo.Utc)),
            endTimeUtc);

        trigger.FinalFireTimeUtc.Should().Be(Start.AddHours(17),
            "the trigger cannot fire between the window closing at 17:00 and the end time three hours later");
    }

    /// <summary>
    /// <see cref="TriggerFireTimes.ComputeBetween" /> bounds the walk by assigning the window's end
    /// to the trigger, so its <c>to</c> is inclusive for the same reason every trigger's end time is.
    /// </summary>
    [TestCaseSource(nameof(TriggerTypes))]
    public void ComputeBetween_IncludesAFireTimeOnTheWindowEnd(TriggerFactory factory)
    {
        IOperableTrigger trigger = Build(factory, endTimeUtc: null);

        List<DateTimeOffset> fireTimes = TriggerFireTimes.ComputeBetween(trigger, calendar: null, from: Start, to: End);

        fireTimes.Should().Equal(
            [Start, Start.AddHours(1), Start.AddHours(2), End],
            "the window's end is a boundary of the same kind as a trigger's own end time");
    }

    /// <summary>
    /// Builds one of the tabulated triggers against a clock reading a minute before <see cref="Start" />,
    /// so that no trigger sees its own schedule as past due.
    /// </summary>
    private static IOperableTrigger Build(TriggerFactory factory, DateTimeOffset? endTimeUtc)
    {
        FakeTimeProvider clock = new FakeTimeProvider(Start.AddMinutes(-1));
        return (IOperableTrigger) factory(clock, endTimeUtc).Build();
    }

    private static TriggerBuilder<IJob> Builder(TimeProvider clock, DateTimeOffset? endTimeUtc)
    {
        return TriggerBuilder.Create(clock)
            .WithIdentity("trigger", "group")
            .ForJob("job", "group")
            .StartAt(Start)
            .EndAt(endTimeUtc);
    }

    private static TestCaseData Case(string name, TriggerFactory factory)
    {
        return new TestCaseData(factory).SetArgDisplayNames(name);
    }

    /// <summary>
    /// Builds one trigger type's hourly schedule, with the given end time.
    /// </summary>
    public delegate TriggerBuilder<IJob> TriggerFactory(TimeProvider clock, DateTimeOffset? endTimeUtc);
}
