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

using System.Globalization;

namespace Quartz.Tests.Integration.Impl;

/// <summary>
/// The trigger shapes the retry matrix runs. The same five families
/// <see cref="MisfireTriggerShape" /> covers, and for the same reason: a repeating simple trigger and
/// a one-shot one take different branches once a retry is in flight, because only one of them has an
/// occurrence left for the retry to be measured against.
/// </summary>
public enum RetryTriggerShape
{
    SimpleRepeating,
    SimpleOneShot,
    Cron,
    CalendarInterval,
    DailyTimeInterval,
    Recurrence
}

/// <summary>
/// One row of the retry matrix: a trigger shape, and whether it has a scheduled occurrence after the
/// one being retried.
/// </summary>
public sealed class RetryMatrixCase
{
    /// <summary>The shape this row is about.</summary>
    public RetryTriggerShape Shape { get; init; }

    /// <summary>The shape, as a failure message names it.</summary>
    public string ShapeName { get; init; }

    /// <summary>
    /// Whether the trigger has another scheduled occurrence after the one that fails. A one-shot
    /// trigger has none, which is the branch where a retry is the only thing keeping it alive.
    /// </summary>
    public bool HasFurtherOccurrences { get; init; }

    /// <summary>
    /// How many times the shape counts having fired, or <see langword="null" /> for a shape that keeps
    /// no such counter. A retry must never move it.
    /// </summary>
    public Func<ITrigger, int?> TimesTriggered { get; init; }

    /// <summary>
    /// Builds the trigger, anchored on the instant the test froze and holding the store's clock. Called
    /// twice per test: once for the copy that is stored, and once for the detached copy the expectation
    /// is computed from.
    /// </summary>
    public Func<DateTimeOffset, TimeProvider, TriggerBuilder<IJob>> Trigger { get; init; }

    public override string ToString() => ShapeName;
}

/// <summary>
/// The trigger shapes every job store has to retry the same way.
/// </summary>
/// <remarks>
/// <para>
/// Every schedule has a period of one day and fires at the test's anchor, so the occurrence that fails
/// is the anchor and the next one is a day later. A retry delay of five minutes therefore has a whole
/// day of room, and a cell reads as a schedule rather than as arithmetic on a boundary.
/// </para>
/// <para>
/// The counted shapes are given a finite count on purpose: a retry that consumed one would show up
/// here as a trigger that ran out early.
/// </para>
/// </remarks>
public static class RetryMatrixCases
{
    /// <summary>The gap between the occurrence that fails and the next one.</summary>
    public static readonly TimeSpan Period = TimeSpan.FromDays(1);

    /// <summary>How long a retry waits. Far inside <see cref="Period" />, so it never supersedes.</summary>
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

    /// <summary>The policy the matrix's triggers carry: two attempts, five minutes apart.</summary>
    public static RetryPolicy Policy => RetryPolicy.Fixed(2, RetryDelay);

    /// <summary>Every row of the matrix.</summary>
    public static IEnumerable<RetryMatrixCase> All()
    {
        yield return new RetryMatrixCase
        {
            Shape = RetryTriggerShape.SimpleRepeating,
            ShapeName = "SimpleTrigger repeating daily",
            HasFurtherOccurrences = true,
            TimesTriggered = x => ((ISimpleTrigger) x).TimesTriggered,
            Trigger = (anchor, clock) => TriggerBuilder.Create(clock)
                .StartAt(anchor)
                .WithSimpleSchedule(x => x
                    .WithInterval(Period)
                    .WithRepeatCount(5))
                .WithRetryPolicy(Policy)
        };

        yield return new RetryMatrixCase
        {
            Shape = RetryTriggerShape.SimpleOneShot,
            ShapeName = "SimpleTrigger one-shot",
            HasFurtherOccurrences = false,
            TimesTriggered = x => ((ISimpleTrigger) x).TimesTriggered,
            Trigger = (anchor, clock) => TriggerBuilder.Create(clock)
                .StartAt(anchor)
                .WithSimpleSchedule(x => x
                    .WithInterval(Period)
                    .WithRepeatCount(0))
                .WithRetryPolicy(Policy)
        };

        yield return new RetryMatrixCase
        {
            Shape = RetryTriggerShape.Cron,
            ShapeName = "CronTrigger firing once a day",
            HasFurtherOccurrences = true,
            TimesTriggered = null,
            Trigger = (anchor, clock) => TriggerBuilder.Create(clock)
                .StartAt(anchor - Period)
                .WithCronSchedule(DailyCronAt(anchor), x => x
                    .InTimeZone(TimeZoneInfo.Utc))
                .WithRetryPolicy(Policy)
        };

        yield return new RetryMatrixCase
        {
            Shape = RetryTriggerShape.CalendarInterval,
            ShapeName = "CalendarIntervalTrigger every day",
            HasFurtherOccurrences = true,
            TimesTriggered = x => ((ICalendarIntervalTrigger) x).TimesTriggered,
            Trigger = (anchor, clock) => TriggerBuilder.Create(clock)
                .StartAt(anchor)
                .WithCalendarIntervalSchedule(x => x
                    .WithInterval(1, IntervalUnit.Day)
                    .InTimeZone(TimeZoneInfo.Utc))
                .WithRetryPolicy(Policy)
        };

        yield return new RetryMatrixCase
        {
            Shape = RetryTriggerShape.DailyTimeInterval,
            ShapeName = "DailyTimeIntervalTrigger with a one-instant window",
            HasFurtherOccurrences = true,
            TimesTriggered = x => ((IDailyTimeIntervalTrigger) x).TimesTriggered,
            Trigger = (anchor, clock) => TriggerBuilder.Create(clock)
                .StartAt(anchor - Period)
                .WithDailyTimeIntervalSchedule(x => x
                    .StartingDailyAt(TimeOfDay(anchor))
                    .EndingDailyAt(TimeOfDay(anchor))
                    .WithInterval(1, IntervalUnit.Hour)
                    .InTimeZone(TimeZoneInfo.Utc))
                .WithRetryPolicy(Policy)
        };

        yield return new RetryMatrixCase
        {
            Shape = RetryTriggerShape.Recurrence,
            ShapeName = "RecurrenceTrigger on FREQ=DAILY;COUNT=5",
            HasFurtherOccurrences = true,
            TimesTriggered = x => ((IRecurrenceTrigger) x).TimesTriggered,
            Trigger = (anchor, clock) => TriggerBuilder.Create(clock)
                .StartAt(anchor)
                .WithRecurrenceSchedule("FREQ=DAILY;COUNT=5", x => x
                    .InTimeZone(TimeZoneInfo.Utc))
                .WithRetryPolicy(Policy)
        };
    }

    /// <summary>One named row, for a test whose subject is something the matrix has already pinned.</summary>
    public static RetryMatrixCase Row(RetryTriggerShape shape) => All().Single(x => x.Shape == shape);

    /// <summary>A cron expression that fires once a day, at the UTC time of <paramref name="when" />.</summary>
    private static string DailyCronAt(DateTimeOffset when)
    {
        DateTime utc = when.UtcDateTime;
        return string.Create(CultureInfo.InvariantCulture, $"{utc.Second} {utc.Minute} {utc.Hour} * * ?");
    }

    /// <summary>The UTC time of day of <paramref name="when" />, to the second.</summary>
    private static TimeOnly TimeOfDay(DateTimeOffset when)
    {
        DateTime utc = when.UtcDateTime;
        return new TimeOnly(utc.Hour, utc.Minute, utc.Second);
    }
}
