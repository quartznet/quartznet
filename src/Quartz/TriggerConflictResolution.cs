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

namespace Quartz;

/// <summary>
/// The one rule every store and scheduler applies to a <see cref="TriggerConflict" />, and the
/// lock-free fallbacks the interfaces' default members run.
/// </summary>
internal static class TriggerConflictResolution
{
    /// <summary>
    /// Refuses a value that is none of the four, which would otherwise be read as <c>Throw</c>.
    /// </summary>
    internal static TriggerConflict RequireDefined(TriggerConflict onConflict, string parameterName)
    {
        if (onConflict is not (TriggerConflict.Throw or TriggerConflict.Replace or TriggerConflict.Keep or TriggerConflict.KeepEarlier))
        {
            Throw.ArgumentOutOfRangeException(parameterName, $"'{onConflict}' is not a TriggerConflict: use Throw, Replace, Keep or KeepEarlier.");
        }

        return onConflict;
    }

    /// <summary>
    /// Whether a trigger already stored has a firing ahead of it — the only kind <c>Keep</c> keeps.
    /// </summary>
    /// <remarks>
    /// A one-shot trigger whose firing has begun has no next fire time; the ADO store reads a missing one
    /// back as <see cref="DateTimeOffset.MinValue" /> on some dialects, which means the same.
    /// </remarks>
    internal static bool IsPending(DateTimeOffset? nextFireTimeUtc)
    {
        return nextFireTimeUtc is { } next && next != DateTimeOffset.MinValue;
    }

    /// <summary>
    /// Whether the trigger already stored stays and the new one is dropped.
    /// </summary>
    /// <param name="onConflict">What the caller asked for.</param>
    /// <param name="existingNextFireTimeUtc">When the stored trigger fires next, if it does.</param>
    /// <param name="newNextFireTimeUtc">When the new trigger would first fire.</param>
    internal static bool KeepsExisting(TriggerConflict onConflict, DateTimeOffset? existingNextFireTimeUtc, DateTimeOffset? newNextFireTimeUtc)
    {
        if (!IsPending(existingNextFireTimeUtc))
        {
            return false;
        }

        return onConflict switch
        {
            TriggerConflict.Keep => true,
            // Ties keep what is there: the new one is not earlier, and replacing would only churn the row.
            TriggerConflict.KeepEarlier => newNextFireTimeUtc is not { } candidate || candidate >= existingNextFireTimeUtc!.Value,
            _ => false,
        };
    }

    /// <summary>
    /// The time a store reports for a trigger it stored: the first fire time the caller computed.
    /// </summary>
    internal static DateTimeOffset FireTimeOf(IOperableTrigger trigger)
    {
        return trigger.NextFireTimeUtc ?? trigger.StartTimeUtc;
    }

    /// <summary>
    /// <see cref="IJobStore.StoreTrigger" /> for a store that has not implemented it, out of the members
    /// it has. Correct, and not atomic: another writer can land between the read and the write.
    /// </summary>
    internal static async ValueTask<ScheduleTriggerResult> StoreWithoutLock(
        IJobStore store,
        IOperableTrigger trigger,
        TriggerConflict onConflict,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        RequireDefined(onConflict, nameof(onConflict));

        if (onConflict == TriggerConflict.Throw)
        {
            await store.AddTrigger(trigger, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new ScheduleTriggerResult(FireTimeOf(trigger), ScheduleOutcome.Created);
        }

        // Twice at most: a trigger created between the read and an insert is read again and decided on.
        for (int attempt = 0; ; attempt++)
        {
            IOperableTrigger? existing = await store.GetTrigger(trigger.Key, cancellationToken).ConfigureAwait(false);

            if (existing is not null && KeepsExisting(onConflict, existing.NextFireTimeUtc, trigger.NextFireTimeUtc))
            {
                return new ScheduleTriggerResult(existing.NextFireTimeUtc!.Value, ScheduleOutcome.Kept);
            }

            try
            {
                await store.AddTrigger(trigger, new AddTriggerOptions { Replace = existing is not null }, cancellationToken).ConfigureAwait(false);
                return new ScheduleTriggerResult(FireTimeOf(trigger), existing is null ? ScheduleOutcome.Created : ScheduleOutcome.Replaced);
            }
            catch (ObjectAlreadyExistsException) when (existing is null && attempt == 0)
            {
            }
        }
    }

    /// <summary>
    /// <see cref="IScheduler.ScheduleTrigger" /> for a scheduler that has not implemented it, out of the
    /// members it has. Correct, and not atomic: another writer can land between the read and the write.
    /// </summary>
    internal static async ValueTask<ScheduleTriggerResult> ScheduleWithoutLock(
        IScheduler scheduler,
        ITrigger trigger,
        TriggerConflict onConflict,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        RequireDefined(onConflict, nameof(onConflict));

        if (onConflict == TriggerConflict.Throw)
        {
            DateTimeOffset created = await scheduler.ScheduleJob(trigger, default(ScheduleJobOptions), cancellationToken).ConfigureAwait(false);
            return new ScheduleTriggerResult(created, ScheduleOutcome.Created);
        }

        DateTimeOffset? firstFireTimeUtc = onConflict == TriggerConflict.KeepEarlier
            ? await FirstFireTimeOf(scheduler, trigger, cancellationToken).ConfigureAwait(false)
            : null;

        for (int attempt = 0; ; attempt++)
        {
            ITrigger? existing = await scheduler.GetTrigger(trigger.Key, cancellationToken).ConfigureAwait(false);

            if (existing is not null && KeepsExisting(onConflict, existing.NextFireTimeUtc, firstFireTimeUtc))
            {
                return new ScheduleTriggerResult(existing.NextFireTimeUtc!.Value, ScheduleOutcome.Kept);
            }

            try
            {
                DateTimeOffset stored = await scheduler.ScheduleJob(
                    trigger,
                    new ScheduleJobOptions { Replace = existing is not null },
                    cancellationToken).ConfigureAwait(false);

                return new ScheduleTriggerResult(stored, existing is null ? ScheduleOutcome.Created : ScheduleOutcome.Replaced);
            }
            catch (ObjectAlreadyExistsException) when (existing is null && attempt == 0)
            {
            }
        }
    }

    /// <summary>
    /// When a trigger not yet stored would first fire, worked out on a copy so the caller's trigger is
    /// handed to the scheduler as it was given.
    /// </summary>
    private static async ValueTask<DateTimeOffset?> FirstFireTimeOf(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken)
    {
        if (trigger.NextFireTimeUtc is { } computed)
        {
            return computed;
        }

        if (trigger.Clone() is not IOperableTrigger copy)
        {
            return trigger.StartTimeUtc;
        }

        ICalendar? calendar = trigger.CalendarName is { } calendarName
            ? await scheduler.GetCalendar(calendarName, cancellationToken).ConfigureAwait(false)
            : null;

        return copy.ComputeFirstFireTimeUtc(calendar);
    }
}
