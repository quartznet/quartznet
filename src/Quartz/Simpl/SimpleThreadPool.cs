/* 
* Copyright 2004-2005 OpenSymphony 
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

using System;
using System.Collections;
using System.Globalization;
using System.Threading;

using Common.Logging;

using Quartz.Spi;

namespace Quartz.Simpl
{
    /// <summary>
    /// This is class is a simple implementation of a thread pool, based on the
    /// <see cref="IThreadPool" /> interface.
    /// <p>
    /// <see cref="IThreadRunnable" /> objects are sent to the pool with the <see cref="RunInThread" />
    /// method, which blocks until a <see cref="Thread" /> becomes available.
    /// </p>
    /// 
    /// <p>
    /// The pool has a fixed number of <see cref="Thread" />s, and does not grow or
    /// shrink based on demand.
    /// </p>
    /// </summary>
    /// <author>James House</author>
    /// <author>Juergen Donnerstag</author>
    public class SimpleThreadPool : IThreadPool
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(SimpleThreadPool));
        private const int DefaultThreadPoolSize = 10;

        private readonly object nextRunnableLock = new object();
        private readonly ArrayList availWorkers = new ArrayList();
        private readonly ArrayList busyWorkers = new ArrayList();

        private int count = DefaultThreadPoolSize;
        private bool handoffPending;
        private bool isShutdown;
        private bool makeThreadsDaemons;
        private ThreadPriority prio = ThreadPriority.Normal;
        private string threadNamePrefix = "SimpleThreadPoolWorker";

        private IList workers;

        /// <summary> 
        /// Create a new (unconfigured) <see cref="SimpleThreadPool" />.
        /// </summary>
        public SimpleThreadPool()
        {
        }

        /// <summary>
        /// Create a new <see cref="SimpleThreadPool" /> with the specified number
        /// of <see cref="Thread" /> s that have the given priority.
        /// </summary>
        /// <param name="threadCount">
        /// the number of worker <see cref="Thread" />s in the pool, must
        /// be > 0.
        /// </param>
        /// <param name="threadPriority">
        /// the thread priority for the worker threads.
        /// 
        /// </param>
        public SimpleThreadPool(int threadCount, ThreadPriority threadPriority)
        {
            ThreadCount = threadCount;
            ThreadPriority = threadPriority;
        }

        /// <summary>
        /// Gets or sets the number of worker threads in the pool.
        /// Set  has no effect after <see cref="Initialize()" /> has been called.
        /// </summary>
        public int ThreadCount
        {
            get { return count; }
            set { count = value; }
        }

        /// <summary>
        /// Get or set the thread priority of worker threads in the pool.
        /// Set operation has no effect after <see cref="Initialize()" /> has been called.
        /// </summary>
        public ThreadPriority ThreadPriority
        {
            get { return prio; }
            set { prio = value; }
        }

        /// <summary>
        /// Gets or sets the thread name prefix.
        /// </summary>
        /// <value>The thread name prefix.</value>
        public virtual string ThreadNamePrefix
        {
            get { return threadNamePrefix; }
            set { threadNamePrefix = value; }
        }

        /// <summary> 
        /// Gets or sets the value of makeThreadsDaemons.
        /// </summary>
        public virtual bool MakeThreadsDaemons
        {
            get { return makeThreadsDaemons; }
            set { makeThreadsDaemons = value; }
        }

        #region IThreadPool Members

        /// <summary>
        /// Gets the size of the pool.
        /// </summary>
        /// <value>The size of the pool.</value>
        public virtual int PoolSize
        {
            get { return ThreadCount; }
        }

        /// <summary>
        /// Called by the QuartzScheduler before the <see cref="ThreadPool" /> is
        /// used, in order to give the it a chance to Initialize.
        /// </summary>
        public virtual void Initialize()
        {
            if (count <= 0)
            {
                throw new SchedulerConfigException("Thread count must be > 0");
            }

            // create the worker threads and start them
            foreach (WorkerThread wt in CreateWorkerThreads(count))
            {
                wt.Start();
                availWorkers.Add(wt);
            }
        }

        /// <summary>
        /// Terminate any worker threads in this thread group.
        /// Jobs currently in progress will complete.
        /// </summary>
        public virtual void Shutdown(bool waitForJobsToComplete)
        {
            // Give waiting (wait(1000)) worker threads a chance to shut down.
            // Active worker threads will shut down after finishing their
            // current job.
            lock (nextRunnableLock)
            {
                isShutdown = true;

                // signal each worker thread to shut down
                for (int i = 0; i < workers.Count; i++)
                {
                    if (workers[i] != null)
                    {
                        ((WorkerThread) workers[i]).Shutdown();
                    }
                }
                Monitor.PulseAll(nextRunnableLock);


                if (waitForJobsToComplete)
                {
                    // wait for hand-off in runInThread to complete...
                    while (handoffPending)
                    {
                        try
                        {
                            Monitor.Wait(nextRunnableLock, 100);
                        }
                        catch (ThreadInterruptedException)
                        {
                        }
                    }

                    // Wait until all worker threads are shut down
                    while (busyWorkers.Count > 0)
                    {
                        WorkerThread wt = (WorkerThread)busyWorkers[0];
                        try
                        {
                            Log.Debug(string.Format(CultureInfo.InvariantCulture, "Waiting for thread {0} to shut down", wt.Name));

                            // note: with waiting infinite time the
                            // application may appear to 'hang'.
                            Monitor.Wait(nextRunnableLock, 2000);
                        }
                        catch (ThreadInterruptedException)
                        {
                        }
                    }

                    Log.Debug("shutdown complete");
                }
            }
        }

        /// <summary>
        /// Run the given <see cref="IThreadRunnable" /> object in the next available
        /// <see cref="Thread" />. If while waiting the thread pool is asked to
        /// shut down, the Runnable is executed immediately within a new additional
        /// thread.
        /// </summary>
        /// <param name="runnable">The <see cref="IThreadRunnable" /> to be added.</param>
        public virtual bool RunInThread(IThreadRunnable runnable)
        {
            if (runnable == null)
            {
                return false;
            }

            lock (nextRunnableLock)
            {
                handoffPending = true;

                // Wait until a worker thread is available
                while ((availWorkers.Count < 1) && !isShutdown)
                {
                    try
                    {
                        Monitor.Wait(nextRunnableLock, 500);
                    }
                    catch (ThreadInterruptedException)
                    {
                    }
                }

                if (!isShutdown)
                {
                    WorkerThread wt = (WorkerThread)availWorkers[0];
                    availWorkers.RemoveAt(0);
                    busyWorkers.Add(wt);
                    wt.Run(runnable);
                }
                else
                {
                    // If the thread pool is going down, execute the Runnable
                    // within a new additional worker thread (no thread from the pool).
                    WorkerThread wt =
                        new WorkerThread(this, "WorkerThread-LastJob", prio, MakeThreadsDaemons, runnable);
                    busyWorkers.Add(wt);
                    workers.Add(wt);
                    wt.Start();
                }
                Monitor.PulseAll(nextRunnableLock);
                handoffPending = false;
            }

            return true;
        }

        #endregion

        public int BlockForAvailableThreads()
        {
            lock (nextRunnableLock)
            {
                while ((availWorkers.Count < 1 || handoffPending) && !isShutdown)
                {
                    try
                    {
                        Monitor.Wait(nextRunnableLock, 500);
                    }
                    catch (ThreadInterruptedException)
                    {
                    }
                }

                return availWorkers.Count;
            }
        }

        protected void MakeAvailable(WorkerThread wt)
        {
            lock (nextRunnableLock)
            {
                if (!isShutdown)
                {
                    availWorkers.Add(wt);
                }
                busyWorkers.Remove(wt);
                Monitor.PulseAll(nextRunnableLock);
            }
        }

        /// <summary>
        /// Creates the worker threads.
        /// </summary>
        /// <param name="threadCount">The thread count.</param>
        /// <returns></returns>
        protected internal virtual IList CreateWorkerThreads(int threadCount)
        {
            workers = new ArrayList();
            for (int i = 1; i <= threadCount; ++i)
            {
                WorkerThread wt = new WorkerThread(
                    this,
                    string.Format(CultureInfo.InvariantCulture, "{0}-{1}", ThreadNamePrefix, i),
                    ThreadPriority,
                    MakeThreadsDaemons);

                workers.Add(wt);
            }

            return workers;
        }

        /// <summary>
        /// Terminate any worker threads in this thread group.
        /// Jobs currently in progress will complete.
        /// </summary>
        public virtual void Shutdown()
        {
            Shutdown(true);
        }

        /// <summary>
        /// A Worker loops, waiting to Execute tasks.
        /// </summary>
        protected internal class WorkerThread : QuartzThread
        {
            // A flag that signals the WorkerThread to terminate.
            private bool run = true;

            private IThreadRunnable runnable;
            private readonly SimpleThreadPool tp;

            /// <summary>
            /// Create a worker thread and start it. Waiting for the next Runnable,
            /// executing it, and waiting for the next Runnable, until the Shutdown
            /// flag is set.
            /// </summary>
            internal WorkerThread(SimpleThreadPool tp, string name,
                                  ThreadPriority prio, bool isDaemon)
                : this( tp, name, prio, isDaemon, null)
            {
            }

            /// <summary>
            /// Create a worker thread, start it, Execute the runnable and terminate
            /// the thread (one time execution).
            /// </summary>
            internal WorkerThread(SimpleThreadPool tp, string name,
                                  ThreadPriority prio, bool isDaemon, IThreadRunnable runnable)
                : base(name)
            {
                this.tp = tp;
                this.runnable = runnable;
                Priority = prio;
                IsBackground = isDaemon;
            }

            /// <summary>
            /// Signal the thread that it should terminate.
            /// </summary>
            internal virtual void Shutdown()
            {
                lock (this)
                {
                    run = false;
                }
            }


            public void Run(IThreadRunnable newRunnable)
            {
                lock (this)
                {
                    if (runnable != null)
                    {
                        throw new ArgumentException("Already running a Runnable!");
                    }

                    runnable = newRunnable;
                    Monitor.PulseAll(this);
                }
            }

            /// <summary>
            /// Loop, executing targets as they are received.
            /// </summary>
            public override void Run()
            {
                bool ran = false;
        	    bool runOnce;
                bool shouldRun;
                lock (this) 
                {
            	    runOnce = (runnable != null);
            	    shouldRun = run;
                }
            
                while (shouldRun) 
                {
                    try
                    {
                        lock (this)
                        {
                            while (runnable == null && run)
                            {
                                Monitor.Wait(this, 500);
                            }
                        }

                        if (runnable != null)
                        {
                            ran = true;
                            runnable.Run();
                        }
                    }
                    catch (Exception exceptionInRunnable)
                    {
                        Log.Error("Error while executing the Runnable: ", exceptionInRunnable);
                    }
                    finally
                    {
                        lock (this)
                        {
                            runnable = null;
                        }
                        // repair the thread in case the runnable mucked it up...
                        if (Priority != tp.ThreadPriority)
                        {
                            Priority = tp.ThreadPriority;
                        }

                        if (runOnce)
                        {
                            lock (this)
                            {
                                run = false;
                            }
                        }
                        else if (ran)
                        {
                            ran = false;
                            tp.MakeAvailable(this);
                        }

                    }
                    
                    // read value of run within synchronized block to be 
                    // sure of its value
                    lock (this) 
                    {
                	    shouldRun = run;
                    }
                }

                
                Log.Debug("WorkerThread is shutting down");
            }
        }
    }
}
