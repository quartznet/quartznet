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

namespace Quartz;

/// <summary>
/// Running a trigger's schedule over a past range you choose: one firing per slot the trigger had in it.
/// </summary>
/// <remarks>
/// <para>
/// An extension rather than an <see cref="IScheduler" /> member: it reads the trigger and its calendar and
/// schedules one-shot triggers, all through members every scheduler has, so it works unchanged through
/// <c>HttpScheduler</c> and any forwarder.
/// </para>
/// <para>
/// A misfire instruction decides what happens to the firings a scheduler missed while it was down. A
/// backfill is the other case: a range somebody decides, however the trigger got through it.
/// </para>
/// </remarks>
public static class SchedulerBackfillExtensions
{
    /// <summary>
    /// Schedules one firing of the trigger's job for each slot the trigger had in
    /// [<paramref name="from" />, <paramref name="to" />).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A slot is a fire time of the trigger — its start and end time honoured — that its calendar does not
    /// exclude. There are none before its start time, which a trigger builder sets to the moment it builds
    /// unless told otherwise, and which a simple trigger moves forward when it is stored with one in the
    /// past or a misfire reschedules it. Each slot gets a one-shot trigger in group
    /// <see cref="SchedulerConstants.BackfillGroupPrefix" /> + the trigger's group, named
    /// <c>{trigger name}@{slot in UTC, ISO 8601}</c>, for the same job. It carries the trigger's
    /// <see cref="JobDataMap" /> with the slot added under
    /// <see cref="SchedulerConstants.BackfillOriginalFireTime" />, its priority, retry policy, preferred node
    /// and execution group, and the <see cref="SimpleTriggerMisfireInstruction.FireNow" /> misfire instruction.
    /// It carries no calendar: the slot has already been checked against it.
    /// </para>
    /// <para>
    /// The name is the slot, so running the same range again skips every slot whose trigger is still stored —
    /// waiting, paused or firing — and counts it in <see cref="BackfillResult.AlreadyScheduled" />. A slot whose
    /// trigger has fired is gone from the store and is scheduled again.
    /// </para>
    /// <para>
    /// The slots are scheduled one call at a time. A failure or a cancellation part way leaves the ones already
    /// scheduled in place; running the same range again schedules the rest.
    /// </para>
    /// </remarks>
    /// <param name="scheduler">The scheduler holding the trigger.</param>
    /// <param name="triggerKey">The trigger whose schedule to backfill.</param>
    /// <param name="from">The start of the range, included.</param>
    /// <param name="to">The end of the range, excluded. Not later than the scheduler's current time.</param>
    /// <param name="options">How many slots may be scheduled, their spacing and their execution group.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The slots found, the triggers scheduled, and the slots already scheduled.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scheduler" /> or <paramref name="triggerKey" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="to" /> is not after <paramref name="from" />; <paramref name="to" /> is later than
    /// the scheduler's <see cref="IScheduler.TimeProvider" /> says it is now; the range holds more slots than
    /// <see cref="BackfillOptions.MaxSlots" />; or an option is out of range. Nothing is scheduled.
    /// </exception>
    /// <exception cref="ObjectDoesNotExistException">
    /// There is no trigger under <paramref name="triggerKey" />, or the calendar it names is gone. Nothing is
    /// scheduled.
    /// </exception>
    public static async ValueTask<BackfillResult> Backfill(
        this IScheduler scheduler,
        TriggerKey triggerKey,
        DateTimeOffset from,
        DateTimeOffset to,
        BackfillOptions options = default,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(triggerKey);

        BackfillOutcome outcome = await Backfilling.Run(scheduler, triggerKey, from, to, options, cancellationToken).ConfigureAwait(false);
        if (outcome.Refusal is { } refusal)
        {
            throw refusal.AsException();
        }

        return outcome.Result
               ?? throw new ObjectDoesNotExistException($"Trigger '{triggerKey}' does not exist, so it has no schedule to backfill. Nothing was scheduled.");
    }
}

/// <summary>
/// A backfill, run so that each carrier can answer its outcome in its own terms: a refusal is returned rather
/// than thrown, and so is a trigger that does not exist.
/// </summary>
/// <remarks>
/// The extension throws a refusal as <see cref="ArgumentException" />, and the HTTP API answers it as a
/// <c>400</c> carrying the same reason, so the two say the same words. Every refusal is decided before
/// <see cref="Schedule" />, the only step that writes.
/// </remarks>
internal static class Backfilling
{
    /// <summary>
    /// How many slots a range is counted to at most, past <see cref="BackfillOptions.MaxSlots" />, before a
    /// refusal says "more than" rather than the exact count.
    /// </summary>
    internal const int CountCeiling = 100_000;

    /// <summary>
    /// How many keys one existence check asks about: the most the HTTP API's bulk fetch takes, so a
    /// backfill through <c>HttpScheduler</c> asks in pages it accepts.
    /// </summary>
    private const int ExistenceCheckBatch = 1000;

    /// <summary>
    /// Backfills the trigger under <paramref name="triggerKey" />, answering a refusal, or neither a refusal
    /// nor a result when there is no such trigger, rather than throwing either.
    /// </summary>
    /// <remarks>
    /// The range is checked against <paramref name="scheduler" />'s own clock before the trigger is read, so
    /// a refused range costs no round trip.
    /// </remarks>
    public static async ValueTask<BackfillOutcome> Run(
        IScheduler scheduler,
        TriggerKey triggerKey,
        DateTimeOffset from,
        DateTimeOffset to,
        BackfillOptions options,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = scheduler.TimeProvider.GetUtcNow();
        if (CheckRange(from, to, now, options) is { } refusal)
        {
            return new BackfillOutcome(null, refusal);
        }

        if (await scheduler.GetTrigger(triggerKey, cancellationToken).ConfigureAwait(false) is not { } trigger)
        {
            return default;
        }

        ICalendar? calendar = await CalendarOf(scheduler, trigger, cancellationToken).ConfigureAwait(false);
        BackfillPlan plan = Plan(trigger, calendar, from, to, now, options, scheduler.TimeProvider);
        if (plan.Refusal is { } planRefusal)
        {
            return new BackfillOutcome(null, planRefusal);
        }

        return new BackfillOutcome(await Schedule(scheduler, plan, cancellationToken).ConfigureAwait(false), null);
    }

    /// <summary>
    /// The refusal of a range that is empty or reaches past <paramref name="now" />, or of options out of
    /// range; <see langword="null" /> when there is nothing to refuse.
    /// </summary>
    private static BackfillRefusal? CheckRange(DateTimeOffset from, DateTimeOffset to, DateTimeOffset now, BackfillOptions options)
    {
        if (options.MaxSlots < 1)
        {
            return Refuse(nameof(options), $"BackfillOptions.MaxSlots must be at least 1, was {options.MaxSlots}.");
        }

        if (options.Spacing < TimeSpan.Zero)
        {
            return Refuse(nameof(options), $"BackfillOptions.Spacing must not be negative, was {options.Spacing:c}.");
        }

        if (ExecutionGroupOf(options) is { } executionGroup && ExecutionLimits.IsReservedGroupName(executionGroup))
        {
            return Refuse(nameof(options), $"BackfillOptions.ExecutionGroup '{executionGroup}' is reserved for limits configuration.");
        }

        if (to <= from)
        {
            return Refuse(nameof(to), $"The range must end after it starts: {to:O} is not after {from:O}.");
        }

        if (to > now)
        {
            return Refuse(nameof(to), $"The range ends at {to:O}, after the scheduler's current time {now:O}. The trigger fires the future itself, so backfilling it would fire those slots twice; end the range at or before now.");
        }

        return null;
    }

    /// <summary>
    /// The calendar the trigger names, or <see langword="null" /> when it names none.
    /// </summary>
    private static async ValueTask<ICalendar?> CalendarOf(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken)
    {
        if (trigger.CalendarName is not { } calendarName)
        {
            return null;
        }

        return await scheduler.GetCalendar(calendarName, cancellationToken).ConfigureAwait(false)
               ?? throw new ObjectDoesNotExistException($"Calendar '{calendarName}', which trigger '{trigger.Key}' names, does not exist, so its slots cannot be told apart from the times it excludes. Nothing was scheduled.");
    }

    /// <summary>
    /// The trigger's slots in [<paramref name="from" />, <paramref name="to" />): its fire times there that
    /// <paramref name="calendar" /> does not exclude, counted up to <paramref name="countUpTo" /> and the first
    /// <paramref name="keep" /> of them kept.
    /// </summary>
    /// <remarks>
    /// <see cref="ITrigger.GetFireTimeAfter" /> does not consult the calendar, so an excluded time is dropped
    /// here rather than moved: the trigger would not have fired then either. The walk starts a second before
    /// <paramref name="from" /> because a calendar-interval trigger looks a second past the time it is asked
    /// about, and a fire time before <paramref name="from" /> is stepped over.
    /// </remarks>
    public static SlotCount Walk(ITrigger trigger, ICalendar? calendar, DateTimeOffset from, DateTimeOffset to, int keep, long countUpTo)
    {
        List<DateTimeOffset> slots = [];
        long count = 0;

        DateTimeOffset cursor = from.UtcTicks >= TimeSpan.TicksPerSecond ? from.AddSeconds(-1) : from;
        while (count < countUpTo)
        {
            // A trigger that answers a time not after the one it was asked about would never get anywhere.
            if (trigger.GetFireTimeAfter(cursor) is not { } slot || slot >= to || slot <= cursor)
            {
                break;
            }

            cursor = slot;
            if (slot < from || (calendar is not null && !calendar.IsTimeIncluded(slot)))
            {
                continue;
            }

            count++;
            if (slots.Count < keep)
            {
                slots.Add(slot.ToUniversalTime());
            }
        }

        return new SlotCount(slots, count, count >= countUpTo);
    }

    /// <summary>
    /// The one-shot triggers that backfill <paramref name="trigger" /> over the range, built before anything
    /// is scheduled, or the refusal of a range holding more slots than the options allow.
    /// </summary>
    private static BackfillPlan Plan(
        ITrigger trigger,
        ICalendar? calendar,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset now,
        BackfillOptions options,
        TimeProvider timeProvider)
    {
        long countUpTo = Math.Max(options.MaxSlots, (long) CountCeiling) + 1;
        SlotCount found = Walk(trigger, calendar, from, to, options.MaxSlots, countUpTo);

        if (found.Count > options.MaxSlots)
        {
            string count = found.Capped
                ? string.Create(CultureInfo.InvariantCulture, $"more than {countUpTo - 1}")
                : found.Count.ToString(CultureInfo.InvariantCulture);

            return Refused(Refuse(nameof(options), $"The range holds {count} slots of trigger '{trigger.Key}', more than BackfillOptions.MaxSlots ({options.MaxSlots}) allows. Narrow the range or raise MaxSlots."));
        }

        List<DateTimeOffset> slots = found.Slots;
        if (slots.Count > 1 && options.Spacing.Ticks > (DateTimeOffset.MaxValue.UtcTicks - now.UtcTicks) / (slots.Count - 1))
        {
            return Refused(Refuse(nameof(options), $"BackfillOptions.Spacing of {options.Spacing:c} starts slot {slots.Count - 1} past the largest time there is."));
        }

        string group = SchedulerConstants.BackfillGroupPrefix + trigger.Key.Group;
        string? executionGroup = ExecutionGroupOf(options) ?? trigger.ExecutionGroup;

        List<ITrigger> triggers = new(slots.Count);
        for (int i = 0; i < slots.Count; i++)
        {
            DateTimeOffset slot = slots[i];

            JobDataMap jobDataMap = new(trigger.JobDataMap);
            jobDataMap.PutAsString(SchedulerConstants.BackfillOriginalFireTime, slot);

            triggers.Add(TriggerBuilder.Create(timeProvider)
                .WithIdentity(SlotName(trigger.Key.Name, slot), group)
                .ForJob(trigger.JobKey)
                .WithDescription(string.Create(CultureInfo.InvariantCulture, $"Backfill of {trigger.Key} for {SlotText(slot)}"))
                .UsingJobData(jobDataMap)
                .WithPriority(trigger.Priority)
                // The group is a stored name, not a template: its braces, if it has any, are literal.
                .WithExecutionGroup(executionGroup is null ? null : ExecutionGroupTemplate.Escape(executionGroup))
                .WithRetryPolicy(trigger.RetryPolicy)
                .WithPreferredNode(trigger.PreferredNode)
                .StartAt(now.AddTicks(options.Spacing.Ticks * i))
                .WithSimpleSchedule(schedule => schedule.WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow))
                .Build());
        }

        return new BackfillPlan(slots, triggers);
    }

    /// <summary>
    /// Stores every trigger of <paramref name="plan" /> that is not stored already.
    /// </summary>
    /// <remarks>
    /// The stored ones are found first, a page of keys at a time, so running a range again costs a read
    /// rather than a refused write per slot. A trigger stored between that read and its own write is refused
    /// with <see cref="ObjectAlreadyExistsException" /> and counted the same way.
    /// </remarks>
    private static async ValueTask<BackfillResult> Schedule(IScheduler scheduler, BackfillPlan plan, CancellationToken cancellationToken)
    {
        HashSet<TriggerKey> stored = [];
        foreach (ITrigger[] page in plan.Triggers.Chunk(ExistenceCheckBatch))
        {
            TriggerKey[] keys = Array.ConvertAll(page, trigger => trigger.Key);
            foreach (ITrigger existing in await scheduler.GetTriggers(keys, cancellationToken).ConfigureAwait(false))
            {
                stored.Add(existing.Key);
            }
        }

        List<TriggerKey> scheduled = new(plan.Triggers.Count - stored.Count);
        int alreadyScheduled = 0;
        foreach (ITrigger trigger in plan.Triggers)
        {
            if (stored.Contains(trigger.Key))
            {
                alreadyScheduled++;
                continue;
            }

            try
            {
                await scheduler.ScheduleJob(trigger, cancellationToken: cancellationToken).ConfigureAwait(false);
                scheduled.Add(trigger.Key);
            }
            catch (ObjectAlreadyExistsException)
            {
                alreadyScheduled++;
            }
        }

        return new BackfillResult
        {
            SlotsFound = plan.Slots.Count,
            AlreadyScheduled = alreadyScheduled,
            FirstSlot = plan.Slots.Count > 0 ? plan.Slots[0] : null,
            LastSlot = plan.Slots.Count > 0 ? plan.Slots[^1] : null,
            ScheduledTriggers = scheduled
        };
    }

    /// <summary>
    /// The name of the trigger that stands in for <paramref name="slot" />: the original's name, <c>@</c>, and
    /// the slot in UTC — whole seconds unless the slot has a fraction.
    /// </summary>
    internal static string SlotName(string triggerName, DateTimeOffset slot)
    {
        return triggerName + "@" + SlotText(slot);
    }

    private static string SlotText(DateTimeOffset slot)
    {
        return slot.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture) + "Z";
    }

    /// <summary>
    /// The execution group the options name, trimmed, or <see langword="null" /> when they leave it to the
    /// original trigger.
    /// </summary>
    private static string? ExecutionGroupOf(BackfillOptions options)
    {
        return string.IsNullOrWhiteSpace(options.ExecutionGroup) ? null : options.ExecutionGroup.Trim();
    }

    private static BackfillRefusal Refuse(string parameterName, FormattableString reason)
    {
        return new BackfillRefusal(reason.ToString(CultureInfo.InvariantCulture) + " Nothing was scheduled.", parameterName);
    }

    private static BackfillPlan Refused(BackfillRefusal refusal)
    {
        return new BackfillPlan([], []) { Refusal = refusal };
    }
}

/// <summary>
/// What a backfill came to: what it scheduled, or why it was refused. Neither is a trigger that does not
/// exist, which each carrier answers in its own terms.
/// </summary>
internal readonly record struct BackfillOutcome(BackfillResult? Result, BackfillRefusal? Refusal);

/// <summary>
/// Why a backfill is refused before anything is scheduled, and which argument of
/// <see cref="SchedulerBackfillExtensions.Backfill" /> the reason is about.
/// </summary>
internal readonly record struct BackfillRefusal(string Reason, string ParameterName)
{
    public ArgumentException AsException()
    {
        return new ArgumentException(Reason, ParameterName);
    }
}

/// <summary>
/// The slots a walk kept, how many it counted, and whether it stopped counting before the range ended.
/// </summary>
internal readonly record struct SlotCount(List<DateTimeOffset> Slots, long Count, bool Capped);

/// <summary>
/// A backfill's slots, earliest first, and the trigger built for each, in the same order — or, with both
/// empty, the reason the range was refused.
/// </summary>
internal sealed record BackfillPlan(List<DateTimeOffset> Slots, List<ITrigger> Triggers)
{
    public BackfillRefusal? Refusal { get; init; }
}
