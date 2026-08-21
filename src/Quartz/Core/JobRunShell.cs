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
using Quartz.Diagnostics;
using Quartz.Impl;
using Quartz.Extensibility;
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
internal sealed class JobRunShell
{
    private readonly ILogger<JobRunShell> logger;

    private JobExecutionContextImpl? context;
    private QuartzScheduler? qs;
    private readonly IScheduler scheduler;
    private readonly TriggerFiredBundle firedTriggerBundle;

    /// <summary>
    /// Create a JobRunShell instance with the given settings.
    /// </summary>
    /// <param name="scheduler">The <see cref="IScheduler" /> instance that should be made
    /// available within the <see cref="IJobExecutionContext" />.</param>
    /// <param name="bundle"></param>
    /// <param name="logger">Logger for this shell, supplied by the factory that creates it.</param>
    public JobRunShell(IScheduler scheduler, TriggerFiredBundle bundle, ILogger<JobRunShell> logger)
    {
        this.scheduler = scheduler;
        firedTriggerBundle = bundle;
        this.logger = logger;
    }

    /// <summary>
    /// Initializes the job execution context with given scheduler and bundle.
    /// </summary>
    /// <remarks>
    /// Job creation via <see cref="IJobFactory.CreateJob"/> is deferred to <see cref="Run"/>
    /// so that AsyncLocal values set during job factory creation flow correctly to <see cref="IJob.Execute"/>.
    /// </remarks>
    /// <param name="scheduler">The scheduler.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public ValueTask Initialize(
        QuartzScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        qs = scheduler;
        return default;
    }

    /// <summary>
    /// This method has to be implemented in order that starting of the thread causes the object's
    /// run method to be called in that separately executing thread.
    /// </summary>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public async ValueTask Run(CancellationToken cancellationToken = default)
    {
        Context.CallerId.Value = Guid.NewGuid();

        // Create the job here (moved from Initialize) so that AsyncLocal values
        // set during IJobFactory.CreateJob flow correctly to IJob.Execute (#1528)
        IJobDetail jobDetail = firedTriggerBundle.JobDetail;

        // Read the factory once: the scope handed out by CreateJob must be returned to the same
        // factory, even if QuartzScheduler.JobFactory is swapped while this job is in flight.
        IJobFactory jobFactory = qs!.JobFactory;
        JobScope jobScope;
        try
        {
            jobScope = await jobFactory.CreateJob(firedTriggerBundle, scheduler, cancellationToken).ConfigureAwait(false);

            if (jobScope.Job is null)
            {
                Throw.SchedulerException(
                    $"Job factory {jobFactory.GetType().FullName} returned an empty JobScope for job '{jobDetail.Key}'. "
                    + "A factory must build its result with the JobScope constructor rather than returning default.");
            }
        }
        catch (SchedulerException se)
        {
            // The factory said what went wrong; the exception handed to listeners adds which trigger
            // and which firing it went wrong for, which the message text alone never carried.
            JobInstantiationException failure = new JobInstantiationException(se.Message, firedTriggerBundle, se);
            await qs!.NotifySchedulerListenersError($"An error occurred instantiating job to be executed. job='{jobDetail.Key}'", failure, cancellationToken).ConfigureAwait(false);

            IOperableTrigger errorTrigger = (IOperableTrigger) firedTriggerBundle.Trigger;
            SchedulerInstruction instruction = se.InnerException is ObjectDisposedException or OperationCanceledException
                ? SchedulerInstruction.NoInstruction
                : SchedulerInstruction.SetAllJobTriggersError;
            await qs.NotifyJobStoreJobComplete(errorTrigger, jobDetail, instruction, cancellationToken).ConfigureAwait(false);
            return;
        }
        catch (Exception e)
        {
            await NotifyInstantiationFailed(e).ConfigureAwait(false);
            return;
        }

        // Everything past this point runs inside the try/finally, so that a job the factory has
        // already handed us is returned to it even if we never get as far as executing it.
        try
        {
            try
            {
                context = new JobExecutionContextImpl(scheduler, firedTriggerBundle, jobScope.Job);
            }
            catch (Exception e)
            {
                await NotifyInstantiationFailed(e).ConfigureAwait(false);
                return;
            }

            IOperableTrigger trigger = (IOperableTrigger) context!.Trigger;
            do
            {
                JobExecutionException? jobExEx = null;

                // notify job & trigger listeners...
                SchedulerInstruction instructionCode;
                try
                {
                    if (!await NotifyListenersBeginning(context, cancellationToken).ConfigureAwait(false))
                    {
                        await qs.NotifyJobStoreJobComplete(trigger, jobDetail, SchedulerInstruction.NoInstruction, cancellationToken).ConfigureAwait(false);
                        break;
                    }
                }
                catch (VetoedException)
                {
                    try
                    {
                        instructionCode = trigger.ExecutionComplete(context, result: null);
                        await qs.NotifyJobStoreJobVetoed(trigger, jobDetail, instructionCode, cancellationToken).ConfigureAwait(false);

                        // Even if trigger got vetoed, we still needs to check to see if it's the trigger's finalized run or not.
                        if (!trigger.MayFireAgain)
                        {
                            await qs.NotifySchedulerListenersFinalized(context.Trigger, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (SchedulerException se)
                    {
                        string msg = $"Error during veto of Job {context.JobDetail.Key}: couldn't finalize execution.";
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

                StartedActivity activity = QuartzActivitySource.StartJobExecute(context, timeProvider.GetUtcNow());
                Instrumentation instrumentation = qs.resources.Meters.StartJobExecute(context);


                // Execute the job
                try
                {
                    await jobScope.Job.Execute(context, context.CancellationToken).ConfigureAwait(false);
                    endTimestamp = timeProvider.GetTimestamp();
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    endTimestamp = timeProvider.GetTimestamp();
                    logger.LogInformation("Job {JobDetailKey} was cancelled", jobDetail.Key);
                }
                catch (JobExecutionException jee)
                {
                    endTimestamp = timeProvider.GetTimestamp();
                    jee.JobDetail = jobDetail;
                    jobExEx = jee;
                    logger.LogError(jee, "Job {JobDetailKey} threw a JobExecutionException: ", jobDetail.Key);
                }
                catch (Exception e)
                {
                    endTimestamp = timeProvider.GetTimestamp();
                    logger.LogError(e, "Job {JobDetailKey} threw an unhandled Exception: ", jobDetail.Key);
                    SchedulerException se = new JobExecutionProcessException(context, e);
                    await qs.NotifySchedulerListenersError($"Job {context.JobDetail.Key} threw an exception.", se, cancellationToken).ConfigureAwait(false);
                    jobExEx = new JobExecutionException(se);
                    jobExEx.JobDetail = jobDetail;
                }

                context.JobRunTime = timeProvider.GetElapsedTime(startTimestamp, endTimestamp);

                activity.Stop(timeProvider.GetUtcNow(), jobExEx);
                instrumentation.EndJobExecute(context.JobRunTime, jobExEx);

                instructionCode = SchedulerInstruction.NoInstruction;

                // update the trigger — must happen before listener notifications
                // so we know whether to refire (and skip notifications) or complete
                try
                {
                    instructionCode = trigger.ExecutionComplete(context, jobExEx);
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug("Trigger instruction : {InstructionCode}", instructionCode);
                    }
                }
                catch (Exception e)
                {
                    // If this happens, there's a bug in the trigger...
                    SchedulerException se = new SchedulerException("Trigger threw an unhandled exception.", e);
                    await qs.NotifySchedulerListenersError("Please report this error to the Quartz developers.", se, cancellationToken).ConfigureAwait(false);
                }

                // re-Execute job — skip listener notifications so that listeners like
                // JobChainingJobListener don't see intermediate refire attempts as completions (#663)
                if (instructionCode == SchedulerInstruction.ReExecuteJob)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug("Rescheduling trigger to reexecute");
                    }
                    context.IncrementRefireCount();
                    continue;
                }

                // notify all job listeners
                if (!await NotifyJobListenersComplete(qs, context, jobExEx, cancellationToken).ConfigureAwait(false))
                {
                    await qs.NotifyJobStoreJobComplete(trigger, jobDetail, instructionCode, cancellationToken).ConfigureAwait(false);
                    break;
                }

                // notify all trigger listeners
                if (!await NotifyTriggerListenersComplete(qs, context, instructionCode, cancellationToken).ConfigureAwait(false))
                {
                    // Ensure finalized notification is still sent when the trigger has no next fire time,
                    // even if trigger listener notification failed.
                    try
                    {
                        if (!trigger.MayFireAgain)
                        {
                            await qs.NotifySchedulerListenersFinalized(context.Trigger, cancellationToken).ConfigureAwait(false);
                        }
                    }
                    catch (Exception e)
                    {
                        SchedulerException se2 = new SchedulerException("Error notifying scheduler listeners of finalized trigger.", e);
                        await qs.NotifySchedulerListenersError("Error notifying scheduler listeners of finalized trigger.", se2, cancellationToken).ConfigureAwait(false);
                    }

                    await qs.NotifyJobStoreJobComplete(trigger, jobDetail, instructionCode, cancellationToken).ConfigureAwait(false);
                    break;
                }

                await qs.NotifyJobStoreJobComplete(trigger, jobDetail, instructionCode, cancellationToken).ConfigureAwait(false);

                break;
            } while (true);
        }
        finally
        {
            try
            {
                await jobFactory.ReturnJob(jobScope, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // Run is handed to the thread pool and nobody awaits it, so letting this escape would
                // lose it entirely. Report it and carry on to the context disposal below.
                await qs.NotifySchedulerListenersError(
                    $"An error occurred returning job to the job factory. job='{jobDetail.Key}'",
                    new SchedulerException($"Problem returning job '{jobDetail.Key}' to the job factory: {e.Message}", e),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                context?.Dispose();
            }
        }

        async ValueTask NotifyInstantiationFailed(Exception e)
        {
            SchedulerException se = new JobInstantiationException($"Problem instantiating type '{jobDetail.JobType.FullName}': {e.Message}", firedTriggerBundle, e);
            await qs!.NotifySchedulerListenersError($"An error occurred instantiating job to be executed. job='{jobDetail.Key}', message='{e.Message}'", se, cancellationToken).ConfigureAwait(false);

            IOperableTrigger errorTrigger = (IOperableTrigger) firedTriggerBundle.Trigger;
            SchedulerInstruction instruction = e is ObjectDisposedException or OperationCanceledException
                ? SchedulerInstruction.NoInstruction
                : SchedulerInstruction.SetAllJobTriggersError;
            await qs.NotifyJobStoreJobComplete(errorTrigger, jobDetail, instruction, cancellationToken).ConfigureAwait(false);
        }
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
        SchedulerInstruction instructionCode,
        CancellationToken cancellationToken = default)
    {
        // check if we can do quick path
        if (ctx.Trigger.MayFireAgain)
        {
            try
            {
                var task = qs.NotifyTriggerListenersComplete(ctx, instructionCode, cancellationToken);
                return task.IsCompletedSuccessfully ? new ValueTask<bool>(true) : DoNotify(task, qs, ctx, cancellationToken);
            }
            catch (SchedulerException se)
            {
                return NotifyError(se, qs, ctx, cancellationToken);
            }
        }

        return NotifyAwaited(qs, ctx, instructionCode, cancellationToken);

        static async ValueTask<bool> NotifyAwaited(QuartzScheduler qs,
            JobExecutionContextImpl ctx,
            SchedulerInstruction instructionCode,
            CancellationToken cancellationToken)
        {
            await DoNotify(qs.NotifyTriggerListenersComplete(ctx, instructionCode, cancellationToken), qs, ctx, cancellationToken).ConfigureAwait(false);
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

    internal sealed class VetoedException : Exception
    {
        public VetoedException(JobRunShell shell)
        {
            EnclosingInstance = shell;
        }

        public JobRunShell EnclosingInstance { get; }
    }
}