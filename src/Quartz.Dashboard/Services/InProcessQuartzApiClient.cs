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

using System.Text.Json;

using Microsoft.Extensions.Options;

using Quartz.Serialization.SystemTextJson;
using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Dashboard.Services;

internal sealed class InProcessQuartzApiClient : IQuartzApiClient
{
    private static readonly JsonSerializerOptions serializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private readonly JsonSerializerOptions deserializerOptions;

    private readonly ISchedulerRepository schedulerRepository;
    private readonly IOptions<QuartzDashboardOptions> options;
    private readonly IDashboardHistoryStore historyStore;

    /// <remarks>
    /// The dashboard shows every scheduler in the container through one client, so it reads the
    /// container's <see cref="SystemTextJsonSerializerRegistry"/> rather than any single scheduler's — a
    /// custom trigger or calendar serializer registered there is what makes a custom type render as
    /// something other than a reflected blob.
    /// </remarks>
    /// <remarks>
    /// <paramref name="quartzSerializerOptions"/> carries the Quartz converters and is built once from the
    /// container's registry rather than per scope: this client is scoped, and System.Text.Json caches type
    /// metadata per options instance.
    /// </remarks>
    public InProcessQuartzApiClient(
        ISchedulerRepository schedulerRepository,
        IOptions<QuartzDashboardOptions> options,
        IDashboardHistoryStore historyStore,
        JsonSerializerOptions quartzSerializerOptions)
    {
        ArgumentNullException.ThrowIfNull(quartzSerializerOptions);

        this.schedulerRepository = schedulerRepository;
        this.options = options;
        this.historyStore = historyStore;
        deserializerOptions = quartzSerializerOptions;
    }

    public ValueTask<List<SchedulerHeaderDto>> GetSchedulers(CancellationToken cancellationToken = default)
    {
        List<IScheduler> schedulers = schedulerRepository.LookupAll();
        List<SchedulerHeaderDto> result = [];
        foreach (IScheduler scheduler in schedulers)
        {
            result.Add(new SchedulerHeaderDto(scheduler.SchedulerName, scheduler.SchedulerInstanceId, scheduler.GetStatus()));
        }

        return ValueTask.FromResult(result);
    }

    public ValueTask<SchedulerDetailDto> GetScheduler(string schedulerName, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        SchedulerDetailDto result = new(scheduler.SchedulerInstanceId, scheduler.SchedulerName, scheduler.GetStatus());
        return ValueTask.FromResult(result);
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

    public async ValueTask<List<JobGroupDto>> GetJobGroups(string schedulerName, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);

        List<JobGroupDto> result = [];
        JobGroupQuery query = new();
        while (true)
        {
            PagedResult<JobGroup> groups = await scheduler.QueryJobGroups(query, cancellationToken).ConfigureAwait(false);
            foreach (JobGroup group in groups.Items)
            {
                result.Add(new JobGroupDto(group.Name, group.Paused));
            }

            if (!groups.HasMore)
            {
                return result;
            }

            query = query with { Skip = query.Skip + groups.Items.Count };
        }
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

        JsonElement jobDataMap = JsonSerializer.SerializeToElement(jobDetail.JobDataMap, serializerOptions);
        return new JobDetailDto(
            Name: jobDetail.Key.Name,
            Group: jobDetail.Key.Group,
            JobType: jobDetail.JobType.FullName,
            Description: jobDetail.Description,
            Durable: jobDetail.Durable,
            RequestsRecovery: jobDetail.RequestsRecovery,
            ConcurrentExecutionDisallowed: jobDetail.ConcurrentExecutionDisallowed,
            PersistJobDataAfterExecution: jobDetail.PersistJobDataAfterExecution,
            JobDataMap: jobDataMap);
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
            result.Add(new TriggerHeaderDto(trigger.Key.Group, trigger.Key.Name, trigger.ExecutionGroup)
            {
                TriggerType = GetTriggerTypeName(trigger),
                ScheduleSummary = DescribeSchedule(trigger),
                State = states.TryGetValue(trigger.Key, out TriggerState state) ? state : null
            });
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

    public ValueTask TriggerJobWithData(string schedulerName, JobKeyDto key, JsonElement jobDataMap, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobDataMap triggerDataMap = DeserializeJobDataMap(jobDataMap);
        return scheduler.TriggerJob(AsJobKey(key), triggerDataMap, cancellationToken);
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
            items.Add(new TriggerHeaderDto(trigger.Key.Group, trigger.Key.Name, trigger.ExecutionGroup)
            {
                State = trigger.State
            });
        }

        return new PagedResult<TriggerHeaderDto>(items, triggers.HasMore, triggers.TotalCount ?? items.Count);
    }

    public async ValueTask<TriggerDetailDto> GetTrigger(string schedulerName, TriggerKeyDto key, CancellationToken cancellationToken = default)
    {
        TriggerKey triggerKey = AsTriggerKey(key);
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        ITrigger? trigger = await scheduler.GetTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
        if (trigger is null)
        {
            throw new KeyNotFoundException($"Trigger '{key.Group}.{key.Name}' was not found in scheduler '{schedulerName}'.");
        }

        return new TriggerDetailDto(SerializeTrigger(trigger));
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
        ITrigger trigger = DeserializeTrigger(request.Trigger);
        if (request.Job is null)
        {
            return ScheduleTriggerOnly(scheduler, trigger, cancellationToken);
        }

        IJobDetail jobDetail = BuildJobDetail(request.Job);
        return ScheduleJobWithTrigger(scheduler, jobDetail, trigger, cancellationToken);
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
        ITrigger newTrigger = DeserializeTrigger(request.NewTrigger);
        return RescheduleTrigger(scheduler, AsTriggerKey(key), newTrigger, cancellationToken);
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

    public async ValueTask<CalendarDetailDto> GetCalendar(string schedulerName, string calendarName, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        ICalendar? calendar = await scheduler.GetCalendar(calendarName, cancellationToken).ConfigureAwait(false);
        if (calendar is null)
        {
            throw new KeyNotFoundException($"Calendar '{calendarName}' was not found in scheduler '{schedulerName}'.");
        }

        JsonElement calendarJson = JsonSerializer.SerializeToElement<object>(calendar, serializerOptions);
        return new CalendarDetailDto(calendarJson);
    }

    public ValueTask AddCalendar(string schedulerName, AddCalendarRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        ICalendar calendar = DeserializeCalendar(request.Calendar);
        return scheduler.AddCalendar(
            request.CalendarName,
            calendar,
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

    private JsonElement SerializeTrigger(ITrigger trigger)
    {
        try
        {
            // Use the canonical Quartz converters (same options used for deserialization) so the JSON
            // exposes TriggerType, schedule fields, JobDataMap and fire times under the property names
            // the dashboard UI reads. Plain reflection omits most of these.
            return JsonSerializer.SerializeToElement<object>(trigger, deserializerOptions);
        }
        catch (JsonSerializationException)
        {
            // Custom trigger types aren't handled by the converter; fall back to a best-effort
            // reflection serialization so the detail page still renders.
            return JsonSerializer.SerializeToElement<object>(trigger, serializerOptions);
        }
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

    private static string GetTriggerTypeName(ITrigger trigger)
    {
        return trigger switch
        {
            ICronTrigger => "Cron",
            ISimpleTrigger => "Simple",
            ICalendarIntervalTrigger => "Calendar interval",
            IDailyTimeIntervalTrigger => "Daily time interval",
            _ => trigger.GetType().Name
        };
    }

    private static string? DescribeSchedule(ITrigger trigger)
    {
        switch (trigger)
        {
            case ICronTrigger cron:
                return cron.CronExpressionString;
            case ISimpleTrigger simple:
                string summary = "Every " + simple.RepeatInterval;
                return summary + (simple.RepeatCount < 0 ? ", repeat forever" : ", " + simple.RepeatCount + " time(s)");
            default:
                return null;
        }
    }

    private JobDataMap DeserializeJobDataMap(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return new JobDataMap();
        }

        JobDataMap? dataMap = element.Deserialize<JobDataMap>(deserializerOptions);
        return dataMap ?? new JobDataMap();
    }

    private ITrigger DeserializeTrigger(JsonElement element)
    {
        ITrigger? trigger = element.Deserialize<ITrigger>(deserializerOptions);
        if (trigger is null)
        {
            throw new InvalidOperationException("Trigger payload cannot be parsed.");
        }

        return trigger;
    }

    private ICalendar DeserializeCalendar(JsonElement element)
    {
        ICalendar? calendar = element.Deserialize<ICalendar>(deserializerOptions);
        if (calendar is null)
        {
            throw new InvalidOperationException("Calendar payload cannot be parsed.");
        }

        return calendar;
    }

    private IJobDetail BuildJobDetail(JobDetailDto source)
    {
        if (string.IsNullOrWhiteSpace(source.JobType))
        {
            throw new InvalidOperationException("Job type is required.");
        }

        JobDataMap jobDataMap = DeserializeJobDataMap(source.JobDataMap);

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

    public async ValueTask<ExecutionLimitsDto?> GetExecutionLimits(string schedulerName, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        try
        {
            ExecutionLimits? limits = await scheduler.GetExecutionLimits(cancellationToken).ConfigureAwait(false);
            if (limits is null || limits.IsEmpty)
            {
                return null;
            }

            Dictionary<string, int?> dict = new();
            foreach (ExecutionGroupLimit limit in limits.Groups)
            {
                // Use display-friendly keys
                string key = limit.Scope.IsDefault ? "(default)" : limit.Scope.ToConfigurationKey();
                dict[key] = limit.MaxConcurrent;
            }

            return new ExecutionLimitsDto(dict);
        }
        catch (NotSupportedException)
        {
            // Scheduler implementation doesn't support execution limits (e.g. HTTP proxy)
            return null;
        }
    }
}
