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
using System.Globalization;
using System.Threading;

using Common.Logging;

using Quartz.Spi;

namespace Quartz.Core
{
	/// <summary> 
	/// JobRunShell instances are responsible for providing the 'safe' environment
	/// for <see cref="IJob" /> s to run in, and for performing all of the work of
	/// executing the <see cref="IJob" />, catching ANY thrown exceptions, updating
	/// the <see cref="Trigger" /> with the <see cref="IJob" />'s completion code,
	/// etc.
	/// <p>
	/// A <see cref="JobRunShell" /> instance is created by a <see cref="IJobRunShellFactory" />
	/// on behalf of the <see cref="QuartzSchedulerThread" /> which then runs the
	/// shell in a thread from the configured <see cref="ThreadPool" /> when the
	/// scheduler determines that a <see cref="IJob" /> has been triggered.
	/// </p>
	/// </summary>
	/// <seealso cref="IJobRunShellFactory" /> 
	/// <seealso cref="QuartzSchedulerThread" />
	/// <seealso cref="IJob" />
	/// <seealso cref="Trigger" />
	/// <author>James House</author>
	/// <author>Marko Lahma (.NET)</author>
	public class JobRunShell : IThreadRunnable
	{
		private readonly ILog log;

		private JobExecutionContext jec = null;
		private QuartzScheduler qs = null;
		private readonly IScheduler scheduler = null;
		private readonly SchedulingContext schdCtxt = null;
		private readonly IJobRunShellFactory jobRunShellFactory = null;
		private bool shutdownRequested = false;


		/// <summary>
		/// Create a JobRunShell instance with the given settings.
		/// </summary>
		/// <param name="jobRunShellFactory">A handle to the <see cref="IJobRunShellFactory" /> that produced
		/// this <see cref="JobRunShell" />.</param>
		/// <param name="scheduler">The <see cref="IScheduler" /> instance that should be made
		/// available within the <see cref="JobExecutionContext" />.</param>
		/// <param name="schdCtxt">the <see cref="SchedulingContext" /> that should be used by the
		/// <see cref="JobRunShell" /> when making updates to the <see cref="IJobStore" />.</param>
		public JobRunShell(IJobRunShellFactory jobRunShellFactory, IScheduler scheduler, SchedulingContext schdCtxt)
		{
			this.jobRunShellFactory = jobRunShellFactory;
			this.scheduler = scheduler;
			this.schdCtxt = schdCtxt;
            log = LogManager.GetLogger(GetType());
		}

		/// <summary>
		/// Initializes the job execution context with given scheduler and bundle.
		/// </summary>
		/// <param name="sched">The scheduler.</param>
		/// <param name="firedBundle">The bundle offired triggers.</param>
		public virtual void Initialize(QuartzScheduler sched, TriggerFiredBundle firedBundle)
		{
			qs = sched;

			IJob job;
			JobDetail jobDetail = firedBundle.JobDetail;

			try
			{
				job = sched.JobFactory.NewJob(firedBundle);
			}
			catch (SchedulerException se)
			{
				sched.NotifySchedulerListenersError(string.Format(CultureInfo.InvariantCulture, "An error occured instantiating job to be executed. job= '{0}'", jobDetail.FullName), se);
				throw;
			}
			catch (Exception e)
			{
				SchedulerException se = new SchedulerException(string.Format(CultureInfo.InvariantCulture, "Problem instantiating type '{0}'", jobDetail.JobType.FullName), e);
				sched.NotifySchedulerListenersError(string.Format(CultureInfo.InvariantCulture, "An error occured instantiating job to be executed. job= '{0}'", jobDetail.FullName), se);
				throw se;
			}

			jec = new JobExecutionContext(scheduler, firedBundle, job);
		}

		/// <summary>
		/// Requests the Shutdown.
		/// </summary>
		public virtual void RequestShutdown()
		{
			shutdownRequested = true;
		}

		/// <summary>
		/// This method has to be implemented in order that starting of the thread causes the object's
		/// run method to be called in that separately executing thread.
		/// </summary>
		public virtual void Run()
		{
            try
            {
                Trigger trigger = jec.Trigger;
                JobDetail jobDetail = jec.JobDetail;
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
                            string.Format(CultureInfo.InvariantCulture, "Error executing Job ({0}: couldn't begin execution.", jec.JobDetail.FullName),
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
                            try
                            {
                                qs.NotifyJobStoreJobVetoed(schdCtxt, trigger, jobDetail, instCode);
                            }
                            catch (JobPersistenceException)
                            {
                                VetoedJobRetryLoop(trigger, jobDetail, instCode);
                            }
                            Complete(true);
                        }
                        catch (SchedulerException se)
                        {
                            qs.NotifySchedulerListenersError(
                                string.Format(CultureInfo.InvariantCulture, "Error during veto of Job ({0}: couldn't finalize execution.",
                                              jec.JobDetail.FullName), se);
                        }
                        break;
                    }

                    DateTime startTime = DateTime.UtcNow;
                    DateTime endTime;

                    // Execute the job
                    try
                    {
                        if (log.IsDebugEnabled)
                        {
                            log.Debug("Calling Execute on job " + jobDetail.FullName);
                        }
                        job.Execute(jec);
                        endTime = DateTime.UtcNow;
                    }
                    catch (JobExecutionException jee)
                    {
                        endTime = DateTime.UtcNow;
                        jobExEx = jee;
                        log.Info(string.Format(CultureInfo.InvariantCulture, "Job {0} threw a JobExecutionException: ", jobDetail.FullName), jobExEx);
                    }
                    catch (Exception e)
                    {
                        endTime = DateTime.UtcNow;
                        log.Error(string.Format(CultureInfo.InvariantCulture, "Job {0} threw an unhandled Exception: ", jobDetail.FullName), e);
                        SchedulerException se = new SchedulerException("Job threw an unhandled exception.", e);
                        se.ErrorCode = SchedulerException.ErrorJobExecutionThrewException;
                        qs.NotifySchedulerListenersError(
                            string.Format(CultureInfo.InvariantCulture, "Job ({0} threw an exception.", jec.JobDetail.FullName), se);
                        jobExEx = new JobExecutionException(se, false);
                        jobExEx.ErrorCode = JobExecutionException.ErrorJobExecutionThrewException;
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
                        se.ErrorCode = SchedulerException.ErrorTriggerThrewException;
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
                                string.Format(CultureInfo.InvariantCulture, "Error executing Job ({0}: couldn't finalize execution.",
                                              jec.JobDetail.FullName), se);
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
                            string.Format(CultureInfo.InvariantCulture, "Error executing Job ({0}: couldn't finalize execution.",
                                          jec.JobDetail.FullName), se);
                        continue;
                    }

                    try
                    {
                        qs.NotifyJobStoreJobComplete(schdCtxt, trigger, jobDetail, instCode);
                    }
                    catch (JobPersistenceException jpe)
                    {
                        qs.NotifySchedulerListenersError(
                            string.Format(CultureInfo.InvariantCulture, "An error occured while marking executed job complete. job= '{0}'",
                                          jobDetail.FullName), jpe);
                        if (!CompleteTriggerRetryLoop(trigger, jobDetail, instCode))
                        {
                        }
                        return;
                    }

                    break;
                } while (true);

            }
		    finally
            {
                jobRunShellFactory.ReturnJobRunShell(this);
            }
		}

		/// <summary>
		/// Runs begin procedures on this instance.
		/// </summary>
		protected internal virtual void Begin()
		{
		}

		/// <summary>
		/// Completes the execution.
		/// </summary>
		/// <param name="successfulExecution">if set to <c>true</c> [successful execution].</param>
		protected internal virtual void Complete(bool successfulExecution)
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

		private bool NotifyListenersBeginning(JobExecutionContext ctx)
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
					string.Format(CultureInfo.InvariantCulture, "Unable to notify TriggerListener(s) while firing trigger (Trigger and Job will NOT be fired!). trigger= {0} job= {1}", ctx.Trigger.FullName, ctx.JobDetail.FullName), se);

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
						string.Format(CultureInfo.InvariantCulture, "Unable to notify JobListener(s) of vetoed execution while firing trigger (Trigger and Job will NOT be fired!). trigger= {0} job= {1}", ctx.Trigger.FullName, ctx.JobDetail.FullName), se);
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
					string.Format(CultureInfo.InvariantCulture, "Unable to notify JobListener(s) of Job to be executed: (Job will NOT be executed!). trigger= {0} job= {1}", ctx.Trigger.FullName, ctx.JobDetail.FullName), se);

				return false;
			}

			return true;
		}

		private bool NotifyJobListenersComplete(JobExecutionContext ctx, JobExecutionException jobExEx)
		{
			try
			{
				qs.NotifyJobListenersWasExecuted(ctx, jobExEx);
			}
			catch (SchedulerException se)
			{
				qs.NotifySchedulerListenersError(
					string.Format(CultureInfo.InvariantCulture, "Unable to notify JobListener(s) of Job that was executed: (error will be ignored). trigger= {0} job= {1}", ctx.Trigger.FullName, ctx.JobDetail.FullName), se);

				return false;
			}

			return true;
		}

		private bool NotifyTriggerListenersComplete(JobExecutionContext ctx, SchedulerInstruction instCode)
		{
			try
			{
				qs.NotifyTriggerListenersComplete(ctx, instCode);
			}
			catch (SchedulerException se)
			{
				qs.NotifySchedulerListenersError(
					string.Format(CultureInfo.InvariantCulture, "Unable to notify TriggerListener(s) of Job that was executed: (error will be ignored). trigger= {0} job= {1}", ctx.Trigger.FullName, ctx.JobDetail.FullName), se);

				return false;
			}

			if (!ctx.Trigger.GetNextFireTimeUtc().HasValue)
			{
				qs.NotifySchedulerListenersFinalized(ctx.Trigger);
			}

			return true;
		}

		/// <summary>
		/// Completes the trigger retry loop.
		/// </summary>
		/// <param name="trigger">The trigger.</param>
		/// <param name="jobDetail">The job detail.</param>
		/// <param name="instCode">The inst code.</param>
		/// <returns></returns>
        public virtual bool CompleteTriggerRetryLoop(Trigger trigger, JobDetail jobDetail, SchedulerInstruction instCode)
		{
            long count = 0;
            while (!shutdownRequested)
            { // FIXME: jhouse: note that there is no longer anthing that calls requestShutdown()
                try
                {
                    Thread.Sleep(TimeSpan.FromSeconds(15)); 
                    // retry every 15 seconds (the db
                    // connection must be failed)
                    qs.NotifyJobStoreJobComplete(schdCtxt, trigger, jobDetail, instCode);
                    return true;
                }
                catch (JobPersistenceException jpe)
                {
                    if (count % 4 == 0)
                        qs.NotifySchedulerListenersError(
                            "An error occured while marking executed job complete (will continue attempts). job= '"
                                    + jobDetail.FullName + "'", jpe);
                }
                catch (ThreadInterruptedException)
                {
                }
                count++;
            }
            return false;
		}

		/// <summary>
		/// Vetoeds the job retry loop.
		/// </summary>
		/// <param name="trigger">The trigger.</param>
		/// <param name="jobDetail">The job detail.</param>
		/// <param name="instCode">The inst code.</param>
		/// <returns></returns>
        public bool VetoedJobRetryLoop(Trigger trigger, JobDetail jobDetail, SchedulerInstruction instCode)
        {
            while (!shutdownRequested)
            {
                try
                {
                    Thread.Sleep(5 * 1000); // retry every 5 seconds (the db
                    // connection must be failed)
                    qs.NotifyJobStoreJobVetoed(schdCtxt, trigger, jobDetail, instCode);
                    return true;
                }
                catch (JobPersistenceException jpe)
                {
                    qs.NotifySchedulerListenersError(
                            string.Format(CultureInfo.InvariantCulture, "An error occured while marking executed job vetoed. job= '{0}'", jobDetail.FullName), jpe);
                }
                catch (ThreadInterruptedException)
                {
                }
            }
            return false;
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
