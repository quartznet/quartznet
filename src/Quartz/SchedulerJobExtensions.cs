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

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Quartz;

/// <summary>
/// Scheduling a job by its type, the way the container-configured builder does.
/// </summary>
/// <remarks>
/// Extensions rather than <see cref="IScheduler" /> members: they compose <see cref="JobBuilder" />,
/// <see cref="TriggerBuilder" /> and the scheduler's own operations, and need nothing an implementation
/// could do better, so every scheduler — including one written outside this repository — gets them
/// without having to write them.
/// </remarks>
public static class SchedulerJobExtensions
{
    /// <summary>
    /// Schedule a job of the given type with the given trigger, building the
    /// <see cref="IJobDetail" /> along the way.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The imperative twin of <c>q.ScheduleJob&lt;TJob&gt;(...)</c> on <see cref="IQuartzBuilder" />,
    /// so that scheduling a job at run time reads like declaring one at start-up and neither has to
    /// name a <see cref="JobBuilder" />.
    /// </para>
    /// <para>
    /// The job's identity is the first of these that exists: the one
    /// <paramref name="configure" /> gave it, the one <paramref name="trigger" /> already points at
    /// through <see cref="ITrigger.JobKey" />, or the trigger's own key. Naming neither is therefore
    /// enough to schedule the pair, and a trigger built with <c>ForJob</c> still gets the job it
    /// asked for.
    /// </para>
    /// </remarks>
    /// <typeparam name="TJob">the type of the job to schedule.</typeparam>
    /// <param name="scheduler">the scheduler to schedule the job with.</param>
    /// <param name="trigger">the trigger that fires the job.</param>
    /// <param name="configure">configures the job; omit to take everything from the type and the trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>the first time at which the trigger will fire.</returns>
    public static ValueTask<DateTimeOffset> ScheduleJob<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob>(
        this IScheduler scheduler,
        ITrigger trigger,
        Action<IJobConfigurator<TJob>>? configure = null,
        CancellationToken cancellationToken = default) where TJob : IJob
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(trigger);

        JobBuilder<TJob> jobBuilder = JobBuilder.Create<TJob>();
        configure?.Invoke(jobBuilder);

        if (jobBuilder.Key is null)
        {
            // The job was given no identity of its own, so it takes one that makes the pair agree
            // rather than the Guid JobBuilder would otherwise generate — which would leave the
            // trigger pointing at a job nobody can name again.
            JobKey borrowed = trigger.JobKey ?? new JobKey(trigger.Key.Name, trigger.Key.Group);
            jobBuilder.WithIdentity(borrowed);
        }

        return scheduler.ScheduleJob(jobBuilder.Build(), trigger, cancellationToken: cancellationToken);
    }

    // -------------------------------------------------------------------------------------------
    // One firing of a typed job: a payload, a time, and nothing else to build.
    //
    // The imperative twin of IQuartzBuilder.ScheduleJob<TJob>, for the firings an application
    // arranges while it runs rather than at start-up - a retry, a timeout, a reminder, the next step
    // of a saga.
    //
    // One durable job per job type, many triggers. The job is stored once under SchedulerConstants.ScheduledJobKey<TJob>()
    // and every call adds a trigger to it, which is the shape a message bus's Quartz integration
    // converges on: a scheduled message is a trigger, and there is no job churn to pay for. The job is
    // ensured with AddJobOptions.Replacing, so it is idempotent and safe for several nodes to do at
    // once, and the result is remembered per scheduler instance so the second call is one round trip
    // rather than two - which is also why the only thing OneOffJobOptions says about the job itself,
    // RequestRecovery, is first-call-wins.
    //
    // Cancelling is UnscheduleJob. Give the firing a name and the returned ScheduledOneOffJob carries
    // the handle to cancel or to replace with, beside the time the store says it will first fire; the
    // durable job stays behind, one row per job type whatever the traffic.
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// The job types already ensured on a scheduler, per scheduler instance.
    /// </summary>
    /// <remarks>
    /// Weak on the scheduler, so remembering does not keep a shut-down scheduler alive. The memo is only
    /// ever an optimization: forgetting one costs an extra idempotent <see cref="IScheduler.AddJob" />,
    /// and a stale one is corrected by the retry in <see cref="Schedule{TJob, TInput}" />.
    /// </remarks>
    private static readonly ConditionalWeakTable<IScheduler, ConcurrentDictionary<Type, bool>> ensuredJobs = new();

    /// <summary>
    /// Schedules one firing of <typeparamref name="TJob" /> with the given payload, at the given time.
    /// </summary>
    /// <typeparam name="TJob">The job to fire.</typeparam>
    /// <typeparam name="TInput">The payload's type, inferred from <paramref name="input" />.</typeparam>
    /// <param name="scheduler">The scheduler to schedule on.</param>
    /// <param name="input">The payload the firing carries, put on the trigger.</param>
    /// <param name="at">When the job should run.</param>
    /// <param name="options">The trigger's identity and the rest of what can be said about one firing.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The trigger that was stored and the time it will first fire.</returns>
    public static ValueTask<ScheduledOneOffJob> ScheduleJob<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob, TInput>(
        this IScheduler scheduler,
        TInput input,
        DateTimeOffset at,
        OneOffJobOptions options = default,
        CancellationToken cancellationToken = default) where TJob : IJob<TInput>
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        return Schedule<TJob, TInput>(scheduler, input, at, options, cancellationToken);
    }

    /// <inheritdoc cref="ScheduleJob{TJob, TInput}(IScheduler, TInput, DateTimeOffset, OneOffJobOptions, CancellationToken)" />
    /// <param name="scheduler">The scheduler to schedule on.</param>
    /// <param name="input">The payload the firing carries, put on the trigger.</param>
    /// <param name="delay">How long from now the job should run.</param>
    /// <param name="options">The trigger's identity and the rest of what can be said about one firing.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <remarks>
    /// "Now" is <see cref="IScheduler.TimeProvider" />, so a delay is measured against the same clock
    /// the scheduling loop will compare it to.
    /// </remarks>
    public static ValueTask<ScheduledOneOffJob> ScheduleJob<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob, TInput>(
        this IScheduler scheduler,
        TInput input,
        TimeSpan delay,
        OneOffJobOptions options = default,
        CancellationToken cancellationToken = default) where TJob : IJob<TInput>
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        return Schedule<TJob, TInput>(scheduler, input, scheduler.TimeProvider.GetUtcNow() + delay, options, cancellationToken);
    }

    private static async ValueTask<ScheduledOneOffJob> Schedule<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob, TInput>(
        IScheduler scheduler,
        TInput input,
        DateTimeOffset at,
        OneOffJobOptions options,
        CancellationToken cancellationToken) where TJob : IJob<TInput>
    {
        JobKey jobKey = SchedulerConstants.ScheduledJobKey<TJob>();
        await EnsureJob<TJob>(scheduler, options.RequestRecovery, cancellationToken).ConfigureAwait(false);

        ITrigger trigger = BuildTrigger<TJob, TInput>(scheduler, jobKey, input, at, options);
        ScheduleJobOptions storeOptions = new() { Replace = options.Replace };

        DateTimeOffset firstFireTimeUtc;
        try
        {
            firstFireTimeUtc = await scheduler.ScheduleJob(trigger, storeOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (JobPersistenceException)
        {
            // A store refuses a trigger whose job is missing, and the job can go missing under a memo
            // that says it is there: another node cleared the schedule, an operator deleted it, the
            // database was restored. Forget what was remembered, put the job back and try once. A
            // second failure, or a failure with the job present, is the caller's to see.
            Forget<TJob>(scheduler);

            if (await scheduler.Exists(jobKey, cancellationToken).ConfigureAwait(false))
            {
                throw;
            }

            await EnsureJob<TJob>(scheduler, options.RequestRecovery, cancellationToken).ConfigureAwait(false);
            firstFireTimeUtc = await scheduler.ScheduleJob(trigger, storeOptions, cancellationToken).ConfigureAwait(false);
        }

        return new ScheduledOneOffJob(trigger.Key, firstFireTimeUtc);
    }

    private static ValueTask EnsureJob<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob>(
        IScheduler scheduler,
        bool requestRecovery,
        CancellationToken cancellationToken) where TJob : IJob
    {
        ConcurrentDictionary<Type, bool> ensured = ensuredJobs.GetOrCreateValue(scheduler);
        if (ensured.ContainsKey(typeof(TJob)))
        {
            return default;
        }

        return Store(scheduler, ensured, requestRecovery, cancellationToken);

        static async ValueTask Store(IScheduler scheduler, ConcurrentDictionary<Type, bool> ensured, bool requestRecovery, CancellationToken cancellationToken)
        {
            IJobDetail job = JobBuilder.Create<TJob>()
                .WithIdentity(SchedulerConstants.ScheduledJobKey<TJob>())
                .WithDescription($"Scheduled firings of {typeof(TJob).FullName}.")
                .RequestRecovery(requestRecovery)
                .StoreDurably()
                .Build();

            // Replacing rather than asking first: storing the same durable job over itself is what every
            // node in a cluster does, and it neither races nor throws. Asking would be a round trip and
            // a window.
            await scheduler.AddJob(job, AddJobOptions.Replacing, cancellationToken).ConfigureAwait(false);
            ensured[typeof(TJob)] = true;
        }
    }

    private static void Forget<TJob>(IScheduler scheduler) where TJob : IJob
    {
        if (ensuredJobs.TryGetValue(scheduler, out ConcurrentDictionary<Type, bool>? ensured))
        {
            ensured.TryRemove(typeof(TJob), out _);
        }
    }

    private static ITrigger BuildTrigger<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob, TInput>(
        IScheduler scheduler,
        JobKey jobKey,
        TInput input,
        DateTimeOffset at,
        OneOffJobOptions options) where TJob : IJob<TInput>
    {
        // The scheduler's clock rather than the machine's: the trigger built here is Quartz's, not the
        // caller's, so it has to compute its fire times from what the scheduling loop calls "now".
        TriggerBuilder<TJob> builder = TriggerBuilder.Create<TJob>(scheduler.TimeProvider)
            .WithIdentity(options.Name ?? Guid.NewGuid().ToString(), options.Group ?? typeof(TJob).Name)
            .ForJob(jobKey)
            .StartAt(at)
            .WithDescription(options.Description)
            .WithExecutionGroup(options.ExecutionGroup)
            .UsingInput(input);

        if (options.Priority is { } priority)
        {
            builder = builder.WithPriority(priority);
        }

        if (options.MisfireInstruction is { } misfireInstruction)
        {
            // The schedule is otherwise left at its default, which is a simple trigger that fires once.
            builder = builder.WithSimpleSchedule(schedule => schedule.WithMisfireInstruction(misfireInstruction));
        }

        return builder.Build();
    }
}
