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
    ValueTask<List<SchedulerHeaderDto>> GetSchedulers();

    ValueTask<SchedulerDetailDto> GetScheduler(string schedulerName);

    ValueTask StartScheduler(string schedulerName);

    ValueTask StandbyScheduler(string schedulerName);

    ValueTask ShutdownScheduler(string schedulerName);

    ValueTask PauseAll(string schedulerName);

    ValueTask ResumeAll(string schedulerName);

    ValueTask<List<JobKeyDto>> GetJobKeys(string schedulerName, string? groupFilter = null);

    ValueTask<JobDetailDto> GetJob(string schedulerName, string group, string name);

    ValueTask<List<TriggerHeaderDto>> GetJobTriggers(string schedulerName, string group, string name);

    ValueTask<List<CurrentlyExecutingJobDto>> GetCurrentlyExecutingJobs(string schedulerName);

    ValueTask PauseJob(string schedulerName, string group, string name);

    ValueTask ResumeJob(string schedulerName, string group, string name);

    ValueTask TriggerJob(string schedulerName, string group, string name);

    ValueTask TriggerJobWithData(string schedulerName, string group, string name, JsonElement jobDataMap);

    ValueTask<bool> IsJobGroupPaused(string schedulerName, string group);

    ValueTask InterruptJob(string schedulerName, string group, string name);

    ValueTask DeleteJob(string schedulerName, string group, string name);

    ValueTask AddJob(string schedulerName, AddJobRequest request);

    ValueTask<List<TriggerHeaderDto>> GetTriggerKeys(string schedulerName, string? groupFilter = null);

    ValueTask<TriggerDetailDto> GetTrigger(string schedulerName, string group, string name);

    ValueTask<string> GetTriggerState(string schedulerName, string group, string name);

    ValueTask PauseTrigger(string schedulerName, string group, string name);

    ValueTask ResumeTrigger(string schedulerName, string group, string name);

    ValueTask ResetTriggerFromErrorState(string schedulerName, string group, string name);

    ValueTask ScheduleJob(string schedulerName, ScheduleJobRequest request);

    ValueTask UnscheduleJob(string schedulerName, string group, string name);

    ValueTask RescheduleJob(string schedulerName, string group, string name, RescheduleRequest request);

    ValueTask<List<string>> GetCalendarNames(string schedulerName);

    ValueTask<CalendarDetailDto> GetCalendar(string schedulerName, string calendarName);

    ValueTask AddCalendar(string schedulerName, AddCalendarRequest request);

    ValueTask DeleteCalendar(string schedulerName, string calendarName);

    ValueTask<JobHistoryPageDto?> GetHistory(JobHistoryQueryDto query);

    ValueTask<ExecutionLimitsDto?> GetExecutionLimits(string schedulerName);
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

public sealed record TriggerHeaderDto(string Group, string Name, string? ExecutionGroup = null);

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
    string? FireInstanceId,
    string? ExecutionGroup = null);

public sealed record TriggerDetailDto(JsonElement Value);

public sealed record ScheduleJobRequest(JsonElement Trigger, JobDetailDto? Job);

public sealed record RescheduleRequest(JsonElement NewTrigger);

public sealed record CalendarDetailDto(JsonElement Value);

public sealed record AddCalendarRequest(string CalendarName, JsonElement Calendar, bool Replace, bool UpdateTriggers);

public sealed record AddJobRequest(JobDetailDto Job, bool Replace, bool? StoreNonDurableWhileAwaitingScheduling);

public sealed record JobHistoryPageDto(JsonElement Value);

public sealed record ExecutionLimitsDto(IReadOnlyDictionary<string, int?> Limits);
