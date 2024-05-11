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

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Spi;

namespace Quartz.Simpl;

/// <summary>
/// This is class is a simple implementation of a zero size thread pool, based on the
/// <see cref="IThreadPool" /> interface.
/// </summary>
/// <remarks>
/// The pool has zero <see cref="Thread" />s and does not grow or shrink based on demand.
/// Which means it is obviously not useful for most scenarios.  When it may be useful
/// is to prevent creating any worker threads at all - which may be desirable for
/// the sole purpose of preserving system resources in the case where the scheduler
/// instance only exists in order to schedule jobs, but which will never execute
/// jobs (e.g. will never have Start() called on it).
/// </remarks>
/// <author>Wayne Fay</author>
/// <author>Marko Lahma (.NET)</author>
public sealed class ZeroSizeThreadPool : IThreadPool
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ZeroSizeThreadPool"/> class.
    /// </summary>
    public ZeroSizeThreadPool()
    {
        logger = LogProvider.CreateLogger<ZeroSizeThreadPool>();
    }

    /// <summary>
    /// Gets the log.
    /// </summary>
    /// <value>The log.</value>
    private ILogger logger { get; }

    /// <summary>
    /// Gets the size of the pool.
    /// </summary>
    /// <value>The size of the pool.</value>
    public int PoolSize => 0;

    /// <summary>
    /// Inform the <see cref="IThreadPool" /> of the Scheduler instance's Id,
    /// prior to initialize being invoked.
    /// </summary>
    public string InstanceId
    {
        set { }
    }

    /// <summary>
    /// Inform the <see cref="IThreadPool" /> of the Scheduler instance's name,
    /// prior to initialize being invoked.
    /// </summary>
    public string InstanceName
    {
        set { }
    }

    /// <summary>
    /// Called by the QuartzScheduler before the thread pool is
    /// used, in order to give the it a chance to Initialize.
    /// </summary>
    public void Initialize()
    {
    }

    /// <summary>
    /// Called by the QuartzScheduler to inform the thread pool
    /// that it should free up all of it's resources because the scheduler is
    /// shutting down.
    /// </summary>
    /// <param name="waitForJobsToComplete"></param>
    public void Shutdown(bool waitForJobsToComplete = true)
    {
        logger.LogDebug("Shutdown complete");
    }

    /// <summary>
    /// Execute the given <see cref="Task" /> in the next
    /// available <see cref="Thread" />.
    /// </summary>
    /// <remarks>
    /// The implementation of this interface should not throw exceptions unless
    /// there is a serious problem (i.e. a serious misconfiguration). If there
    /// are no available threads, rather it should either queue the Runnable, or
    /// block until a thread is available, depending on the desired strategy.
    /// </remarks>
    public bool RunInThread(Func<Task> runnable)
    {
        ThrowHelper.ThrowNotSupportedException("This ThreadPool should not be used on Scheduler instances that are started.");
        return false;
    }

    /// <summary>
    /// Determines the number of threads that are currently available in
    /// the pool.  Useful for determining the number of times
    /// <see cref="RunInThread"/>  can be called before returning
    /// false.
    /// </summary>
    /// <returns>
    /// the number of currently available threads
    /// </returns>
    /// <remarks>
    /// The implementation of this method should block until there is at
    /// least one available thread.
    /// </remarks>
    public int BlockForAvailableThreads()
    {
        ThrowHelper.ThrowNotSupportedException("This ThreadPool should not be used on Scheduler instances that are started.");
        return default;
    }
}