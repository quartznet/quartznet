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

using Quartz.Core;

namespace Quartz.Extensibility;

/// <summary>
/// An interface to be used by <see cref="IJobStore" /> instances in order to
/// communicate signals back to the <see cref="QuartzScheduler" />.
/// </summary>
/// <remarks>
/// Every member here runs scheduler and listener code on the calling thread, and a listener is
/// entitled to do what any other caller does: pause a trigger, reschedule one, ask the store a
/// question. That call comes straight back into the store that signalled. A store whose lock is not
/// re-entrant therefore has to release it first, or the listener deadlocks against the caller it is
/// running on: <see cref="Quartz.Impl.RAMJobStore" /> collects what it owes while it holds its
/// semaphore and raises it once the lock is gone, in the order it recorded it. A store holding a
/// database lock is re-entrant for its own connection by construction, but what it announces from
/// inside a transaction may still roll back, so it too prefers to notify after the commit.
/// </remarks>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public interface ISchedulerSignaler
{
    /// <summary>
    /// Notifies the scheduler about misfired trigger.
    /// </summary>
    /// <param name="trigger">The trigger that misfired.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask NotifyTriggerListenersMisfired(
        ITrigger trigger,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the scheduler about finalized trigger.
    /// </summary>
    /// <param name="trigger">The trigger that has finalized.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask NotifySchedulerListenersFinalized(
        ITrigger trigger,
        CancellationToken cancellationToken = default);

    ValueTask NotifySchedulerListenersJobDeleted(
        JobKey jobKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the scheduler that a trigger has been parked in the <see cref="TriggerState.Error" />
    /// state and will not fire again until it is reset.
    /// </summary>
    /// <remarks>
    /// Default-implemented as a no-op so that an existing signaler keeps compiling; a job store that
    /// calls it against one gets today's behaviour, which is no notification at all.
    /// </remarks>
    ValueTask NotifySchedulerListenersTriggerInError(
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Notifies the scheduler that every trigger of a job has been parked in the
    /// <see cref="TriggerState.Error" /> state.
    /// </summary>
    /// <remarks>
    /// Default-implemented as a no-op, for the same reason as
    /// <see cref="NotifySchedulerListenersTriggerInError" />.
    /// </remarks>
    ValueTask NotifySchedulerListenersTriggersInError(
        JobKey jobKey,
        CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Signals the scheduling change.
    /// </summary>
    ValueTask SignalSchedulingChange(
        DateTimeOffset? candidateNewNextFireTimeUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Informs scheduler listeners about an exception that has occurred.
    /// </summary>
    /// <remarks>
    /// The context is how a job store reports which trigger, job or firing the failure was for; a store
    /// that knows none of them fills in <see cref="SchedulerErrorContext.Message" /> and
    /// <see cref="SchedulerErrorContext.Exception" /> alone.
    /// </remarks>
    /// <param name="errorContext">What went wrong, and what it went wrong for.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask NotifySchedulerListenersError(
        SchedulerErrorContext errorContext,
        CancellationToken cancellationToken = default);
}