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

using Quartz.Core;
using Quartz.Impl.Triggers;

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

    /// <summary>
    /// The attempts <see cref="TriggerJob{TInput}" /> makes at a free trigger name before it lets the
    /// collision through. A name is 63 random bits, so a second attempt is already a curiosity.
    /// </summary>
    private const int ManualFiringAttempts = 3;

    /// <summary>
    /// Fires the stored job now with the given input, and returns the key of the trigger that fires it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The typed twin of <see cref="IScheduler.TriggerJob" />, and the same firing: a one-shot trigger
    /// named <c>MT_</c> and a number in the default group, marked with
    /// <see cref="SchedulerConstants.ManualTrigger" /> so history and the dashboard show a manual run, and
    /// in the execution group <c>TriggerJob</c> would give it. The input goes under
    /// <see cref="SchedulerConstants.JobInput" />, where <c>UsingInput</c> puts it, and the scheduler's
    /// <see cref="Quartz.Extensibility.IJobInputSerializer" /> writes it as the trigger is stored, so an
    /// <see cref="IJob{TInput}" /> receives it as its parameter.
    /// </para>
    /// <para>
    /// The trigger is stored with <see cref="IScheduler.ScheduleJob(ITrigger, ScheduleJobOptions, CancellationToken)" />,
    /// so its key is known before it can fire. Look the run up with it: <see cref="ExecutionHistoryQuery.Trigger" />
    /// in the execution history, or <see cref="FireInstanceQuery.TriggerName" /> while it runs.
    /// </para>
    /// <para>
    /// Trimmed or native AOT: declare <typeparamref name="TInput" /> with
    /// <see cref="Quartz.Serialization.SystemTextJson.SystemTextJsonSerializerRegistry.AddTypeInfoResolver" />,
    /// as for <c>UsingInput</c>. Nothing here reflects.
    /// </para>
    /// <para>
    /// Over <c>HttpScheduler</c> the input crosses the wire as a job data value, before any input
    /// serializer sees it, so it has to be a string or a job data value type. Pass a structured payload
    /// as the string the host's serializer reads: JSON, for the default one. The execution group is
    /// resolved by the calling process, which skips a job type it cannot load.
    /// </para>
    /// </remarks>
    /// <typeparam name="TInput">The input's type, inferred from <paramref name="input" />.</typeparam>
    /// <param name="scheduler">The scheduler to fire the job on.</param>
    /// <param name="jobKey">The stored job to fire.</param>
    /// <param name="input">The payload the firing carries, put on the trigger.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The key of the manual firing's trigger.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scheduler" /> or <paramref name="jobKey" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="input" /> is a <see cref="CancellationToken" />, which only an explicit
    /// <c>TriggerJob&lt;CancellationToken&gt;(…)</c> can pass: <c>TriggerJob(jobKey, token)</c> binds to
    /// <see cref="TriggerJob(IScheduler, JobKey, CancellationToken)" />.
    /// </exception>
    /// <exception cref="SchedulerException">The scheduler has been shut down.</exception>
    /// <exception cref="JobPersistenceException">No job is stored under <paramref name="jobKey" />.</exception>
    public static ValueTask<TriggerKey> TriggerJob<TInput>(
        this IScheduler scheduler,
        JobKey jobKey,
        TInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(jobKey);

        // TriggerJob(jobKey, token) binds to the non-generic overload below, so a token arrives here only
        // through an explicit TriggerJob<CancellationToken>(…). It is not a payload; refuse it as one.
        if (input is CancellationToken)
        {
            throw new ArgumentException(
                "A CancellationToken is not a job input. Pass it as the cancellation token, as in TriggerJob(jobKey, token), "
                + "to fire the job with no input.",
                nameof(input));
        }

        return FireNow(scheduler, jobKey, input, cancellationToken);
    }

    /// <summary>
    /// Fires the stored job now with no data of its own: <see cref="IScheduler.TriggerJob" /> with the token
    /// as the second argument.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The interface member's second parameter is a <see cref="JobDataMap" />, so <c>TriggerJob(jobKey, token)</c>
    /// does not bind to it. Without this overload it would bind to <see cref="TriggerJob{TInput}" />, the token
    /// as the input. A non-generic overload is preferred over a generic one, so the call means what it reads as.
    /// </para>
    /// <para>
    /// A <see cref="JobDataMap" />, or <see langword="null" />, as the second argument still binds to the
    /// interface member, which an instance member always wins.
    /// </para>
    /// </remarks>
    /// <param name="scheduler">The scheduler to fire the job on.</param>
    /// <param name="jobKey">The stored job to fire.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scheduler" /> or <paramref name="jobKey" /> is <see langword="null" />.</exception>
    /// <exception cref="SchedulerException">The scheduler has been shut down.</exception>
    /// <exception cref="JobPersistenceException">No job is stored under <paramref name="jobKey" />.</exception>
    public static ValueTask TriggerJob(
        this IScheduler scheduler,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(jobKey);

        return scheduler.TriggerJob(jobKey, data: null, cancellationToken);
    }

    private static async ValueTask<TriggerKey> FireNow<TInput>(
        IScheduler scheduler,
        JobKey jobKey,
        TInput input,
        CancellationToken cancellationToken)
    {
        JobDataMap data = new() { [SchedulerConstants.JobInput] = input };

        // TriggerJob's order, read through the scheduler rather than its store: RunAtStartup does the same.
        string? executionGroup = await ManualFiringExecutionGroup.Resolve(
            jobKey,
            data,
            scheduler.GetJobDetail,
            scheduler.QueryTriggers,
            cancellationToken).ConfigureAwait(false);

        SimpleTriggerImpl trigger = ManualFiring.CreateTrigger(jobKey, data, executionGroup, scheduler.TimeProvider);

        int attempt = 1;
        while (true)
        {
            try
            {
                await scheduler.ScheduleJob(trigger, cancellationToken: cancellationToken).ConfigureAwait(false);
                return trigger.Key;
            }
            catch (ObjectAlreadyExistsException) when (attempt < ManualFiringAttempts)
            {
                attempt++;
                trigger.Key = ManualFiring.NewTriggerKey();
            }
        }
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

    /// <inheritdoc cref="ScheduleJob{TJob, TInput}(IScheduler, TInput, DateTimeOffset, OneOffJobOptions, CancellationToken)" path="/summary|/typeparam|/returns" />
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

    /// <inheritdoc cref="ScheduleJob{TJob, TInput}(IScheduler, TInput, DateTimeOffset, OneOffJobOptions, CancellationToken)" path="/summary|/typeparam|/returns" />
    /// <param name="scheduler">The scheduler to schedule on.</param>
    /// <param name="input">The payload the firing carries, put on the trigger.</param>
    /// <param name="after">
    /// The trigger whose firing this one waits for, and the outcomes of it that release the wait.
    /// <see cref="Continuation.After" /> builds one; <see cref="ScheduledOneOffJob.TriggerKey" /> is
    /// the handle an earlier call answered with.
    /// </param>
    /// <param name="options">The trigger's identity and the rest of what can be said about one firing.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <remarks>
    /// <para>
    /// The firing is stored in <see cref="TriggerState.Awaiting" /> and never acquired until the
    /// parent's completion settles it, inside the parent's own lock and transaction. An outcome the
    /// condition names releases it; any other outcome deletes it.
    /// </para>
    /// <para>
    /// There is no time argument, because the time is the parent's completion. Give
    /// <see cref="OneOffJobOptions" /> nothing else and the firing happens as soon as it is released;
    /// a floor — "and never before nine" — is a <c>StartAfter</c> trigger built by hand.
    /// </para>
    /// <para>
    /// The parent has to be in the store when this is called: a one-shot parent is deleted once it has
    /// fired, so schedule the continuation before the parent can finish — the handle an earlier call
    /// answered with is the one to use straight away — or check the parent's key.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDoesNotExistException">
    /// The parent <paramref name="after" /> names is not in the store. Nothing is stored.
    /// </exception>
    /// <returns>The trigger that was stored, and the time it would first fire were it released now.</returns>
    public static ValueTask<ScheduledOneOffJob> ScheduleJob<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob, TInput>(
        this IScheduler scheduler,
        TInput input,
        Continuation after,
        OneOffJobOptions options = default,
        CancellationToken cancellationToken = default) where TJob : IJob<TInput>
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        if (after.IsNone)
        {
            throw new ArgumentException(
                $"A continuation names the trigger it waits for; {nameof(Quartz.Continuation)}.{nameof(Quartz.Continuation.None)} names none. "
                + $"Use {nameof(Quartz.Continuation)}.{nameof(Quartz.Continuation.After)} to build one, or one of the overloads taking a time.",
                nameof(after));
        }

        // "Now" as the start time, so that a released continuation fires at once rather than at an
        // instant that has already gone: the release floors the fire time at the trigger's start time.
        return Schedule<TJob, TInput>(
            scheduler,
            input,
            scheduler.TimeProvider.GetUtcNow(),
            options with { Continuation = after },
            cancellationToken);
    }

    private static async ValueTask<ScheduledOneOffJob> Schedule<[DynamicallyAccessedMembers(JobTypeMembers.Required)] TJob, TInput>(
        IScheduler scheduler,
        TInput input,
        DateTimeOffset at,
        OneOffJobOptions options,
        CancellationToken cancellationToken) where TJob : IJob<TInput>
    {
        // Before anything is stored, so options that contradict themselves leave nothing behind.
        TriggerConflict onConflict = options.ResolveOnConflict();

        JobKey jobKey = SchedulerConstants.ScheduledJobKey<TJob>();
        await EnsureJob<TJob>(scheduler, options.RequestRecovery, cancellationToken).ConfigureAwait(false);

        ITrigger trigger = BuildTrigger<TJob, TInput>(scheduler, jobKey, input, at, options);

        ScheduleTriggerResult result;
        try
        {
            result = await Store(scheduler, trigger, onConflict, cancellationToken).ConfigureAwait(false);
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
            result = await Store(scheduler, trigger, onConflict, cancellationToken).ConfigureAwait(false);
        }

        return new ScheduledOneOffJob(trigger.Key, result.NextFireTimeUtc) { Outcome = result.Outcome };

        // The default is the call the one-liner has always made, so a scheduler of one's own, a fake in a
        // test or a host older than 4.3 behind HttpScheduler sees what it always saw; a success there can
        // only have created the trigger. Every other mode is the store's decision to make.
        static async ValueTask<ScheduleTriggerResult> Store(IScheduler scheduler, ITrigger trigger, TriggerConflict onConflict, CancellationToken cancellationToken)
        {
            if (onConflict == TriggerConflict.Throw)
            {
                DateTimeOffset created = await scheduler.ScheduleJob(trigger, default(ScheduleJobOptions), cancellationToken).ConfigureAwait(false);
                return new ScheduleTriggerResult(created, ScheduleOutcome.Created);
            }

            return await scheduler.ScheduleTrigger(trigger, onConflict, cancellationToken).ConfigureAwait(false);
        }
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
            .WithExecutionGroup(ResolveExecutionGroup<TJob>(options.ExecutionGroup))
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

        if (options.Continuation.Parent is { } parent)
        {
            builder = builder.StartAfter(parent, options.Continuation.When);
        }

        return builder.Build();
    }

    /// <summary>
    /// The execution group the one-liner's trigger stores: the options' name as written, else the job
    /// type's <see cref="ExecutionGroupAttribute" /> when it needs no values.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The options' group is a name, not a template, so it is escaped before the builder reads it: a
    /// brace in it stays a brace, as it did before templates existed. A per-tenant one-off says the
    /// tenant at the call site, <c>ExecutionGroup = $"tenant:{input.TenantId}"</c>.
    /// </para>
    /// <para>
    /// The one-liner's job data is the input as a whole, so an attribute with placeholders has nothing
    /// to read them from. Rather than store the firing in no group, outside the limit the attribute
    /// exists to apply, the call is refused with the spelling that works.
    /// </para>
    /// </remarks>
    /// <exception cref="FormatException">The options name no group and the job type's attribute has
    /// placeholders.</exception>
    private static string? ResolveExecutionGroup<TJob>(string? executionGroup) where TJob : IJob
    {
        if (!string.IsNullOrWhiteSpace(executionGroup))
        {
            return ExecutionGroupTemplate.Escape(executionGroup.Trim());
        }

        string? declared = DeclaredExecutionGroup<TJob>.Template;

        if (declared is not null && ExecutionGroupTemplate.HasPlaceholders(declared))
        {
            throw new FormatException(
                $"{typeof(TJob).Name} declares [ExecutionGroup(\"{declared}\")], and a one-off trigger has no job data "
                + "to fill its placeholders from: the input is stored whole. Name the group at the call site instead, "
                + "as in OneOffJobOptions.ExecutionGroup = $\"tenant:{input.TenantId}\".");
        }

        // A declared name with no placeholders is handed on for the builder to read, which unescapes
        // any {{ or }} in it the way it would have applied the attribute itself.
        return declared;
    }
}
