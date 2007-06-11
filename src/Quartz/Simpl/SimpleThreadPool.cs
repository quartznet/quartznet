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
		private static readonly ILog Log = LogManager.GetLogger(typeof (SimpleThreadPool));

		/// <summary>
		/// Gets the size of the pool.
		/// </summary>
		/// <value>The size of the pool.</value>
		public virtual int PoolSize
		{
			get { return ThreadCount; }
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
		public int ThreadPriority
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

		/// <summary>
		/// Dequeue the next pending <see cref="IThreadRunnable" />.
		/// <p>
		/// getNextRunnable() should return null if within a specific time no new
		/// Runnable is available. This gives the worker thread the chance to check
		/// its Shutdown flag. In case the worker thread is asked to shut down it
		/// will notify on nextRunnableLock, hence interrupt the wait state. That
		/// is, the time used for waiting need not be short.
		/// </p>
		/// </summary>
		private IThreadRunnable GetNextRunnable()
		{
			IThreadRunnable toRun = null;

			// Wait for new Runnable (see runInThread()) and notify runInThread()
			// in case the next Runnable is already waiting.
			lock (nextRunnableLock)
			{
				if (nextRunnable == null)
				{
					Monitor.Wait(nextRunnableLock, TimeSpan.FromMilliseconds(1000));
				}

				if (nextRunnable != null)
				{
					toRun = nextRunnable;
					nextRunnable = null;
					Monitor.PulseAll(nextRunnableLock);
				}
			}

			return toRun;
		}

		private int count = - 1;
		private int prio = (int) System.Threading.ThreadPriority.Normal;
		private bool isShutdown = false;
		private bool makeThreadsDaemons = false;
		private IThreadRunnable nextRunnable;
		private object nextRunnableLock = new object();
		private WorkerThread[] workers;
		private string threadNamePrefix = "SimpleThreadPoolWorker";

		/// <summary> 
		/// Create a new (unconfigured) <see cref="SimpleThreadPool" />.
		/// </summary>
		public SimpleThreadPool()
		{
		}

		/// <summary> <p>
		/// Create a new <see cref="SimpleThreadPool" /> with the specified number
		/// of <see cref="Thread" /> s that have the given priority.
		/// </p>
		/// 
		/// </summary>
		/// <param name="threadCount">
		/// the number of worker <see cref="Thread" />s in the pool, must
		/// be > 0.
		/// </param>
		/// <param name="threadPriority">
		/// the thread priority for the worker threads.
		/// 
		/// </param>
		public SimpleThreadPool(int threadCount, int threadPriority)
		{
			ThreadCount = threadCount;
			ThreadPriority = threadPriority;
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
			if (prio <= 0 || prio > 9)
			{
				throw new SchedulerConfigException("Thread priority must be > 0 and <= 9");
			}

			// create the worker threads and start them
			workers = CreateWorkerThreads(count);
		}

		/// <summary>
		/// Creates the worker threads.
		/// </summary>
		/// <param name="threadCount">The thread count.</param>
		/// <returns></returns>
		protected internal virtual WorkerThread[] CreateWorkerThreads(int threadCount)
		{
			workers = new WorkerThread[threadCount];
			for (int i = 0; i < threadCount; ++i)
			{
				workers[i] =
					new WorkerThread(this, this, ThreadNamePrefix + "-" + i, ThreadPriority, MakeThreadsDaemons);
			}

			return workers;
		}

		/// <summary> <p>
		/// Terminate any worker threads in this thread group.
		/// </p>
		/// 
		/// <p>
		/// Jobs currently in progress will complete.
		/// </p>
		/// </summary>
		public virtual void Shutdown()
		{
			Shutdown(true);
		}

		/// <summary> <p>
		/// Terminate any worker threads in this thread group.
		/// </p>
		/// 
		/// <p>
		/// Jobs currently in progress will complete.
		/// </p>
		/// </summary>
		public virtual void Shutdown(bool waitForJobsToComplete)
		{
			isShutdown = true;

			// signal each worker thread to shut down
			for (int i = 0; i < workers.Length; i++)
			{
				if (workers[i] != null)
				{
					workers[i].shutdown();
				}
			}

			// Give waiting (wait(1000)) worker threads a chance to shut down.
			// Active worker threads will shut down after finishing their
			// current job.
			lock (nextRunnableLock)
			{
				Monitor.PulseAll(nextRunnableLock);
			}

			if (waitForJobsToComplete)
			{
				// Wait until all worker threads are shut down
				int alive = workers.Length;
				while (alive > 0)
				{
					alive = 0;
					for (int i = 0; i < workers.Length; i++)
					{
						if (workers[i].IsAlive)
						{
							try
							{
								//if (logger.isDebugEnabled())
								Log.Debug("Waiting for thread no. " + i + " to shut down");

								// note: with waiting infinite - join(0) - the
								// application
								// may appear to 'hang'. Waiting for a finite time
								// however
								// requires an additional loop (alive).
								alive++;
								workers[i].Join(200);
							}
							catch (ThreadInterruptedException)
							{
							}
						}
					}
				}

				//if (logger.isDebugEnabled()) {
				//UPGRADE_ISSUE: Method 'java.lang.ThreadGroup.activeCount' was not converted. 'ms-help://MS.VSCC.2003/commoner/redir/redirect.htm?keyword="jlca1000_javalangThreadGroup_3"'
				int activeCount = 0; // TODO threadGroup.activeCount();
				if (activeCount > 0)
				{
					Log.Info("There are still " + activeCount + " worker threads active." +
					         " See javadoc runInThread(Runnable) for a possible explanation");
				}

				Log.Debug("Shutdown complete");
				//}
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

			if (isShutdown)
			{
				try
				{
					Log.Info("SimpleThreadPool.runInThread(): thread pool has been Shutdown. Runnable will not be executed");
				}
				catch (Exception)
				{
					// ignore to help with a tomcat glitch
				}

				return false;
			}

			lock (nextRunnableLock)
			{
				// Wait until a worker thread has taken the previous Runnable
				// or until the thread pool is asked to Shutdown.
				while ((nextRunnable != null) && !isShutdown)
				{
					try
					{
						Monitor.Wait(nextRunnableLock, TimeSpan.FromMilliseconds(1000));
					}
					catch (ThreadInterruptedException)
					{
					}
				}

				// During normal operation, not Shutdown, set the nextRunnable
				// and notify the worker threads waiting (getNextRunnable()).
				if (!isShutdown)
				{
					nextRunnable = runnable;
					Monitor.PulseAll(nextRunnableLock);
				}
			}

			// If the thread pool is going down, Execute the Runnable
			// within a new additional worker thread (no thread from the pool).
			// note: the synchronized section should be as short (time) as
			//  possible. Starting a new thread is not a quick action.
			if (isShutdown)
			{
				new WorkerThread(this, this, "WorkerThread-LastJob", prio, false, runnable);
			}

			return true;
		}


		/// <summary>
		/// A Worker loops, waiting to Execute tasks.
		/// </summary>
		protected internal class WorkerThread : QuartzThread
		{
			// A flag that signals the WorkerThread to terminate.
			private bool run = true;

			private SimpleThreadPool tp;
			private IThreadRunnable runnable = null;
			private SimpleThreadPool enclosingInstance;

			/// <summary>
			/// Gets the simple thread pool.
			/// </summary>
			/// <value>The simple thread pool.</value>
			public SimpleThreadPool SimpleThreadPool
			{
				get { return enclosingInstance; }
			}

			/// <summary> <p>
			/// Create a worker thread and start it. Waiting for the next Runnable,
			/// executing it, and waiting for the next Runnable, until the Shutdown
			/// flag is set.
			/// </p>
			/// </summary>
			internal WorkerThread(SimpleThreadPool enclosingInstance, SimpleThreadPool tp, string name,
			                      int prio, bool isDaemon) : this(enclosingInstance, tp, name, prio, isDaemon, null)
			{
			}

			/// <summary> <p>
			/// Create a worker thread, start it, Execute the runnable and terminate
			/// the thread (one time execution).
			/// </p>
			/// </summary>
			internal WorkerThread(SimpleThreadPool enclosingInstance, SimpleThreadPool tp, string name,
			                      int prio, bool isDaemon, IThreadRunnable runnable) : base(name)
			{
				this.enclosingInstance = enclosingInstance;
				this.tp = tp;
				this.runnable = runnable;
				Priority = (ThreadPriority) prio;
				IsBackground = isDaemon;
				Start();
			}

			/// <summary> <p>
			/// Signal the thread that it should terminate.
			/// </p>
			/// </summary>
			internal virtual void shutdown()
			{
				run = false;

				// @todo I'm not really sure if we should interrupt the thread.
				// Javadoc mentions that it interrupts blocked I/O operations as
				// well. Hence the job will most likely fail. I think we should
				// shut the work thread gracefully, by letting the job finish
				// uninterrupted. See SimpleThreadPool.Shutdown()
				//interrupt();
			}

			/// <summary> <p>
			/// Loop, executing targets as they are received.
			/// </p>
			/// </summary>
			public override void Run()
			{
				bool runOnce = (runnable != null);

				while (run)
				{
					try
					{
						if (runnable == null)
						{
							runnable = tp.GetNextRunnable();
						}

						if (runnable != null)
						{
							runnable.Run();
						}
					}
					catch (ThreadInterruptedException unblock)
					{
						// do nothing (loop will terminate if Shutdown() was called
						try
						{
							Log.Error("worker threat got 'interrupt'ed.", unblock);
						}
						catch (Exception)
						{
							// ignore to help with a tomcat glitch
						}
					}
					catch (Exception exceptionInRunnable)
					{
						try
						{
							Log.Error("Error while executing the Runnable: ", exceptionInRunnable);
						}
						catch (Exception)
						{
							// ignore to help with a tomcat glitch
						}
					}
					finally
					{
						if (runOnce)
						{
							run = false;
						}

						runnable = null;

						// repair the thread in case the runnable mucked it up...
						Priority = (ThreadPriority) tp.ThreadPriority;
					}
				}

				//if (log.isDebugEnabled())
				try
				{
					Log.Debug("WorkerThread is shutting down");
				}
				catch (Exception)
				{
					// ignore to help with a tomcat glitch
				}
			}
		}
	}
}