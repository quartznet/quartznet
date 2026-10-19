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

namespace Quartz.Impl.Triggers;

/// <summary>
/// The arithmetic of a simple trigger that measures its interval from the previous run's completion
/// (<see cref="ISimpleTrigger.MeasureIntervalFromCompletion" />, #4140).
/// </summary>
/// <remarks>
/// <para>
/// Shared by the trigger, which computes a provisional next fire time as it fires and applies its misfire
/// instruction, and by both job stores, which compute the real one when the firing completes.
/// </para>
/// <para>
/// Runs are counted by <see cref="ISimpleTrigger.TimesTriggered" /> alone. A fixed-rate trigger reads its
/// count off the grid its start time lays down, and a fixed-delay trigger's runs drift off that grid by
/// every run's duration, so the grid would spend the repeat count early.
/// </para>
/// </remarks>
internal static class FixedDelaySchedule
{
    /// <summary>
    /// Whether <paramref name="trigger" /> is held while it runs and measures its interval from completion.
    /// </summary>
    public static bool Applies(ITrigger trigger) => trigger is ISimpleTrigger { MeasureIntervalFromCompletion: true };

    /// <summary>
    /// Whether <paramref name="trigger" /> has runs left by its repeat count.
    /// </summary>
    public static bool HasRunsLeft(ISimpleTrigger trigger)
    {
        return trigger.RepeatCount == SimpleTriggerImpl.RepeatIndefinitely || trigger.TimesTriggered <= trigger.RepeatCount;
    }

    /// <summary>
    /// The fire time one interval after <paramref name="afterUtc" />, moved on by whole intervals past what
    /// <paramref name="calendar" /> excludes.
    /// </summary>
    /// <returns>
    /// The fire time, or <see langword="null" /> when the repeat count is spent or the end time is behind it.
    /// </returns>
    public static DateTimeOffset? FireTimeAfter(ISimpleTrigger trigger, DateTimeOffset afterUtc, ICalendar? calendar)
    {
        if (!HasRunsLeft(trigger))
        {
            return null;
        }

        TimeSpan interval = trigger.RepeatInterval;
        if (interval <= TimeSpan.Zero || afterUtc > DateTimeOffset.MaxValue - interval)
        {
            return null;
        }

        return FirstIncludedFrom(trigger, afterUtc + interval, calendar);
    }

    /// <summary>
    /// <paramref name="candidate" />, or the first instant whole intervals after it that
    /// <paramref name="calendar" /> includes.
    /// </summary>
    /// <returns>
    /// The fire time, or <see langword="null" /> when it is past the end time, or the calendar excludes
    /// everything up to <see cref="TriggerConstants.YearToGiveUpSchedulingAt" />.
    /// </returns>
    public static DateTimeOffset? FirstIncludedFrom(ISimpleTrigger trigger, DateTimeOffset candidate, ICalendar? calendar)
    {
        TimeSpan interval = trigger.RepeatInterval;
        DateTimeOffset next = candidate;
        while (calendar is not null && !calendar.IsTimeIncluded(next))
        {
            if (interval <= TimeSpan.Zero || next > DateTimeOffset.MaxValue - interval)
            {
                return null;
            }

            next += interval;
            if (next.Year > TriggerConstants.YearToGiveUpSchedulingAt)
            {
                return null;
            }
        }

        // A fire time that lands exactly on the end time is one the trigger fires, as for a fixed rate.
        if (trigger.EndTimeUtc is { } endTimeUtc && endTimeUtc < next)
        {
            return null;
        }

        return next;
    }
}
