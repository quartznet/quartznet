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
/// The interface to be implemented by classes that want to provide a thread
/// pool for the <see cref="IScheduler" />'s use.
/// </summary>
/// <remarks>
/// <see cref="IThreadPool" /> implementation instances should ideally be made
/// for the sole use of Quartz.  Most importantly, when the method
///  <see cref="WaitForAvailableThreads" /> returns a value of 1 or greater,
/// there must still be at least one available thread in the pool when the
/// method  <see cref="TryRun"/> is called a few moments (or
/// many moments) later.  If this assumption does not hold true, it may
/// result in extra JobStore queries and updates, and if clustering features
/// are being used, it may result in greater imbalance of load.
/// </remarks>
/// <seealso cref="QuartzScheduler" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public interface IThreadPool
{
    /// <summary>
    /// Get the current number of threads in the <see cref="IThreadPool" />.
    /// </summary>
    int PoolSize { get; }

    /// <summary>
    /// Must be called before the thread pool is
    /// used, in order to give the it a chance to Initialize.
    /// </summary>
    /// <remarks>
    /// Typically called by the <see cref="ISchedulerFactory" />.
    /// </remarks>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask Initialize(CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines the number of execution slots that are currently available in
    /// the pool. The scheduler uses the count to size the batch of triggers it
    /// acquires next.
    /// </summary>
    ///<remarks>
    /// The implementation of this method should wait until there is at
    /// least one available slot. It is awaited by the scheduler's own loop, so an
    /// implementation must not block the calling thread while it waits.
    ///</remarks>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>the number of currently available execution slots</returns>
    ValueTask<int> WaitForAvailableThreads(CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules the given work to run as soon as the pool's concurrency
    /// rules allow it.
    /// </summary>
    /// <remarks>
    /// The implementation of this interface should not throw exceptions unless
    /// there is a serious problem (i.e. a serious misconfiguration). If there
    /// are no available slots, rather it should either queue the action, or
    /// wait until a slot is available, depending on the desired strategy.
    /// </remarks>
    /// <param name="action">The work to run.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns><see langword="true" /> if the work was scheduled; otherwise, <see langword="false" />
    /// (the pool has been shut down or was never initialized).</returns>
    ValueTask<bool> TryRun(Func<ValueTask> action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Called by the QuartzScheduler to inform the thread pool
    /// that it should free up all of it's resources because the scheduler is
    /// shutting down.
    /// </summary>
    /// <param name="waitForJobsToComplete">Whether to wait for executing jobs to finish first.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask Shutdown(bool waitForJobsToComplete = true, CancellationToken cancellationToken = default);
}
