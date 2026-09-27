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
using Quartz.Impl.Triggers;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// What a trigger's <see cref="OverlapPolicy" /> does on the fire path, and what undoes it.
/// </summary>
/// <remarks>
/// <para>
/// Every decision is made under <c>TRIGGER_ACCESS</c>, in the transaction that fires the trigger, so two
/// nodes reaching for the same trigger decide one after the other and see each other's fired rows.
/// </para>
/// <para>
/// <see cref="OverlapPolicy.BufferOne" /> asks nothing here. Its firing stores the trigger
/// <c>BLOCKED</c> when it starts, exactly as a <see cref="DisallowConcurrentExecutionAttribute" />
/// job's firing does for all of that job's triggers, and its completion lets go of it — so no firing of
/// it is ever acquired while one runs. <see cref="OverlapPolicy.Skip" /> and
/// <see cref="OverlapPolicy.CancelPrevious" /> look at the trigger's running firings when a firing of
/// it comes due. <see cref="OverlapPolicy.Default" /> and <see cref="OverlapPolicy.AllowAll" /> cost
/// nothing.
/// </para>
/// </remarks>
internal abstract partial class AdoJobStoreBase
{
    /// <summary>
    /// Asks a <see cref="OverlapPolicy.Skip" /> or <see cref="OverlapPolicy.CancelPrevious" /> trigger's
    /// policy about the firing it was acquired for.
    /// </summary>
    /// <returns>
    /// <see cref="TriggerFiredResult.Declined" /> when the firing was skipped or held back, with the
    /// trigger already stored where it belongs; otherwise <see langword="null" />, and the firing goes
    /// ahead, having <paramref name="superseded" /> interrupted first when there are any.
    /// </returns>
    private async ValueTask<TriggerFiredResult?> ApplyOverlapPolicy(
        ConnectionAndTransactionHolder conn,
        TriggerBase trigger,
        IJobDetail job,
        ICalendar? calendar,
        List<string> superseded,
        CancellationToken cancellationToken)
    {
        bool running = await Guarded(
            () => Delegate.IsTriggerCurrentlyExecuting(conn, trigger.Key, cancellationToken),
            $"check running firings of trigger '{trigger.Key}'").ConfigureAwait(false);

        if (!running)
        {
            // Nothing of the trigger runs, so every firing from here on starts under the policy the row
            // holds: whatever made it unsettled is over. The fire below writes the row.
            trigger.OverlapPolicyUnsettled = false;
            return null;
        }

        if (trigger.OverlapPolicy == OverlapPolicy.Skip)
        {
            await SkipOverlappingFiring(conn, trigger, job, calendar, cancellationToken).ConfigureAwait(false);
            return TriggerFiredResult.Declined;
        }

        // CancelPrevious. An interrupt reaches only this node's firings; one on another node can only be
        // waited for. It is waited for only when every running firing started under this policy, because
        // it is such a firing's completion that lets go of the trigger — an unsettled one fires beside it.
        List<FiredTriggerRecord> firings = await Guarded(
            () => Delegate.SelectFiredTriggerRecords(conn, new FiredTriggerQuery { Trigger = trigger.Key }, cancellationToken),
            $"read running firings of trigger '{trigger.Key}'").ConfigureAwait(false);

        string? elsewhere = null;
        foreach (FiredTriggerRecord firing in firings)
        {
            if (firing.FireInstanceState != StoredTriggerState.Executing)
            {
                continue;
            }

            if (string.Equals(firing.SchedulerInstanceId, InstanceId, StringComparison.Ordinal))
            {
                superseded.Add(firing.FireInstanceId);
            }
            else
            {
                elsewhere ??= firing.SchedulerInstanceId;
            }
        }

        if (elsewhere is not null && !trigger.OverlapPolicyUnsettled)
        {
            await HoldBehindRunningFiring(conn, trigger, elsewhere, cancellationToken).ConfigureAwait(false);
            return TriggerFiredResult.Declined;
        }

        return null;
    }

    /// <summary>
    /// Drops the firing a <see cref="OverlapPolicy.Skip" /> trigger was acquired for, and moves the
    /// trigger on to the occurrence after it.
    /// </summary>
    /// <remarks>
    /// The occurrence is advanced past as a firing advances past it, so it never falls behind the
    /// misfire threshold and the misfire handler never sees it. A trigger left with nothing to fire is
    /// stored <c>COMPLETE</c>; the running firing's completion deletes it.
    /// </remarks>
    private async ValueTask SkipOverlappingFiring(
        ConnectionAndTransactionHolder conn,
        TriggerBase trigger,
        IJobDetail job,
        ICalendar? calendar,
        CancellationToken cancellationToken)
    {
        Logger.OverlappingFiringSkipped(trigger.Key, trigger.NextFireTimeUtc);

        // As the trigger is now, with the dropped firing still its next one. Announced once the fire
        // transaction commits, never from inside it.
        ITrigger skipped = (ITrigger) trigger.Clone();
        conn.NotifyAfterCommit((notifier, token) => notifier.NotifyTriggerListenersSkipped(skipped, token));

        bool clearMisfireOriginal = trigger.MisfiredFromFireTimeUtc.HasValue;
        trigger.MisfiredFromFireTimeUtc = null;
        trigger.Triggered(calendar);

        StoredTriggerState state = trigger.NextFireTimeUtc.HasValue ? StoredTriggerState.Waiting : StoredTriggerState.Complete;

        await Guarded(
            async () =>
            {
                await Delegate.UpdateTrigger(conn, trigger, state, job, cancellationToken).ConfigureAwait(false);
                if (clearMisfireOriginal)
                {
                    await Delegate.ClearMisfireOriginalFireTime(conn, trigger.Key, cancellationToken).ConfigureAwait(false);
                }

                await Delegate.DeleteFiredTrigger(conn, trigger.FireInstanceId!, cancellationToken).ConfigureAwait(false);
            },
            $"skip the firing of trigger '{trigger.Key}'").ConfigureAwait(false);

        if (state == StoredTriggerState.Complete)
        {
            ITrigger finalized = (ITrigger) trigger.Clone();
            conn.NotifyAfterCommit((notifier, token) => notifier.NotifySchedulerListenersFinalized(finalized, token));
        }

        conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
    }

    /// <summary>
    /// Holds a <see cref="OverlapPolicy.CancelPrevious" /> trigger back until its firing on another node
    /// ends, which is <see cref="OverlapPolicy.BufferOne" />: the firing it was acquired for stays due.
    /// </summary>
    private async ValueTask HoldBehindRunningFiring(
        ConnectionAndTransactionHolder conn,
        TriggerBase trigger,
        string runningOn,
        CancellationToken cancellationToken)
    {
        Logger.OverlappingFiringHeld(trigger.Key, runningOn);

        StoredTriggerState held = await ApplyPausedGroupState(
            conn,
            trigger.Key.Group,
            trigger.JobKey.Group,
            StoredTriggerState.Blocked,
            cancellationToken).ConfigureAwait(false);

        await Guarded(
            async () =>
            {
                await Delegate.UpdateTriggerStateFromOtherState(conn, trigger.Key, held, StoredTriggerState.Acquired, cancellationToken).ConfigureAwait(false);
                await Delegate.DeleteFiredTrigger(conn, trigger.FireInstanceId!, cancellationToken).ConfigureAwait(false);
            },
            $"hold trigger '{trigger.Key}' behind its running firing").ConfigureAwait(false);
    }

    /// <summary>
    /// Lets go of a trigger a <see cref="OverlapPolicy.BufferOne" /> or
    /// <see cref="OverlapPolicy.CancelPrevious" /> firing of it held back, and applies the misfire
    /// instruction to what came due meanwhile.
    /// </summary>
    /// <remarks>
    /// Two statements that match nothing when nothing is held, which is the common case for
    /// <see cref="OverlapPolicy.CancelPrevious" />; issued only for firings of those two policies.
    /// </remarks>
    private async ValueTask ReleaseOverlapHold(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken)
    {
        int released = await Delegate.UpdateTriggerStateFromOtherState(conn, triggerKey, StoredTriggerState.Waiting, StoredTriggerState.Blocked, cancellationToken).ConfigureAwait(false);
        await Delegate.UpdateTriggerStateFromOtherState(conn, triggerKey, StoredTriggerState.Paused, StoredTriggerState.PausedBlocked, cancellationToken).ConfigureAwait(false);

        if (released > 0)
        {
            conn.SignalSchedulingChangeOnTxCompletion = SchedulerConstants.SchedulingSignalDateTime;
            await RecoverReleasedMisfires(conn, [triggerKey], cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Whether a trigger about to be stored with <paramref name="policy" /> is unsettled: given
    /// <see cref="OverlapPolicy.CancelPrevious" /> while a firing of it that may have started under
    /// another policy is running.
    /// </summary>
    /// <param name="conn">The unit of work the trigger is being stored in.</param>
    /// <param name="stored">The trigger as its row holds it now.</param>
    /// <param name="policy">The policy about to be written.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <remarks>
    /// A firing that started under another policy does not let go of the trigger when it ends, so a
    /// firing on another node must not be waited for while one may be running. A row that already held
    /// a settled <see cref="OverlapPolicy.CancelPrevious" /> has no such firing, and neither has a
    /// trigger with nothing running.
    /// </remarks>
    private async ValueTask<bool> IsOverlapPolicyUnsettled(
        ConnectionAndTransactionHolder conn,
        TriggerBase stored,
        OverlapPolicy policy,
        CancellationToken cancellationToken)
    {
        if (policy != OverlapPolicy.CancelPrevious)
        {
            return false;
        }

        if (stored.OverlapPolicy == OverlapPolicy.CancelPrevious && !stored.OverlapPolicyUnsettled)
        {
            return false;
        }

        return await Delegate.IsTriggerCurrentlyExecuting(conn, stored.Key, cancellationToken).ConfigureAwait(false);
    }
}
