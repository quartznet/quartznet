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

namespace Quartz;

/// <summary>
/// Which trigger's firing a trigger waits for, and on which outcomes of it the wait ends.
/// </summary>
/// <remarks>
/// <para>
/// A trigger carrying a continuation is stored in <see cref="TriggerState.Awaiting" /> and is never
/// acquired while it is there. The parent's completion settles it, inside the parent's own lock and
/// transaction: a matching outcome releases the trigger into the ordinary schedule, and any other
/// outcome deletes it. Whichever node completes the parent is the node that settles, so a
/// continuation survives the failure of the node that scheduled it. A one-shot parent lost with the
/// node <em>running</em> it is deleted by cluster recovery, so its continuations are settled as for a
/// deleted parent, and the recovery firing — under a key of its own — settles nothing.
/// </para>
/// <para>
/// The parent is a <see cref="TriggerKey" /> rather than a <see cref="JobKey" /> because a
/// continuation waits for one <em>firing</em>, and a job may be fired by several triggers. The
/// one-call scheduling overloads answer with the key to use as
/// <see cref="ScheduledOneOffJob.TriggerKey" />.
/// </para>
/// <para>
/// <see cref="ITrigger.StartTimeUtc" /> stays a floor rather than a schedule: releasing a
/// continuation sets its next fire time to the later of "now" and its start time, so
/// "an hour after the import, and never before nine" is expressible. A calendar the trigger names
/// moves that instant on to the next one it includes, and a continuation whose
/// <see cref="ITrigger.EndTimeUtc" /> is behind it by then is discarded rather than released.
/// </para>
/// </remarks>
/// <param name="Parent">
/// The trigger whose firing this one waits for, or <see langword="null" /> when the trigger waits
/// for nothing.
/// </param>
/// <param name="When">The outcomes of that firing which release the wait.</param>
/// <seealso cref="ITrigger.Continuation" />
/// <seealso cref="ContinuationCondition" />
public readonly record struct Continuation(TriggerKey? Parent, ContinuationCondition When)
{
    /// <summary>
    /// No continuation: the trigger fires on its own schedule and waits for nothing. The default.
    /// </summary>
    public static Continuation None => default;

    /// <summary>
    /// Wait for the given trigger's firing, and be released when it ends in one of the named ways.
    /// </summary>
    /// <remarks>
    /// The parent has to be in the store when the trigger carrying this is stored, or the store
    /// refuses it with <see cref="ObjectDoesNotExistException" /> naming both keys. A one-shot parent
    /// is deleted once it has fired, so schedule the continuation before the parent can finish, or
    /// check the parent's key.
    /// </remarks>
    /// <param name="parent">The trigger whose firing to wait for.</param>
    /// <param name="condition">
    /// The outcomes that release the wait. Any other outcome discards the continuation, so
    /// <see cref="ContinuationCondition.OnSuccess" /> — the default — means "run this only if that
    /// one worked".
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="parent" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="condition" /> names no outcome at all, or names a value that is not an
    /// outcome. A condition nothing satisfies would discard the continuation whatever the parent
    /// did, which is a schedule nobody means to write.
    /// </exception>
    public static Continuation After(TriggerKey parent, ContinuationCondition condition = ContinuationCondition.OnSuccess)
    {
        if (parent is null)
        {
            Throw.ArgumentNullException(nameof(parent));
        }

        if (!IsSatisfiable(condition))
        {
            Throw.ArgumentOutOfRangeException(
                nameof(condition),
                $"A continuation is released by at least one outcome; '{condition}' names none, or names something that is not one. "
                + $"Use {nameof(ContinuationCondition)}.{nameof(ContinuationCondition.OnAnyOutcome)} to wait for the firing to end however it ends.");
        }

        return new Continuation(parent, condition);
    }

    /// <summary>
    /// Whether a condition names at least one outcome, and nothing that is not one.
    /// </summary>
    private static bool IsSatisfiable(ContinuationCondition condition)
    {
        int bits = (int) condition;
        return bits != 0 && (bits & ~(int) ContinuationCondition.OnAnyOutcome) == 0;
    }

    /// <summary>
    /// Whether the trigger waits for nothing, which is what a trigger with an ordinary schedule
    /// carries.
    /// </summary>
    public bool IsNone => Parent is null;

    /// <summary>
    /// The condition an outcome satisfies, or <see langword="null" /> for one that satisfies none —
    /// which <see cref="ExecutionOutcome.NotExecuted" /> is, because there was no occurrence to
    /// settle.
    /// </summary>
    /// <remarks>
    /// The whole of the matching rule, shared by both job stores so that neither can answer
    /// differently: the in-memory one tests it against the value, the ADO.NET one against the
    /// integer its row carries.
    /// </remarks>
    internal static ContinuationCondition? ConditionFor(ExecutionOutcome outcome)
    {
        return outcome switch
        {
            ExecutionOutcome.Succeeded => ContinuationCondition.OnSuccess,
            ExecutionOutcome.Failed => ContinuationCondition.OnFailure,
            ExecutionOutcome.Cancelled => ContinuationCondition.OnCancellation,
            ExecutionOutcome.Vetoed => ContinuationCondition.OnVeto,
            _ => null
        };
    }

    /// <summary>
    /// When a continuation released at <paramref name="now" /> fires, or <see langword="null" /> when
    /// it has no firing left and is discarded rather than released.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The later of now and the trigger's start time — which is what keeps the start time a floor —
    /// moved on to the calendar's next included instant when the trigger's calendar excludes it. A
    /// trigger whose end time is behind that instant can never fire, which is the position a condition
    /// the outcome did not name leaves a continuation in too, so both are discarded the same way.
    /// </para>
    /// <para>
    /// Shared by both job stores so that neither can answer differently: the in-memory one hands in
    /// the calendar it holds, the ADO.NET one the calendar it reads for the row it is releasing.
    /// </para>
    /// </remarks>
    /// <param name="trigger">The continuation being released.</param>
    /// <param name="calendar">The calendar the trigger names, or <see langword="null" /> for none.</param>
    /// <param name="now">The store's reading of "now".</param>
    internal static DateTimeOffset? ReleaseFireTime(ITrigger trigger, ICalendar? calendar, DateTimeOffset now)
    {
        DateTimeOffset fireTime = trigger.StartTimeUtc > now ? trigger.StartTimeUtc : now;

        if (calendar is not null && !calendar.IsTimeIncluded(fireTime))
        {
            try
            {
                fireTime = calendar.GetNextIncludedTimeUtc(fireTime);
            }
            catch (SchedulerException)
            {
                // A calendar that includes no instant at all says so by throwing — CronCalendar over an
                // expression that matches every second does. Such a trigger has nothing to fire at, and
                // letting the exception out would fail the parent's completion over and over rather than
                // settle it.
                return null;
            }
        }

        return trigger.EndTimeUtc is { } end && fireTime > end ? null : fireTime;
    }

    /// <summary>
    /// The value as the triggers table holds it: the condition column, which is the integer of
    /// <see cref="When" /> for a continuation and <see langword="null" /> for a trigger that waits
    /// for nothing.
    /// </summary>
    internal int? StoredCondition => Parent is null ? null : (int) When;

    /// <summary>
    /// Rebuilds the value from the triple stored in the triggers table. A row naming no parent is
    /// <see cref="None" />, whatever the condition column holds.
    /// </summary>
    internal static Continuation FromStored(string? parentName, string? parentGroup, int? condition)
    {
        if (string.IsNullOrWhiteSpace(parentName))
        {
            return default;
        }

        // A condition a row is missing, or one this version cannot read, is read as "however it
        // ends": the alternative is a continuation nothing can release, which is a job that silently
        // never runs.
        ContinuationCondition when = condition is { } stored && IsSatisfiable((ContinuationCondition) stored)
            ? (ContinuationCondition) stored
            : ContinuationCondition.OnAnyOutcome;

        return new Continuation(new TriggerKey(parentName!, parentGroup ?? TriggerKey.DefaultGroup), when);
    }

    /// <inheritdoc />
    public override string ToString() => Parent is null ? "none" : $"after {Parent} ({When})";
}
