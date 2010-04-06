#region License
/* 
 * Copyright 2001-2009 Terracotta, Inc. 
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
using System.Globalization;
using System.Threading;

using Common.Logging;

using Quartz.Spi;

namespace Quartz.Core
{
    /// <summary>
    /// The thread responsible for performing the work of firing <see cref="Trigger" />
    /// s that are registered with the <see cref="QuartzScheduler" />.
    /// </summary>
    /// <seealso cref="QuartzScheduler" />
    /// <seealso cref="IJob" />
    /// <seealso cref="Trigger" />
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class QuartzSchedulerThread : QuartzThread
    {
        private readonly ILog log;
        private QuartzScheduler qs;
        private QuartzSchedulerResources qsRsrcs;
        private readonly object sigLock = new object();

        private bool signaled;
        private DateTime? signaledNextFireTimeUtc;
        private bool paused;
        private bool halted;

        private readonly SchedulingContext ctxt;
        private readonly Random random = new Random((int) DateTime.Now.Ticks);

        // When the scheduler finds there is no current trigger to fire, how long
        // it should wait until checking again...
        private static readonly TimeSpan DefaultIdleWaitTime = TimeSpan.FromSeconds(30);

        private TimeSpan idleWaitTime = DefaultIdleWaitTime;
        private int idleWaitVariablness = 7*1000;
        private TimeSpan dbFailureRetryInterval = TimeSpan.FromSeconds(15);

        /// <summary>
        /// Gets the log.
        /// </summary>
        /// <value>The log.</value>
        protected ILog Log
        {
            get { return log; }
        }

        /// <summary>
        /// Sets the idle wait time.
        /// </summary>
        /// <value>The idle wait time.</value>
        [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
        internal virtual TimeSpan IdleWaitTime
        {
            set
            {
                idleWaitTime = value;
                idleWaitVariablness = (int) (value.TotalMilliseconds*0.2);
            }
        }

        /// <summary>
        /// Gets the randomized idle wait time.
        /// </summary>
        /// <value>The randomized idle wait time.</value>
        private TimeSpan GetRandomizedIdleWaitTime()
        {
            return idleWaitTime.Add(TimeSpan.FromMilliseconds(random.Next(idleWaitVariablness)));
        }

        /// <summary>
        /// Gets a value indicating whether this <see cref="QuartzSchedulerThread"/> is paused.
        /// </summary>
        /// <value><c>true</c> if paused; otherwise, <c>false</c>.</value>
        internal virtual bool Paused
        {
            get { return paused; }
        }


        /// <summary>
        /// Construct a new <see cref="QuartzSchedulerThread" /> for the given
        /// <see cref="QuartzScheduler" /> as a non-daemon <see cref="Thread" />
        /// with normal priority.
        /// </summary>
        internal QuartzSchedulerThread(QuartzScheduler qs, QuartzSchedulerResources qsRsrcs, SchedulingContext ctxt)
            : this(qs, qsRsrcs, ctxt, qsRsrcs.MakeSchedulerThreadDaemon, (int) ThreadPriority.Normal)
        {
        }

        /// <summary>
        /// Construct a new <see cref="QuartzSchedulerThread" /> for the given
        /// <see cref="QuartzScheduler" /> as a <see cref="Thread" /> with the given
        /// attributes.
        /// </summary>
        internal QuartzSchedulerThread(QuartzScheduler qs, QuartzSchedulerResources qsRsrcs, SchedulingContext ctxt,
                                       bool setDaemon, int threadPrio) : base(qsRsrcs.ThreadName)
        {
            log = LogManager.GetLogger(GetType());
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
        [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
        public TimeSpan DbFailureRetryInterval
        {
            get { return dbFailureRetryInterval; }
            set { dbFailureRetryInterval = value; }
        }

        /// <summary>
        /// Signals the main processing loop to pause at the next possible point.
        /// </summary>
        internal virtual void TogglePause(bool pause)
        {
            lock (sigLock)
            {
                paused = pause;

                if (paused)
                {
                    SignalSchedulingChange(null);
                }
                else
                {
                    Monitor.PulseAll(sigLock);
                }
            }
        }

        /// <summary>
        /// Signals the main processing loop to pause at the next possible point.
        /// </summary>
        internal virtual void Halt()
        {
            lock (sigLock)
            {
                halted = true;

                if (paused)
                {
                    Monitor.PulseAll(sigLock);
                }
                else
                {
                    SignalSchedulingChange(null);
                }
            }
        }

        /// <summary>
        /// Signals the main processing loop that a change in scheduling has been
        /// made - in order to interrupt any sleeping that may be occuring while
        /// waiting for the fire time to arrive.
        /// </summary>
        /// <param name="candidateNewNextFireTimeUtc">
        /// the time when the newly scheduled trigger
        /// will fire.  If this method is being called do to some other even (rather
        /// than scheduling a trigger), the caller should pass null.
        /// </param>
        public void SignalSchedulingChange(DateTime? candidateNewNextFireTimeUtc) 
        {
            lock (sigLock) 
            {
                signaled = true;
                signaledNextFireTimeUtc = candidateNewNextFireTimeUtc;
                Monitor.PulseAll(sigLock);
            }
        }

        public void ClearSignaledSchedulingChange() 
        {
            lock (sigLock) 
            {
                signaled = false;
                signaledNextFireTimeUtc = null;
            }
        }

        public bool IsScheduleChanged() 
        {
            lock(sigLock) 
            {
                return signaled;
            }
        }

        
        public DateTime? GetSignaledNextFireTimeUtc() 
        {
            lock (sigLock) 
            {
                return signaledNextFireTimeUtc;
            }
        }


        /// <summary>
        /// The main processing loop of the <see cref="QuartzSchedulerThread" />.
        /// </summary>
        public override void Run()
        {
            bool lastAcquireFailed = false;

            while (!halted)
            {
                try
                {
                    // check if we're supposed to pause...
                    lock (sigLock)
                    {
                        while (paused && !halted)
                        {
                            try
                            {
                                // wait until togglePause(false) is called...
                                Monitor.Wait(sigLock, 1000);
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

                    int availTreadCount = qsRsrcs.ThreadPool.BlockForAvailableThreads();
                    if (availTreadCount > 0) // will always be true, due to semantics of blockForAvailableThreads...
                    {
                        Trigger trigger = null;

                        DateTime now = SystemTime.UtcNow();

                        ClearSignaledSchedulingChange();
                        try
                        {
                            trigger = qsRsrcs.JobStore.AcquireNextTrigger(ctxt, now.Add(idleWaitTime));
                            lastAcquireFailed = false;
                        }
                        catch (JobPersistenceException jpe)
                        {
                            if (!lastAcquireFailed)
                            {
                                qs.NotifySchedulerListenersError(
                                    "An error occured while scanning for the next trigger to fire.",
                                    jpe);
                            }
                            lastAcquireFailed = true;
                        }
                        catch (Exception e)
                        {
                            if (!lastAcquireFailed)
                            {
                                Log.Error("quartzSchedulerThreadLoop: RuntimeException "
                                          + e.Message, e);
                            }
                            lastAcquireFailed = true;
                        }

                        if (trigger != null)
                        {
                            now = SystemTime.UtcNow();
                            DateTime triggerTime = trigger.GetNextFireTimeUtc().Value;
                            TimeSpan timeUntilTrigger =  triggerTime - now;

                            while (timeUntilTrigger > TimeSpan.Zero) 
                            {
                                lock (sigLock)
                                {
                                    try
                                    {
                                        // we could have blocked a long while
                                        // on 'synchronize', so we must recompute
                                        now = SystemTime.UtcNow();
                                        timeUntilTrigger = triggerTime - now;
                                        if (timeUntilTrigger.TotalMilliseconds >= 1)
                                        {
                                            Monitor.Wait(sigLock, timeUntilTrigger);
                                        }
                                    }
                                    catch (ThreadInterruptedException)
                                    {
                                    }
                                }
                                if (IsScheduleChanged())
                                {
                                    if (IsCandidateNewTimeEarlierWithinReason(triggerTime))
                                    {
                                        // above call does a clearSignaledSchedulingChange()
                                        try
                                        {
                                            qsRsrcs.JobStore.ReleaseAcquiredTrigger(ctxt, trigger);
                                        }
                                        catch (JobPersistenceException jpe)
                                        {
                                            qs.NotifySchedulerListenersError(
                                                "An error occured while releasing trigger '"
                                                + trigger.FullName + "'",
                                                jpe);
                                            // db connection must have failed... keep
                                            // retrying until it's up...
                                            ReleaseTriggerRetryLoop(trigger);
                                        }
                                        catch (Exception e)
                                        {
                                            Log.Error(
                                                "releaseTriggerRetryLoop: RuntimeException "
                                                + e.Message, e);
                                            // db connection must have failed... keep
                                            // retrying until it's up...
                                            ReleaseTriggerRetryLoop(trigger);
                                        }
                                        trigger = null;
                                        break;
                                    }
                                    
                                }
                                now = SystemTime.UtcNow();
                                timeUntilTrigger = triggerTime - now;
                            }

                            if (trigger == null)
                            {
                                continue;
                            }
                                              
                            // set trigger to 'executing'
                            TriggerFiredBundle bndle = null;

                            bool goAhead = true;
                            lock (sigLock) 
                            {
                        	    goAhead = !halted;
                            }
                            if(goAhead) 
                            {
                                try
                                {
                                    bndle = qsRsrcs.JobStore.TriggerFired(ctxt,
                                                                          trigger);
                                }
                                catch (SchedulerException se)
                                {
                                    qs.NotifySchedulerListenersError(
                                        string.Format(CultureInfo.InvariantCulture,
                                                      "An error occured while firing trigger '{0}'",
                                                      trigger.FullName), se);
                                }
                                catch (Exception e)
                                {
                                    Log.Error(
                                        string.Format(CultureInfo.InvariantCulture,
                                                      "RuntimeException while firing trigger {0}", trigger.FullName),
                                        e);
                                    // db connection must have failed... keep
                                    // retrying until it's up...
                                    ReleaseTriggerRetryLoop(trigger);
                                }
                            }
                            
                            // it's possible to get 'null' if the trigger was paused,
                            // blocked, or other similar occurrences that prevent it being
                            // fired at this time...  or if the scheduler was shutdown (halted)
                            if (bndle == null)
                            {
                                try
                                {
                                    qsRsrcs.JobStore.ReleaseAcquiredTrigger(ctxt,
                                                                            trigger);
                                }
                                catch (SchedulerException se)
                                {
                                    qs.NotifySchedulerListenersError(
                                        string.Format(CultureInfo.InvariantCulture, "An error occured while releasing trigger '{0}'",
                                                      trigger.FullName), se);
                                    // db connection must have failed... keep retrying
                                    // until it's up...
                                    ReleaseTriggerRetryLoop(trigger);
                                }
                                continue;
                            }

                            // TODO: improvements:
                            //
                            // 2- make sure we can get a job runshell before firing trigger, or
                            //   don't let that throw an exception (right now it never does,
                            //   but the signature says it can).
                            // 3- acquire more triggers at a time (based on num threads available?)


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
                                    qsRsrcs.JobStore.TriggeredJobComplete(ctxt,
                                                                          trigger, bndle.JobDetail,
                                                                          SchedulerInstruction.
                                                                              SetAllJobTriggersError);
                                }
                                catch (SchedulerException se2)
                                {
                                    qs.NotifySchedulerListenersError(
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "An error occured while placing job's triggers in error state '{0}'",
                                            trigger.FullName), se2);
                                    // db connection must have failed... keep retrying
                                    // until it's up...
                                    ErrorTriggerRetryLoop(bndle);
                                }
                                continue;
                            }

                            if (qsRsrcs.ThreadPool.RunInThread(shell) == false)
                            {
                                try
                                {
                                    // this case should never happen, as it is indicative of the
                                    // scheduler being shutdown or a bug in the thread pool or
                                    // a thread pool being used concurrently - which the docs
                                    // say not to do...
                                    Log.Error("ThreadPool.runInThread() return false!");
                                    qsRsrcs.JobStore.TriggeredJobComplete(ctxt,
                                                                          trigger, bndle.JobDetail,
                                                                          SchedulerInstruction.
                                                                              SetAllJobTriggersError);
                                }
                                catch (SchedulerException se2)
                                {
                                    qs.NotifySchedulerListenersError(
                                        string.Format(CultureInfo.InvariantCulture,
                                            "An error occured while placing job's triggers in error state '{0}'",
                                            trigger.FullName), se2);
                                    // db connection must have failed... keep retrying
                                    // until it's up...
                                    ReleaseTriggerRetryLoop(trigger);
                                }
                            }

                            continue;
                        }
                    }
                    else
                    {
                        // if(availTreadCount > 0)
                        continue; // should never happen, if threadPool.blockForAvailableThreads() follows contract
                    }

                    DateTime utcNow = SystemTime.UtcNow();
                    DateTime waitTime = utcNow.Add(GetRandomizedIdleWaitTime());
                    TimeSpan timeUntilContinue = waitTime - utcNow;
                    lock (sigLock) 
                    {
                	    try 
                        {
						    Monitor.Wait(sigLock, timeUntilContinue);
					    } 
                        catch (ThreadInterruptedException) 
                        {
					    }
                    }
                }
                catch (Exception re)
                {
                    if (Log != null)
                    {
                        Log.Error("Runtime error occured in main trigger firing loop.", re);
                    }
                }
            } // loop...

            // drop references to scheduler stuff to aid garbage collection...
            qs = null;
            qsRsrcs = null;
        }

        private bool IsCandidateNewTimeEarlierWithinReason(DateTime oldTimeUtc) 
        {    	
		    // So here's the deal: We know due to being signaled that 'the schedule'
		    // has changed.  We may know (if getSignaledNextFireTime() != DateTime.MinValue) the
		    // new earliest fire time.  We may not (in which case we will assume
		    // that the new time is earlier than the trigger we have acquired).
		    // In either case, we only want to abandon our acquired trigger and
		    // go looking for a new one if "it's worth it".  It's only worth it if
		    // the time cost incurred to abandon the trigger and acquire a new one 
		    // is less than the time until the currently acquired trigger will fire,
		    // otherwise we're just "thrashing" the job store (e.g. database).
		    //
		    // So the question becomes when is it "worth it"?  This will depend on
		    // the job store implementation (and of course the particular database
		    // or whatever behind it).  Ideally we would depend on the job store 
		    // implementation to tell us the amount of time in which it "thinks"
		    // it can abandon the acquired trigger and acquire a new one.  However
		    // we have no current facility for having it tell us that, so we make
		    // a somewhat educated but arbitrary guess ;-).

    	    lock (sigLock) 
            {	
			    bool earlier = false;
    			
			    if(!GetSignaledNextFireTimeUtc().HasValue)
			    {
			        earlier = true;
			    }
			    else if (GetSignaledNextFireTimeUtc().Value < oldTimeUtc)
			    {
			        earlier = true;
			    }
    			
			    if(earlier) 
                {
				    // so the new time is considered earlier, but is it enough earlier?
                    TimeSpan diff = oldTimeUtc - SystemTime.UtcNow();
				    if(diff < (qsRsrcs.JobStore.SupportsPersistence ? TimeSpan.FromMilliseconds(80) : TimeSpan.FromMilliseconds(7)))
				    {
				        earlier = false;
				    }
			    }
    			
			    ClearSignaledSchedulingChange();
    			
			    return earlier;
            }
	    }

        /// <summary>
        /// Trigger retry loop that is executed on error condition.
        /// </summary>
        /// <param name="bndle">The bndle.</param>
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
                                                              SchedulerInstruction.SetAllJobTriggersError);
                        retryCount = 0;
                        break;
                    }
                    catch (JobPersistenceException jpe)
                    {
                        if (retryCount%4 == 0)
                        {
                            qs.NotifySchedulerListenersError(
                                string.Format(CultureInfo.InvariantCulture, "An error occured while releasing trigger '{0}'", bndle.Trigger.FullName),
                                jpe);
                        }
                    }
                    catch (ThreadInterruptedException e)
                    {
                        Log.Error(string.Format(CultureInfo.InvariantCulture, "ReleaseTriggerRetryLoop: InterruptedException {0}", e.Message), e);
                    }
                    catch (Exception e)
                    {
                        Log.Error(string.Format(CultureInfo.InvariantCulture, "ReleaseTriggerRetryLoop: Exception {0}", e.Message), e);
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

        /// <summary>
        /// Releases the trigger retry loop.
        /// </summary>
        /// <param name="trigger">The trigger.</param>
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
                            qs.NotifySchedulerListenersError(
                                string.Format(CultureInfo.InvariantCulture, "An error occured while releasing trigger '{0}'", trigger.FullName), jpe);
                        }
                    }
                    catch (ThreadInterruptedException e)
                    {
                        Log.Error(string.Format(CultureInfo.InvariantCulture, "ReleaseTriggerRetryLoop: InterruptedException {0}", e.Message), e);
                    }
                    catch (Exception e)
                    {
                        Log.Error(string.Format(CultureInfo.InvariantCulture, "ReleaseTriggerRetryLoop: Exception {0}", e.Message), e);
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