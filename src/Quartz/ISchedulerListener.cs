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

using Quartz.Spi;

namespace Quartz;

/// <summary>
/// The interface to be implemented by classes that want to be informed of major
/// <see cref="IScheduler" /> events.
/// </summary>
/// <seealso cref="IScheduler" />
/// <seealso cref="IJobListener" />
/// <seealso cref="ITriggerListener" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public interface ISchedulerListener
{
    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
    /// is scheduled.
    /// </summary>
    ValueTask JobScheduled(ITrigger trigger, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
    /// is unscheduled.
    /// </summary>
    /// <seealso cref="SchedulingDataCleared"/>
    ValueTask JobUnscheduled(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="ITrigger" />
    /// has reached the condition in which it will never fire again.
    /// </summary>
    ValueTask TriggerFinalized(ITrigger trigger, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the <see cref="IScheduler"/> a <see cref="ITrigger"/>s has been paused.
    /// </summary>
    ValueTask TriggerPaused(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the <see cref="IScheduler"/> a group of
    /// <see cref="ITrigger"/>s has been paused.
    /// </summary>
    /// <remarks>
    /// If a all groups were paused, then the <see param="triggerName"/> parameter
    /// will be null.
    /// </remarks>
    /// <param name="triggerGroup">The trigger group.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask TriggersPaused(string? triggerGroup, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the <see cref="IScheduler"/> when a <see cref="ITrigger"/>
    /// has been un-paused.
    /// </summary>
    ValueTask TriggerResumed(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the <see cref="IScheduler"/> when a
    /// group of <see cref="ITrigger"/>s has been un-paused.
    /// </summary>
    /// <remarks>
    /// If all groups were resumed, then the <see param="triggerName"/> parameter
    /// will be null.
    /// </remarks>
    /// <param name="triggerGroup">The trigger group.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask TriggersResumed(string? triggerGroup, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
    /// has been added.
    /// </summary>
    ValueTask JobAdded(IJobDetail jobDetail, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
    /// has been deleted.
    /// </summary>
    ValueTask JobDeleted(JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the <see cref="IScheduler"/> when a <see cref="IJobDetail"/>
    /// has been  paused.
    /// </summary>
    ValueTask JobPaused(JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the <see cref="IScheduler"/> when a <see cref="IJobDetail"/>
    /// has been interrupted.
    /// </summary>
    ValueTask JobInterrupted(JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the <see cref="IScheduler"/> when a
    /// group of <see cref="IJobDetail"/>s has been  paused.
    /// <para>
    /// If all groups were paused, then the <see param="jobName"/> parameter will be
    /// null. If all jobs were paused, then both parameters will be null.
    /// </para>
    /// </summary>
    /// <param name="jobGroup">The job group.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask JobsPaused(string jobGroup, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
    /// has been  un-paused.
    /// </summary>
    ValueTask JobResumed(JobKey jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a <see cref="IJobDetail" />
    /// has been  un-paused.
    /// </summary>
    /// <param name="jobGroup">The job group.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask JobsResumed(string jobGroup, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the <see cref="IScheduler" /> when a serious error has
    /// occurred within the scheduler - such as repeated failures in the <see cref="IJobStore" />,
    /// or the inability to instantiate a <see cref="IJob" /> instance when its
    /// <see cref="ITrigger" /> has fired.
    /// </summary>
    ValueTask SchedulerError(
        string msg,
        SchedulerException cause,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the <see cref="IScheduler" /> to inform the listener
    /// that it has move to standby mode.
    /// </summary>
    ValueTask SchedulerInStandbyMode(CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the <see cref="IScheduler" /> to inform the listener
    /// that it has started.
    /// </summary>
    ValueTask SchedulerStarted(CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the <see cref="IScheduler" /> to inform the listener that it is starting.
    /// </summary>
    ValueTask SchedulerStarting(CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the <see cref="IScheduler" /> to inform the listener
    /// that it has Shutdown.
    /// </summary>
    ValueTask SchedulerShutdown(CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the <see cref="IScheduler" /> to inform the listener
    /// that it has begun the shutdown sequence.
    /// </summary>
    ValueTask SchedulerShuttingdown(CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the <see cref="IScheduler" /> to inform the listener
    /// that all jobs, triggers and calendars were deleted.
    /// </summary>
    ValueTask SchedulingDataCleared(CancellationToken cancellationToken = default);
}