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

using Microsoft.Extensions.Options;

using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Dashboard.Services;

/// <remarks>
/// Nothing here serializes anything. The schedulers are in this process, so a trigger, a calendar and
/// a job data map travel as themselves; the JSON round trip this client used to make existed only
/// because the client's contract spoke <c>JsonElement</c>, and it lost every trigger type the
/// serializer registry did not know.
/// </remarks>
internal sealed class InProcessQuartzApiClient : IQuartzApiClient
{
    private readonly ISchedulerRepository schedulerRepository;
    private readonly ISchedulerRegistry schedulerRegistry;
    private readonly IOptions<QuartzDashboardOptions> options;
    private readonly IDashboardHistoryStore historyStore;

    public InProcessQuartzApiClient(
        ISchedulerRepository schedulerRepository,
        ISchedulerRegistry schedulerRegistry,
        IOptions<QuartzDashboardOptions> options,
        IDashboardHistoryStore historyStore)
    {
        this.schedulerRepository = schedulerRepository;
        this.schedulerRegistry = schedulerRegistry;
        this.options = options;
        this.historyStore = historyStore;
    }

    /// <remarks>
    /// The registrations, not the repository: a tenant nothing has built yet is still a tenant, and the
    /// dashboard is where an operator goes to find out that it has not started. The repository is asked
    /// only for the instance id of the schedulers that do exist, which a registration does not carry.
    /// </remarks>
    public async ValueTask<List<SchedulerHeaderDto>> GetSchedulers(CancellationToken cancellationToken = default)
    {
        List<SchedulerRegistration> registrations = await schedulerRegistry.QuerySchedulers(cancellationToken).ConfigureAwait(false);

        List<SchedulerHeaderDto> result = new(registrations.Count);
        foreach (SchedulerRegistration registration in registrations)
        {
            IScheduler? scheduler = registration.IsCreated ? schedulerRepository.Lookup(registration.Name) : null;
            result.Add(new SchedulerHeaderDto(
                registration.Name,
                scheduler?.SchedulerInstanceId,
                registration.Status,
                registration.Origin));
        }

        return result;
    }

    public async ValueTask<SchedulerDetailDto> GetScheduler(string schedulerName, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        SchedulerMetadata metadata = await scheduler.GetMetadata(cancellationToken).ConfigureAwait(false);

        return new SchedulerDetailDto(
            scheduler.SchedulerInstanceId,
            scheduler.SchedulerName,
            metadata.Status,
            metadata.JobStoreClustered,
            metadata.JobStorePersistent,
            metadata.JobStoreTypeName,
            metadata.ThreadPoolTypeName,
            metadata.ThreadPoolSize,
            metadata.RunningSince,
            metadata.JobsExecuted,
            metadata.Version);
    }

    public ValueTask StartScheduler(string schedulerName, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return scheduler.Start(cancellationToken);
    }

    public ValueTask StandbyScheduler(string schedulerName, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return scheduler.Standby(cancellationToken);
    }

    public ValueTask ShutdownScheduler(string schedulerName, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return scheduler.Shutdown(cancellationToken: cancellationToken);
    }

    public ValueTask PauseAll(string schedulerName, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return scheduler.PauseAll(cancellationToken);
    }

    public ValueTask ResumeAll(string schedulerName, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return scheduler.ResumeAll(cancellationToken);
    }

    public async ValueTask<PagedResult<JobKeyDto>> GetJobs(string schedulerName, DashboardJobQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobQuery storeQuery = new()
        {
            Group = BuildGroupMatcher<JobKey>(query.GroupContains),
            Skip = query.Skip,
            Take = query.Take,
            IncludeTotalCount = true
        };

        PagedResult<JobHeader> jobs = await scheduler.QueryJobs(storeQuery, cancellationToken).ConfigureAwait(false);

        List<JobKeyDto> items = new(jobs.Items.Count);
        foreach (JobHeader job in jobs.Items)
        {
            items.Add(new JobKeyDto(job.Key.Group, job.Key.Name));
        }

        return new PagedResult<JobKeyDto>(items, jobs.HasMore, jobs.TotalCount ?? items.Count);
    }

    public async ValueTask<PagedResult<JobGroupDto>> GetJobGroups(string schedulerName, DashboardGroupQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobGroupQuery storeQuery = new()
        {
            Paused = query.Paused,
            Skip = query.Skip,
            Take = query.Take,
            IncludeTotalCount = true
        };

        PagedResult<JobGroup> groups = await scheduler.QueryJobGroups(storeQuery, cancellationToken).ConfigureAwait(false);

        List<JobGroupDto> items = new(groups.Items.Count);
        foreach (JobGroup group in groups.Items)
        {
            items.Add(new JobGroupDto(group.Name, group.Paused));
        }

        return new PagedResult<JobGroupDto>(items, groups.HasMore, groups.TotalCount ?? items.Count);
    }

    public async ValueTask<PagedResult<TriggerGroupDto>> GetTriggerGroups(string schedulerName, DashboardGroupQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        TriggerGroupQuery storeQuery = new()
        {
            Paused = query.Paused,
            Skip = query.Skip,
            Take = query.Take,
            IncludeTotalCount = true
        };

        PagedResult<TriggerGroup> groups = await scheduler.QueryTriggerGroups(storeQuery, cancellationToken).ConfigureAwait(false);

        List<TriggerGroupDto> items = new(groups.Items.Count);
        foreach (TriggerGroup group in groups.Items)
        {
            items.Add(new TriggerGroupDto(group.Name, group.Paused));
        }

        return new PagedResult<TriggerGroupDto>(items, groups.HasMore, groups.TotalCount ?? items.Count);
    }

    public async ValueTask<JobDetailDto> GetJob(string schedulerName, JobKeyDto key, CancellationToken cancellationToken = default)
    {
        JobKey jobKey = AsJobKey(key);
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        IJobDetail? jobDetail = await scheduler.GetJobDetail(jobKey, cancellationToken).ConfigureAwait(false);
        if (jobDetail is null)
        {
            throw new KeyNotFoundException($"Job '{key.Group}.{key.Name}' was not found in scheduler '{schedulerName}'.");
        }

        return new JobDetailDto(
            Name: jobDetail.Key.Name,
            Group: jobDetail.Key.Group,
            JobType: jobDetail.JobType.FullName,
            Description: jobDetail.Description,
            Durable: jobDetail.Durable,
            RequestsRecovery: jobDetail.RequestsRecovery,
            ConcurrentExecutionDisallowed: jobDetail.ConcurrentExecutionDisallowed,
            PersistJobDataAfterExecution: jobDetail.PersistJobDataAfterExecution,
            JobDataMap: jobDetail.JobDataMap);
    }

    /// <remarks>
    /// The triggers themselves are needed for the schedule summary, and their states come from a single
    /// query rather than one <see cref="IScheduler.GetTriggerState"/> call per trigger.
    /// </remarks>
    public async ValueTask<List<TriggerHeaderDto>> GetJobTriggers(string schedulerName, JobKeyDto key, CancellationToken cancellationToken = default)
    {
        JobKey jobKey = AsJobKey(key);
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        List<ITrigger> triggers = await scheduler.GetTriggersOfJob(jobKey, cancellationToken).ConfigureAwait(false);

        Dictionary<TriggerKey, TriggerState> states = new(triggers.Count);
        TriggerQuery stateQuery = new() { Job = jobKey };
        while (true)
        {
            PagedResult<TriggerHeader> headers = await scheduler.QueryTriggers(stateQuery, cancellationToken).ConfigureAwait(false);
            foreach (TriggerHeader header in headers.Items)
            {
                states[header.Key] = header.State;
            }

            if (!headers.HasMore)
            {
                break;
            }

            stateQuery = stateQuery with { Skip = stateQuery.Skip + headers.Items.Count };
        }

        List<TriggerHeaderDto> result = new(triggers.Count);
        foreach (ITrigger trigger in triggers)
        {
            result.Add(new TriggerHeaderDto(
                Group: trigger.Key.Group,
                Name: trigger.Key.Name,
                TriggerType: TriggerDisplay.TypeName(trigger),
                ScheduleSummary: TriggerDisplay.ScheduleSummary(trigger),
                State: states.TryGetValue(trigger.Key, out TriggerState state) ? state : null,
                ExecutionGroup: trigger.ExecutionGroup));
        }

        return result;
    }

    public async ValueTask<PagedResult<FireInstanceDto>> GetFireInstances(string schedulerName, DashboardFireInstanceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        FireInstanceQuery storeQuery = new()
        {
            TriggerGroup = BuildGroupMatcher<TriggerKey>(query.GroupContains),
            State = query.State,
            Skip = query.Skip,
            Take = query.Take,
            IncludeTotalCount = true
        };

        PagedResult<FireInstance> page = await scheduler.QueryFireInstances(storeQuery, cancellationToken).ConfigureAwait(false);

        List<FireInstanceDto> items = new(page.Items.Count);
        foreach (FireInstance instance in page.Items)
        {
            items.Add(new FireInstanceDto(
                FireInstanceId: instance.FireInstanceId,
                TriggerKey: new TriggerKeyDto(instance.TriggerKey.Group, instance.TriggerKey.Name),
                JobKey: instance.JobKey is null ? null : new JobKeyDto(instance.JobKey.Group, instance.JobKey.Name),
                SchedulerInstanceId: instance.SchedulerInstanceId,
                State: instance.State,
                FireTimeUtc: instance.FireTimeUtc,
                ScheduledFireTimeUtc: instance.ScheduledFireTimeUtc,
                ExecutionGroup: instance.ExecutionGroup));
        }

        return new PagedResult<FireInstanceDto>(items, page.HasMore, page.TotalCount ?? items.Count);
    }

    public async ValueTask<List<ClusterNodeDto>> GetClusterNodes(string schedulerName, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        List<ClusterNode> nodes = await scheduler.QueryClusterNodes(cancellationToken).ConfigureAwait(false);

        List<ClusterNodeDto> items = new(nodes.Count);
        foreach (ClusterNode node in nodes)
        {
            items.Add(new ClusterNodeDto(
                InstanceId: node.InstanceId,
                LastCheckInUtc: node.LastCheckInUtc,
                CheckInInterval: node.CheckInInterval,
                State: node.State,
                IsCurrentNode: node.IsCurrentNode));
        }

        return items;
    }

    public ValueTask<bool> PauseJob(string schedulerName, JobKeyDto key, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return scheduler.PauseJob(AsJobKey(key), cancellationToken);
    }

    public ValueTask<bool> ResumeJob(string schedulerName, JobKeyDto key, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return scheduler.ResumeJob(AsJobKey(key), cancellationToken);
    }

    public ValueTask TriggerJob(string schedulerName, JobKeyDto key, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return scheduler.TriggerJob(AsJobKey(key), cancellationToken: cancellationToken);
    }

    public ValueTask TriggerJobWithData(string schedulerName, JobKeyDto key, JobDataMap jobDataMap, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobDataMap);
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return scheduler.TriggerJob(AsJobKey(key), jobDataMap, cancellationToken);
    }

    public async ValueTask InterruptJob(string schedulerName, JobKeyDto key, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        _ = await scheduler.Interrupt(AsJobKey(key), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask InterruptFireInstance(string schedulerName, string fireInstanceId, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        _ = await scheduler.InterruptFireInstance(fireInstanceId, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DeleteJob(string schedulerName, JobKeyDto key, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        _ = await scheduler.DeleteJob(AsJobKey(key), cancellationToken).ConfigureAwait(false);
    }

    public ValueTask AddJob(string schedulerName, AddJobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        IJobDetail jobDetail = BuildJobDetail(request.Job);
        AddJobOptions options = new()
        {
            Replace = request.Replace,
            StoreNonDurableWhileAwaitingScheduling = request.StoreNonDurableWhileAwaitingScheduling.GetValueOrDefault(),
        };

        return scheduler.AddJob(jobDetail, options, cancellationToken);
    }

    public async ValueTask<PagedResult<TriggerHeaderDto>> GetTriggers(
        string schedulerName,
        DashboardTriggerQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        TriggerQuery storeQuery = new()
        {
            Group = BuildGroupMatcher<TriggerKey>(query.GroupContains),
            State = query.State,
            Skip = query.Skip,
            Take = query.Take,
            IncludeTotalCount = true
        };

        PagedResult<TriggerHeader> triggers = await scheduler.QueryTriggers(storeQuery, cancellationToken).ConfigureAwait(false);

        List<TriggerHeaderDto> items = new(triggers.Items.Count);
        foreach (TriggerHeader trigger in triggers.Items)
        {
            // The trigger listing does not load the triggers, so it has no schedule to summarise and
            // no kind to name: the store's own trigger-type discriminator is not the display name the
            // associated-triggers table shows, and the two must not disagree.
            items.Add(new TriggerHeaderDto(
                Group: trigger.Key.Group,
                Name: trigger.Key.Name,
                TriggerType: null,
                ScheduleSummary: null,
                State: trigger.State,
                ExecutionGroup: trigger.ExecutionGroup));
        }

        return new PagedResult<TriggerHeaderDto>(items, triggers.HasMore, triggers.TotalCount ?? items.Count);
    }

    public async ValueTask<ITrigger> GetTrigger(string schedulerName, TriggerKeyDto key, CancellationToken cancellationToken = default)
    {
        TriggerKey triggerKey = AsTriggerKey(key);
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        ITrigger? trigger = await scheduler.GetTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
        if (trigger is null)
        {
            throw new KeyNotFoundException($"Trigger '{key.Group}.{key.Name}' was not found in scheduler '{schedulerName}'.");
        }

        return trigger;
    }

    public ValueTask<TriggerState> GetTriggerState(string schedulerName, TriggerKeyDto key, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return scheduler.GetTriggerState(AsTriggerKey(key), cancellationToken);
    }

    public ValueTask<bool> PauseTrigger(string schedulerName, TriggerKeyDto key, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return scheduler.PauseTrigger(AsTriggerKey(key), cancellationToken);
    }

    public ValueTask<bool> ResumeTrigger(string schedulerName, TriggerKeyDto key, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return scheduler.ResumeTrigger(AsTriggerKey(key), cancellationToken);
    }

    public ValueTask<bool> ResetTriggerFromErrorState(string schedulerName, TriggerKeyDto key, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return scheduler.ResetTriggerFromErrorState(AsTriggerKey(key), cancellationToken);
    }

    public ValueTask ScheduleJob(string schedulerName, ScheduleJobRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        if (request.Job is null)
        {
            return ScheduleTriggerOnly(scheduler, request.Trigger, cancellationToken);
        }

        IJobDetail jobDetail = BuildJobDetail(request.Job);
        return ScheduleJobWithTrigger(scheduler, jobDetail, request.Trigger, cancellationToken);
    }

    public async ValueTask UnscheduleJob(string schedulerName, TriggerKeyDto key, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        _ = await scheduler.UnscheduleJob(AsTriggerKey(key), cancellationToken).ConfigureAwait(false);
    }

    public ValueTask RescheduleJob(string schedulerName, TriggerKeyDto key, RescheduleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return RescheduleTrigger(scheduler, AsTriggerKey(key), request.NewTrigger, cancellationToken);
    }

    public async ValueTask<List<string>> GetCalendarNames(string schedulerName, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);

        List<string> result = [];
        CalendarQuery query = new();
        while (true)
        {
            PagedResult<string> names = await scheduler.QueryCalendarNames(query, cancellationToken).ConfigureAwait(false);
            result.AddRange(names.Items);
            if (!names.HasMore)
            {
                return result;
            }

            query = query with { Skip = query.Skip + names.Items.Count };
        }
    }

    public async ValueTask<ICalendar> GetCalendar(string schedulerName, string calendarName, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        ICalendar? calendar = await scheduler.GetCalendar(calendarName, cancellationToken).ConfigureAwait(false);
        if (calendar is null)
        {
            throw new KeyNotFoundException($"Calendar '{calendarName}' was not found in scheduler '{schedulerName}'.");
        }

        return calendar;
    }

    public ValueTask AddCalendar(string schedulerName, AddCalendarRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return scheduler.AddCalendar(
            request.CalendarName,
            request.Calendar,
            new AddCalendarOptions { Replace = request.Replace, UpdateTriggers = request.UpdateTriggers },
            cancellationToken);
    }

    public async ValueTask DeleteCalendar(string schedulerName, string calendarName, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        _ = await scheduler.DeleteCalendar(calendarName, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<PagedResult<DashboardHistoryEntry>?> GetHistory(DashboardHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await historyStore.GetPage(query, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<PagedResult<DashboardMisfireEntry>?> GetMisfires(DashboardMisfireQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return await historyStore.GetMisfires(query, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<int?> CountMisfires(string schedulerName, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        return await historyStore.CountMisfires(schedulerName, since, cancellationToken).ConfigureAwait(false);
    }

    private static GroupMatcher<TKey>? BuildGroupMatcher<TKey>(string? groupFilter) where TKey : Key<TKey>
    {
        return string.IsNullOrWhiteSpace(groupFilter) ? null : GroupMatcher<TKey>.GroupContains(groupFilter);
    }

    private static JobKey AsJobKey(JobKeyDto key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new JobKey(key.Name, key.Group);
    }

    private static TriggerKey AsTriggerKey(TriggerKeyDto key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return new TriggerKey(key.Name, key.Group);
    }

    private static IJobDetail BuildJobDetail(JobDetailDto source)
    {
        if (string.IsNullOrWhiteSpace(source.JobType))
        {
            throw new InvalidOperationException("Job type is required.");
        }

        JobDataMap jobDataMap = source.JobDataMap ?? new JobDataMap();

        // The type name is stored unresolved on purpose: resolving a name that arrived with the request
        // would have this process probe its assemblies for whatever the caller named. The scheduler
        // resolves it through the type load path when the job runs.
        IJobDetail jobDetail = JobBuilder.Create()
            .OfType(source.JobType)
            .WithIdentity(source.Name, source.Group)
            .WithDescription(source.Description)
            .StoreDurably(source.Durable)
            .RequestRecovery(source.RequestsRecovery)
            .DisallowConcurrentExecution(source.ConcurrentExecutionDisallowed)
            .PersistJobDataAfterExecution(source.PersistJobDataAfterExecution)
            .UsingJobData(jobDataMap)
            .Build();
        return jobDetail;
    }

    private static async ValueTask ScheduleTriggerOnly(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken = default)
    {
        _ = await scheduler.ScheduleJob(trigger, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ScheduleJobWithTrigger(IScheduler scheduler, IJobDetail jobDetail, ITrigger trigger, CancellationToken cancellationToken = default)
    {
        _ = await scheduler.ScheduleJob(jobDetail, trigger, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask RescheduleTrigger(IScheduler scheduler, TriggerKey key, ITrigger trigger, CancellationToken cancellationToken = default)
    {
        _ = await scheduler.RescheduleJob(key, trigger, cancellationToken).ConfigureAwait(false);
    }

    private IScheduler GetSchedulerOrThrow(string schedulerName)
    {
        IScheduler? scheduler = schedulerRepository.Lookup(schedulerName);
        if (scheduler is null)
        {
            throw new KeyNotFoundException($"Scheduler '{schedulerName}' was not found.");
        }

        return scheduler;
    }

    private void EnsureWritable()
    {
        if (options.Value.ReadOnly)
        {
            throw new InvalidOperationException("Quartz dashboard is configured as read-only.");
        }
    }

    /// <remarks>
    /// A scheduler that limits nothing and a scheduler that cannot say are different answers, and the
    /// overview shows them differently: the first has every group unlimited, the second has an execution
    /// panel that can only say so. Both used to arrive here as a bare <see langword="null" />.
    /// </remarks>
    public async ValueTask<ExecutionLimitsDto> GetExecutionLimits(string schedulerName, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);

        ExecutionLimits? limits;
        try
        {
            limits = await scheduler.GetExecutionLimits(cancellationToken).ConfigureAwait(false);
        }
        catch (NotSupportedException)
        {
            // Every scheduler Quartz ships implements this, HttpScheduler included — it reads the HTTP
            // API's execution-limits endpoint. An IScheduler of an application's own need not, and
            // reporting its refusal as "nothing is limited" would be a fabricated answer.
            return ExecutionLimitsDto.CannotReport;
        }

        // The configuration spellings, so that the overview can join a firing's execution group to the
        // limit that governs it: "_" for the ungrouped bucket, "*" for the catch-all, else the name.
        Dictionary<string, DashboardExecutionLimit> groups = new(StringComparer.Ordinal);
        if (limits is not null)
        {
            foreach (ExecutionGroupLimit limit in limits.Groups)
            {
                groups[limit.Group.ToConfigurationKey()] = new DashboardExecutionLimit(limit.MaxConcurrent, limit.Scope);
            }
        }

        return new ExecutionLimitsDto(groups, limits?.UsesTriggerGroupWhenUnset ?? false);
    }
}
