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

namespace Quartz.Dashboard.Services;

public interface IQuartzApiClient
{
    Task<List<SchedulerHeaderDto>> GetSchedulers();

    Task<SchedulerDetailDto> GetScheduler(string schedulerName);

    Task StartScheduler(string schedulerName);

    Task StandbyScheduler(string schedulerName);

    Task ShutdownScheduler(string schedulerName);

    Task PauseAll(string schedulerName);

    Task ResumeAll(string schedulerName);

    Task<List<JobKeyDto>> GetJobKeys(string schedulerName, string? groupFilter = null);

    Task<JobDetailDto> GetJob(string schedulerName, string group, string name);

    Task<List<TriggerHeaderDto>> GetJobTriggers(string schedulerName, string group, string name);

    Task<List<CurrentlyExecutingJobDto>> GetCurrentlyExecutingJobs(string schedulerName);

    Task PauseJob(string schedulerName, string group, string name);

    Task ResumeJob(string schedulerName, string group, string name);

    Task TriggerJob(string schedulerName, string group, string name);

    Task TriggerJobWithData(string schedulerName, string group, string name, JsonElement jobDataMap);

    Task<bool> IsJobGroupPaused(string schedulerName, string group);

    Task InterruptJob(string schedulerName, string group, string name);

    Task DeleteJob(string schedulerName, string group, string name);

    Task AddJob(string schedulerName, AddJobRequest request);

    Task<List<TriggerKeyDto>> GetTriggerKeys(string schedulerName, string? groupFilter = null);

    Task<TriggerDetailDto> GetTrigger(string schedulerName, string group, string name);

    Task<string> GetTriggerState(string schedulerName, string group, string name);

    Task PauseTrigger(string schedulerName, string group, string name);

    Task ResumeTrigger(string schedulerName, string group, string name);

    Task ResetTriggerFromErrorState(string schedulerName, string group, string name);

    Task ScheduleJob(string schedulerName, ScheduleJobRequest request);

    Task UnscheduleJob(string schedulerName, string group, string name);

    Task RescheduleJob(string schedulerName, string group, string name, RescheduleRequest request);

    Task<List<string>> GetCalendarNames(string schedulerName);

    Task<CalendarDetailDto> GetCalendar(string schedulerName, string calendarName);

    Task AddCalendar(string schedulerName, AddCalendarRequest request);

    Task DeleteCalendar(string schedulerName, string calendarName);

    Task<JobHistoryPageDto?> GetHistory(JobHistoryQueryDto query);
}

public sealed record JobHistoryQueryDto(
    string SchedulerName,
    int Page = 1,
    int PageSize = 25,
    string? JobFilter = null,
    string? TriggerFilter = null);

public sealed record SchedulerHeaderDto(string SchedulerName, string SchedulerInstanceId, string Status);

public sealed record SchedulerDetailDto(string SchedulerInstanceId, string SchedulerName, string Status);

public sealed record JobKeyDto(string Group, string Name);

public sealed record TriggerKeyDto(string Group, string Name);

public sealed record TriggerHeaderDto(string Group, string Name);

public sealed record JobDetailDto(
    string Name,
    string Group,
    string JobType,
    string? Description,
    bool Durable,
    bool RequestsRecovery,
    bool ConcurrentExecutionDisallowed,
    bool PersistJobDataAfterExecution,
    JsonElement JobDataMap);

public sealed record CurrentlyExecutingJobDto(
    JobKeyDto JobKey,
    TriggerKeyDto TriggerKey,
    DateTimeOffset FireTimeUtc,
    string? FireInstanceId);

public sealed record TriggerDetailDto(JsonElement Value);

public sealed record ScheduleJobRequest(JsonElement Trigger, JobDetailDto? Job);

public sealed record RescheduleRequest(JsonElement NewTrigger);

public sealed record CalendarDetailDto(JsonElement Value);

public sealed record AddCalendarRequest(string CalendarName, JsonElement Calendar, bool Replace, bool UpdateTriggers);

public sealed record AddJobRequest(JobDetailDto Job, bool Replace, bool? StoreNonDurableWhileAwaitingScheduling);

public sealed record JobHistoryPageDto(JsonElement Value);

