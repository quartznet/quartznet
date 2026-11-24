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

namespace Quartz.Impl;

/// <summary>
/// A scheduler that can wait for how a firing of one of its triggers ends (#4414).
/// </summary>
/// <remarks>
/// <para>
/// What <see cref="SchedulerWaitExtensions" /> needs and no <see cref="IScheduler" /> member says: a wait that
/// begins before the trigger is stored, answered by the scheduler's own completion where it runs here, and by
/// its execution history and the trigger's state where it may not.
/// </para>
/// <para>
/// Internal, as <see cref="IBackfillingScheduler" /> is: the scheduler Quartz builds, the container's
/// <see cref="IScheduler" />, <see cref="DelegatingScheduler" /> and <c>HttpScheduler</c> implement it, and the
/// extensions are the one caller. A scheduler without it is refused with <see cref="NotSupportedException" />.
/// </para>
/// </remarks>
internal interface IFiringWaitingScheduler
{
    /// <summary>
    /// Starts a wait for the next final completion of <paramref name="triggerKey" />.
    /// </summary>
    /// <param name="triggerKey">The trigger whose firing to wait for.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <exception cref="SchedulerException">The scheduler has been shut down.</exception>
    /// <exception cref="NotSupportedException">
    /// The scheduler is clustered and records no execution history the other nodes write, so a firing that
    /// ran on another node could not be told.
    /// </exception>
    ValueTask<FiringWatch> Watch(TriggerKey triggerKey, CancellationToken cancellationToken = default);
}
