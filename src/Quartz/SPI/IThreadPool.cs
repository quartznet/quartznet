/* 
* Copyright 2004-2009 James House 
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

/*
* Previously Copyright (c) 2001-2004 James House
*/

using System.Threading;

using Quartz.Core;

namespace Quartz.Spi
{
    /// <summary>
    /// The interface to be implemented by classes that want to provide a thread
    /// pool for the <see cref="IScheduler" />'s use.
    /// </summary>
    /// <remarks>
    /// <see cref="IThreadPool" /> implementation instances should ideally be made
    /// for the sole use of Quartz.  Most importantly, when the method
    ///  <see cref="BlockForAvailableThreads()" /> returns a value of 1 or greater,
    /// there must still be at least one available thread in the pool when the
    /// method  <see cref="RunInThread(IThreadRunnable)"/> is called a few moments (or
    /// many moments) later.  If this assumption does not hold true, it may
    /// result in extra JobStore queries and updates, and if clustering features
    /// are being used, it may result in greater imballance of load.
    /// </remarks>
    /// <seealso cref="QuartzScheduler" />
    /// <author>James House</author>
    public interface IThreadPool
    {
        /// <summary>
        /// Gets the size of the pool.
        /// </summary>
        /// <value>The size of the pool.</value>
        int PoolSize { get; }

        /// <summary>
        /// Execute the given <see cref="IThreadRunnable" /> in the next
        /// available <see cref="Thread" />.
        /// </summary>
        /// <remarks>
        /// The implementation of this interface should not throw exceptions unless
        /// there is a serious problem (i.e. a serious misconfiguration). If there
        /// are no available threads, rather it should either queue the Runnable, or
        /// block until a thread is available, depending on the desired strategy.
        /// </remarks>
        bool RunInThread(IThreadRunnable runnable);

        /// <summary>
        /// Determines the number of threads that are currently available in in
        /// the pool.  Useful for determining the number of times
        /// <see cref="RunInThread(IThreadRunnable)"/>  can be called before returning
        /// false.
        /// </summary>
        ///<remarks>
        /// The implementation of this method should block until there is at
        /// least one available thread.
        ///</remarks>
        /// <returns>the number of currently available threads</returns>
        int BlockForAvailableThreads();

        /// <summary>
        /// Called by the QuartzScheduler before the <see cref="ThreadPool" /> is
        /// used, in order to give the it a chance to Initialize.
        /// </summary>
        void Initialize();

        /// <summary>
        /// Called by the QuartzScheduler to inform the <see cref="ThreadPool" />
        /// that it should free up all of it's resources because the scheduler is
        /// shutting down.
        /// </summary>
        void Shutdown(bool waitForJobsToComplete);
    }
}