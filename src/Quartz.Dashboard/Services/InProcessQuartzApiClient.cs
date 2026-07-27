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
using Quartz.Serialization.Json;
using Quartz.Extensibility;

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
        return scheduler.Shutdown(cancellationToken);
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

    public async ValueTask<List<JobKeyDto>> GetJobKeys(string schedulerName, string? groupFilter = null, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        GroupMatcher<JobKey> matcher = groupFilter is null ? GroupMatcher<JobKey>.AnyGroup() : GroupMatcher<JobKey>.GroupContains(groupFilter);
        List<JobKey> jobKeys = await scheduler.GetJobKeys(matcher, cancellationToken).ConfigureAwait(false);

        List<JobKeyDto> result = [];
        foreach (JobKey jobKey in jobKeys)
        {
            result.Add(new JobKeyDto(jobKey.Group, jobKey.Name));
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

    public async ValueTask<List<TriggerHeaderDto>> GetJobTriggers(string schedulerName, string group, string name, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobKey jobKey = new(name, group);
        List<ITrigger> triggers = await scheduler.GetTriggersOfJob(jobKey, cancellationToken).ConfigureAwait(false);

        List<TriggerHeaderDto> result = [];
        foreach (ITrigger trigger in triggers)
        {
            result.Add(new TriggerHeaderDto(trigger.Key.Group, trigger.Key.Name, trigger.ExecutionGroup)
            {
                TriggerType = GetTriggerTypeName(trigger),
                ScheduleSummary = DescribeSchedule(trigger)
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
        return scheduler.TriggerJob(jobKey, cancellationToken);
    }

    public ValueTask TriggerJobWithData(string schedulerName, string group, string name, JsonElement jobDataMap, CancellationToken cancellationToken = default)
    {
        EnsureWritable();
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        JobKey jobKey = new(name, group);
        JobDataMap triggerDataMap = DeserializeJobDataMap(jobDataMap);
        return scheduler.TriggerJob(jobKey, triggerDataMap, cancellationToken);
    }

    public ValueTask<bool> IsJobGroupPaused(string schedulerName, string group, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        return scheduler.IsJobGroupPaused(group, cancellationToken);
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
        if (request.StoreNonDurableWhileAwaitingScheduling.HasValue)
        {
            return scheduler.AddJob(jobDetail, request.Replace, request.StoreNonDurableWhileAwaitingScheduling.Value, cancellationToken);
        }

        return scheduler.AddJob(jobDetail, request.Replace, cancellationToken);
    }

    public async ValueTask<List<TriggerHeaderDto>> GetTriggerKeys(string schedulerName, string? groupFilter = null, CancellationToken cancellationToken = default)
    {
        IScheduler scheduler = GetSchedulerOrThrow(schedulerName);
        GroupMatcher<TriggerKey> matcher = groupFilter is null ? GroupMatcher<TriggerKey>.AnyGroup() : GroupMatcher<TriggerKey>.GroupContains(groupFilter);
        List<TriggerKey> triggerKeys = await scheduler.GetTriggerKeys(matcher, cancellationToken).ConfigureAwait(false);

        List<TriggerHeaderDto> result = [];
        foreach (TriggerKey triggerKey in triggerKeys)
        {
            ITrigger? trigger = await scheduler.GetTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
            result.Add(new TriggerHeaderDto(triggerKey.Group, triggerKey.Name, trigger?.ExecutionGroup));
        }

        return result;
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
        List<string> names = await scheduler.GetCalendarNames(cancellationToken).ConfigureAwait(false);
        return names;
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
        return scheduler.AddCalendar(request.CalendarName, calendar, request.Replace, request.UpdateTriggers, cancellationToken);
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
