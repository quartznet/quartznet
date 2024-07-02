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

namespace Quartz.Spi;

/// <summary>
/// An interface to be used by <see cref="IJobStore" /> instances in order to
/// communicate signals back to the <see cref="QuartzScheduler" />.
/// </summary>
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
    /// Signals the scheduling change.
    /// </summary>
    ValueTask SignalSchedulingChange(
        DateTimeOffset? candidateNewNextFireTimeUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Informs scheduler listeners about an exception that has occurred.
    /// </summary>
    ValueTask NotifySchedulerListenersError(
        string message,
        SchedulerException jpe,
        CancellationToken cancellationToken = default);
}