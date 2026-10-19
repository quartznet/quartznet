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

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz;

/// <summary>
/// What a scheduled trigger fires next: its upcoming fire times, read from the scheduler that holds it.
/// </summary>
/// <remarks>
/// An extension rather than an <see cref="IScheduler" /> member: it reads the trigger and its calendar through
/// members every scheduler has and computes with <see cref="TriggerFireTimes" />, so it works unchanged through
/// any forwarder. <c>HttpScheduler</c> asks its host instead, in one request, which computes them there.
/// </remarks>
public static class SchedulerFireTimesExtensions
{
    /// <summary>
    /// The next <paramref name="count" /> fire times of the trigger under <paramref name="triggerKey" />, in UTC
    /// and earliest first, or <see langword="null" /> when the scheduler holds no such trigger.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The list starts at the trigger's <see cref="ITrigger.NextFireTimeUtc" />, the time the scheduler fires
    /// it next, and advances the trigger's schedule from there: in the trigger's own time zone, skipping what
    /// its calendar excludes, and stopping at its end time. It holds fewer than <paramref name="count" /> times
    /// when the schedule ends sooner, and none for a trigger that does not fire again.
    /// </para>
    /// <para>
    /// The schedule is the stored one. A paused trigger answers the times it would fire once resumed, before
    /// any misfire handling resuming applies.
    /// </para>
    /// <para>
    /// On an <c>HttpScheduler</c> this is one request to the host's <c>fire-times</c> route, computed where the
    /// trigger and its calendar are stored. A host older than 4.7 has no such route, and the times are computed
    /// here from the trigger and calendar it answers with.
    /// </para>
    /// </remarks>
    /// <param name="scheduler">The scheduler holding the trigger.</param>
    /// <param name="triggerKey">The trigger whose fire times to compute.</param>
    /// <param name="count">How many fire times to answer at most, from 1 to 100.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scheduler" /> or <paramref name="triggerKey" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count" /> is less than 1 or more than 100.</exception>
    /// <exception cref="ObjectDoesNotExistException">The calendar the trigger names does not exist.</exception>
    /// <exception cref="ArgumentException">
    /// The trigger is not an <see cref="IOperableTrigger" />, so its schedule cannot be advanced.
    /// </exception>
    public static ValueTask<List<DateTimeOffset>?> GetUpcomingFireTimes(
        this IScheduler scheduler,
        TriggerKey triggerKey,
        int count,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(triggerKey);
        UpcomingFireTimes.CheckCount(count);

        if (scheduler is IUpcomingFireTimesScheduler computing)
        {
            return computing.GetUpcomingFireTimes(triggerKey, count, cancellationToken);
        }

        return UpcomingFireTimes.Compose(scheduler, triggerKey, count, cancellationToken);
    }
}

/// <summary>
/// The one computation of a stored trigger's upcoming fire times, which the extension, the HTTP API's route,
/// the dashboard agent and the dashboard's client each run.
/// </summary>
internal static class UpcomingFireTimes
{
    /// <summary>
    /// How many fire times one call answers when it does not say.
    /// </summary>
    public const int DefaultCount = 10;

    /// <summary>
    /// The most fire times one call answers.
    /// </summary>
    public const int MaxCount = 100;

    /// <summary>
    /// Why <paramref name="count" /> is refused, or <see langword="null" /> when it is in range.
    /// </summary>
    public static string? CountRefusal(int count)
    {
        return count is < 1 or > MaxCount
            ? $"count must be from 1 to {MaxCount}, was {count}"
            : null;
    }

    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count" /> is out of range.</exception>
    public static void CheckCount(int count)
    {
        if (CountRefusal(count) is { } refusal)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, refusal);
        }
    }

    /// <summary>
    /// The fire times composed from members every scheduler has: the trigger, then its calendar.
    /// </summary>
    /// <remarks>
    /// What the extension runs for a scheduler that does not compute them in one call of its own, and what
    /// such a scheduler falls back to when the other end cannot.
    /// </remarks>
    public static async ValueTask<List<DateTimeOffset>?> Compose(
        IScheduler scheduler,
        TriggerKey triggerKey,
        int count,
        CancellationToken cancellationToken)
    {
        if (await scheduler.GetTrigger(triggerKey, cancellationToken).ConfigureAwait(false) is not { } trigger)
        {
            return null;
        }

        ICalendar? calendar = null;
        if (CalendarNameOf(trigger) is { } calendarName)
        {
            calendar = await scheduler.GetCalendar(calendarName, cancellationToken).ConfigureAwait(false)
                       ?? throw new ObjectDoesNotExistException(
                           $"Calendar '{calendarName}', which trigger '{trigger.Key}' names, does not exist, so its fire times cannot be told apart from the times it excludes.");
        }

        return Compute(trigger, calendar, count);
    }

    /// <summary>
    /// The calendar <paramref name="trigger" /> names, or <see langword="null" /> when it names none.
    /// </summary>
    public static string? CalendarNameOf(ITrigger trigger)
    {
        return string.IsNullOrWhiteSpace(trigger.CalendarName) ? null : trigger.CalendarName;
    }

    /// <summary>
    /// The next <paramref name="count" /> fire times of a stored <paramref name="trigger" />, in UTC, from its
    /// next fire time on.
    /// </summary>
    /// <remarks>
    /// A stored trigger with no next fire time does not fire again — it has fired for the last time — so it
    /// answers none, rather than the first fire times <see cref="TriggerFireTimes.Compute(ITrigger, ICalendar?, int)" />
    /// would compute again from its start.
    /// </remarks>
    public static List<DateTimeOffset> Compute(ITrigger trigger, ICalendar? calendar, int count)
    {
        if (trigger.NextFireTimeUtc is null)
        {
            return [];
        }

        List<DateTimeOffset> fireTimes = TriggerFireTimes.Compute(trigger, calendar, count);
        for (int i = 0; i < fireTimes.Count; i++)
        {
            fireTimes[i] = fireTimes[i].ToUniversalTime();
        }

        return fireTimes;
    }
}
