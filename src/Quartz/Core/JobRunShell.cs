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
using System.Globalization;
using System.Threading;

using Common.Logging;

using Quartz.Impl;
using Quartz.Listener;
using Quartz.Spi;

namespace Quartz.Core
{
	/// <summary> 
	/// JobRunShell instances are responsible for providing the 'safe' environment
	/// for <see cref="IJob" /> s to run in, and for performing all of the work of
	/// executing the <see cref="IJob" />, catching ANY thrown exceptions, updating
	/// the <see cref="ITrigger" /> with the <see cref="IJob" />'s completion code,
	/// etc.
	/// <para>
	/// A <see cref="JobRunShell" /> instance is created by a <see cref="IJobRunShellFactory" />
	/// on behalf of the <see cref="QuartzSchedulerThread" /> which then runs the
	/// shell in a thread from the configured <see cref="ThreadPool" /> when the
	/// scheduler determines that a <see cref="IJob" /> has been triggered.
	/// </para>
	/// </summary>
	/// <seealso cref="IJobRunShellFactory" /> 
	/// <seealso cref="QuartzSchedulerThread" />
	/// <seealso cref="IJob" />
	/// <seealso cref="ITrigger" />
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
	public class JobRunShell : SchedulerListenerSupport, IThreadRunnable
	{
		private readonly ILog log;

        private JobExecutionContextImpl jec;
		private QuartzScheduler qs;
		private readonly IScheduler scheduler;
	    private readonly TriggerFiredBundle firedTriggerBundle;

        /// <summary>
		/// Create a JobRunShell instance with the given settings.
		/// </summary>
		/// <param name="scheduler">The <see cref="IScheduler" /> instance that should be made
		/// available within the <see cref="IJobExecutionContext" />.</param>
		/// <param name="bundle"></param>
        public JobRunShell(IScheduler scheduler, TriggerFiredBundle bundle)
		{
			this.scheduler = scheduler;
            firedTriggerBundle = bundle;
            log = LogManager.GetLogger(GetType());
		}

        public override void SchedulerShuttingdown()
        {
            RequestShutdown();
        }

		/// <summary>
		/// Initializes the job execution context with given scheduler and bundle.
		/// </summary>
		/// <param name="sched">The scheduler.</param>
		public virtual void Initialize(QuartzScheduler sched)
		{
			qs = sched;

			IJob job;
            IJobDetail jobDetail = firedTriggerBundle.JobDetail;

			try
			{
                job = sched.JobFactory.NewJob(firedTriggerBundle, scheduler);
			}
			catch (SchedulerException se)
			{
				sched.NotifySchedulerListenersError(string.Format(CultureInfo.InvariantCulture, "An error occurred instantiating job to be executed. job= '{0}'", jobDetail.Key), se);
				throw;
			}
			catch (Exception e)
			{
				SchedulerException se = new SchedulerException(string.Format(CultureInfo.InvariantCulture, "Problem instantiating type '{0}'", jobDetail.JobType.FullName), e);
				sched.NotifySchedulerListenersError(string.Format(CultureInfo.InvariantCulture, "An error occurred instantiating job to be executed. job= '{0}'", jobDetail.Key), se);
				throw se;
			}

            jec = new JobExecutionContextImpl(scheduler, firedTriggerBundle, job);
		}

		/// <summary>
		/// Requests the Shutdown.
		/// </summary>
		public virtual void RequestShutdown()
		{
		}

		/// <summary>
		/// This method has to be implemented in order that starting of the thread causes the object's
		/// run method to be called in that separately executing thread.
		/// </summary>
		public virtual void Run()
		{
            qs.AddInternalSchedulerListener(this);

            try
            {
                IOperableTrigger trigger = (IOperableTrigger) jec.Trigger;
                IJobDetail jobDetail = jec.JobDetail;
                do
                {
                    JobExecutionException jobExEx = null;
                    IJob job = jec.JobInstance;

                    try
                    {
                        Begin();
                    }
                    catch (SchedulerException se)
                    {
                        qs.NotifySchedulerListenersError(
                            string.Format(CultureInfo.InvariantCulture, "Error executing Job {0}: couldn't begin execution.", jec.JobDetail.Key),
                            se);
                        break;
                    }

                    // notify job & trigger listeners...
                    SchedulerInstruction instCode;
                    try
                    {
                        if (!NotifyListenersBeginning(jec))
                        {
                            break;
                        }
                    }
                    catch (VetoedException)
                    {
                        try
                        {
                            instCode = trigger.ExecutionComplete(jec, null);
                            qs.NotifyJobStoreJobVetoed(trigger, jobDetail, instCode);

                            // Even if trigger got vetoed, we still needs to check to see if it's the trigger's finalized run or not.
                            if (jec.Trigger.GetNextFireTimeUtc() == null)
                            {
                                qs.NotifySchedulerListenersFinalized(jec.Trigger);
                            }
                            Complete(true);
                        }
                        catch (SchedulerException se)
                        {
                            qs.NotifySchedulerListenersError(
                                string.Format(CultureInfo.InvariantCulture, "Error during veto of Job {0}: couldn't finalize execution.",
                                              jec.JobDetail.Key), se);
                        }
                        break;
                    }

                    DateTimeOffset startTime = SystemTime.UtcNow();
                    DateTimeOffset endTime;

                    // Execute the job
                    try
                    {
                        if (log.IsDebugEnabled)
                        {
                            log.Debug("Calling Execute on job " + jobDetail.Key);
                        }
                        job.Execute(jec);
                        endTime = SystemTime.UtcNow();
                    }
                    catch (JobExecutionException jee)
                    {
                        endTime = SystemTime.UtcNow();
                        jobExEx = jee;
                        log.Info(string.Format(CultureInfo.InvariantCulture, "Job {0} threw a JobExecutionException: ", jobDetail.Key), jobExEx);
                    }
                    catch (Exception e)
                    {
                        endTime = SystemTime.UtcNow();
                        log.Error(string.Format(CultureInfo.InvariantCulture, "Job {0} threw an unhandled Exception: ", jobDetail.Key), e);
                        SchedulerException se = new SchedulerException("Job threw an unhandled exception.", e);
                        qs.NotifySchedulerListenersError(
                            string.Format(CultureInfo.InvariantCulture, "Job {0} threw an exception.", jec.JobDetail.Key), se);
                        jobExEx = new JobExecutionException(se, false);
                    }

                    jec.JobRunTime = endTime - startTime;

                    // notify all job listeners
                    if (!NotifyJobListenersComplete(jec, jobExEx))
                    {
                        break;
                    }

                    instCode = SchedulerInstruction.NoInstruction;

                    // update the trigger
                    try
                    {
                        instCode = trigger.ExecutionComplete(jec, jobExEx);
                        if (log.IsDebugEnabled)
                        {
                            log.Debug(string.Format(CultureInfo.InvariantCulture, "Trigger instruction : {0}", instCode));
                        }
                    }
                    catch (Exception e)
                    {
                        // If this happens, there's a bug in the trigger...
                        SchedulerException se = new SchedulerException("Trigger threw an unhandled exception.", e);
                        qs.NotifySchedulerListenersError("Please report this error to the Quartz developers.", se);
                    }

                    // notify all trigger listeners
                    if (!NotifyTriggerListenersComplete(jec, instCode))
                    {
                        break;
                    }
                    // update job/trigger or re-Execute job
                    if (instCode == SchedulerInstruction.ReExecuteJob)
                    {
                        if (log.IsDebugEnabled)
                        {
                            log.Debug("Rescheduling trigger to reexecute");
                        }
                        jec.IncrementRefireCount();
                        try
                        {
                            Complete(false);
                        }
                        catch (SchedulerException se)
                        {
                            qs.NotifySchedulerListenersError(
                                string.Format(CultureInfo.InvariantCulture, "Error executing Job {0}: couldn't finalize execution.",
                                              jec.JobDetail.Key), se);
                        }
                        continue;
                    }

                    try
                    {
                        Complete(true);
                    }
                    catch (SchedulerException se)
                    {
                        qs.NotifySchedulerListenersError(
                            string.Format(CultureInfo.InvariantCulture, "Error executing Job {0}: couldn't finalize execution.",
                                          jec.JobDetail.Key), se);
                        continue;
                    }

                    qs.NotifyJobStoreJobComplete(trigger, jobDetail, instCode);

                    break;
                } while (true);

            }
		    finally
            {
                qs.RemoveInternalSchedulerListener(this);
                if (jec != null && jec.JobInstance != null)
                {
                    qs.JobFactory.ReturnJob(jec.JobInstance);
                }
            }
		}

		/// <summary>
		/// Runs begin procedures on this instance.
		/// </summary>
		protected virtual void Begin()
		{
		}

		/// <summary>
		/// Completes the execution.
		/// </summary>
		/// <param name="successfulExecution">if set to <c>true</c> [successful execution].</param>
        protected virtual void Complete(bool successfulExecution)
		{
		}

		/// <summary>
		/// Passivates this instance.
		/// </summary>
		public virtual void Passivate()
		{
			jec = null;
			qs = null;
		}

		private bool NotifyListenersBeginning(IJobExecutionContext ctx)
		{
			bool vetoed;

			// notify all trigger listeners
			try
			{
				vetoed = qs.NotifyTriggerListenersFired(ctx);
			}
			catch (SchedulerException se)
			{
				qs.NotifySchedulerListenersError(
					string.Format(CultureInfo.InvariantCulture, "Unable to notify TriggerListener(s) while firing trigger (Trigger and Job will NOT be fired!). trigger= {0} job= {1}", ctx.Trigger.Key, ctx.JobDetail.Key), se);

				return false;
			}

			if (vetoed)
			{
				try
				{
					qs.NotifyJobListenersWasVetoed(ctx);
				}
				catch (SchedulerException se)
				{
					qs.NotifySchedulerListenersError(
						string.Format(CultureInfo.InvariantCulture, "Unable to notify JobListener(s) of vetoed execution while firing trigger (Trigger and Job will NOT be fired!). trigger= {0} job= {1}", ctx.Trigger.Key, ctx.JobDetail.Key), se);
				}
				throw new VetoedException(this);
			}

			// notify all job listeners
			try
			{
				qs.NotifyJobListenersToBeExecuted(ctx);
			}
			catch (SchedulerException se)
			{
				qs.NotifySchedulerListenersError(
					string.Format(CultureInfo.InvariantCulture, "Unable to notify JobListener(s) of Job to be executed: (Job will NOT be executed!). trigger= {0} job= {1}", ctx.Trigger.Key, ctx.JobDetail.Key), se);

				return false;
			}

			return true;
		}

		private bool NotifyJobListenersComplete(IJobExecutionContext ctx, JobExecutionException jobExEx)
		{
			try
			{
				qs.NotifyJobListenersWasExecuted(ctx, jobExEx);
			}
			catch (SchedulerException se)
			{
				qs.NotifySchedulerListenersError(
					string.Format(CultureInfo.InvariantCulture, "Unable to notify JobListener(s) of Job that was executed: (error will be ignored). trigger= {0} job= {1}", ctx.Trigger.Key, ctx.JobDetail.Key), se);

				return false;
			}

			return true;
		}

		private bool NotifyTriggerListenersComplete(IJobExecutionContext ctx, SchedulerInstruction instCode)
		{
			try
			{
				qs.NotifyTriggerListenersComplete(ctx, instCode);
			}
			catch (SchedulerException se)
			{
				qs.NotifySchedulerListenersError(
					string.Format(CultureInfo.InvariantCulture, "Unable to notify TriggerListener(s) of Job that was executed: (error will be ignored). trigger= {0} job= {1}", ctx.Trigger.Key, ctx.JobDetail.Key), se);

				return false;
			}

			if (!ctx.Trigger.GetNextFireTimeUtc().HasValue)
			{
				qs.NotifySchedulerListenersFinalized(ctx.Trigger);
			}

			return true;
		}

		[Serializable]
		internal class VetoedException : Exception
		{
			private readonly JobRunShell enclosingInstance;

			public JobRunShell EnclosingInstance
			{
				get { return enclosingInstance; }
			}

			public VetoedException(JobRunShell shell)
			{
				enclosingInstance = shell;
			}
		}
	}
}
