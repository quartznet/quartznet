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
/// <remarks>
/// <para>
/// The shape is frozen at six members, and each is here because exactly one caller in the scheduler
/// needs it — this looks like a lot of interface for a pool whose configuration is one integer, and the
/// integer is not what it is for. <see cref="Initialize" /> is called once by the scheduler factory
/// before the pool is used; <see cref="WaitForAvailableThreads" /> is what the scheduling loop asks to
/// size its next batch of triggers; <see cref="TryRun" /> is how every firing reaches a thread;
/// <see cref="PoolSize" /> is what <c>SchedulerMetadata.ThreadPoolSize</c> reports;
/// <see cref="Shutdown" /> ends the pool; and <see cref="Drain" /> is the same ending with a deadline,
/// which <see cref="Shutdown" /> cannot express. Removing any of them removes a thing the scheduler
/// does, not a spelling of a thing it already does.
/// </para>
/// <para>
/// Only <see cref="Drain" /> has a default implementation, and that is because it arrived after the
/// others: a pool written against the earlier interface is left correct rather than fast.
/// </para>
/// </remarks>
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

    /// <summary>
    /// Stops the pool accepting new work, waits for the work already running to finish, and frees the
    /// pool's resources.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bounded form of <see cref="Shutdown" /> with <c>waitForJobsToComplete: true</c>: the wait
    /// ends when <paramref name="cancellationToken" /> fires, and the outcome is reported rather than
    /// thrown. That is what lets a caller say "drain, but give up after this long" and still carry on
    /// with the rest of its own shutdown — which <see cref="Shutdown" /> cannot express, because a wait
    /// it abandoned by throwing would skip everything the caller still has to tear down.
    /// </para>
    /// <para>
    /// The wait must not block the calling thread. Callers include a host's graceful-shutdown path, and
    /// an implementation that blocks pins a thread for as long as the slowest job runs.
    /// </para>
    /// <para>
    /// Giving up abandons the wait, never the work: running work is not cancelled, because the pool has
    /// no means to interrupt it and whether a shutting-down scheduler interrupts its jobs is
    /// <see cref="ShutdownJobInterruption" />'s decision, already made by the time this is called. The
    /// pool is left shut down either way, so the answer says what was true when it stopped waiting, not
    /// what the caller should do about it.
    /// </para>
    /// <para>
    /// The barrier has to cover everything a work item does, and not merely the part of it a caller can
    /// see: <see cref="TryRun" /> is handed the whole of a job's execution, of which the last act is the
    /// job store update that completes the trigger. A pool that waits for its work items therefore waits
    /// for those writes too — which a count of executing jobs does not, since a job leaves that count
    /// before its store update is issued.
    /// </para>
    /// <para>
    /// The default implementation calls <see cref="Shutdown" /> with <c>waitForJobsToComplete: true</c>,
    /// whose wait cannot be given up on, and so it can only report that it drained. That keeps a pool
    /// written before this member existed correct rather than fast; override it to honour a deadline.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the wait, not the running work.</param>
    /// <returns><see langword="true" /> if the work that was running finished; <see langword="false" />
    /// if <paramref name="cancellationToken" /> fired first and work is still running.</returns>
    async ValueTask<bool> Drain(CancellationToken cancellationToken = default)
    {
        // The token is dropped rather than forwarded. Shutdown's contract is to wait for the running jobs
        // however long they take, so a pool that honoured a token there would throw where this member says
        // report - and this fallback exists precisely for pools written before it existed.
        await Shutdown(waitForJobsToComplete: true, CancellationToken.None).ConfigureAwait(false);
        return true;
    }
}
