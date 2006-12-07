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
	/// <summary> <p>
	/// This is class is a simple implementation of a thread pool, based on the
	/// <code>{@link IThreadPool}</code> interface.
	/// </p>
	/// 
	/// <p>
	/// <code>Runnable</code> objects are sent to the pool with the <code>{@link #runInThread(Runnable)}</code>
	/// method, which blocks until a <code>Thread</code> becomes available.
	/// </p>
	/// 
	/// <p>
	/// The pool has a fixed number of <code>Thread</code>s, and does not grow or
	/// shrink based on demand.
	/// </p>
	/// 
	/// </summary>
	/// <author>James House</author>
	/// <author>Juergen Donnerstag</author>
	public class SimpleThreadPool : IThreadPool
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof (SimpleThreadPool));

		public virtual int PoolSize
		{
			get { return ThreadCount; }
		}

		/// <summary>
		/// Gets or sets the number of worker threads in the pool.
		/// Set  has no effect after <code>Initialize()</code> has been called.
		/// </summary>
		public int ThreadCount
		{
			get { return count; }
			set { count = value; }
		}

		/// <summary>
		/// Get or set the thread priority of worker threads in the pool.
		/// Set operation has no effect after <code>initialize()</code> has been called.
		/// </summary>
		public int ThreadPriority
		{
			get { return prio; }
			set { prio = value; }
		}

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
		/// Dequeue the next pending <code>Runnable</code>.
		/// <p>
		/// getNextRunnable() should return null if within a specific time no new
		/// Runnable is available. This gives the worker thread the chance to check
		/// its shutdown flag. In case the worker thread is asked to shut down it
		/// will notify on nextRunnableLock, hence interrupt the wait state. That
		/// is, the time used for waiting need not be short.
		/// </p>
		/// </summary>
		private IThreadRunnable NextRunnable
		{
			get
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
		/// Create a new (unconfigured) <code>SimpleThreadPool</code>.
		/// </summary>
		public SimpleThreadPool()
		{
		}

		/// <summary> <p>
		/// Create a new <code>SimpleThreadPool</code> with the specified number
		/// of <code>Thread</code> s that have the given priority.
		/// </p>
		/// 
		/// </summary>
		/// <param name="threadCount">
		/// the number of worker <code>Threads</code> in the pool, must
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

				Log.Debug("shutdown complete");
				//}
			}
		}

		/// <summary> <p>
		/// Run the given <code>Runnable</code> object in the next available
		/// <code>Thread</code>. If while waiting the thread pool is asked to
		/// shut down, the Runnable is executed immediately within a new additional
		/// thread.
		/// </p>
		/// 
		/// </summary>
		/// <param name="runnable">
		/// the <code>Runnable</code> to be added.
		/// </param>
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
					Log.Info("SimpleThreadPool.runInThread(): thread pool has been shutdown. Runnable will not be executed");
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
				// or until the thread pool is asked to shutdown.
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

				// During normal operation, not shutdown, set the nextRunnable
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


		/// <summary> <p>
		/// A Worker loops, waiting to Execute tasks.
		/// </p>
		/// </summary>
		protected internal class WorkerThread : SupportClass.QuartzThread
		{
			private SimpleThreadPool enclosingInstance;

			public SimpleThreadPool Enclosing_Instance
			{
				get { return enclosingInstance; }
			}

			// A flag that signals the WorkerThread to terminate.
			private bool run_Renamed_Field = true;

			private SimpleThreadPool tp;

			private IThreadRunnable runnable = null;

			/// <summary> <p>
			/// Create a worker thread and start it. Waiting for the next Runnable,
			/// executing it, and waiting for the next Runnable, until the shutdown
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
				run_Renamed_Field = false;

				// @todo I'm not really sure if we should interrupt the thread.
				// Javadoc mentions that it interrupts blocked I/O operations as
				// well. Hence the job will most likely fail. I think we should
				// shut the work thread gracefully, by letting the job finish
				// uninterrupted. See SimpleThreadPool.shutdown()
				//interrupt();
			}

			/// <summary> <p>
			/// Loop, executing targets as they are received.
			/// </p>
			/// </summary>
			public override void Run()
			{
				bool runOnce = (runnable != null);

				while (run_Renamed_Field)
				{
					try
					{
						if (runnable == null)
						{
							runnable = tp.NextRunnable;
						}

						if (runnable != null)
						{
							runnable.Run();
						}
					}
					catch (ThreadInterruptedException unblock)
					{
						// do nothing (loop will terminate if shutdown() was called
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
							run_Renamed_Field = false;
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