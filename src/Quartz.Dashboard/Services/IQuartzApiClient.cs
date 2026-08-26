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

namespace Quartz.Dashboard.Services;

/// <summary>
/// The data source behind the dashboard's pages: either the schedulers in this process or a Quartz HTTP
/// API somewhere else.
/// </summary>
/// <remarks>
/// <para>
/// This is the dashboard's own projection of the HTTP API, not the wire contract itself — it is shaped
/// for the pages that read it, and it is public so that an application can replace it. It speaks Quartz's
/// vocabulary throughout: <see cref="TriggerState" /> and <see cref="SchedulerStatus" /> rather than
/// strings, <see cref="JobKeyDto" /> and <see cref="TriggerKeyDto" /> rather than loose group/name pairs,
/// <see cref="PagedQuery" />'s <c>Skip</c>/<c>Take</c> with <see cref="PagedResult{T}" /> rather than
/// a paging model of its own, and <see cref="ITrigger" />, <see cref="ICalendar" /> and
/// <see cref="JobDataMap" /> rather than JSON.
/// </para>
/// <para>
/// A trigger and a calendar arrive as themselves because Quartz already owns the polymorphism they
/// need: the serializer registry maps each kind to its own serializer, custom kinds an application
/// registered included, and the wire format is that discriminated shape. A DTO family of the
/// dashboard's own would have to be extended for every trigger kind and would still not describe a
/// kind it had never heard of.
/// </para>
/// </remarks>
public interface IQuartzApiClient
{
    /// <summary>
    /// Returns every scheduler the container knows about, ordered by name — the registrations included,
    /// so a scheduler nothing has built yet is listed with a null <see cref="SchedulerHeaderDto.Status" />
    /// rather than being invisible.
    /// </summary>
    /// <remarks>
    /// Nothing is created by asking. That is why the listing is the registrations rather than the
    /// repository: an operator enumerating tenants must not start every one of them.
    /// </remarks>
    ValueTask<List<SchedulerHeaderDto>> GetSchedulers(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one scheduler with the metadata it was built with. Only a scheduler that exists can
    /// answer; a registration nothing has built is reported by <see cref="GetSchedulers" /> and has no
    /// detail to read.
    /// </summary>
    ValueTask<SchedulerDetailDto> GetScheduler(string schedulerName, CancellationToken cancellationToken = default);

    ValueTask StartScheduler(string schedulerName, CancellationToken cancellationToken = default);

    ValueTask StandbyScheduler(string schedulerName, CancellationToken cancellationToken = default);

    ValueTask ShutdownScheduler(string schedulerName, CancellationToken cancellationToken = default);

    ValueTask PauseAll(string schedulerName, CancellationToken cancellationToken = default);

    ValueTask ResumeAll(string schedulerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of jobs, ordered by group and then name. The page always reports
    /// <see cref="PagedResult{T}.HasMore" /> and, because the dashboard asks for it, a
    /// <see cref="PagedResult{T}.TotalCount" />.
    /// </summary>
    ValueTask<PagedResult<JobKeyDto>> GetJobs(string schedulerName, DashboardJobQuery query, CancellationToken cancellationToken = default);

    ValueTask<List<JobGroupDto>> GetJobGroups(string schedulerName, CancellationToken cancellationToken = default);

    ValueTask<JobDetailDto> GetJob(string schedulerName, JobKeyDto jobKey, CancellationToken cancellationToken = default);

    ValueTask<List<TriggerHeaderDto>> GetJobTriggers(string schedulerName, JobKeyDto jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of firings — by default the ones that are running — ordered by trigger group,
    /// then trigger name, then fire instance id. With a persistent job store this covers the whole
    /// cluster, so a firing owned by another node is listed too, marked with that node's
    /// <see cref="FireInstanceDto.SchedulerInstanceId" />.
    /// </summary>
    ValueTask<PagedResult<FireInstanceDto>> GetFireInstances(string schedulerName, DashboardFireInstanceQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the scheduler's cluster nodes, the node that answered first. A scheduler that is not
    /// clustered answers with the one node it is, with no check-in times.
    /// </summary>
    /// <remarks>
    /// Joins to <see cref="GetFireInstances" /> on <see cref="FireInstanceDto.SchedulerInstanceId" />,
    /// which is how the Cluster page counts what each node is running.
    /// </remarks>
    ValueTask<List<ClusterNodeDto>> GetClusterNodes(string schedulerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses the job. Returns <see langword="true" /> when the job existed and was paused,
    /// <see langword="false" /> when there was nothing to pause.
    /// </summary>
    ValueTask<bool> PauseJob(string schedulerName, JobKeyDto jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes the job. Returns <see langword="true" /> when the job existed and was resumed,
    /// <see langword="false" /> when there was nothing to resume.
    /// </summary>
    ValueTask<bool> ResumeJob(string schedulerName, JobKeyDto jobKey, CancellationToken cancellationToken = default);

    ValueTask TriggerJob(string schedulerName, JobKeyDto jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Triggers the job once, with <paramref name="jobDataMap" /> merged over the job's own data for
    /// that one firing.
    /// </summary>
    ValueTask TriggerJobWithData(string schedulerName, JobKeyDto jobKey, JobDataMap jobDataMap, CancellationToken cancellationToken = default);

    ValueTask InterruptJob(string schedulerName, JobKeyDto jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Interrupts one execution, named by its fire instance id.
    /// </summary>
    /// <remarks>
    /// The single-execution form of <see cref="InterruptJob" />, which interrupts every execution of the
    /// job. Node-local on the server side: a firing owned by another node is interrupted by asking that
    /// node.
    /// </remarks>
    ValueTask InterruptFireInstance(string schedulerName, string fireInstanceId, CancellationToken cancellationToken = default);

    ValueTask DeleteJob(string schedulerName, JobKeyDto jobKey, CancellationToken cancellationToken = default);

    ValueTask AddJob(string schedulerName, AddJobRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of triggers, ordered by group and then name, each carrying its state and
    /// execution group.
    /// </summary>
    ValueTask<PagedResult<TriggerHeaderDto>> GetTriggers(string schedulerName, DashboardTriggerQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the trigger itself — a <see cref="ICronTrigger" />, <see cref="ISimpleTrigger" /> or
    /// whichever kind it is, including one an application registered its own serializer for.
    /// </summary>
    ValueTask<ITrigger> GetTrigger(string schedulerName, TriggerKeyDto triggerKey, CancellationToken cancellationToken = default);

    ValueTask<TriggerState> GetTriggerState(string schedulerName, TriggerKeyDto triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses the trigger. Returns <see langword="true" /> when the trigger existed and was moved
    /// into the paused state, <see langword="false" /> when there was nothing to pause.
    /// </summary>
    ValueTask<bool> PauseTrigger(string schedulerName, TriggerKeyDto triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes the trigger. Returns <see langword="true" /> when the trigger existed in a paused
    /// state and was resumed, <see langword="false" /> when there was nothing to resume.
    /// </summary>
    ValueTask<bool> ResumeTrigger(string schedulerName, TriggerKeyDto triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the trigger from the error state. Returns <see langword="true" /> when the trigger
    /// existed in the error state and was reset, <see langword="false" /> otherwise.
    /// </summary>
    ValueTask<bool> ResetTriggerFromErrorState(string schedulerName, TriggerKeyDto triggerKey, CancellationToken cancellationToken = default);

    ValueTask ScheduleJob(string schedulerName, ScheduleJobRequest request, CancellationToken cancellationToken = default);

    ValueTask UnscheduleJob(string schedulerName, TriggerKeyDto triggerKey, CancellationToken cancellationToken = default);

    ValueTask RescheduleJob(string schedulerName, TriggerKeyDto triggerKey, RescheduleRequest request, CancellationToken cancellationToken = default);

    ValueTask<List<string>> GetCalendarNames(string schedulerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the calendar itself, of whichever kind it is.
    /// </summary>
    ValueTask<ICalendar> GetCalendar(string schedulerName, string calendarName, CancellationToken cancellationToken = default);

    ValueTask AddCalendar(string schedulerName, AddCalendarRequest request, CancellationToken cancellationToken = default);

    ValueTask DeleteCalendar(string schedulerName, string calendarName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of execution history, newest first, or <see langword="null" /> when the data
    /// source keeps no history.
    /// </summary>
    ValueTask<PagedResult<DashboardHistoryEntry>?> GetHistory(DashboardHistoryQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of the triggers that missed a firing, newest first, or <see langword="null" />
    /// when the data source keeps no history.
    /// </summary>
    ValueTask<PagedResult<DashboardMisfireEntry>?> GetMisfires(DashboardMisfireQuery query, CancellationToken cancellationToken = default);

    ValueTask<ExecutionLimitsDto?> GetExecutionLimits(string schedulerName, CancellationToken cancellationToken = default);
}

/// <summary>
/// One page of the job listing, optionally narrowed to the groups whose name contains
/// <see cref="GroupContains" />.
/// </summary>
public sealed record DashboardJobQuery : PagedQuery
{
    public string? GroupContains { get; init; }
}

/// <summary>
/// One page of the trigger listing, optionally narrowed by group name and by state.
/// </summary>
public sealed record DashboardTriggerQuery : PagedQuery
{
    public string? GroupContains { get; init; }

    public TriggerState? State { get; init; }
}

/// <summary>
/// One page of the firings a scheduler knows about.
/// </summary>
public sealed record DashboardFireInstanceQuery : PagedQuery
{
    public string? GroupContains { get; init; }

    /// <summary>
    /// Which firings to list. Defaults to <see cref="FireInstanceState.Executing" />, so an unfiltered
    /// query lists what is running; <see langword="null" /> lists reserved firings as well.
    /// </summary>
    public FireInstanceState? State { get; init; } = FireInstanceState.Executing;
}

/// <summary>
/// One page of the execution history of a scheduler, optionally narrowed by node, job and trigger.
/// </summary>
/// <remarks>
/// A filter matches a key's group, its name, or the two joined as <c>group.name</c>, case-insensitively.
/// </remarks>
public sealed record DashboardHistoryQuery : PagedQuery
{
    public required string SchedulerName { get; init; }

    /// <summary>
    /// The node whose executions to list, or <see langword="null" /> for every node's.
    /// </summary>
    public string? SchedulerInstanceId { get; init; }

    public string? JobFilter { get; init; }

    public string? TriggerFilter { get; init; }
}

/// <summary>
/// One page of the misfires of a scheduler, optionally narrowed by node and trigger.
/// </summary>
/// <remarks>
/// <inheritdoc cref="DashboardHistoryQuery" path="/remarks" />
/// </remarks>
public sealed record DashboardMisfireQuery : PagedQuery
{
    public required string SchedulerName { get; init; }

    /// <summary>
    /// The node whose misfires to list, or <see langword="null" /> for every node's.
    /// </summary>
    public string? SchedulerInstanceId { get; init; }

    public string? TriggerFilter { get; init; }
}

/// <summary>
/// One scheduler the container knows about, whether or not anything has built it.
/// </summary>
/// <remarks>
/// <see cref="Status" /> and <see cref="SchedulerInstanceId" /> are null for a registration nothing has
/// created: there is no scheduler to ask, and listing the registrations must not build one. The rest of
/// what a built scheduler is made of arrives with <see cref="SchedulerDetailDto" />, which is a read per
/// scheduler rather than part of the listing.
/// </remarks>
/// <param name="SchedulerName">The scheduler's name, spelled as it was registered.</param>
/// <param name="SchedulerInstanceId">
/// The instance id of the scheduler behind this registration, or <see langword="null" /> when nothing
/// has built one.
/// </param>
/// <param name="Status">
/// What state the scheduler is in, or <see langword="null" /> when no scheduler exists under this name.
/// </param>
/// <param name="Origin">Where the scheduler came from.</param>
public sealed record SchedulerHeaderDto(
    string SchedulerName,
    string? SchedulerInstanceId,
    SchedulerStatus? Status,
    SchedulerOrigin Origin)
{
    /// <summary>
    /// Whether a scheduler exists under this name, which is what decides whether the rest of the
    /// dashboard has anything to show for it.
    /// </summary>
    public bool IsCreated => Status is not null;
}

/// <summary>
/// One scheduler and what it is made of: its identity, its state, and the settings and capabilities
/// <see cref="SchedulerMetadata" /> reports.
/// </summary>
/// <remarks>
/// The metadata is on the detail rather than on <see cref="SchedulerHeaderDto" /> because reading it is
/// a call per scheduler — over HTTP for a remote one — while the listing must stay one call.
/// </remarks>
/// <param name="SchedulerInstanceId">The scheduler's instance id.</param>
/// <param name="SchedulerName">The scheduler's name.</param>
/// <param name="Status">Where the scheduler is in its lifecycle.</param>
/// <param name="Clustered">
/// Whether the job store is clustered. This is the answer to "is this scheduler part of a cluster";
/// the node listing cannot be, because a clustered store whose only node has not finished its first
/// check-in looks exactly like a store that keeps no check-in state at all.
/// </param>
/// <param name="Persistent">Whether the job store survives a restart.</param>
/// <param name="JobStoreTypeName">The job store's type name, without its assembly version.</param>
/// <param name="ThreadPoolTypeName">The thread pool's type name, without its assembly version.</param>
/// <param name="ThreadPoolSize">How many threads the pool has.</param>
/// <param name="RunningSince">
/// When the scheduler started, or <see langword="null" /> when it has not been started.
/// </param>
/// <param name="JobsExecuted">How many jobs this node has executed since it started.</param>
/// <param name="Version">The version of Quartz that is running.</param>
public sealed record SchedulerDetailDto(
    string SchedulerInstanceId,
    string SchedulerName,
    SchedulerStatus Status,
    bool Clustered,
    bool Persistent,
    string JobStoreTypeName,
    string ThreadPoolTypeName,
    int ThreadPoolSize,
    DateTimeOffset? RunningSince,
    int JobsExecuted,
    string Version);

public sealed record JobKeyDto(string Group, string Name);

public sealed record JobGroupDto(string Name, bool Paused);

public sealed record TriggerKeyDto(string Group, string Name);

/// <summary>
/// One row of a trigger listing: enough to show the trigger without loading it.
/// </summary>
/// <remarks>
/// <see cref="ScheduleSummary" /> is null on the listings that do not load the triggers themselves,
/// and <see cref="State" /> is null when the listing could not pair the header with a state.
/// </remarks>
public sealed record TriggerHeaderDto(
    string Group,
    string Name,
    string? TriggerType,
    string? ScheduleSummary,
    TriggerState? State,
    string? ExecutionGroup);

/// <remarks>
/// <see cref="JobDataMap" /> holds whatever the job was given, of whatever type — but that is exactly
/// what <see cref="Quartz.JobDataMap" /> is for, and it is what the scheduler hands back, so there is
/// no honesty to be had from a looser type here.
/// </remarks>
public sealed record JobDetailDto(
    string Name,
    string Group,
    string JobType,
    string? Description,
    bool Durable,
    bool RequestsRecovery,
    bool ConcurrentExecutionDisallowed,
    bool PersistJobDataAfterExecution,
    JobDataMap JobDataMap);

/// <summary>
/// One firing, as the dashboard shows it.
/// </summary>
/// <remarks>
/// <see cref="JobKey" /> is null while the firing is only <see cref="FireInstanceState.Acquired" />: the
/// job is not loaded until the execution starts.
/// </remarks>
public sealed record FireInstanceDto(
    string FireInstanceId,
    TriggerKeyDto TriggerKey,
    JobKeyDto? JobKey,
    string SchedulerInstanceId,
    FireInstanceState State,
    DateTimeOffset FireTimeUtc,
    DateTimeOffset? ScheduledFireTimeUtc,
    string? ExecutionGroup);

/// <summary>
/// One scheduler node, as the dashboard shows it.
/// </summary>
/// <remarks>
/// <see cref="LastCheckInUtc" /> and <see cref="CheckInInterval" /> are null when the store keeps no
/// check-in history, which is what a non-clustered scheduler looks like: one node, no times, and
/// nothing to be late for.
/// </remarks>
public sealed record ClusterNodeDto(
    string InstanceId,
    DateTimeOffset? LastCheckInUtc,
    TimeSpan? CheckInInterval,
    ClusterNodeState State,
    bool IsCurrentNode);

/// <summary>
/// A trigger to schedule, and the job it fires when that job is not already stored.
/// </summary>
public sealed record ScheduleJobRequest(ITrigger Trigger, JobDetailDto? Job);

/// <summary>
/// The trigger that replaces the one being rescheduled.
/// </summary>
public sealed record RescheduleRequest(ITrigger NewTrigger);

public sealed record AddCalendarRequest(string CalendarName, ICalendar Calendar, bool Replace, bool UpdateTriggers);

public sealed record AddJobRequest(JobDetailDto Job, bool Replace, bool? StoreNonDurableWhileAwaitingScheduling);

/// <summary>
/// The execution limits a scheduler is running with, keyed by a display-friendly group name.
/// </summary>
/// <param name="Limits">Each group's limit, with the scope it is counted in.</param>
public sealed record ExecutionLimitsDto(Dictionary<string, DashboardExecutionLimit> Limits);

/// <summary>
/// One group's limit as the dashboard reads it.
/// </summary>
/// <param name="MaxConcurrent">The limit, or <see langword="null" /> when the group is explicitly
/// unlimited.</param>
/// <param name="Scope">Whether the number is what one node may run or what the cluster may run.</param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct DashboardExecutionLimit(int? MaxConcurrent, ExecutionLimitScope Scope);
