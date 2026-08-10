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
            result.Add(new SchedulerHeaderDto(scheduler.SchedulerName, scheduler.SchedulerInstanceId, GetSchedulerStatus(scheduler)));
        }

        return ValueTask.FromResult(result);
    }

    public ValueTask<SchedulerDetailDto> GetScheduler(string schedulerName, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        SchedulerDetailDto result = new(scheduler.SchedulerInstanceId, scheduler.SchedulerName, GetSchedulerStatus(scheduler));
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

    public async ValueTask<JobPageDto> GetJobs(string schedulerName, string? groupFilter, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobQuery query = new()
        {
            Group = BuildGroupMatcher<JobKey>(groupFilter),
            Skip = GetSkip(page, pageSize),
            Take = pageSize,
            IncludeTotalCount = true
        };

        PagedResult<JobHeader> jobs = await scheduler.QueryJobs(query, cancellationToken).ConfigureAwait(false);

        List<JobKeyDto> items = new(jobs.Items.Count);
        foreach (JobHeader job in jobs.Items)
        {
            items.Add(new JobKeyDto(job.Key.Group, job.Key.Name));
        }

        return new JobPageDto(page, pageSize, jobs.TotalCount ?? items.Count, jobs.HasMore, items);
    }

    public async ValueTask<List<JobGroupDto>> GetJobGroups(string schedulerName, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        PagedResult<JobGroup> groups = await scheduler.QueryJobGroups(new JobGroupQuery(), cancellationToken).ConfigureAwait(false);

        List<JobGroupDto> result = new(groups.Items.Count);
        foreach (JobGroup group in groups.Items)
        {
            result.Add(new JobGroupDto(group.Name, group.Paused));
        }

        return result;
    }

    public async ValueTask<JobDetailDto> GetJob(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobKey jobKey = new(name, group);
        IJobDetail? jobDetail = await scheduler.GetJobDetail(jobKey, cancellationToken).ConfigureAwait(false);
        if (jobDetail is null)
        {
            throw new KeyNotFoundException($"Job '{group}.{name}' was not found in scheduler '{schedulerName}'.");
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
    public async ValueTask<List<TriggerHeaderDto>> GetJobTriggers(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobKey jobKey = new(name, group);
        List<ITrigger> triggers = await scheduler.GetTriggersOfJob(jobKey, cancellationToken).ConfigureAwait(false);
        PagedResult<TriggerHeader> headers = await scheduler
            .QueryTriggers(new TriggerQuery { Job = jobKey }, cancellationToken)
            .ConfigureAwait(false);

        Dictionary<TriggerKey, TriggerState> states = new(headers.Items.Count);
        foreach (TriggerHeader header in headers.Items)
        {
            states[header.Key] = header.State;
        }

        List<TriggerHeaderDto> result = new(triggers.Count);
        foreach (ITrigger trigger in triggers)
        {
            result.Add(new TriggerHeaderDto(trigger.Key.Group, trigger.Key.Name, trigger.ExecutionGroup)
            {
                TriggerType = GetTriggerTypeName(trigger),
                ScheduleSummary = DescribeSchedule(trigger),
                State = states.TryGetValue(trigger.Key, out TriggerState state) ? state.ToString() : null
            });
        }

        return result;
    }

    public async ValueTask<List<CurrentlyExecutingJobDto>> GetCurrentlyExecutingJobs(string schedulerName, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        List<IJobExecutionContext> currentlyExecutingJobs = await scheduler.GetCurrentlyExecutingJobs(cancellationToken).ConfigureAwait(false);

        List<CurrentlyExecutingJobDto> result = [];
        foreach (IJobExecutionContext jobExecutionContext in currentlyExecutingJobs)
        {
            result.Add(
                new CurrentlyExecutingJobDto(
                    JobKey: new JobKeyDto(jobExecutionContext.JobDetail.Key.Group, jobExecutionContext.JobDetail.Key.Name),
                    TriggerKey: new TriggerKeyDto(jobExecutionContext.Trigger.Key.Group, jobExecutionContext.Trigger.Key.Name),
                    FireTimeUtc: jobExecutionContext.FireTimeUtc,
                    FireInstanceId: jobExecutionContext.FireInstanceId,
                    ExecutionGroup: jobExecutionContext.Trigger.ExecutionGroup));
        }

        return result;
    }

    public ValueTask PauseJob(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobKey jobKey = new(name, group);
        return scheduler.PauseJob(jobKey, cancellationToken);
    }

    public ValueTask ResumeJob(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobKey jobKey = new(name, group);
        return scheduler.ResumeJob(jobKey, cancellationToken);
    }

    public ValueTask TriggerJob(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobKey jobKey = new(name, group);
        return scheduler.TriggerJob(jobKey, cancellationToken: cancellationToken);
    }

    public ValueTask TriggerJobWithData(string schedulerName, string group, string name, JsonElement jobDataMap, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobKey jobKey = new(name, group);
        JobDataMap triggerDataMap = DeserializeJobDataMap(jobDataMap);
        return scheduler.TriggerJob(jobKey, triggerDataMap, cancellationToken);
    }

    public async ValueTask InterruptJob(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobKey jobKey = new(name, group);
        _ = await scheduler.Interrupt(jobKey, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DeleteJob(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobKey jobKey = new(name, group);
        _ = await scheduler.DeleteJob(jobKey, cancellationToken).ConfigureAwait(false);
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

    public async ValueTask<TriggerPageDto> GetTriggers(
        string schedulerName,
        string? groupFilter,
        TriggerState? state,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        TriggerQuery query = new()
        {
            Group = BuildGroupMatcher<TriggerKey>(groupFilter),
            State = state,
            Skip = GetSkip(page, pageSize),
            Take = pageSize,
            IncludeTotalCount = true
        };

        PagedResult<TriggerHeader> triggers = await scheduler.QueryTriggers(query, cancellationToken).ConfigureAwait(false);

        List<TriggerHeaderDto> items = new(triggers.Items.Count);
        foreach (TriggerHeader trigger in triggers.Items)
        {
            items.Add(new TriggerHeaderDto(trigger.Key.Group, trigger.Key.Name, trigger.ExecutionGroup)
            {
                State = trigger.State.ToString()
            });
        }

        return new TriggerPageDto(page, pageSize, triggers.TotalCount ?? items.Count, triggers.HasMore, items);
    }

    public async ValueTask<TriggerDetailDto> GetTrigger(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        TriggerKey triggerKey = new(name, group);
        ITrigger? trigger = await scheduler.GetTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
        if (trigger is null)
        {
            throw new KeyNotFoundException($"Trigger '{group}.{name}' was not found in scheduler '{schedulerName}'.");
        }

        return new TriggerDetailDto(SerializeTrigger(trigger));
    }

    public async ValueTask<string> GetTriggerState(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        TriggerKey triggerKey = new(name, group);
        TriggerState triggerState = await scheduler.GetTriggerState(triggerKey, cancellationToken).ConfigureAwait(false);
        return triggerState.ToString();
    }

    public ValueTask PauseTrigger(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        TriggerKey triggerKey = new(name, group);
        return scheduler.PauseTrigger(triggerKey, cancellationToken);
    }

    public ValueTask ResumeTrigger(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        TriggerKey triggerKey = new(name, group);
        return scheduler.ResumeTrigger(triggerKey, cancellationToken);
    }

    public ValueTask ResetTriggerFromErrorState(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        TriggerKey triggerKey = new(name, group);
        return scheduler.ResetTriggerFromErrorState(triggerKey, cancellationToken);
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

    public async ValueTask UnscheduleJob(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        TriggerKey triggerKey = new(name, group);
        _ = await scheduler.UnscheduleJob(triggerKey, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask RescheduleJob(string schedulerName, string group, string name, RescheduleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        TriggerKey triggerKey = new(name, group);
        ITrigger newTrigger = DeserializeTrigger(request.NewTrigger);
        return RescheduleTrigger(scheduler, triggerKey, newTrigger, cancellationToken);
    }

    public async ValueTask<List<string>> GetCalendarNames(string schedulerName, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        PagedResult<string> names = await scheduler.QueryCalendarNames(new CalendarQuery(), cancellationToken).ConfigureAwait(false);
        return [.. names.Items];
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

    public async ValueTask<JobHistoryPageDto?> GetHistory(JobHistoryQueryDto query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        DashboardHistoryPage historyPage = await historyStore
            .GetPage(query.SchedulerName, query.Page, query.PageSize, query.JobFilter, query.TriggerFilter, cancellationToken)
            .ConfigureAwait(false);
        object payload = new
        {
            page = historyPage.Page,
            pageSize = historyPage.PageSize,
            totalCount = historyPage.TotalCount,
            entries = historyPage.Entries.Select(x => new
            {
                schedulerName = x.SchedulerName,
                jobGroup = x.JobGroup,
                jobName = x.JobName,
                triggerGroup = x.TriggerGroup,
                triggerName = x.TriggerName,
                firedAtUtc = x.FiredAtUtc,
                durationMs = x.DurationMs,
                succeeded = x.Succeeded,
                exceptionMessage = x.ExceptionMessage
            }).ToList()
        };

        return new JobHistoryPageDto(JsonSerializer.SerializeToElement(payload, serializerOptions));
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

    private static int GetSkip(int page, int pageSize)
    {
        if (page <= 1 || pageSize <= 0)
        {
            return 0;
        }

        long skip = (long) (page - 1) * pageSize;
        return skip > int.MaxValue ? int.MaxValue : (int) skip;
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

    private static string GetSchedulerStatus(IScheduler scheduler)
    {
        if (scheduler.IsShutdown)
        {
            return "Shutdown";
        }

        if (scheduler.InStandbyMode)
        {
            return "Standby";
        }

        if (scheduler.IsStarted)
        {
            return "Started";
        }

        return "Unknown";
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
