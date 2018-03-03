#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
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
using System.Threading.Tasks;

using Quartz.Impl;
using Quartz.Listener;
using Quartz.Logging;
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
    /// shell in a thread from the configured thread pool when the
    /// scheduler determines that a <see cref="IJob" /> has been triggered.
    /// </para>
    /// </summary>
    /// <seealso cref="IJobRunShellFactory" />
    /// <seealso cref="QuartzSchedulerThread" />
    /// <seealso cref="IJob" />
    /// <seealso cref="ITrigger" />
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class JobRunShell : SchedulerListenerSupport
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
            log = LogProvider.GetLogger(GetType());
        }

        public override Task SchedulerShuttingdown(CancellationToken cancellationToken = default)
        {
            RequestShutdown();
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Initializes the job execution context with given scheduler and bundle.
        /// </summary>
        /// <param name="sched">The scheduler.</param>
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual async Task Initialize(
            QuartzScheduler sched,
            CancellationToken cancellationToken = default)
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
                await sched.NotifySchedulerListenersError($"An error occurred instantiating job to be executed. job= '{jobDetail.Key}'", se, cancellationToken).ConfigureAwait(false);
                throw;
            }
            catch (Exception e)
            {
                SchedulerException se = new SchedulerException($"Problem instantiating type '{jobDetail.JobType.FullName}'", e);
                await sched.NotifySchedulerListenersError($"An error occurred instantiating job to be executed. job= '{jobDetail.Key}'", se, cancellationToken).ConfigureAwait(false);
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
        /// <param name="cancellationToken">The cancellation instruction.</param>
        public virtual async Task Run(CancellationToken cancellationToken = default)
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
                        string msg = $"Error executing Job {jec.JobDetail.Key}: couldn't begin execution.";
                        await qs.NotifySchedulerListenersError(msg, se, cancellationToken).ConfigureAwait(false);
                        break;
                    }

                    // notify job & trigger listeners...
                    SchedulerInstruction instCode;
                    try
                    {
                        if (!await NotifyListenersBeginning(jec, cancellationToken).ConfigureAwait(false))
                        {
                            break;
                        }
                    }
                    catch (VetoedException)
                    {
                        try
                        {
                            instCode = trigger.ExecutionComplete(jec, null);
                            await qs.NotifyJobStoreJobVetoed(trigger, jobDetail, instCode, cancellationToken).ConfigureAwait(false);

                            // Even if trigger got vetoed, we still needs to check to see if it's the trigger's finalized run or not.
                            if (jec.Trigger.GetNextFireTimeUtc() == null)
                            {
                                await qs.NotifySchedulerListenersFinalized(jec.Trigger, cancellationToken).ConfigureAwait(false);
                            }
                            Complete(true);
                        }
                        catch (SchedulerException se)
                        {
                            string msg = $"Error during veto of Job {jec.JobDetail.Key}: couldn't finalize execution.";
                            await qs.NotifySchedulerListenersError(msg, se, cancellationToken).ConfigureAwait(false);
                        }
                        break;
                    }

                    DateTimeOffset startTime = SystemTime.UtcNow();
                    DateTimeOffset endTime;

                    // Execute the job
                    try
                    {
                        if (log.IsDebugEnabled())
                        {
                            log.Debug("Calling Execute on job " + jobDetail.Key);
                        }

                        await job.Execute(jec).ConfigureAwait(false);

                        endTime = SystemTime.UtcNow();
                    }
                    catch (OperationCanceledException)
                    {
                        endTime = SystemTime.UtcNow();
                        log.InfoFormat($"Job {jobDetail.Key} was cancelled");
                    }
                    catch (JobExecutionException jee)
                    {
                        endTime = SystemTime.UtcNow();
                        jobExEx = jee;
                        log.ErrorException($"Job {jobDetail.Key} threw a JobExecutionException: ", jobExEx);
                    }
                    catch (Exception e)
                    {
                        endTime = SystemTime.UtcNow();
                        log.ErrorException($"Job {jobDetail.Key} threw an unhandled Exception: ", e);
                        SchedulerException se = new SchedulerException("Job threw an unhandled exception.", e);
                        string msg = $"Job {jec.JobDetail.Key} threw an exception.";
                        await qs.NotifySchedulerListenersError(msg, se, cancellationToken).ConfigureAwait(false);
                        jobExEx = new JobExecutionException(se, false);
                    }

                    jec.JobRunTime = endTime - startTime;

                    // notify all job listeners
                    if (!await NotifyJobListenersComplete(jec, jobExEx, cancellationToken).ConfigureAwait(false))
                    {
                        break;
                    }

                    instCode = SchedulerInstruction.NoInstruction;

                    // update the trigger
                    try
                    {
                        instCode = trigger.ExecutionComplete(jec, jobExEx);
                        if (log.IsDebugEnabled())
                        {
                            log.Debug($"Trigger instruction : {instCode}");
                        }
                    }
                    catch (Exception e)
                    {
                        // If this happens, there's a bug in the trigger...
                        SchedulerException se = new SchedulerException("Trigger threw an unhandled exception.", e);
                        await qs.NotifySchedulerListenersError("Please report this error to the Quartz developers.", se, cancellationToken).ConfigureAwait(false);
                    }

                    // notify all trigger listeners
                    if (!await NotifyTriggerListenersComplete(jec, instCode, cancellationToken).ConfigureAwait(false))
                    {
                        break;
                    }
                    // update job/trigger or re-Execute job
                    if (instCode == SchedulerInstruction.ReExecuteJob)
                    {
                        if (log.IsDebugEnabled())
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
                            await qs.NotifySchedulerListenersError($"Error executing Job {jec.JobDetail.Key}: couldn't finalize execution.", se, cancellationToken).ConfigureAwait(false);
                        }
                        continue;
                    }

                    try
                    {
                        Complete(true);
                    }
                    catch (SchedulerException se)
                    {
                        await qs.NotifySchedulerListenersError($"Error executing Job {jec.JobDetail.Key}: couldn't finalize execution.", se, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    await qs.NotifyJobStoreJobComplete(trigger, jobDetail, instCode, cancellationToken).ConfigureAwait(false);

                    break;
                } while (true);
            }
            finally
            {
                qs.RemoveInternalSchedulerListener(this);
                if (jec != null)
                {
                    if (jec.JobInstance != null)
                    {
                        qs.JobFactory.ReturnJob(jec.JobInstance);
                    }

                    jec.Dispose();
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

        private async Task<bool> NotifyListenersBeginning(
            IJobExecutionContext ctx,
            CancellationToken cancellationToken = default)
        {
            bool vetoed;

            // notify all trigger listeners
            try
            {
                vetoed = await qs.NotifyTriggerListenersFired(ctx, cancellationToken).ConfigureAwait(false);
            }
            catch (SchedulerException se)
            {
                string msg = $"Unable to notify TriggerListener(s) while firing trigger (Trigger and Job will NOT be fired!). trigger= {ctx.Trigger.Key} job= {ctx.JobDetail.Key}";
                await qs.NotifySchedulerListenersError(msg, se, cancellationToken).ConfigureAwait(false);

                return false;
            }

            if (vetoed)
            {
                try
                {
                    await qs.NotifyJobListenersWasVetoed(ctx, cancellationToken).ConfigureAwait(false);
                }
                catch (SchedulerException se)
                {
                    string msg = $"Unable to notify JobListener(s) of vetoed execution while firing trigger (Trigger and Job will NOT be fired!). trigger= {ctx.Trigger.Key} job= {ctx.JobDetail.Key}";
                    await qs.NotifySchedulerListenersError(msg, se, cancellationToken).ConfigureAwait(false);
                }
                throw new VetoedException(this);
            }

            // notify all job listeners
            try
            {
                await qs.NotifyJobListenersToBeExecuted(ctx, cancellationToken).ConfigureAwait(false);
            }
            catch (SchedulerException se)
            {
                string msg = $"Unable to notify JobListener(s) of Job to be executed: (Job will NOT be executed!). trigger= {ctx.Trigger.Key} job= {ctx.JobDetail.Key}";
                await qs.NotifySchedulerListenersError(msg, se, cancellationToken).ConfigureAwait(false);

                return false;
            }

            return true;
        }

        private async Task<bool> NotifyJobListenersComplete(
            IJobExecutionContext ctx,
            JobExecutionException jobExEx,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await qs.NotifyJobListenersWasExecuted(ctx, jobExEx, cancellationToken).ConfigureAwait(false);
            }
            catch (SchedulerException se)
            {
                string msg = $"Unable to notify JobListener(s) of Job that was executed: (error will be ignored). trigger= {ctx.Trigger.Key} job= {ctx.JobDetail.Key}";
                await qs.NotifySchedulerListenersError(msg, se, cancellationToken).ConfigureAwait(false);

                return false;
            }

            return true;
        }

        private async Task<bool> NotifyTriggerListenersComplete(
            IJobExecutionContext ctx,
            SchedulerInstruction instCode,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await qs.NotifyTriggerListenersComplete(ctx, instCode, cancellationToken).ConfigureAwait(false);
            }
            catch (SchedulerException se)
            {
                string msg = $"Unable to notify TriggerListener(s) of Job that was executed: (error will be ignored). trigger= {ctx.Trigger.Key} job= {ctx.JobDetail.Key}";
                await qs.NotifySchedulerListenersError(msg, se, cancellationToken).ConfigureAwait(false);

                return false;
            }

            if (!ctx.Trigger.GetNextFireTimeUtc().HasValue)
            {
                await qs.NotifySchedulerListenersFinalized(ctx.Trigger, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }

        [Serializable]
        internal class VetoedException : Exception
        {
            public VetoedException(JobRunShell shell)
            {
                EnclosingInstance = shell;
            }

            public JobRunShell EnclosingInstance { get; }
        }
    }
}