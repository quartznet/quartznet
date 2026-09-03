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

using Quartz.Spi;

namespace Quartz.Tests.Unit.Impl.Triggers;

/// <summary>
/// <see cref="ITrigger.EndTimeUtc" /> is the last instant at which a daily time interval trigger may
/// fire: nothing past it is produced, wherever in the daily window it falls.
/// </summary>
/// <remarks>
/// The daily half of #3458. The trigger's walk consulted the end time only where it advanced to
/// another day, so an end time falling between two fire times of the same day let it go on firing
/// until the daily window closed, and <see cref="ITrigger.FinalFireTimeUtc" /> reported that close
/// even when it was a day past the end. The inclusive-end alignment of the other trigger types is a
/// 4.0 change and is deliberately not part of this.
/// </remarks>
public class DailyTimeIntervalTriggerEndTimeTest
{
    private static readonly DateTimeOffset Start = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    /// <summary>
    /// An end time that falls between two fire times, where "the trigger fires at the end time"
    /// cannot be what stops the schedule.
    /// </summary>
    private static readonly DateTimeOffset MisalignedEnd = Start.AddHours(2).AddMinutes(30);

    [Test]
    public void NothingFiresPastAnEndTimeThatIsNoFireTime()
    {
        IOperableTrigger trigger = Hourly(MisalignedEnd);

        trigger.GetFireTimeAfter(Start.AddHours(2)).Should().BeNull(
            "the next repeat falls past the end time, and an end time between two fire times ends the schedule at the earlier one");

        TriggerUtils.ComputeFireTimes(trigger, null, 10).Should().Equal(
            new[] { Start, Start.AddHours(1), Start.AddHours(2) },
            "an end time is a bound on firing, not merely on how far the schedule is walked");

        trigger.FinalFireTimeUtc.Should().BeOnOrBefore(MisalignedEnd,
            "a trigger's last fire cannot be past the last instant at which it may fire");
    }

    /// <summary>
    /// The daily-time-interval trigger reports the last instant at which it may fire rather than a
    /// fire time its schedule produces. What it may not report is a time past the end time.
    /// </summary>
    [Test]
    public void FinalFireTimeStopsAtTheEndTimeRatherThanTheDailyWindow()
    {
        IOperableTrigger trigger = Hourly(MisalignedEnd);

        trigger.FinalFireTimeUtc.Should().Be(MisalignedEnd,
            "the daily window closing at 23:59:59 is no reason to report a final fire past the end time");
    }

    /// <summary>
    /// The other reading of the same rule: where the daily window closes before the end time, the
    /// window is the last instant at which the trigger may fire.
    /// </summary>
    [Test]
    public void FinalFireTimeStopsAtTheDailyWindowWhenThatComesFirst()
    {
        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger", "group")
            .ForJob("job", "group")
            .StartAt(Start)
            .EndAt(Start.AddHours(20))
            .WithDailyTimeIntervalSchedule(schedule => schedule
                .WithInterval(1, IntervalUnit.Hour)
                .OnEveryDay()
                .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(8, 0))
                .EndingDailyAt(TimeOfDay.HourAndMinuteOfDay(17, 0))
                .InTimeZone(TimeZoneInfo.Utc))
            .Build();

        trigger.FinalFireTimeUtc.Should().Be(Start.AddHours(17),
            "the trigger cannot fire between the window closing at 17:00 and the end time three hours later");
    }

    /// <summary>
    /// The window's close is a local wall-clock time, so where the end time falls a day later in
    /// its own zone the window is read on that day and in that zone rather than at the offset the
    /// end time happens to carry.
    /// </summary>
    [Test]
    public void FinalFireTimeReadsTheDailyWindowInTheTriggersTimeZone()
    {
        TimeZoneInfo helsinki = TestTimeZones.Helsinki;

        // 2026-06-01 22:00 UTC is 2026-06-02 01:00 in Helsinki: the end time's local day is the second.
        DateTimeOffset endTimeUtc = Start.AddHours(22);

        IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger", "group")
            .ForJob("job", "group")
            .StartAt(Start)
            .EndAt(endTimeUtc)
            .WithDailyTimeIntervalSchedule(schedule => schedule
                .WithInterval(1, IntervalUnit.Hour)
                .OnEveryDay()
                .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(0, 0))
                .EndingDailyAt(TimeOfDay.HourAndMinuteOfDay(0, 30))
                .InTimeZone(helsinki))
            .Build();

        // The window on the end time's local day closes at 00:30 Helsinki, which is 21:30 UTC - before the end time.
        trigger.FinalFireTimeUtc.Should().Be(new DateTimeOffset(2026, 6, 2, 0, 30, 0, TimeSpan.FromHours(3)),
            "the window closes on the end time's own local day, half an hour before the end time");
    }

    private static IOperableTrigger Hourly(DateTimeOffset endTimeUtc)
    {
        return (IOperableTrigger) TriggerBuilder.Create()
            .WithIdentity("trigger", "group")
            .ForJob("job", "group")
            .StartAt(Start)
            .EndAt(endTimeUtc)
            .WithDailyTimeIntervalSchedule(schedule => schedule
                .WithInterval(1, IntervalUnit.Hour)
                .OnEveryDay()
                .InTimeZone(TimeZoneInfo.Utc))
            .Build();
    }
}
