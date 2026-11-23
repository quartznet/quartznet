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

using Quartz.Core;
using Quartz.Impl;
using Quartz.Impl.Triggers;

namespace Quartz;

/// <summary>
/// Firing a stored job and waiting for how its run ended, or waiting for a firing already triggered (#4414).
/// </summary>
/// <remarks>
/// <para>
/// Extensions rather than <see cref="IScheduler" /> members, so no interface grows. They need the scheduler Quartz
/// builds, the <see cref="IScheduler" /> the container injects, a <see cref="DelegatingScheduler" /> over one, or
/// <c>HttpScheduler</c>; any other scheduler is refused with <see cref="NotSupportedException" />.
/// </para>
/// <para>
/// The wait is keyed by the trigger. It answers the trigger's next final completion after the wait begins, or,
/// for a trigger already gone, its newest final row in the execution history. A retried occurrence ends at its
/// last attempt. A veto and a firing the job never ran for end the wait too.
/// </para>
/// <para>
/// In process the run shell answers it as the store commits the completion: when the wait returns, a one-off is
/// gone and a <see cref="DisallowConcurrentExecutionAttribute" /> block is lifted. On a cluster the firing may
/// run on another node, so the wait reads the execution history every node writes,
/// <c>UsePersistentStore(store =&gt; store.UseExecutionHistory())</c>, and the trigger's state: from 200 ms
/// apart, doubling to 5 s. A cluster without that history is refused before anything is stored. Over
/// <c>HttpScheduler</c> it reads the host's history and trigger routes the same way.
/// </para>
/// <para>
/// The timeout stops the wait and never the job: a wait that runs out answers
/// <see cref="FiringWaitStatus.Pending" /> with the key to wait again with. <see cref="TimeSpan.Zero" /> looks
/// once, and <see cref="Timeout.InfiniteTimeSpan" /> has no limit. It is measured on the system clock, whatever
/// clock the scheduler keeps. The token throws <see cref="OperationCanceledException" /> and leaves the job
/// alone. A wait still open when the scheduler shuts down throws <see cref="SchedulerException" />.
/// </para>
/// </remarks>
public static class SchedulerWaitExtensions
{
    /// <summary>
    /// Fires the stored job now and waits up to <paramref name="timeout" /> for how its run ended.
    /// </summary>
    /// <remarks>
    /// The firing <see cref="IScheduler.TriggerJob" /> makes: a one-shot trigger named <c>MT_</c> and a number in
    /// the default group, marked manual, in the execution group <c>TriggerJob</c> would give it. Its key is on
    /// <see cref="FiringResult.TriggerKey" />.
    /// </remarks>
    /// <param name="scheduler">The scheduler to fire the job on.</param>
    /// <param name="jobKey">The stored job to fire.</param>
    /// <param name="timeout">How long to wait; it stops the wait, never the job.</param>
    /// <param name="cancellationToken">The cancellation instruction; it stops the wait, never the job.</param>
    /// <returns>How the firing ended, or <see cref="FiringWaitStatus.Pending" /> when the timeout ran out first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scheduler" /> or <paramref name="jobKey" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeout" /> is negative and is not <see cref="Timeout.InfiniteTimeSpan" />.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The scheduler cannot wait for a firing, or it is clustered without a history every node writes. Nothing is
    /// stored.
    /// </exception>
    /// <exception cref="SchedulerException">The scheduler has been shut down, or shut down while waiting.</exception>
    /// <exception cref="JobPersistenceException">No job is stored under <paramref name="jobKey" />.</exception>
    public static ValueTask<FiringResult> TriggerJobAndWait(
        this IScheduler scheduler,
        JobKey jobKey,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return TriggerJobAndWait(scheduler, jobKey, data: null, timeout, cancellationToken);
    }

    /// <summary>
    /// Fires the stored job now with the given data and waits up to <paramref name="timeout" /> for how its run
    /// ended.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="TriggerJobAndWait(IScheduler, JobKey, TimeSpan, CancellationToken)" path="/remarks" />
    /// The map is copied onto the trigger; the one passed in is left as it was.
    /// </remarks>
    /// <param name="scheduler">The scheduler to fire the job on.</param>
    /// <param name="jobKey">The stored job to fire.</param>
    /// <param name="data">The firing's own job data, or <see langword="null" /> for none.</param>
    /// <param name="timeout">How long to wait; it stops the wait, never the job.</param>
    /// <param name="cancellationToken">The cancellation instruction; it stops the wait, never the job.</param>
    /// <returns>How the firing ended, or <see cref="FiringWaitStatus.Pending" /> when the timeout ran out first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scheduler" /> or <paramref name="jobKey" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeout" /> is negative and is not <see cref="Timeout.InfiniteTimeSpan" />.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The scheduler cannot wait for a firing, or it is clustered without a history every node writes. Nothing is
    /// stored.
    /// </exception>
    /// <exception cref="SchedulerException">The scheduler has been shut down, or shut down while waiting.</exception>
    /// <exception cref="JobPersistenceException">No job is stored under <paramref name="jobKey" />.</exception>
    public static ValueTask<FiringResult> TriggerJobAndWait(
        this IScheduler scheduler,
        JobKey jobKey,
        JobDataMap? data,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(jobKey);
        FiringWait.ValidateTimeout(timeout);

        IFiringWaitingScheduler waiting = Waiting(scheduler);
        JobDataMap? copy = data is null ? null : new JobDataMap((IDictionary<string, object?>) data);
        return TriggerAndWait(scheduler, waiting, jobKey, copy, timeout, cancellationToken);
    }

    /// <summary>
    /// Fires the stored job now with the given input and waits up to <paramref name="timeout" /> for how its run
    /// ended.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="TriggerJobAndWait(IScheduler, JobKey, TimeSpan, CancellationToken)" path="/remarks" />
    /// The input goes under <see cref="SchedulerConstants.JobInput" />, as with
    /// <see cref="SchedulerJobExtensions.TriggerJob{TInput}" />, whose trimming and HTTP notes apply.
    /// </remarks>
    /// <typeparam name="TInput">The input's type, inferred from <paramref name="input" />.</typeparam>
    /// <param name="scheduler">The scheduler to fire the job on.</param>
    /// <param name="jobKey">The stored job to fire.</param>
    /// <param name="input">The payload the firing carries, put on the trigger.</param>
    /// <param name="timeout">How long to wait; it stops the wait, never the job.</param>
    /// <param name="cancellationToken">The cancellation instruction; it stops the wait, never the job.</param>
    /// <returns>How the firing ended, or <see cref="FiringWaitStatus.Pending" /> when the timeout ran out first.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scheduler" /> or <paramref name="jobKey" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="input" /> is a <see cref="CancellationToken" />: the token goes last, as in
    /// <c>TriggerJobAndWait(jobKey, timeout, token)</c>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeout" /> is negative and is not <see cref="Timeout.InfiniteTimeSpan" />.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The scheduler cannot wait for a firing, or it is clustered without a history every node writes. Nothing is
    /// stored.
    /// </exception>
    /// <exception cref="SchedulerException">The scheduler has been shut down, or shut down while waiting.</exception>
    /// <exception cref="JobPersistenceException">No job is stored under <paramref name="jobKey" />.</exception>
    public static ValueTask<FiringResult> TriggerJobAndWait<TInput>(
        this IScheduler scheduler,
        JobKey jobKey,
        TInput input,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(jobKey);

        // TriggerJobAndWait(jobKey, token, timeout) binds here, the token as the input: refuse it as one, as
        // TriggerJob<TInput> does.
        if (input is CancellationToken)
        {
            throw new ArgumentException(
                "A CancellationToken is not a job input. Pass it last, as the cancellation token, as in "
                + "TriggerJobAndWait(jobKey, timeout, token), to fire the job with no input.",
                nameof(input));
        }

        FiringWait.ValidateTimeout(timeout);

        IFiringWaitingScheduler waiting = Waiting(scheduler);
        JobDataMap data = new() { [SchedulerConstants.JobInput] = input };
        return TriggerAndWait(scheduler, waiting, jobKey, data, timeout, cancellationToken);
    }

    /// <summary>
    /// Waits up to <paramref name="timeout" /> for how the next firing of the trigger ends, or, for a trigger
    /// already gone, for what its newest row in the execution history says.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second half of <c>TriggerJobAndWait</c>: after <see cref="FiringWaitStatus.Pending" />, wait again with
    /// <see cref="FiringResult.TriggerKey" />. Also the wait for a trigger stored another way, such as by
    /// <see cref="SchedulerJobExtensions.TriggerJob{TInput}" /> or the named-key
    /// <see cref="SchedulerJobExtensions.TriggerJob(IScheduler, JobKey, JobDataMap?, TriggerKey, CancellationToken)" />.
    /// </para>
    /// <para>
    /// A firing that ended before the wait began is read from the execution history. Without one it answers
    /// <see cref="FiringWaitStatus.Unknown" />, as it does for a trigger unscheduled or whose job was deleted while
    /// waited on, and for a row retention has trimmed.
    /// </para>
    /// <para>
    /// The timeout stops the wait and never the job, as the token does.
    /// </para>
    /// </remarks>
    /// <param name="scheduler">The scheduler the trigger is stored in.</param>
    /// <param name="triggerKey">The trigger whose firing to wait for.</param>
    /// <param name="timeout">How long to wait; it stops the wait, never the job.</param>
    /// <param name="cancellationToken">The cancellation instruction; it stops the wait, never the job.</param>
    /// <returns>How the firing ended, or where the wait stood when it answered.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scheduler" /> or <paramref name="triggerKey" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeout" /> is negative and is not <see cref="Timeout.InfiniteTimeSpan" />.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The scheduler cannot wait for a firing, or it is clustered without a history every node writes.
    /// </exception>
    /// <exception cref="SchedulerException">The scheduler has been shut down, or shut down while waiting.</exception>
    public static ValueTask<FiringResult> WaitForFiring(
        this IScheduler scheduler,
        TriggerKey triggerKey,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(triggerKey);
        FiringWait.ValidateTimeout(timeout);

        return Wait(Waiting(scheduler), triggerKey, timeout, cancellationToken);

        static async ValueTask<FiringResult> Wait(IFiringWaitingScheduler waiting, TriggerKey triggerKey, TimeSpan timeout, CancellationToken cancellationToken)
        {
            FiringWatch watch = await waiting.Watch(triggerKey, cancellationToken).ConfigureAwait(false);
            await using (watch.ConfigureAwait(false))
            {
                return await watch.Result(timeout, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async ValueTask<FiringResult> TriggerAndWait(
        IScheduler scheduler,
        IFiringWaitingScheduler waiting,
        JobKey jobKey,
        JobDataMap? data,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        SimpleTriggerImpl trigger = await SchedulerJobExtensions.ManualTrigger(scheduler, jobKey, data, triggerKey: null, cancellationToken).ConfigureAwait(false);

        int attempt = 1;
        while (true)
        {
            // The wait begins before the trigger is stored, so no completion can come before it, and its first look
            // at the history comes first too, so a cluster or a host that cannot answer refuses before anything runs.
            FiringWatch watch = await waiting.Watch(trigger.Key, cancellationToken).ConfigureAwait(false);
            await using (watch.ConfigureAwait(false))
            {
                await watch.Begin(cancellationToken).ConfigureAwait(false);

                try
                {
                    await scheduler.ScheduleJob(trigger, cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (ObjectAlreadyExistsException) when (attempt < SchedulerJobExtensions.ManualFiringAttempts)
                {
                    attempt++;
                    trigger.Key = ManualFiring.NewTriggerKey();
                    continue;
                }

                return await watch.Result(timeout, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static IFiringWaitingScheduler Waiting(IScheduler scheduler)
    {
        return scheduler as IFiringWaitingScheduler ?? throw CannotWait(scheduler);
    }

    /// <summary>
    /// The refusal of a scheduler that cannot wait for a firing.
    /// </summary>
    internal static NotSupportedException CannotWait(IScheduler scheduler)
    {
        return new NotSupportedException(
            $"{scheduler.GetType().Name} cannot wait for a firing: no IScheduler member says when one ends. Wait on the scheduler "
            + "Quartz builds, the IScheduler the container injects, a DelegatingScheduler over one of them, or HttpScheduler.");
    }
}
