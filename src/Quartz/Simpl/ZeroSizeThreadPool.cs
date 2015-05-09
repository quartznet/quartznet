#region License
/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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

using System;
using System.Threading;

using Common.Logging;

using Quartz.Spi;

namespace Quartz.Simpl
{
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
    public class ZeroSizeThreadPool : IThreadPool
    {
        private readonly ILog log;

        /// <summary>
        /// Initializes a new instance of the <see cref="ZeroSizeThreadPool"/> class.
        /// </summary>
        public ZeroSizeThreadPool()
        {
            log = LogManager.GetLogger(GetType());
        }

        /// <summary>
        /// Gets the log.
        /// </summary>
        /// <value>The log.</value>
        protected virtual ILog Log
        {
            get { return log; }
        }

        /// <summary>
        /// Gets the size of the pool.
        /// </summary>
        /// <value>The size of the pool.</value>
        public virtual int PoolSize
        {
            get { return 0; }
        }

        /// <summary>
        /// Inform the <see cref="IThreadPool" /> of the Scheduler instance's Id, 
        /// prior to initialize being invoked.
        /// </summary>
        public virtual string InstanceId
        {
            set { }
        }

        /// <summary>
        /// Inform the <see cref="IThreadPool" /> of the Scheduler instance's name, 
        /// prior to initialize being invoked.
        /// </summary>
        public virtual string InstanceName
        {
            set { }
        }

        /// <summary>
        /// Called by the QuartzScheduler before the <see cref="ThreadPool"/> is
        /// used, in order to give the it a chance to Initialize.
        /// </summary>
        public virtual void Initialize()
        {
        }

        /// <summary>
        /// Shutdowns this instance.
        /// </summary>
        public virtual void Shutdown()
        {
            Shutdown(true);
        }

        /// <summary>
        /// Called by the QuartzScheduler to inform the <see cref="ThreadPool"/>
        /// that it should free up all of it's resources because the scheduler is
        /// shutting down.
        /// </summary>
        /// <param name="waitForJobsToComplete"></param>
        public virtual void Shutdown(bool waitForJobsToComplete)
        {
            Log.Debug("shutdown complete");
        }

        /// <summary>
        /// Execute the given <see cref="IThreadRunnable"/> in the next
        /// available <see cref="Thread"/>.
        /// </summary>
        /// <param name="runnable"></param>
        /// <returns></returns>
        /// <remarks>
        /// The implementation of this interface should not throw exceptions unless
        /// there is a serious problem (i.e. a serious misconfiguration). If there
        /// are no available threads, rather it should either queue the Runnable, or
        /// block until a thread is available, depending on the desired strategy.
        /// </remarks>
        public virtual bool RunInThread(IThreadRunnable runnable)
        {
            throw new NotSupportedException("This ThreadPool should not be used on Scheduler instances that are start()ed.");
        }

        /// <summary>
        /// Determines the number of threads that are currently available in
        /// the pool.  Useful for determining the number of times
        /// <see cref="RunInThread(IThreadRunnable)"/>  can be called before returning
        /// false.
        /// </summary>
        /// <returns>
        /// the number of currently available threads
        /// </returns>
        /// <remarks>
        /// The implementation of this method should block until there is at
        /// least one available thread.
        /// </remarks>
        public virtual int BlockForAvailableThreads()
        {
            throw new NotSupportedException("This ThreadPool should not be used on Scheduler instances that are start()ed.");
        }

    }
}