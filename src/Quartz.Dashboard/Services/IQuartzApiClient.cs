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
    ValueTask<List<SchedulerHeaderDto>> GetSchedulers(CancellationToken cancellationToken = default);

    ValueTask<SchedulerDetailDto> GetScheduler(string schedulerName, CancellationToken cancellationToken = default);

    ValueTask StartScheduler(string schedulerName, CancellationToken cancellationToken = default);

    ValueTask StandbyScheduler(string schedulerName, CancellationToken cancellationToken = default);

    ValueTask ShutdownScheduler(string schedulerName, CancellationToken cancellationToken = default);

    ValueTask PauseAll(string schedulerName, CancellationToken cancellationToken = default);

    ValueTask ResumeAll(string schedulerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of jobs, ordered by group and then name. <paramref name="groupFilter"/> matches
    /// groups that contain it, <paramref name="page"/> is 1-based, and a <paramref name="pageSize"/> of
    /// zero returns no items but still counts them.
    /// </summary>
    ValueTask<JobPageDto> GetJobs(string schedulerName, string? groupFilter, int page, int pageSize, CancellationToken cancellationToken = default);

    ValueTask<List<JobGroupDto>> GetJobGroups(string schedulerName, CancellationToken cancellationToken = default);

    ValueTask<JobDetailDto> GetJob(string schedulerName, string group, string name, CancellationToken cancellationToken = default);

    ValueTask<List<TriggerHeaderDto>> GetJobTriggers(string schedulerName, string group, string name, CancellationToken cancellationToken = default);

    ValueTask<List<CurrentlyExecutingJobDto>> GetCurrentlyExecutingJobs(string schedulerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the fire instances currently executing across every node of the cluster, unlike
    /// <see cref="GetCurrentlyExecutingJobs" /> which only sees the node it is called on.
    /// </summary>
    ValueTask<List<ExecutingFireInstanceDto>> GetExecutingFireInstances(string schedulerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses the job. Returns <see langword="true" /> when the job existed and was paused,
    /// <see langword="false" /> when there was nothing to pause.
    /// </summary>
    ValueTask<bool> PauseJob(string schedulerName, string group, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes the job. Returns <see langword="true" /> when the job existed and was resumed,
    /// <see langword="false" /> when there was nothing to resume.
    /// </summary>
    ValueTask<bool> ResumeJob(string schedulerName, string group, string name, CancellationToken cancellationToken = default);

    ValueTask TriggerJob(string schedulerName, string group, string name, CancellationToken cancellationToken = default);

    ValueTask TriggerJobWithData(string schedulerName, string group, string name, JsonElement jobDataMap, CancellationToken cancellationToken = default);

    ValueTask InterruptJob(string schedulerName, string group, string name, CancellationToken cancellationToken = default);

    ValueTask DeleteJob(string schedulerName, string group, string name, CancellationToken cancellationToken = default);

    ValueTask AddJob(string schedulerName, AddJobRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of triggers, ordered by group and then name, each carrying its state and
    /// execution group. <paramref name="groupFilter"/> matches groups that contain it,
    /// <paramref name="state"/> limits the result to one trigger state, <paramref name="page"/> is
    /// 1-based, and a <paramref name="pageSize"/> of zero returns no items but still counts them.
    /// </summary>
    ValueTask<TriggerPageDto> GetTriggers(string schedulerName, string? groupFilter, TriggerState? state, int page, int pageSize, CancellationToken cancellationToken = default);

    ValueTask<TriggerDetailDto> GetTrigger(string schedulerName, string group, string name, CancellationToken cancellationToken = default);

    ValueTask<string> GetTriggerState(string schedulerName, string group, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses the trigger. Returns <see langword="true" /> when the trigger existed and was moved
    /// into the paused state, <see langword="false" /> when there was nothing to pause.
    /// </summary>
    ValueTask<bool> PauseTrigger(string schedulerName, string group, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes the trigger. Returns <see langword="true" /> when the trigger existed in a paused
    /// state and was resumed, <see langword="false" /> when there was nothing to resume.
    /// </summary>
    ValueTask<bool> ResumeTrigger(string schedulerName, string group, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the trigger from the error state. Returns <see langword="true" /> when the trigger
    /// existed in the error state and was reset, <see langword="false" /> otherwise.
    /// </summary>
    ValueTask<bool> ResetTriggerFromErrorState(string schedulerName, string group, string name, CancellationToken cancellationToken = default);

    ValueTask ScheduleJob(string schedulerName, ScheduleJobRequest request, CancellationToken cancellationToken = default);

    ValueTask UnscheduleJob(string schedulerName, string group, string name, CancellationToken cancellationToken = default);

    ValueTask RescheduleJob(string schedulerName, string group, string name, RescheduleRequest request, CancellationToken cancellationToken = default);

    ValueTask<List<string>> GetCalendarNames(string schedulerName, CancellationToken cancellationToken = default);

    ValueTask<CalendarDetailDto> GetCalendar(string schedulerName, string calendarName, CancellationToken cancellationToken = default);

    ValueTask AddCalendar(string schedulerName, AddCalendarRequest request, CancellationToken cancellationToken = default);

    ValueTask DeleteCalendar(string schedulerName, string calendarName, CancellationToken cancellationToken = default);

    ValueTask<JobHistoryPageDto?> GetHistory(JobHistoryQueryDto query, CancellationToken cancellationToken = default);

    ValueTask<ExecutionLimitsDto?> GetExecutionLimits(string schedulerName, CancellationToken cancellationToken = default);
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

public sealed record JobGroupDto(string Name, bool Paused);

public sealed record TriggerKeyDto(string Group, string Name);

public sealed record TriggerHeaderDto(string Group, string Name, string? ExecutionGroup = null)
{
    public string? TriggerType { get; init; }

    public string? ScheduleSummary { get; init; }

    public string? State { get; init; }
}

public sealed record JobPageDto(int Page, int PageSize, int TotalCount, bool HasMore, List<JobKeyDto> Items);

public sealed record TriggerPageDto(int Page, int PageSize, int TotalCount, bool HasMore, List<TriggerHeaderDto> Items);

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

public sealed record ExecutingFireInstanceDto(
    string FireInstanceId,
    TriggerKeyDto TriggerKey,
    JobKeyDto JobKey,
    string SchedulerInstanceId,
    DateTimeOffset FireTimeUtc,
    DateTimeOffset? ScheduledFireTimeUtc);

public sealed record TriggerDetailDto(JsonElement Value);

public sealed record ScheduleJobRequest(JsonElement Trigger, JobDetailDto? Job);

public sealed record RescheduleRequest(JsonElement NewTrigger);

public sealed record CalendarDetailDto(JsonElement Value);

public sealed record AddCalendarRequest(string CalendarName, JsonElement Calendar, bool Replace, bool UpdateTriggers);

public sealed record AddJobRequest(JobDetailDto Job, bool Replace, bool? StoreNonDurableWhileAwaitingScheduling);

public sealed record JobHistoryPageDto(JsonElement Value);

public sealed record ExecutionLimitsDto(Dictionary<string, int?> Limits);
