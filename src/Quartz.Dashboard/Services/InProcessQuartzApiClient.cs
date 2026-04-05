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

using Quartz.Impl.Matchers;
using Quartz.Spi;

namespace Quartz.Dashboard.Services;

public sealed class InProcessQuartzApiClient : IQuartzApiClient
{
    private static readonly JsonSerializerOptions serializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private static readonly JsonSerializerOptions deserializerOptions = CreateDeserializerOptions();

    private readonly ISchedulerRepository schedulerRepository;
    private readonly IOptions<QuartzDashboardOptions> options;
    private readonly IDashboardHistoryStore historyStore;

    public InProcessQuartzApiClient(
        ISchedulerRepository schedulerRepository,
        IOptions<QuartzDashboardOptions> options,
        IDashboardHistoryStore historyStore)
    {
        this.schedulerRepository = schedulerRepository;
        this.options = options;
        this.historyStore = historyStore;
    }

    public ValueTask<List<SchedulerHeaderDto>> GetSchedulers()
    {
        List<IScheduler> schedulers = schedulerRepository.LookupAll();
        List<SchedulerHeaderDto> result = [];
        foreach (IScheduler scheduler in schedulers)
        {
            result.Add(new SchedulerHeaderDto(scheduler.SchedulerName, scheduler.SchedulerInstanceId, GetSchedulerStatus(scheduler)));
        }

        return ValueTask.FromResult(result);
    }

    public ValueTask<SchedulerDetailDto> GetScheduler(string schedulerName)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        SchedulerDetailDto result = new(scheduler.SchedulerInstanceId, scheduler.SchedulerName, GetSchedulerStatus(scheduler));
        return ValueTask.FromResult(result);
    }

    public ValueTask StartScheduler(string schedulerName)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return scheduler.Start();
    }

    public ValueTask StandbyScheduler(string schedulerName)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return scheduler.Standby();
    }

    public ValueTask ShutdownScheduler(string schedulerName)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return scheduler.Shutdown();
    }

    public ValueTask PauseAll(string schedulerName)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return scheduler.PauseAll();
    }

    public ValueTask ResumeAll(string schedulerName)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return scheduler.ResumeAll();
    }

    public async ValueTask<List<JobKeyDto>> GetJobKeys(string schedulerName, string? groupFilter = null)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        GroupMatcher<JobKey> matcher = groupFilter is null ? GroupMatcher<JobKey>.AnyGroup() : GroupMatcher<JobKey>.GroupContains(groupFilter);
        List<JobKey> jobKeys = await scheduler.GetJobKeys(matcher).ConfigureAwait(false);

        List<JobKeyDto> result = [];
        foreach (JobKey jobKey in jobKeys)
        {
            result.Add(new JobKeyDto(jobKey.Group, jobKey.Name));
        }

        return result;
    }

    public async ValueTask<JobDetailDto> GetJob(string schedulerName, string group, string name)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobKey jobKey = new(name, group);
        IJobDetail? jobDetail = await scheduler.GetJobDetail(jobKey).ConfigureAwait(false);
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

    public async ValueTask<List<TriggerHeaderDto>> GetJobTriggers(string schedulerName, string group, string name)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobKey jobKey = new(name, group);
        List<ITrigger> triggers = await scheduler.GetTriggersOfJob(jobKey).ConfigureAwait(false);

        List<TriggerHeaderDto> result = [];
        foreach (ITrigger trigger in triggers)
        {
            result.Add(new TriggerHeaderDto(trigger.Key.Group, trigger.Key.Name, trigger.ExecutionGroup));
        }

        return result;
    }

    public async ValueTask<List<CurrentlyExecutingJobDto>> GetCurrentlyExecutingJobs(string schedulerName)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        List<IJobExecutionContext> currentlyExecutingJobs = await scheduler.GetCurrentlyExecutingJobs().ConfigureAwait(false);

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

    public ValueTask PauseJob(string schedulerName, string group, string name)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobKey jobKey = new(name, group);
        return scheduler.PauseJob(jobKey);
    }

    public ValueTask ResumeJob(string schedulerName, string group, string name)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobKey jobKey = new(name, group);
        return scheduler.ResumeJob(jobKey);
    }

    public ValueTask TriggerJob(string schedulerName, string group, string name)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobKey jobKey = new(name, group);
        return scheduler.TriggerJob(jobKey);
    }

    public ValueTask TriggerJobWithData(string schedulerName, string group, string name, JsonElement jobDataMap)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobKey jobKey = new(name, group);
        JobDataMap triggerDataMap = DeserializeJobDataMap(jobDataMap);
        return scheduler.TriggerJob(jobKey, triggerDataMap);
    }

    public ValueTask<bool> IsJobGroupPaused(string schedulerName, string group)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return scheduler.IsJobGroupPaused(group);
    }

    public async ValueTask InterruptJob(string schedulerName, string group, string name)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobKey jobKey = new(name, group);
        _ = await scheduler.Interrupt(jobKey).ConfigureAwait(false);
    }

    public async ValueTask DeleteJob(string schedulerName, string group, string name)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobKey jobKey = new(name, group);
        _ = await scheduler.DeleteJob(jobKey).ConfigureAwait(false);
    }

    public ValueTask AddJob(string schedulerName, AddJobRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        IJobDetail jobDetail = BuildJobDetail(request.Job);
        if (request.StoreNonDurableWhileAwaitingScheduling.HasValue)
        {
            return scheduler.AddJob(jobDetail, request.Replace, request.StoreNonDurableWhileAwaitingScheduling.Value);
        }

        return scheduler.AddJob(jobDetail, request.Replace);
    }

    public async ValueTask<List<TriggerHeaderDto>> GetTriggerKeys(string schedulerName, string? groupFilter = null)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        GroupMatcher<TriggerKey> matcher = groupFilter is null ? GroupMatcher<TriggerKey>.AnyGroup() : GroupMatcher<TriggerKey>.GroupContains(groupFilter);
        List<TriggerKey> triggerKeys = await scheduler.GetTriggerKeys(matcher).ConfigureAwait(false);

        List<TriggerHeaderDto> result = [];
        foreach (TriggerKey triggerKey in triggerKeys)
        {
            ITrigger? trigger = await scheduler.GetTrigger(triggerKey).ConfigureAwait(false);
            result.Add(new TriggerHeaderDto(triggerKey.Group, triggerKey.Name, trigger?.ExecutionGroup));
        }

        return result;
    }

    public async ValueTask<TriggerDetailDto> GetTrigger(string schedulerName, string group, string name)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        TriggerKey triggerKey = new(name, group);
        ITrigger? trigger = await scheduler.GetTrigger(triggerKey).ConfigureAwait(false);
        if (trigger is null)
        {
            throw new KeyNotFoundException($"Trigger '{group}.{name}' was not found in scheduler '{schedulerName}'.");
        }

        JsonElement triggerJson = JsonSerializer.SerializeToElement<object>(trigger, serializerOptions);
        return new TriggerDetailDto(triggerJson);
    }

    public async ValueTask<string> GetTriggerState(string schedulerName, string group, string name)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        TriggerKey triggerKey = new(name, group);
        TriggerState triggerState = await scheduler.GetTriggerState(triggerKey).ConfigureAwait(false);
        return triggerState.ToString();
    }

    public ValueTask PauseTrigger(string schedulerName, string group, string name)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        TriggerKey triggerKey = new(name, group);
        return scheduler.PauseTrigger(triggerKey);
    }

    public ValueTask ResumeTrigger(string schedulerName, string group, string name)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        TriggerKey triggerKey = new(name, group);
        return scheduler.ResumeTrigger(triggerKey);
    }

    public ValueTask ResetTriggerFromErrorState(string schedulerName, string group, string name)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        TriggerKey triggerKey = new(name, group);
        return scheduler.ResetTriggerFromErrorState(triggerKey);
    }

    public ValueTask ScheduleJob(string schedulerName, ScheduleJobRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        ITrigger trigger = DeserializeTrigger(request.Trigger);
        if (request.Job is null)
        {
            return ScheduleTriggerOnlyAsync(scheduler, trigger);
        }

        IJobDetail jobDetail = BuildJobDetail(request.Job);
        return ScheduleJobWithTriggerAsync(scheduler, jobDetail, trigger);
    }

    public async ValueTask UnscheduleJob(string schedulerName, string group, string name)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        TriggerKey triggerKey = new(name, group);
        _ = await scheduler.UnscheduleJob(triggerKey).ConfigureAwait(false);
    }

    public ValueTask RescheduleJob(string schedulerName, string group, string name, RescheduleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        TriggerKey triggerKey = new(name, group);
        ITrigger newTrigger = DeserializeTrigger(request.NewTrigger);
        return RescheduleTriggerAsync(scheduler, triggerKey, newTrigger);
    }

    public async ValueTask<List<string>> GetCalendarNames(string schedulerName)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        List<string> names = await scheduler.GetCalendarNames().ConfigureAwait(false);
        return names;
    }

    public async ValueTask<CalendarDetailDto> GetCalendar(string schedulerName, string calendarName)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        ICalendar? calendar = await scheduler.GetCalendar(calendarName).ConfigureAwait(false);
        if (calendar is null)
        {
            throw new KeyNotFoundException($"Calendar '{calendarName}' was not found in scheduler '{schedulerName}'.");
        }

        JsonElement calendarJson = JsonSerializer.SerializeToElement<object>(calendar, serializerOptions);
        return new CalendarDetailDto(calendarJson);
    }

    public ValueTask AddCalendar(string schedulerName, AddCalendarRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        ICalendar calendar = DeserializeCalendar(request.Calendar);
        return scheduler.AddCalendar(request.CalendarName, calendar, request.Replace, request.UpdateTriggers);
    }

    public async ValueTask DeleteCalendar(string schedulerName, string calendarName)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        _ = await scheduler.DeleteCalendar(calendarName).ConfigureAwait(false);
    }

    public ValueTask<JobHistoryPageDto?> GetHistory(JobHistoryQueryDto query)
    {
        ArgumentNullException.ThrowIfNull(query);

        DashboardHistoryPage historyPage = historyStore.GetPage(query.SchedulerName, query.Page, query.PageSize, query.JobFilter, query.TriggerFilter);
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

        return ValueTask.FromResult<JobHistoryPageDto?>(new JobHistoryPageDto(JsonSerializer.SerializeToElement(payload, serializerOptions)));
    }

    private static JsonSerializerOptions CreateDeserializerOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web);
        options.AddQuartzConverters(newtonsoftCompatibilityMode: false);
        return options;
    }

    private static JobDataMap DeserializeJobDataMap(JsonElement element)
    {
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
        {
            return new JobDataMap();
        }

        JobDataMap? dataMap = element.Deserialize<JobDataMap>(deserializerOptions);
        return dataMap ?? new JobDataMap();
    }

    private static ITrigger DeserializeTrigger(JsonElement element)
    {
        ITrigger? trigger = element.Deserialize<ITrigger>(deserializerOptions);
        if (trigger is null)
        {
            throw new InvalidOperationException("Trigger payload cannot be parsed.");
        }

        return trigger;
    }

    private static ICalendar DeserializeCalendar(JsonElement element)
    {
        ICalendar? calendar = element.Deserialize<ICalendar>(deserializerOptions);
        if (calendar is null)
        {
            throw new InvalidOperationException("Calendar payload cannot be parsed.");
        }

        return calendar;
    }

    private static IJobDetail BuildJobDetail(JobDetailDto source)
    {
        Type? jobType = Type.GetType(source.JobType, throwOnError: false);
        if (jobType is null)
        {
            throw new InvalidOperationException("Unknown job type: " + source.JobType);
        }

        JobDataMap jobDataMap = DeserializeJobDataMap(source.JobDataMap);
        IJobDetail jobDetail = JobBuilder.Create(jobType)
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

    private static async ValueTask ScheduleTriggerOnlyAsync(IScheduler scheduler, ITrigger trigger)
    {
        _ = await scheduler.ScheduleJob(trigger).ConfigureAwait(false);
    }

    private static async ValueTask ScheduleJobWithTriggerAsync(IScheduler scheduler, IJobDetail jobDetail, ITrigger trigger)
    {
        _ = await scheduler.ScheduleJob(jobDetail, trigger).ConfigureAwait(false);
    }

    private static async ValueTask RescheduleTriggerAsync(IScheduler scheduler, TriggerKey key, ITrigger trigger)
    {
        _ = await scheduler.RescheduleJob(key, trigger).ConfigureAwait(false);
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

    public async ValueTask<ExecutionLimitsDto?> GetExecutionLimits(string schedulerName)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        try
        {
            ExecutionLimits? limits = await scheduler.GetExecutionLimits().ConfigureAwait(false);
            if (limits is null || limits.Count == 0)
            {
                return null;
            }

            Dictionary<string, int?> dict = new();
            foreach (KeyValuePair<string, int?> kvp in limits)
            {
                // Use display-friendly keys
                string key = kvp.Key == ExecutionLimits.DefaultGroupKey ? "(default)" : kvp.Key;
                dict[key] = kvp.Value;
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
