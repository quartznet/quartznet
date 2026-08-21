using Microsoft.Extensions.Options;

using Quartz.Configuration;

namespace Quartz.Impl;

/// <summary>
/// The <see cref="IScheduler"/> a container hands out: a handle to a scheduler that is built when it is
/// first used rather than when it is injected.
/// </summary>
/// <remarks>
/// <para>
/// A scheduler cannot be constructed by the container directly — building one is asynchronous, and the
/// container only constructs synchronously — which is why <see cref="ISchedulerFactory"/> exists. This
/// proxy is what lets <c>IScheduler</c> nevertheless be an ordinary service: every member forwards to the
/// scheduler the factory produces, resolving it on first use and remembering it.
/// </para>
/// <para>
/// The asynchronous members are always safe, because they can await the scheduler being built. The
/// synchronous ones — <see cref="SchedulerInstanceId"/>, <see cref="IsStarted"/>,
/// <see cref="InStandbyMode"/>, <see cref="IsShutdown"/>, <see cref="Context"/> and
/// <see cref="ListenerManager"/> — can only answer once the scheduler exists, and throw
/// <see cref="InvalidOperationException"/> when reading one would have to build it. Under the hosted
/// service that never happens: it builds and starts every scheduler in the container before the
/// application runs. <see cref="SchedulerName"/> is answered without resolving anything, because a
/// registration always knows the name it was made under.
/// </para>
/// </remarks>
internal sealed class DeferredScheduler : IScheduler
{
    private readonly ISchedulerFactory factory;
    private readonly IOptionsMonitor<QuartzSchedulerOptions> options;
    private readonly string optionsName;
    private readonly string? registrationName;

    private IScheduler? scheduler;
    private Task<IScheduler>? creation;

    public DeferredScheduler(
        ISchedulerFactory factory,
        IOptionsMonitor<QuartzSchedulerOptions> options,
        SchedulerKey schedulerKey)
    {
        this.factory = factory;
        this.options = options;
        optionsName = schedulerKey.OptionsName;
        registrationName = schedulerKey.Key as string;
    }

    /// <summary>
    /// The scheduler, resolving it if this is the first use.
    /// </summary>
    private ValueTask<IScheduler> Resolve(CancellationToken cancellationToken)
    {
        // The factory caches, so asking it again would be correct — but it takes a lock and a repository
        // lookup to say so, and this sits in front of every call a caller makes.
        var resolved = scheduler;
        return resolved is not null ? new ValueTask<IScheduler>(resolved) : Create(cancellationToken);

        async ValueTask<IScheduler> Create(CancellationToken cancellationToken)
        {
            return scheduler = await factory.GetScheduler(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The scheduler, for the members that cannot wait for one to be built.
    /// </summary>
    /// <remarks>
    /// A scheduler that already exists is handed back — which is the normal case, since the hosted
    /// service builds every scheduler in the container at startup. Otherwise the build is started and
    /// kept, so a caller that asks again gets that same attempt rather than starting another, and its
    /// failure is reported rather than lost.
    /// </remarks>
    private IScheduler Resolved
    {
        get
        {
            var resolved = scheduler;
            if (resolved is not null)
            {
                return resolved;
            }

            var pending = creation ??= factory.GetScheduler().AsTask();
            if (pending.IsCompleted)
            {
                // Rethrows on the awaiter rather than wrapping in AggregateException, so a configuration
                // failure reads the same here as it does from an awaited call.
                return scheduler = pending.GetAwaiter().GetResult();
            }

            throw new InvalidOperationException(
                $"Scheduler '{SchedulerName}' has not been started yet, so this member cannot be read. "
                + "Resolve the scheduler after the host has started, or await any of its methods first — "
                + "building a scheduler is asynchronous, and a property cannot wait for it.");
        }
    }

    public string SchedulerName => registrationName ?? options.Get(optionsName).InstanceName;

    public string SchedulerInstanceId => Resolved.SchedulerInstanceId;

    public SchedulerContext Context => Resolved.Context;

    public bool InStandbyMode => Resolved.InStandbyMode;

    public bool IsShutdown => Resolved.IsShutdown;

    public bool IsStarted => Resolved.IsStarted;

    public IListenerManager ListenerManager => Resolved.ListenerManager;

    public async ValueTask<SchedulerMetadata> GetMetadata(CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.GetMetadata(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<List<IJobExecutionContext>> GetCurrentlyExecutingJobs(CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.GetCurrentlyExecutingJobs(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask Start(CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        await target.Start(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask StartDelayed(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        await target.StartDelayed(delay, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask Standby(CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        await target.Standby(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask Shutdown(bool waitForJobsToComplete = false, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        await target.Shutdown(waitForJobsToComplete, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<DateTimeOffset> ScheduleJob(IJobDetail jobDetail, ITrigger trigger, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.ScheduleJob(jobDetail, trigger, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<DateTimeOffset> ScheduleJob(ITrigger trigger, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.ScheduleJob(trigger, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, ScheduleJobOptions options = default, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        await target.ScheduleJobs(triggersAndJobs, options, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ScheduleJob(IJobDetail jobDetail, IReadOnlyCollection<ITrigger> triggersForJob, ScheduleJobOptions options = default, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        await target.ScheduleJob(jobDetail, triggersForJob, options, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> UnscheduleJob(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.UnscheduleJob(triggerKey, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> UnscheduleJobs(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.UnscheduleJobs(triggerKeys, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<DateTimeOffset?> RescheduleJob(TriggerKey triggerKey, ITrigger newTrigger, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.RescheduleJob(triggerKey, newTrigger, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> UpdateTriggerDetails(TriggerKey triggerKey, TriggerDetailsUpdate update, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.UpdateTriggerDetails(triggerKey, update, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask SetExecutionLimits(ExecutionLimits? limits, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        await target.SetExecutionLimits(limits, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ExecutionLimits?> GetExecutionLimits(CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.GetExecutionLimits(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask AddJob(IJobDetail jobDetail, AddJobOptions options = default, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        await target.AddJob(jobDetail, options, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.DeleteJob(jobKey, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.DeleteJobs(jobKeys, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask TriggerJob(JobKey jobKey, JobDataMap? data = null, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        await target.TriggerJob(jobKey, data, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.PauseJob(jobKey, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<List<string>> PauseJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.PauseJobs(matcher, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.PauseTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<List<string>> PauseTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.PauseTriggers(matcher, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.ResumeJob(jobKey, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<List<string>> ResumeJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.ResumeJobs(matcher, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.ResumeTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<List<string>> ResumeTriggers(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.ResumeTriggers(matcher, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask PauseAll(CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        await target.PauseAll(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask ResumeAll(CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        await target.ResumeAll(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<PagedResult<JobHeader>> QueryJobs(JobQuery query, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.QueryJobs(query, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<PagedResult<TriggerHeader>> QueryTriggers(TriggerQuery query, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.QueryTriggers(query, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<PagedResult<JobGroup>> QueryJobGroups(JobGroupQuery query, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.QueryJobGroups(query, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<PagedResult<TriggerGroup>> QueryTriggerGroups(TriggerGroupQuery query, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.QueryTriggerGroups(query, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<PagedResult<string>> QueryCalendarNames(CalendarQuery query, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.QueryCalendarNames(query, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<List<IJobDetail>> GetJobDetails(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.GetJobDetails(jobKeys, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<List<ITrigger>> GetTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.GetTriggers(triggerKeys, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<IJobDetail?> GetJobDetail(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.GetJobDetail(jobKey, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ITrigger?> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.GetTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<TriggerState> GetTriggerState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.GetTriggerState(triggerKey, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.ResetTriggerFromErrorState(triggerKey, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask AddCalendar(string calendarName, ICalendar calendar, AddCalendarOptions options = default, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        await target.AddCalendar(calendarName, calendar, options, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> DeleteCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.DeleteCalendar(calendarName, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ICalendar?> GetCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.GetCalendar(calendarName, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> Interrupt(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.Interrupt(jobKey, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> InterruptFireInstance(string fireInstanceId, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.InterruptFireInstance(fireInstanceId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> Exists(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.Exists(jobKey, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<bool> Exists(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        return await target.Exists(triggerKey, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask Clear(CancellationToken cancellationToken = default)
    {
        var target = await Resolve(cancellationToken).ConfigureAwait(false);
        await target.Clear(cancellationToken).ConfigureAwait(false);
    }
}
