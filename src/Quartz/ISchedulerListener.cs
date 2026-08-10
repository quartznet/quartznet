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
/// The interface to be implemented by classes that want to be informed of major
/// <see cref="IScheduler" /> events.
/// </summary>
/// <remarks>
/// Every member has a default implementation, so an implementation only has to write the
/// notifications it cares about. <see cref="Name" /> defaults to the implementing type's name.
/// </remarks>
/// <seealso cref="IScheduler" />
/// <seealso cref="IJobListener" />
/// <seealso cref="ITriggerListener" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public interface ISchedulerListener
{
    /// <summary>
    /// The name this listener is registered and removed under.
    /// </summary>
    /// <remarks>
    /// Defaults to the implementing type's name, which is the right answer whenever a scheduler
    /// has at most one listener of a given type. Override it when several instances of one type
    /// are registered with the same scheduler, because the later registration would otherwise
    /// replace the earlier one.
    /// </remarks>
    string Name => GetType().Name;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
    /// is scheduled.
    /// </summary>
    ValueTask JobScheduled(ITrigger trigger, CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
    /// is unscheduled.
    /// </summary>
    /// <seealso cref="SchedulingDataCleared"/>
    ValueTask JobUnscheduled(TriggerKey triggerKey, CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
    /// has reached the condition in which it will never fire again.
    /// </summary>
    ValueTask TriggerFinalized(ITrigger trigger, CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler"/> a <see cref="ITrigger"/>s has been paused.
    /// </summary>
    ValueTask TriggerPaused(TriggerKey triggerKey, CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler"/> a group of
    /// <see cref="ITrigger"/>s has been paused.
    /// </summary>
    /// <remarks>
    /// A null <paramref name="triggerGroup" /> means every group.
    /// </remarks>
    /// <param name="triggerGroup">The trigger group, or null for all groups.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask TriggersPaused(string? triggerGroup, CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler"/> when a <see cref="ITrigger"/>
    /// has been un-paused.
    /// </summary>
    ValueTask TriggerResumed(TriggerKey triggerKey, CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler"/> when a
    /// group of <see cref="ITrigger"/>s has been un-paused.
    /// </summary>
    /// <remarks>
    /// A null <paramref name="triggerGroup" /> means every group.
    /// </remarks>
    /// <param name="triggerGroup">The trigger group, or null for all groups.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask TriggersResumed(string? triggerGroup, CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
    /// has been added.
    /// </summary>
    ValueTask JobAdded(IJobDetail jobDetail, CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
    /// has been deleted.
    /// </summary>
    ValueTask JobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler"/> when a <see cref="IJobDetail"/>
    /// has been  paused.
    /// </summary>
    ValueTask JobPaused(JobKey jobKey, CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler"/> when a <see cref="IJobDetail"/>
    /// has been interrupted.
    /// </summary>
    ValueTask JobInterrupted(JobKey jobKey, CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler"/> when a
    /// group of <see cref="IJobDetail"/>s has been  paused.
    /// </summary>
    /// <remarks>
    /// A null <paramref name="jobGroup" /> means every group, matching
    /// <see cref="TriggersPaused" />. The scheduler's own pause-all path raises this once per
    /// group rather than once with no group, but a job store or a caller raising the event itself
    /// may report all groups that way.
    /// </remarks>
    /// <param name="jobGroup">The job group, or null for all groups.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask JobsPaused(string? jobGroup, CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
    /// has been  un-paused.
    /// </summary>
    ValueTask JobResumed(JobKey jobKey, CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a group of <see cref="IJobDetail" />s has
    /// been un-paused.
    /// </summary>
    /// <remarks>
    /// A null <paramref name="jobGroup" /> means every group, matching
    /// <see cref="TriggersResumed" />.
    /// </remarks>
    /// <param name="jobGroup">The job group, or null for all groups.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask JobsResumed(string? jobGroup, CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a serious error has
    /// occurred within the scheduler - such as repeated failures in the <see cref="IJobStore" />,
    /// or the inability to instantiate a <see cref="IJob" /> instance when its
    /// <see cref="ITrigger" /> has fired.
    /// </summary>
    ValueTask SchedulerError(
        string message,
        SchedulerException exception,
        CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" /> has moved into the
    /// <see cref="TriggerState.Error" /> state and will not fire again until it is reset with
    /// <see cref="IScheduler.ResetTriggerFromErrorState" />.
    /// </summary>
    /// <remarks>
    /// This says what changed, not why. Where a cause exists it arrives separately through
    /// <see cref="SchedulerError" /> — as a <see cref="JobInstantiationException" />, for a job
    /// that could not be built. Some transitions have no scheduler-side cause at all: the job store
    /// also parks a trigger here when it cannot load the job's type or read the job back.
    /// <para>
    /// The default implementation does nothing.
    /// </para>
    /// </remarks>
    ValueTask TriggerInError(TriggerKey triggerKey, CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when every <see cref="ITrigger" /> of a job has moved
    /// into the <see cref="TriggerState.Error" /> state, which is what a failure to instantiate the
    /// job leads to.
    /// </summary>
    /// <remarks>
    /// Keyed by job rather than by trigger because that is the shape of the underlying operation —
    /// the persistent store updates the job's triggers in one statement and never enumerates them.
    /// Call <see cref="SchedulerQueryExtensions.GetTriggersOfJob" /> if the individual keys matter.
    /// <para>
    /// The default implementation does nothing.
    /// </para>
    /// </remarks>
    ValueTask TriggersInError(JobKey jobKey, CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> to inform the listener
    /// that it has move to standby mode.
    /// </summary>
    ValueTask SchedulerInStandbyMode(CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> to inform the listener
    /// that it has started.
    /// </summary>
    ValueTask SchedulerStarted(CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> to inform the listener that it is starting.
    /// </summary>
    ValueTask SchedulerStarting(CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> to inform the listener
    /// that it has Shutdown.
    /// </summary>
    ValueTask SchedulerShutdown(CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> to inform the listener
    /// that it has begun the shutdown sequence.
    /// </summary>
    ValueTask SchedulerShuttingDown(CancellationToken cancellationToken = default) => default;

    /// <summary>
    /// Called by the <see cref="IScheduler" /> to inform the listener
    /// that all jobs, triggers and calendars were deleted.
    /// </summary>
    ValueTask SchedulingDataCleared(CancellationToken cancellationToken = default) => default;
}