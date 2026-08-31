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

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Impl;

namespace Quartz.Core;

/// <summary>
/// Bounds how long a firing may run: when the budget is spent the firing is interrupted, and the
/// overrun is reported as the job's own failure.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It interrupts the firing rather than cancelling a token of its own.</strong> A middleware
/// that made a linked <see cref="CancellationTokenSource" /> and handed <em>that</em> token to
/// <c>next</c> would break the rule <see cref="IJobExecutionMiddleware" /> states: the job's
/// <c>Execute</c> parameter would stop being
/// <see cref="IJobExecutionContext.CancellationToken" />, so a job reading the context would watch the
/// wrong one — and the run shell classifies a cancellation by asking whether the <em>context's</em>
/// token was signalled, so the resulting <see cref="OperationCanceledException" /> would be reported as
/// an unhandled failure with a <c>SchedulerError</c> behind it. Going through the public
/// <see cref="IScheduler.InterruptFireInstance" /> cancels the context's own source, which is the same
/// thing an operator's interrupt does, so everything downstream — the token the job holds, the
/// <see cref="ISchedulerListener.JobInterrupted" /> notification, the log — behaves as it always has.
/// </para>
/// <para>
/// <strong>It then rethrows, because an interrupt on its own is success-shaped.</strong> The run shell
/// treats an <see cref="OperationCanceledException" /> whose context token was signalled as a
/// cancellation rather than a failure: no <see cref="JobExecutionException" /> reaches
/// <see cref="IJobListener.JobWasExecuted" />, the trigger advances as though the job had finished, and
/// a retry policy is never consulted. That is right for an operator who asked a job to stop, and wrong
/// for a job that ran out of time. So the middleware raises a <see cref="JobExecutionException" /> of
/// its own naming the budget, which makes a timeout a failure like any other: listeners see it, the
/// trigger's <c>RetryPolicy</c> decides what happens next, and the log says which job overran.
/// </para>
/// <para>
/// An exception the job threw that is <em>not</em> a cancellation is left alone even when the budget
/// had expired, because it says more about what went wrong than the timeout does; the overrun is
/// logged either way.
/// </para>
/// <para>
/// Internal because nothing needs to name it: <c>AddJobTimeout</c> registers it, and every knob it has
/// is that call's argument or the job's <see cref="JobTimeoutAttribute" />. Registered like any other
/// middleware, so it takes its place in the chain where the call was written.
/// </para>
/// </remarks>
internal sealed class JobTimeoutMiddleware : IJobExecutionMiddleware
{
    private readonly ILogger<JobTimeoutMiddleware> logger = LogProvider.CreateLogger<JobTimeoutMiddleware>();
    private readonly TimeSpan defaultTimeout;
    private readonly TimeProvider timeProvider;

    /// <summary>
    /// Builds the middleware with the scheduler-wide budget <c>AddJobTimeout</c> was given.
    /// </summary>
    /// <param name="defaultTimeout">
    /// The budget for a job that does not carry <see cref="JobTimeoutAttribute" />, or
    /// <see cref="TimeSpan.Zero" /> when only the jobs that declare one are bounded.
    /// </param>
    /// <param name="timeProvider">The scheduler's clock, which is also what times the budget.</param>
    public JobTimeoutMiddleware(TimeSpan defaultTimeout, TimeProvider timeProvider)
    {
        this.defaultTimeout = defaultTimeout;
        this.timeProvider = timeProvider;
    }

    public async ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default)
    {
        TimeSpan budget = ResolveTimeout(context.JobDetail);
        if (budget <= TimeSpan.Zero)
        {
            await next(context, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Per firing, because the outcome is per firing: one instance of this middleware serves every
        // execution the scheduler performs, and several of them are in flight at once.
        Budget spend = new(context, budget, logger);

        // The token is not linked and not replaced - it is passed on exactly as it arrived. What the
        // timer does is interrupt the firing, which cancels the token the context already carries.
        using ITimer timer = timeProvider.CreateTimer(static state => ((Budget) state!).Expire(), spend, budget, Timeout.InfiniteTimeSpan);

        try
        {
            await next(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException e) when (spend.Expired)
        {
            throw new JobExecutionException(TimedOutMessage(context, budget), e);
        }
        finally
        {
            // Closes the window: a job that finishes as the timer is about to fire is not interrupted
            // afterwards, and the two outcomes cannot both be claimed.
            spend.Complete();
        }

        if (spend.Expired)
        {
            // The job outlived its budget and returned anyway - it swallowed the cancellation, or never
            // looked. Reported as a timeout all the same, because the alternative is a firing that ran
            // over and said nothing.
            throw new JobExecutionException(TimedOutMessage(context, budget));
        }
    }

    /// <summary>
    /// The budget this firing gets: what the job type declares, or the scheduler's default.
    /// </summary>
    /// <remarks>
    /// <see cref="JobTimeoutAttribute" /> wins whenever the job carries one, including when it declares
    /// <see cref="TimeSpan.Zero" /> — a job that says it has no timeout is exempt from the scheduler's
    /// default rather than overruled by it. A job type that cannot be resolved gets the default; the
    /// firing would not be running at all if the type were truly unreachable, and refusing to time out
    /// on a technicality is the wrong way round.
    /// </remarks>
    private TimeSpan ResolveTimeout(IJobDetail jobDetail)
    {
        if (jobDetail.JobType.TryResolve(out Type? jobType))
        {
            return JobTypeInformation.GetOrCreate(jobType).Timeout ?? defaultTimeout;
        }

        return defaultTimeout;
    }

    private static string TimedOutMessage(IJobExecutionContext context, TimeSpan budget)
    {
        return $"Job {context.JobDetail.Key} timed out: fire instance {context.FireInstanceId} was allowed {budget} and was interrupted when it ran longer.";
    }

    /// <summary>
    /// One firing's budget, and which of the two ends of it got there first.
    /// </summary>
    /// <remarks>
    /// The state is a single word so the timer callback and the awaiting middleware settle the race
    /// between them with one compare-and-swap rather than with a lock either of them could be holding
    /// while the other runs.
    /// </remarks>
    private sealed class Budget
    {
        private const int StateRunning = 0;
        private const int StateExpired = 1;
        private const int StateCompleted = 2;

        private readonly IJobExecutionContext context;
        private readonly TimeSpan budget;
        private readonly ILogger logger;

        private int state;

        public Budget(IJobExecutionContext context, TimeSpan budget, ILogger logger)
        {
            this.context = context;
            this.budget = budget;
            this.logger = logger;
        }

        /// <summary>
        /// Whether the budget ran out before the job finished.
        /// </summary>
        public bool Expired => Volatile.Read(ref state) == StateExpired;

        /// <summary>
        /// Records that the job finished, so a timer that has not fired yet no longer can.
        /// </summary>
        public void Complete() => Interlocked.CompareExchange(ref state, StateCompleted, StateRunning);

        /// <summary>
        /// The timer callback: claims the firing, then interrupts it through the scheduler.
        /// </summary>
        public void Expire()
        {
            if (Interlocked.CompareExchange(ref state, StateExpired, StateRunning) != StateRunning)
            {
                // The job finished first. Nothing to interrupt, and nothing to report.
                return;
            }

            logger.JobTimedOut(context.JobDetail.Key, budget, context.FireInstanceId);

            // Fire and forget from a timer callback, which cannot await: the interrupt is a dictionary
            // lookup and a listener notification, and what it produces is observed through the token the
            // job is holding rather than through this task.
            _ = Interrupt();
        }

        private async Task Interrupt()
        {
            try
            {
                // Not the firing's own token. InterruptFireInstance throws when the token it is given is
                // already cancelled, and the firing's token is the one this call exists to cancel - so
                // passing it would let the interrupt refuse to run because it had already run.
                bool interrupted = await context.Scheduler.InterruptFireInstance(context.FireInstanceId, CancellationToken.None).ConfigureAwait(false);
                if (!interrupted)
                {
                    logger.JobTimeoutFoundNothingToInterrupt(context.JobDetail.Key, context.FireInstanceId);
                }
            }
            catch (Exception e) when (e is not OutOfMemoryException and not StackOverflowException)
            {
                logger.JobTimeoutInterruptFailed(context.JobDetail.Key, context.FireInstanceId, e);
            }
        }
    }
}
