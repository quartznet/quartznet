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
    /// <param name="bundle">The firing this shell is to run: its trigger, its job and the times it fired at.</param>
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

        // No scheduler logging scope is opened here, though this is where one would have to go for a
        // job's own log lines to name the scheduler that fired it. It was written, measured and taken
        // back out: ILogger.BeginScope pushes onto an AsyncLocal, and an AsyncLocal write copies the
        // execution context, so one scope per firing cost 240 bytes and about 130ns of the roughly 960ns
        // a no-op firing takes — 14%, for a firing that does no work at all. The scheduler thread opens
        // the scope once for the lifetime of its loop instead, and the thread pools Quartz ships dispatch
        // through a Task that captures the execution context, so a job inherits it from there at no
        // per-firing cost. What that does not cover is an IThreadPool of somebody else's that does not
        // flow the context.

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
            await qs!.NotifySchedulerListenersError(
                ErrorFor(firedTriggerBundle, $"An error occurred instantiating job to be executed. job='{jobDetail.Key}'", failure),
                cancellationToken).ConfigureAwait(false);

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
        IDisposable? ambient = null;
        try
        {
            try
            {
                context = new JobExecutionContextImpl(scheduler, firedTriggerBundle, jobScope.Job, qs.resources.JobInputSerializer);
            }
            catch (Exception e)
            {
                await NotifyInstantiationFailed(e).ConfigureAwait(false);
                return;
            }

            // The firing becomes ambient here, which is the earliest it can: the execution context
            // takes the job instance, so it does not exist while the job is being built. Set in this
            // method rather than in a called one, because an async method restores the caller's
            // execution context when it returns and would take the value with it (#1528). Everything
            // from the listener notifications below to the job factory being handed the job back
            // therefore reads it, and nothing outside this firing can.
            ambient = AmbientJobExecution.Enter(context);

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
                        await NotifyFinalizedIfDone(qs, context, cancellationToken).ConfigureAwait(false);
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
                        await qs.NotifySchedulerListenersError(ErrorFor(context, msg, se), cancellationToken).ConfigureAwait(false);
                    }
                    break;
                }

                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.JobExecuting(jobDetail.Key);
                }

                TimeProvider timeProvider = qs.resources.TimeProvider;
                long startTimestamp = timeProvider.GetTimestamp();
                long endTimestamp;

                StartedActivity activity = QuartzActivitySource.StartJobExecute(context, timeProvider.GetUtcNow());
                Instrumentation instrumentation = qs.resources.Meters.StartJobExecute(context);


                // Execute the job, through this scheduler's middleware when it has any. Inside the
                // activity and the instrumentation above, so what a middleware costs is part of what the
                // firing cost, and outside the classification below, so an exception a middleware throws
                // is treated exactly as one the job threw.
                try
                {
                    JobExecutionDelegate? pipeline = qs.resources.JobExecutionPipeline;
                    ValueTask execution = pipeline is null
                        ? jobScope.Job.Execute(context, context.CancellationToken)
                        : pipeline(context, context.CancellationToken);

                    await execution.ConfigureAwait(false);
                    endTimestamp = timeProvider.GetTimestamp();
                }
                catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
                {
                    endTimestamp = timeProvider.GetTimestamp();
                    logger.JobCancelled(jobDetail.Key);
                }
                catch (JobExecutionException jee)
                {
                    endTimestamp = timeProvider.GetTimestamp();
                    jee.JobDetail = jobDetail;
                    jobExEx = jee;
                    logger.JobThrewJobExecutionException(jobDetail.Key, jee);
                }
                catch (Exception e)
                {
                    endTimestamp = timeProvider.GetTimestamp();
                    logger.JobThrewUnhandledException(jobDetail.Key, e);
                    SchedulerException se = new JobExecutionProcessException(context, e);
                    await qs.NotifySchedulerListenersError(
                        ErrorFor(context, $"Job {context.JobDetail.Key} threw an exception.", se),
                        cancellationToken).ConfigureAwait(false);
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
                        logger.TriggerInstructionDecided(instructionCode);
                    }

                    if (instructionCode == SchedulerInstruction.RetryTrigger)
                    {
                        // Reported at Information, unlike the instruction itself: a job that keeps
                        // failing and retrying is the thing an operator wants in the log without
                        // turning Debug on, and the retry instant is what tells them when to look.
                        logger.TriggerRetryScheduled(
                            trigger.Key,
                            trigger.RetryAttempt,
                            trigger.RetryPolicy?.MaxAttempts ?? 0,
                            trigger.NextFireTimeUtc.GetValueOrDefault());

                        qs.resources.Meters.TriggerRetryScheduled(qs.resources.Name, qs.resources.InstanceId, trigger);
                    }
                }
                catch (Exception e)
                {
                    // If this happens, there's a bug in the trigger...
                    SchedulerException se = new SchedulerException("Trigger threw an unhandled exception.", e);
                    await qs.NotifySchedulerListenersError(
                        ErrorFor(context, "Please report this error to the Quartz developers.", se),
                        cancellationToken).ConfigureAwait(false);
                }

                // re-Execute job — skip listener notifications so that listeners like
                // JobChainingJobListener don't see intermediate refire attempts as completions (#663)
                if (instructionCode == SchedulerInstruction.ReExecuteJob)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.TriggerRefiring();
                    }
                    context.IncrementRefireCount();
                    continue;
                }

                // notify all job listeners
                if (!await NotifyJobListenersComplete(qs, context, jobExEx, cancellationToken).ConfigureAwait(false))
                {
                    await NotifyFinalizedIfDone(qs, context, cancellationToken).ConfigureAwait(false);
                    await qs.NotifyJobStoreJobComplete(trigger, jobDetail, instructionCode, cancellationToken).ConfigureAwait(false);
                    break;
                }

                // notify all trigger listeners
                if (!await NotifyTriggerListenersComplete(qs, context, instructionCode, cancellationToken).ConfigureAwait(false))
                {
                    await NotifyFinalizedIfDone(qs, context, cancellationToken).ConfigureAwait(false);
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
                    ErrorFor(
                        firedTriggerBundle,
                        $"An error occurred returning job to the job factory. job='{jobDetail.Key}'",
                        new SchedulerException($"Problem returning job '{jobDetail.Key}' to the job factory: {e.Message}", e)),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                // Cleared before the context is disposed, and cleared for every flow that captured it
                // rather than only this one — so work a job left running reads nothing rather than a
                // context whose scope has gone and whose cancellation handle is about to.
                ambient?.Dispose();
                context?.Dispose();
            }
        }

        async ValueTask NotifyInstantiationFailed(Exception e)
        {
            SchedulerException se = new JobInstantiationException($"Problem instantiating type '{jobDetail.JobType.FullName}': {e.Message}", firedTriggerBundle, e);
            await qs!.NotifySchedulerListenersError(
                ErrorFor(firedTriggerBundle, $"An error occurred instantiating job to be executed. job='{jobDetail.Key}', message='{e.Message}'", se),
                cancellationToken).ConfigureAwait(false);

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
            await qs!.NotifySchedulerListenersError(ErrorFor(ctx, msg, se), cancellationToken).ConfigureAwait(false);
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
                await qs.NotifySchedulerListenersError(ErrorFor(ctx, msg, se), cancellationToken).ConfigureAwait(false);
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
            await qs.NotifySchedulerListenersError(ErrorFor(ctx, msg, se), cancellationToken).ConfigureAwait(false);

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
            await qs.NotifySchedulerListenersError(ErrorFor(ctx, msg, se), cancellationToken).ConfigureAwait(false);

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
            await qs.NotifySchedulerListenersError(ErrorFor(ctx, msg, se), cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    /// <summary>
    /// Announces that the trigger has fired for the last time, when it has.
    /// </summary>
    /// <remarks>
    /// Every way out of a firing has to do this, the ones a failed listener cut short included: a
    /// trigger whose last firing was abandoned is as finished as one whose last firing ran, and a
    /// scheduler listener told only about the tidy paths would go on believing the trigger is still
    /// there. The veto path says it in its own words, because the message it reports a failure with
    /// names the veto.
    /// </remarks>
    private static async ValueTask NotifyFinalizedIfDone(
        QuartzScheduler qs,
        JobExecutionContextImpl ctx,
        CancellationToken cancellationToken)
    {
        if (ctx.Trigger.MayFireAgain)
        {
            return;
        }

        try
        {
            await qs.NotifySchedulerListenersFinalized(ctx.Trigger, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            SchedulerException se = new SchedulerException("Error notifying scheduler listeners of finalized trigger.", e);
            await qs.NotifySchedulerListenersError(
                ErrorFor(ctx, "Error notifying scheduler listeners of finalized trigger.", se),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The error context for a failure inside this firing, taken from the bundle the store fired.
    /// </summary>
    /// <remarks>
    /// Used before the execution context exists — a job that could not be built has no context, but the
    /// trigger, the job and the fire instance are all known regardless.
    /// </remarks>
    private static SchedulerErrorContext ErrorFor(TriggerFiredBundle bundle, string message, SchedulerException exception)
    {
        return new SchedulerErrorContext
        {
            Message = message,
            Exception = exception,
            TriggerKey = bundle.Trigger.Key,
            JobKey = bundle.JobDetail.Key,
            FireInstanceId = bundle.Trigger.FireInstanceId,
        };
    }

    /// <summary>
    /// The error context for a failure inside this firing, taken from the execution context.
    /// </summary>
    private static SchedulerErrorContext ErrorFor(JobExecutionContextImpl context, string message, SchedulerException exception)
    {
        return new SchedulerErrorContext
        {
            Message = message,
            Exception = exception,
            TriggerKey = context.Trigger.Key,
            JobKey = context.JobDetail.Key,
            FireInstanceId = context.FireInstanceId,
        };
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