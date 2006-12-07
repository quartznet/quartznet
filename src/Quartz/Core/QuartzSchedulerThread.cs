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

namespace Quartz.Core
{
	/// <summary>
	/// The thread responsible for performing the work of firing <code>Trigger</code>
	/// s that are registered with the <code>QuartzScheduler</code>.
	/// </summary>
	/// <seealso cref="QuartzScheduler" />
	/// <seealso cref="IJob" />
	/// <seealso cref="Trigger" />
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
	public class QuartzSchedulerThread : SupportClass.QuartzThread
	{
		private static readonly ILog Log = LogManager.GetLogger(typeof (QuartzSchedulerThread));

		/// <summary>
		/// Sets the idle wait time.
		/// </summary>
		/// <value>The idle wait time.</value>
		internal virtual long IdleWaitTime
		{
			set
			{
				idleWaitTime = value;
				idleWaitVariablness = (int) (value*0.2);
			}
		}

		/// <summary>
		/// Gets the randomized idle wait time.
		/// </summary>
		/// <value>The randomized idle wait time.</value>
		private long RandomizedIdleWaitTime
		{
			get { return idleWaitTime - random.Next(idleWaitVariablness); }
		}

		/// <summary>
		/// Gets a value indicating whether this <see cref="QuartzSchedulerThread"/> is paused.
		/// </summary>
		/// <value><c>true</c> if paused; otherwise, <c>false</c>.</value>
		internal virtual bool Paused
		{
			get { return paused; }
		}

		private QuartzScheduler qs;
		private QuartzSchedulerResources qsRsrcs;
		private object pauseLock = new object();
/*
		private object idleLock = new object();
*/
		
		private bool signaled;
		private bool paused;
		private bool halted;
		
		private SchedulingContext ctxt = null;
		private Random random = new Random((int) DateTime.Now.Ticks);

		// When the scheduler finds there is no current trigger to fire, how long
		// it should wait until checking again...
		private static long DEFAULT_IDLE_WAIT_TIME = 30*1000;

		private long idleWaitTime = DEFAULT_IDLE_WAIT_TIME;
		private int idleWaitVariablness = 7*1000;
		private int dbFailureRetryInterval = 15*1000;


		/// <summary>
		/// Construct a new <code>QuartzSchedulerThread</code> for the given
		/// <code>QuartzScheduler</code> as a non-daemon <code>Thread</code>
		/// with normal priority.
		/// </summary>
		internal QuartzSchedulerThread(QuartzScheduler qs, QuartzSchedulerResources qsRsrcs, SchedulingContext ctxt)
			: this(qs, qsRsrcs, ctxt, false, (int) ThreadPriority.Normal)
		{
		}

		/// <summary>
		/// Construct a new <code>QuartzSchedulerThread</code> for the given
		/// <code>QuartzScheduler</code> as a <code>Thread</code> with the given
		/// attributes.
		/// </summary>
		internal QuartzSchedulerThread(QuartzScheduler qs, QuartzSchedulerResources qsRsrcs, SchedulingContext ctxt,
		                               bool setDaemon, int threadPrio) : base(qsRsrcs.ThreadName)
		{
			//ThreadGroup generatedAux = qs.SchedulerThreadGroup;
			this.qs = qs;
			this.qsRsrcs = qsRsrcs;
			this.ctxt = ctxt;
			IsBackground = setDaemon;
			Priority = (ThreadPriority) threadPrio;

			// start the underlying thread, but put this object into the 'paused'
			// state
			// so processing doesn't start yet...
			paused = true;
			halted = false;
			Start();
		}

		/// <summary>
		/// Gets or sets the db failure retry interval.
		/// </summary>
		/// <value>The db failure retry interval.</value>
		public int DbFailureRetryInterval
		{
			get { return dbFailureRetryInterval; }
			set { dbFailureRetryInterval = value; }
		}

		/// <summary>
		/// Signals the main processing loop to pause at the next possible point.
		/// </summary>
		internal virtual void TogglePause(bool pause)
		{
			lock (pauseLock)
			{
				paused = pause;

				if (paused)
				{
					SignalSchedulingChange();
				}
				else
				{
					Monitor.Pulse(pauseLock);
				}
			}
		}

		/// <summary>
		/// Signals the main processing loop to pause at the next possible point.
		/// </summary>
		internal virtual void halt()
		{
			lock (pauseLock)
			{
				halted = true;

				if (paused)
				{
					Monitor.Pulse(pauseLock);
				}
				else
				{
					SignalSchedulingChange();
				}
			}
		}

		/// <summary>
		/// Signals the main processing loop that a change in scheduling has been
		/// made - in order to interrupt any sleeping that may be occuring while
		/// waiting for the fire time to arrive.
		/// </summary>
		internal virtual void SignalSchedulingChange()
		{
			signaled = true;
		}

		/// <summary>
		/// The main processing loop of the <code>QuartzSchedulerThread</code>.
		/// </summary>
		public override void Run()
		{
			bool lastAcquireFailed = false;

			while (!halted)
			{
				signaled = false;

				try
				{
					// check if we're supposed to pause...
					lock (pauseLock)
					{
						while (paused && !halted)
						{
							try
							{
								// wait until TogglePause(false) is called...
								Monitor.Wait(pauseLock, TimeSpan.FromMilliseconds(100));
							}
							catch (ThreadInterruptedException)
							{
							}
						}

						if (halted)
						{
							break;
						}
					}

					Trigger trigger = null;

					DateTime now = DateTime.Now;

					try
					{
						trigger = qsRsrcs.JobStore.AcquireNextTrigger(ctxt, now.AddMilliseconds(idleWaitTime));
						lastAcquireFailed = false;
					}
					catch (JobPersistenceException jpe)
					{
						if (!lastAcquireFailed)
						{
							qs.NotifySchedulerListenersError("An error occured while scanning for the next trigger to fire.", jpe);
						}
						lastAcquireFailed = true;
					}
					catch (SystemException e)
					{
						if (!lastAcquireFailed)
						{
							Log.Error("quartzSchedulerThreadLoop: RuntimeException " + e.Message, e);
						}
						lastAcquireFailed = true;
					}

					if (trigger != null)
					{
						now = DateTime.Now;
						DateTime triggerTime = trigger.GetNextFireTime().Value;
						TimeSpan ts = triggerTime - now;
						long timeUntilTrigger = (long) ts.TotalMilliseconds;
						int spinInterval = 10;

						// this looping may seem a bit silly, but it's the
						// current work-around
						// for a dead-lock that can occur if the Thread.sleep()
						// is replaced with
						// a obj.wait() that gets notified when the signal is
						// set...
						// so to be able to detect the signal change without
						// sleeping the entire
						// timeUntilTrigger, we spin here... don't worry
						// though, this spinning
						// doesn't even register 0.2% cpu usage on a pentium 4.
						int numPauses = (int) (timeUntilTrigger/spinInterval);
						while (numPauses >= 0 && !signaled)
						{
							try
							{
								Thread.Sleep(spinInterval);
							}
							catch (ThreadInterruptedException)
							{
							}

							now = DateTime.Now;
							timeUntilTrigger = (long) (triggerTime - now).TotalMilliseconds;
							numPauses = (int) (timeUntilTrigger/spinInterval);
						}
						if (signaled)
						{
							try
							{
								qsRsrcs.JobStore.ReleaseAcquiredTrigger(ctxt, trigger);
							}
							catch (JobPersistenceException jpe)
							{
								qs.NotifySchedulerListenersError("An error occured while releasing trigger '" + trigger.FullName + "'", jpe);
								// db connection must have failed... keep
								// retrying until it's up...
								ReleaseTriggerRetryLoop(trigger);
							}
							catch (SystemException e)
							{
								Log.Error("ReleaseTriggerRetryLoop: RuntimeException " + e.Message, e);
								// db connection must have failed... keep
								// retrying until it's up...
								ReleaseTriggerRetryLoop(trigger);
							}
							signaled = false;
							continue;
						}

						// set trigger to 'executing'
						TriggerFiredBundle bndle = null;

						try
						{
							bndle = qsRsrcs.JobStore.TriggerFired(ctxt, trigger);
						}
						catch (SchedulerException se)
						{
							qs.NotifySchedulerListenersError("An error occured while firing trigger '" + trigger.FullName + "'", se);
						}
						catch (SystemException e)
						{
							Log.Error("RuntimeException while firing trigger " + trigger.FullName, e);
							// db connection must have failed... keep
							// retrying until it's up...
							ReleaseTriggerRetryLoop(trigger);
						}

						// it's possible to get 'null' if the trigger was paused,
						// blocked, or other similar occurances that prevent it being
						// fired at this time...
						if (bndle == null)
						{
							try
							{
								qsRsrcs.JobStore.ReleaseAcquiredTrigger(ctxt, trigger);
							}
							catch (SchedulerException se)
							{
								qs.NotifySchedulerListenersError("An error occured while releasing trigger '" + trigger.FullName + "'", se);
								// db connection must have failed... keep retrying
								// until it's up...
								ReleaseTriggerRetryLoop(trigger);
							}
							continue;
						}

						// TODO: improvements:
						//
						// 1- get thread from pool before firing trigger.
						// 2- make sure we can get a job runshell first as well, or 
						//   don't let that throw an exception (right now it never does, 
						//   bugthe signature says it can).
						// 3- acquire more triggers at a time (based on num threads?)


						JobRunShell shell;
						try
						{
							shell = qsRsrcs.JobRunShellFactory.BorrowJobRunShell();
							shell.Initialize(qs, bndle);
						}
						catch (SchedulerException)
						{
							try
							{
								qsRsrcs.JobStore.TriggeredJobComplete(ctxt, trigger, bndle.JobDetail,
								                                      Trigger.INSTRUCTION_SET_ALL_JOB_TRIGGERS_ERROR);
							}
							catch (SchedulerException se2)
							{
								qs.NotifySchedulerListenersError("An error occured while releasing trigger '" + trigger.FullName + "'", se2);
								// db connection must have failed... keep retrying
								// until it's up...
								ErrorTriggerRetryLoop(bndle);
							}
							continue;
						}

						qsRsrcs.ThreadPool.RunInThread(shell);

						continue;
					}

					// this looping may seem a bit silly, but it's the current
					// work-around
					// for a dead-lock that can occur if the Thread.sleep() is replaced
					// with
					// a obj.wait() that gets notified when the signal is set...
					// so to be able to detect the signal change without sleeping the
					// entier
					// getRandomizedIdleWaitTime(), we spin here... don't worry though,
					// the
					// CPU usage of this spinning can't even be measured on a pentium
					// 4.
					now = DateTime.Now;
					long waitTime = now.Ticks + RandomizedIdleWaitTime;
					long timeUntilContinue = waitTime - now.Ticks;
					long spinInterval2 = 10;
					int numPauses2 = (int) (timeUntilContinue/spinInterval2);

					while (numPauses2 > 0 && !signaled)
					{
						try
						{
							Thread.Sleep(10);
						}
						catch (ThreadInterruptedException)
						{
						}

						now = DateTime.Now;
						timeUntilContinue = (waitTime - now.Ticks);
						numPauses2 = (int) (timeUntilContinue/spinInterval2);
					}
				}
				catch (SystemException re)
				{
					Log.Error("Runtime error occured in main trigger firing loop.", re);
				}
			} // loop...

			// drop references to scheduler stuff to aid garbage collection...
			qs = null;
			qsRsrcs = null;
		}

		public virtual void ErrorTriggerRetryLoop(TriggerFiredBundle bndle)
		{
			int retryCount = 0;
			try
			{
				while (!halted)
				{
					try
					{
						Thread.Sleep(DbFailureRetryInterval); 
						// retry every N seconds (the db connection must be failed)
						retryCount++;
						qsRsrcs.JobStore.TriggeredJobComplete(ctxt, bndle.Trigger, bndle.JobDetail,
						                                      Trigger.INSTRUCTION_SET_ALL_JOB_TRIGGERS_ERROR);
						retryCount = 0;
						break;
					}
					catch (JobPersistenceException jpe)
					{
						if (retryCount%4 == 0)
						{
							qs.NotifySchedulerListenersError("An error occured while releasing trigger '" + bndle.Trigger.FullName + "'", jpe);
						}
					}
					catch (ThreadInterruptedException e)
					{
						Log.Error("ReleaseTriggerRetryLoop: InterruptedException " + e.Message, e);
					}
					catch (Exception e)
					{
						Log.Error("ReleaseTriggerRetryLoop: Exception " + e.Message, e);
					}
				}
			}
			finally
			{
				if (retryCount == 0)
				{
					Log.Info("ReleaseTriggerRetryLoop: connection restored.");
				}
			}
		}

		public virtual void ReleaseTriggerRetryLoop(Trigger trigger)
		{
			int retryCount = 0;
			try
			{
				while (!halted)
				{
					try
					{
						Thread.Sleep(DbFailureRetryInterval);
						// retry every N seconds (the db connection must be failed)
						retryCount++;
						qsRsrcs.JobStore.ReleaseAcquiredTrigger(ctxt, trigger);
						retryCount = 0;
						break;
					}
					catch (JobPersistenceException jpe)
					{
						if (retryCount%4 == 0)
						{
							qs.NotifySchedulerListenersError("An error occured while releasing trigger '" + trigger.FullName + "'", jpe);
						}
					}
					catch (ThreadInterruptedException e)
					{
						Log.Error("ReleaseTriggerRetryLoop: InterruptedException " + e.Message, e);
					}
					catch (Exception e)
					{
						Log.Error("ReleaseTriggerRetryLoop: Exception " + e.Message, e);
					}
				}
			}
			finally
			{
				if (retryCount == 0)
				{
					Log.Info("ReleaseTriggerRetryLoop: connection restored.");
				}
			}
		}
	} // end of QuartzSchedulerThread
}