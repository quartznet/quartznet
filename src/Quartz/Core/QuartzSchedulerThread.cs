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
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Threading;

using Common.Logging;

using Quartz.Spi;

namespace Quartz.Core
{
    /// <summary>
    /// The thread responsible for performing the work of firing <see cref="ITrigger" />
    /// s that are registered with the <see cref="QuartzScheduler" />.
    /// </summary>
    /// <seealso cref="QuartzScheduler" />
    /// <seealso cref="IJob" />
    /// <seealso cref="ITrigger" />
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class QuartzSchedulerThread : QuartzThread
    {
        private readonly ILog log;
        private QuartzScheduler qs;
        private QuartzSchedulerResources qsRsrcs;
        private readonly object sigLock = new object();

        private bool signaled;
        private DateTimeOffset? signaledNextFireTimeUtc;
        private bool paused;
        private bool halted;

        private readonly Random random = new Random((int) DateTimeOffset.Now.Ticks);

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
        internal QuartzSchedulerThread(QuartzScheduler qs, QuartzSchedulerResources qsRsrcs)
            : this(qs, qsRsrcs, qsRsrcs.MakeSchedulerThreadDaemon, (int) ThreadPriority.Normal)
        {
        }

        /// <summary>
        /// Construct a new <see cref="QuartzSchedulerThread" /> for the given
        /// <see cref="QuartzScheduler" /> as a <see cref="Thread" /> with the given
        /// attributes.
        /// </summary>
        internal QuartzSchedulerThread(QuartzScheduler qs, QuartzSchedulerResources qsRsrcs, 
                                       bool setDaemon, int threadPrio) : base(qsRsrcs.ThreadName)
        {
            log = LogManager.GetLogger(GetType());
            //ThreadGroup generatedAux = qs.SchedulerThreadGroup;
            this.qs = qs;
            this.qsRsrcs = qsRsrcs;
            IsBackground = setDaemon;
            Priority = (ThreadPriority) threadPrio;

            // start the underlying thread, but put this object into the 'paused'
            // state
            // so processing doesn't start yet...
            paused = true;
            halted = false;
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
        public void SignalSchedulingChange(DateTimeOffset? candidateNewNextFireTimeUtc) 
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

        
        public DateTimeOffset? GetSignaledNextFireTimeUtc() 
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

                    int availThreadCount = qsRsrcs.ThreadPool.BlockForAvailableThreads();
                    if (availThreadCount > 0) // will always be true, due to semantics of blockForAvailableThreads...
                    {
                        IList<IOperableTrigger> triggers = null;

                        DateTimeOffset now = SystemTime.UtcNow();

                        ClearSignaledSchedulingChange();
                        try
                        {
                            triggers = qsRsrcs.JobStore.AcquireNextTriggers(
                                now + idleWaitTime, Math.Min(availThreadCount, qsRsrcs.MaxBatchSize), qsRsrcs.BatchTimeWindow);
                            lastAcquireFailed = false;
                            if (log.IsDebugEnabled)
                            {
                                log.DebugFormat("Batch acquisition of {0} triggers", (triggers == null ? 0 : triggers.Count));
                            }
                        }
                        catch (JobPersistenceException jpe)
                        {
                            if (!lastAcquireFailed)
                            {
                                qs.NotifySchedulerListenersError(
                                    "An error occurred while scanning for the next trigger to fire.",
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

                        if (triggers != null && triggers.Count > 0)
                        {
                            now = SystemTime.UtcNow();
                            DateTimeOffset triggerTime = triggers[0].GetNextFireTimeUtc().Value;
                            TimeSpan timeUntilTrigger =  triggerTime - now;

                            while (timeUntilTrigger > TimeSpan.FromMilliseconds(2)) 
                            {
                                if (ReleaseIfScheduleChangedSignificantly(triggers, triggerTime))
                                {
                                    break;
                                }
                                lock (sigLock)
                                {
                                    if (halted)
                                    {
                                        break;
                                    }
                                    if (!IsCandidateNewTimeEarlierWithinReason(triggerTime, false))
                                    {
                                        try
                                        {
                                            // we could have blocked a long while
                                            // on 'synchronize', so we must recompute
                                            now = SystemTime.UtcNow();
                                            timeUntilTrigger = triggerTime - now;
                                            if (timeUntilTrigger > TimeSpan.Zero)
                                            {
                                                Monitor.Wait(sigLock, timeUntilTrigger);
                                            }
                                        }
                                        catch (ThreadInterruptedException)
                                        {
                                        }
                                    }
                                }
                                if (ReleaseIfScheduleChangedSignificantly(triggers, triggerTime))
                                {
                                    break;
                                } 
                                now = SystemTime.UtcNow();
                                timeUntilTrigger = triggerTime - now;
                            }

                            // this happens if releaseIfScheduleChangedSignificantly decided to release triggers
                            if (triggers.Count == 0)
                            {
                                continue;
                            }
                                              
                            // set triggers to 'executing'
                            IList<TriggerFiredResult> bndles = new List<TriggerFiredResult>();

                            bool goAhead = true;
                            lock (sigLock) 
                            {
                        	    goAhead = !halted;
                            }

                            if (goAhead)
                            {
                                try
                                {
                                    IList<TriggerFiredResult> res = qsRsrcs.JobStore.TriggersFired(triggers);
                                    if (res != null)
                                    {
                                        bndles = res;
                                    }
                                }
                                catch (SchedulerException se)
                                {
                                    qs.NotifySchedulerListenersError("An error occurred while firing triggers '" + triggers + "'", se);
                                }

                            }
                            

                        for (int i = 0; i < bndles.Count; i++)
                        {
                            TriggerFiredResult result = bndles[i];
                            TriggerFiredBundle bndle = result.TriggerFiredBundle;
                            Exception exception = result.Exception;

                            IOperableTrigger trigger = triggers[i];
                            // TODO SQL exception?
                            if (exception != null &&  (exception is DbException || exception.InnerException is DbException))
                            {
                                Log.Error("DbException while firing trigger " + trigger, exception);
                                // db connection must have failed... keep
                                // retrying until it's up...
                                ReleaseTriggerRetryLoop(trigger);
                                continue;
                            }

                            // it's possible to get 'null' if the triggers was paused,
                            // blocked, or other similar occurrences that prevent it being
                            // fired at this time...  or if the scheduler was shutdown (halted)
                            if (bndle == null)
                            {
                                try
                                {
                                    qsRsrcs.JobStore.ReleaseAcquiredTrigger(trigger);
                                }
                                catch (SchedulerException se)
                                {
                                    qs.NotifySchedulerListenersError(
                                        "An error occurred while releasing triggers '" + trigger.Key + "'", se);
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

                            JobRunShell shell = null;
                            try
                            {
                                shell = qsRsrcs.JobRunShellFactory.CreateJobRunShell(bndle);
                                shell.Initialize(qs);
                            }
                            catch (SchedulerException)
                            {
                                try
                                {
                                    qsRsrcs.JobStore.TriggeredJobComplete(trigger, bndle.JobDetail,
                                                                          SchedulerInstruction.SetAllJobTriggersError);
                                }
                                catch (SchedulerException se2)
                                {
                                    qs.NotifySchedulerListenersError(
                                        "An error occurred while placing job's triggers in error state '" +
                                        trigger.Key + "'", se2);
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
                                    qsRsrcs.JobStore.TriggeredJobComplete(trigger, bndle.JobDetail,
                                                                          SchedulerInstruction.
                                                                              SetAllJobTriggersError);
                                }
                                catch (SchedulerException se2)
                                {
                                    qs.NotifySchedulerListenersError(
                                        string.Format(CultureInfo.InvariantCulture,
                                                      "An error occurred while placing job's triggers in error state '{0}'",
                                                      trigger.Key), se2);
                                    // db connection must have failed... keep retrying
                                    // until it's up...
                                    ReleaseTriggerRetryLoop(trigger);
                                }
                            }
                        }

                            continue; // while (!halted)
                        }
                    }
                    else // if(availThreadCount > 0)
                    {
                        // should never happen, if threadPool.blockForAvailableThreads() follows contract
                        continue;
                        // while (!halted)
                    }

                    DateTimeOffset utcNow = SystemTime.UtcNow();
                    DateTimeOffset waitTime = utcNow.Add(GetRandomizedIdleWaitTime());
                    TimeSpan timeUntilContinue = waitTime - utcNow;
                    lock (sigLock)
                    {
                        if (!halted)
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
                }
                catch (Exception re)
                {
                    if (Log != null)
                    {
                        Log.Error("Runtime error occurred in main trigger firing loop.", re);
                    }
                }
            } // while (!halted)

            // drop references to scheduler stuff to aid garbage collection...
            qs = null;
            qsRsrcs = null;
        }


        private bool ReleaseIfScheduleChangedSignificantly(IList<IOperableTrigger> triggers, DateTimeOffset triggerTime)
        {
            if (IsCandidateNewTimeEarlierWithinReason(triggerTime, true))
            {
                foreach (IOperableTrigger trigger in triggers)
                {
                    try
                    {
                        // above call does a clearSignaledSchedulingChange()
                        qsRsrcs.JobStore.ReleaseAcquiredTrigger(trigger);
                    }
                    catch (JobPersistenceException jpe)
                    {
                        qs.NotifySchedulerListenersError(
                            string.Format("An error occurred while releasing trigger '{0}'", trigger.Key), jpe);
                        // db connection must have failed... keep
                        // retrying until it's up...
                        ReleaseTriggerRetryLoop(trigger);
                    }
                    catch (Exception e)
                    {
                        Log.Error("ReleaseTriggerRetryLoop: Exception " + e.Message, e);
                        // db connection must have failed... keep
                        // retrying until it's up...
                        ReleaseTriggerRetryLoop(trigger);
                    }
                }
                triggers.Clear();
                return true;
            }
            
            return false;
        }

        private bool IsCandidateNewTimeEarlierWithinReason(DateTimeOffset oldTimeUtc, bool clearSignal) 
        {    	
		    // So here's the deal: We know due to being signaled that 'the schedule'
		    // has changed.  We may know (if getSignaledNextFireTime() != DateTimeOffset.MinValue) the
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

                if (!IsScheduleChanged())
                {
                    return false;
                }

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
				    if(diff < (qsRsrcs.JobStore.SupportsPersistence ? TimeSpan.FromMilliseconds(70) : TimeSpan.FromMilliseconds(7)))
				    {
				        earlier = false;
				    }
			    }
    			
                if (clearSignal)
                {
                    ClearSignaledSchedulingChange();
                }
    			
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
                        qsRsrcs.JobStore.TriggeredJobComplete(bndle.Trigger, bndle.JobDetail,
                                                              SchedulerInstruction.SetAllJobTriggersError);
                        retryCount = 0;
                        break;
                    }
                    catch (JobPersistenceException jpe)
                    {
                        if (retryCount%4 == 0)
                        {
                            qs.NotifySchedulerListenersError(
                                string.Format(CultureInfo.InvariantCulture, "An error occurred while releasing trigger '{0}'", bndle.Trigger.Key),
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
        public virtual void ReleaseTriggerRetryLoop(IOperableTrigger trigger)
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
                        qsRsrcs.JobStore.ReleaseAcquiredTrigger(trigger);
                        retryCount = 0;
                        break;
                    }
                    catch (JobPersistenceException jpe)
                    {
                        if (retryCount%4 == 0)
                        {
                            qs.NotifySchedulerListenersError(
                                string.Format(CultureInfo.InvariantCulture, "An error occurred while releasing trigger '{0}'", trigger.Key), jpe);
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