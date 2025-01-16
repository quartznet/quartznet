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

using System.Diagnostics;

using Microsoft.Extensions.Logging;

using Quartz.Impl;
using Quartz.Listener;
using Quartz.Diagnostics;
using Quartz.Spi;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Core;

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
    private readonly ILogger<JobRunShell> logger;

    private JobExecutionContextImpl? jec;
    private QuartzScheduler? qs;
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
        logger = LogProvider.CreateLogger<JobRunShell>();
    }

    public override ValueTask SchedulerShuttingdown(CancellationToken cancellationToken = default)
    {
        RequestShutdown();
        return default;
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

        try
        {
            job = sched.JobFactory.NewJob(firedTriggerBundle, scheduler);
        }
        catch (SchedulerException se)
        {
            await sched.NotifySchedulerListenersError($"An error occurred instantiating job to be executed. job= '{firedTriggerBundle.JobDetail.Key}'", se, cancellationToken).ConfigureAwait(false);
            throw;
        }
        catch (Exception e)
        {
            SchedulerException se = new SchedulerException($"Problem instantiating type '{firedTriggerBundle.JobDetail.JobType.FullName}: {e.Message}'", e);
            await sched.NotifySchedulerListenersError($"An error occurred instantiating job to be executed. job= '{firedTriggerBundle.JobDetail.Key}, message={e.Message}'", se, cancellationToken).ConfigureAwait(false);
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
        Context.CallerId.Value = Guid.NewGuid();
        qs!.AddInternalSchedulerListener(this);

        IJob job = jec!.jobInstance;

        try
        {
            IOperableTrigger trigger = (IOperableTrigger) jec.Trigger;
            IJobDetail jobDetail = jec.JobDetail;
            do
            {
                JobExecutionException? jobExEx = null;

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
                        instCode = trigger.ExecutionComplete(jec, result: null);
                        await qs.NotifyJobStoreJobVetoed(trigger, jobDetail, instCode, cancellationToken).ConfigureAwait(false);

                        // Even if trigger got vetoed, we still needs to check to see if it's the trigger's finalized run or not.
                        if (!trigger.GetMayFireAgain())
                        {
                            await qs.NotifySchedulerListenersFinalized(jec.Trigger, cancellationToken).ConfigureAwait(false);
                        }
                        Complete(successfulExecution: true);
                    }
                    catch (SchedulerException se)
                    {
                        string msg = $"Error during veto of Job {jec.JobDetail.Key}: couldn't finalize execution.";
                        await qs.NotifySchedulerListenersError(msg, se, cancellationToken).ConfigureAwait(false);
                    }
                    break;
                }

                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("Calling Execute on job {JobKey}", jobDetail.Key);
                }

                TimeProvider timeProvider = qs.resources.TimeProvider;
                long startTimestamp = timeProvider.GetTimestamp();
                long endTimestamp;

                StartedActivity activity = QuartzActivitySource.StartJobExecute(jec, timeProvider.GetUtcNow());
                Instrumentation instrumentation = Meters.StartJobExecute(jec);


                // Execute the job
                try
                {
                    await job.Execute(jec).ConfigureAwait(false);
                    endTimestamp = timeProvider.GetTimestamp();
                }
                catch (OperationCanceledException) when (jec.CancellationToken.IsCancellationRequested)
                {
                    endTimestamp = timeProvider.GetTimestamp();
                    logger.LogInformation("Job {JobDetailKey} was cancelled", jobDetail.Key);
                }
                catch (JobExecutionException jee)
                {
                    endTimestamp = timeProvider.GetTimestamp();
                    jobExEx = jee;
                    logger.LogError(jee, "Job {JobDetailKey} threw a JobExecutionException: ", jobDetail.Key);
                }
                catch (Exception e)
                {
                    endTimestamp = timeProvider.GetTimestamp();
                    logger.LogError(e, "Job {JobDetailKey} threw an unhandled Exception: ", jobDetail.Key);
                    SchedulerException se = new("Job threw an unhandled exception.", e);
                    await qs.NotifySchedulerListenersError($"Job {jec.JobDetail.Key} threw an exception.", se, cancellationToken).ConfigureAwait(false);
                    jobExEx = new JobExecutionException(se, refireImmediately: false);
                }

                jec.JobRunTime = timeProvider.GetElapsedTime(startTimestamp, endTimestamp);

                activity.Stop(timeProvider.GetUtcNow(), jobExEx);
                instrumentation.EndJobExecute(jec.JobRunTime, jobExEx);

                // notify all job listeners
                if (!await NotifyJobListenersComplete(qs, jec, jobExEx, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }

                instCode = SchedulerInstruction.NoInstruction;

                // update the trigger
                try
                {
                    instCode = trigger.ExecutionComplete(jec, jobExEx);
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug("Trigger instruction : {InstCode}", instCode);
                    }
                }
                catch (Exception e)
                {
                    // If this happens, there's a bug in the trigger...
                    SchedulerException se = new SchedulerException("Trigger threw an unhandled exception.", e);
                    await qs.NotifySchedulerListenersError("Please report this error to the Quartz developers.", se, cancellationToken).ConfigureAwait(false);
                }

                // notify all trigger listeners
                if (!await NotifyTriggerListenersComplete(qs, jec, instCode, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
                // update job/trigger or re-Execute job
                if (instCode == SchedulerInstruction.ReExecuteJob)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug("Rescheduling trigger to reexecute");
                    }
                    jec.IncrementRefireCount();
                    try
                    {
                        Complete(successfulExecution: false);
                    }
                    catch (SchedulerException se)
                    {
                        await qs.NotifySchedulerListenersError($"Error executing Job {jec.JobDetail.Key}: couldn't finalize execution.", se, cancellationToken).ConfigureAwait(false);
                    }
                    continue;
                }

                try
                {
                    Complete(successfulExecution: true);
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
            await qs.JobFactory.ReturnJob(job).ConfigureAwait(false);
            jec.Dispose();
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

    private async ValueTask<bool> NotifyListenersBeginning(
        JobExecutionContextImpl ctx,
        CancellationToken cancellationToken = default)
    {
        bool vetoed;

        // notify all trigger listeners
        try
        {
            vetoed = await qs!.NotifyTriggerListenersFired(ctx, cancellationToken).ConfigureAwait(false);
        }
        catch (SchedulerException se)
        {
            var msg = $"Unable to notify TriggerListener(s) while firing trigger (Trigger and Job will NOT be fired!). trigger= {ctx.Trigger.Key} job= {ctx.JobDetail.Key}";
            await qs!.NotifySchedulerListenersError(msg, se, cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (vetoed)
        {
            try
            {
                using Activity? activity = QuartzActivitySource.Instance.StartActivity(OperationName.Job.Veto);
                activity?.EnrichFrom(ctx);

                await qs.NotifyJobListenersWasVetoed(ctx, cancellationToken).ConfigureAwait(false);
            }
            catch (SchedulerException se)
            {
                var msg = $"Unable to notify JobListener(s) of vetoed execution while firing trigger (Trigger and Job will NOT be fired!). trigger= {ctx.Trigger.Key} job= {ctx.JobDetail.Key}";
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

    private static async ValueTask<bool> NotifyJobListenersComplete(QuartzScheduler qs,
        JobExecutionContextImpl ctx,
        JobExecutionException? jobExEx,
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

    private static ValueTask<bool> NotifyTriggerListenersComplete(QuartzScheduler qs,
        JobExecutionContextImpl ctx,
        SchedulerInstruction instCode,
        CancellationToken cancellationToken = default)
    {
        // check if we can do quick path
        if (ctx.Trigger.GetMayFireAgain())
        {
            try
            {
                var task = qs.NotifyTriggerListenersComplete(ctx, instCode, cancellationToken);
                return task.IsCompletedSuccessfully ? new ValueTask<bool>(true) : DoNotify(task, qs, ctx, cancellationToken);
            }
            catch (SchedulerException se)
            {
                return NotifyError(se, qs, ctx, cancellationToken);
            }
        }

        return NotifyAwaited(qs, ctx, instCode, cancellationToken);

        static async ValueTask<bool> NotifyAwaited(QuartzScheduler qs,
            JobExecutionContextImpl ctx,
            SchedulerInstruction instCode,
            CancellationToken cancellationToken)
        {
            await DoNotify(qs.NotifyTriggerListenersComplete(ctx, instCode, cancellationToken), qs, ctx, cancellationToken).ConfigureAwait(false);
            await qs.NotifySchedulerListenersFinalized(ctx.Trigger, cancellationToken).ConfigureAwait(false);

            return true;
        }

        static async ValueTask<bool> DoNotify(ValueTask t,
            QuartzScheduler qs,
            JobExecutionContextImpl ctx,
            CancellationToken cancellationToken)
        {
            try
            {
                await t.ConfigureAwait(false);
                return true;
            }
            catch (SchedulerException se)
            {
                return await NotifyError(se, qs, ctx, cancellationToken).ConfigureAwait(false);
            }
        }

        static async ValueTask<bool> NotifyError(SchedulerException se,
            QuartzScheduler qs,
            JobExecutionContextImpl ctx,
            CancellationToken cancellationToken)
        {
            string msg = $"Unable to notify TriggerListener(s) of Job that was executed: (error will be ignored). trigger= {ctx.Trigger.Key} job= {ctx.JobDetail.Key}";
            await qs.NotifySchedulerListenersError(msg, se, cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    [Serializable]
    internal sealed class VetoedException : Exception
    {
        public VetoedException(JobRunShell shell)
        {
            EnclosingInstance = shell;
        }

        public JobRunShell EnclosingInstance { get; }
    }
}