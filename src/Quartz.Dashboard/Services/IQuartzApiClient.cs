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
/// The verbs are <see cref="IScheduler" />'s own, spelled the same way: an operation this interface
/// forwards carries the name the scheduler gives it — <see cref="Start" />, <see cref="Interrupt" />,
/// one <see cref="TriggerJob" /> with an optional map, and the <c>Query*</c> family for the paged
/// listings. Only what has no counterpart on <see cref="IScheduler" /> — the scheduler listing, the
/// execution history — names itself.
/// </para>
/// <para>
/// A mutation that can find nothing to act on answers with the scheduler's own <see cref="bool" />:
/// whether it applied. Every such member reports it, because a name shared with
/// <see cref="IScheduler" /> that dropped the answer would read as the same operation and quietly not
/// be one — and a page cannot tell "deleted" from "was already gone" without it.
/// </para>
/// <para>
/// A trigger and a calendar arrive as themselves because Quartz already owns the polymorphism they
/// need: the serializer registry maps each kind to its own serializer, custom kinds an application
/// registered included, and the wire format is that discriminated shape. A DTO family of the
/// dashboard's own would have to be extended for every trigger kind and would still not describe a
/// kind it had never heard of.
/// </para>
/// <para>
/// <b>Missing things.</b> Every member that takes a <c>schedulerName</c> raises
/// <see cref="KeyNotFoundException" /> when no scheduler goes by that name, and the four members that
/// return the thing itself rather than a listing — <see cref="GetScheduler" />,
/// <see cref="GetJobDetail" />, <see cref="GetTrigger" /> and <see cref="GetCalendar" /> — raise it
/// again when the thing is gone. Their return types are non-nullable, so there is no other answer
/// available to them, and the dashboard's error boundary renders that exception as the "not found"
/// page. A replacement that answered <c>null!</c> instead would fault the page with a
/// <see cref="NullReferenceException" /> somewhere further in.
/// </para>
/// <para>
/// <b>Things this data source cannot report.</b> A capability the source does not have is a value,
/// not an exception: <see cref="GetExecutionLimits" /> answers
/// <see cref="ExecutionLimitsDto.CannotReport" /> where the underlying scheduler refuses with
/// <see cref="NotSupportedException" />, because "this source cannot say" and "nothing is limited"
/// are different facts and the overview draws them differently.
/// </para>
/// <para>
/// <b>Additivity.</b> This interface is frozen from 4.0.0-beta.1 in the sense the release promises:
/// a member added to it in 4.x arrives as a default interface member, so an implementation of an
/// application's own keeps compiling. Its default body reports the datum as unavailable the way
/// <see cref="ExecutionLimitsDto.CannotReport" /> does, rather than inventing one.
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
    /// <exception cref="KeyNotFoundException">No scheduler goes by <paramref name="schedulerName" />.</exception>
    ValueTask<SchedulerDetailDto> GetScheduler(string schedulerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the scheduler, so its triggers begin firing.
    /// </summary>
    ValueTask Start(string schedulerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts the scheduler in standby: it keeps its state and stops firing until it is started again.
    /// </summary>
    ValueTask Standby(string schedulerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Shuts the scheduler down, waiting for the jobs in flight. A scheduler that has shut down cannot
    /// be started again.
    /// </summary>
    ValueTask Shutdown(string schedulerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses every trigger group, so nothing fires until <see cref="ResumeAll" />.
    /// </summary>
    ValueTask PauseAll(string schedulerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes every trigger group, applying each trigger's misfire instruction to what it missed.
    /// </summary>
    ValueTask ResumeAll(string schedulerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of jobs, ordered by group and then name. The page always reports
    /// <see cref="PagedResult{T}.HasMore" /> and, because the dashboard asks for it, a
    /// <see cref="PagedResult{T}.TotalCount" />.
    /// </summary>
    ValueTask<PagedResult<JobKeyDto>> QueryJobs(string schedulerName, DashboardJobQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of job groups, ordered by name, each carrying whether it is paused.
    /// </summary>
    ValueTask<PagedResult<JobGroupDto>> QueryJobGroups(string schedulerName, DashboardGroupQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of trigger groups, ordered by name, each carrying whether it is paused.
    /// </summary>
    ValueTask<PagedResult<TriggerGroupDto>> QueryTriggerGroups(string schedulerName, DashboardGroupQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the job's definition — its type, its durability and its data map.
    /// </summary>
    /// <exception cref="KeyNotFoundException">
    /// No scheduler goes by <paramref name="schedulerName" />, or it holds no job under
    /// <paramref name="jobKey" />.
    /// </exception>
    ValueTask<JobDetailDto> GetJobDetail(string schedulerName, JobKeyDto jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every trigger scheduled against the job, which is a short list rather than a page: a job
    /// has as many triggers as somebody wrote for it.
    /// </summary>
    ValueTask<List<TriggerHeaderDto>> GetTriggersOfJob(string schedulerName, JobKeyDto jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of firings — by default the ones that are running — ordered by trigger group,
    /// then trigger name, then fire instance id. With a persistent job store this covers the whole
    /// cluster, so a firing owned by another node is listed too, marked with that node's
    /// <see cref="FireInstanceDto.SchedulerInstanceId" />.
    /// </summary>
    ValueTask<PagedResult<FireInstanceDto>> QueryFireInstances(string schedulerName, DashboardFireInstanceQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the scheduler's cluster nodes, the node that answered first. A scheduler that is not
    /// clustered answers with the one node it is, with no check-in times.
    /// </summary>
    /// <remarks>
    /// Joins to <see cref="QueryFireInstances" /> on <see cref="FireInstanceDto.SchedulerInstanceId" />,
    /// which is how the Cluster page counts what each node is running.
    /// </remarks>
    ValueTask<List<ClusterNodeDto>> QueryClusterNodes(string schedulerName, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Triggers the job once, with <paramref name="jobDataMap" /> merged over the job's own data for
    /// that one firing when a map is given.
    /// </summary>
    /// <remarks>
    /// One method with an optional map, exactly as <see cref="IScheduler.TriggerJob" /> is: firing a job
    /// with data and firing it without are the same operation, and the pair of methods this replaces made
    /// them look like two.
    /// </remarks>
    ValueTask TriggerJob(string schedulerName, JobKeyDto jobKey, JobDataMap? jobDataMap = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Interrupts every execution of the job on this node. Returns <see langword="true" /> when at
    /// least one execution was asked to stop, <see langword="false" /> when the job was not running
    /// here.
    /// </summary>
    /// <remarks>
    /// A job that does not watch its cancellation token runs to completion regardless, so the flag says
    /// the interrupt was delivered rather than that the work stopped.
    /// </remarks>
    ValueTask<bool> Interrupt(string schedulerName, JobKeyDto jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Interrupts one execution, named by its fire instance id. Returns <see langword="true" /> when
    /// that execution was found and asked to stop, <see langword="false" /> when it had already
    /// finished or belongs to another node.
    /// </summary>
    /// <remarks>
    /// The single-execution form of <see cref="Interrupt" />, which interrupts every execution of the
    /// job. Node-local on the server side: a firing owned by another node is interrupted by asking that
    /// node.
    /// </remarks>
    ValueTask<bool> InterruptFireInstance(string schedulerName, string fireInstanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the job and every trigger that fires it. Returns <see langword="true" /> when the job
    /// existed and was deleted, <see langword="false" /> when there was nothing to delete.
    /// </summary>
    ValueTask<bool> DeleteJob(string schedulerName, JobKeyDto jobKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a job with no trigger, for one that is triggered by hand or scheduled later.
    /// </summary>
    ValueTask AddJob(string schedulerName, AddJobRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of triggers, ordered by group and then name, each carrying its state and
    /// execution group.
    /// </summary>
    ValueTask<PagedResult<TriggerHeaderDto>> QueryTriggers(string schedulerName, DashboardTriggerQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the trigger itself — a <see cref="ICronTrigger" />, <see cref="ISimpleTrigger" /> or
    /// whichever kind it is, including one an application registered its own serializer for.
    /// </summary>
    /// <exception cref="KeyNotFoundException">
    /// No scheduler goes by <paramref name="schedulerName" />, or it holds no trigger under
    /// <paramref name="triggerKey" />.
    /// </exception>
    ValueTask<ITrigger> GetTrigger(string schedulerName, TriggerKeyDto triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the trigger's state, which is <see cref="TriggerState.None" /> when there is no such
    /// trigger — the one read here that answers rather than raising.
    /// </summary>
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

    /// <summary>
    /// Schedules a trigger, together with the job it fires when the request carries one.
    /// </summary>
    ValueTask ScheduleJob(string schedulerName, ScheduleJobRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the trigger, and the job it fired if that job is not durable and has no triggers left.
    /// Returns <see langword="true" /> when the trigger existed and was removed,
    /// <see langword="false" /> when there was nothing to remove.
    /// </summary>
    ValueTask<bool> UnscheduleJob(string schedulerName, TriggerKeyDto triggerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the trigger with the one in the request, keeping the job it fires.
    /// </summary>
    ValueTask RescheduleJob(string schedulerName, TriggerKeyDto triggerKey, RescheduleRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the names of every calendar the scheduler holds — a short list rather than a page.
    /// </summary>
    ValueTask<List<string>> GetCalendarNames(string schedulerName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the calendar itself, of whichever kind it is.
    /// </summary>
    /// <exception cref="KeyNotFoundException">
    /// No scheduler goes by <paramref name="schedulerName" />, or it holds no calendar named
    /// <paramref name="calendarName" />.
    /// </exception>
    ValueTask<ICalendar> GetCalendar(string schedulerName, string calendarName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a calendar under a name, which triggers refer to in order to exclude times from firing.
    /// </summary>
    ValueTask AddCalendar(string schedulerName, AddCalendarRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the calendar. Returns <see langword="true" /> when it existed and was deleted,
    /// <see langword="false" /> when there was nothing to delete.
    /// </summary>
    ValueTask<bool> DeleteCalendar(string schedulerName, string calendarName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of execution history, newest first.
    /// </summary>
    /// <remarks>
    /// The history is <see cref="IDashboardHistoryStore" />'s, and that store always answers: a store
    /// holding nothing returns an empty page, which is what "no executions recorded" is. This used to be
    /// nullable because the deleted remote client turned a 404 into "no history at all", and nothing
    /// in this process has ever had that answer to give.
    /// </remarks>
    ValueTask<PagedResult<DashboardHistoryEntry>> QueryExecutions(DashboardHistoryQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of the triggers that missed a firing, newest first.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="QueryExecutions" path="/remarks" />
    /// </remarks>
    ValueTask<PagedResult<DashboardMisfireEntry>> QueryMisfires(DashboardMisfireQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the misfires the scheduler has recorded since <paramref name="since" />.
    /// </summary>
    /// <remarks>
    /// A count rather than a page, because the overview's tile asks "how bad is it right now" and a
    /// store keeping its history in a database can answer that without loading rows it would discard.
    /// </remarks>
    ValueTask<int> CountMisfires(string schedulerName, DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the execution limits the scheduler is running with, or
    /// <see cref="ExecutionLimitsDto.CannotReport" /> when this data source cannot say.
    /// </summary>
    /// <remarks>
    /// Never <see langword="null" />: a scheduler with nothing limited answers with an empty
    /// <see cref="ExecutionLimitsDto.Limits" />, which is a different fact from a scheduler that cannot
    /// report limits at all, and the overview says which of the two it is looking at.
    /// </remarks>
    ValueTask<ExecutionLimitsDto> GetExecutionLimits(string schedulerName, CancellationToken cancellationToken = default);
}

/// <summary>
/// One page of a group listing — job groups or trigger groups — optionally narrowed to the groups that
/// are paused or to the ones that are not.
/// </summary>
public sealed record DashboardGroupQuery : PagedQuery
{
    /// <summary>
    /// Limits the result by paused state: <see langword="true" /> for paused groups only,
    /// <see langword="false" /> for unpaused only, <see langword="null" /> for every group.
    /// </summary>
    /// <remarks>
    /// With <see cref="PagedQuery.Take" /> of zero this counts the paused groups, which the unfiltered
    /// listing cannot be made to do: a group can be paused while it holds nothing, and the unfiltered
    /// listing enumerates the groups that hold something.
    /// </remarks>
    public bool? Paused { get; init; }
}

/// <summary>
/// One page of the job listing, optionally narrowed to the groups whose name contains
/// <see cref="GroupContains" />.
/// </summary>
public sealed record DashboardJobQuery : PagedQuery
{
    /// <summary>
    /// Lists only the jobs whose group name contains this, or every job when null.
    /// </summary>
    public string? GroupContains { get; init; }
}

/// <summary>
/// One page of the trigger listing, optionally narrowed by group name and by state.
/// </summary>
public sealed record DashboardTriggerQuery : PagedQuery
{
    /// <summary>
    /// Lists only the triggers whose group name contains this, or every trigger when null.
    /// </summary>
    public string? GroupContains { get; init; }

    /// <summary>
    /// Lists only the triggers in this state, or every state when null.
    /// </summary>
    public TriggerState? State { get; init; }
}

/// <summary>
/// One page of the firings a scheduler knows about.
/// </summary>
public sealed record DashboardFireInstanceQuery : PagedQuery
{
    /// <summary>
    /// Lists only the firings whose trigger group name contains this, or every firing when null.
    /// </summary>
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
    /// <summary>
    /// The scheduler whose history to list. Required: the store keeps every scheduler's rows together.
    /// </summary>
    public required string SchedulerName { get; init; }

    /// <summary>
    /// The node whose executions to list, or <see langword="null" /> for every node's.
    /// </summary>
    public string? SchedulerInstanceId { get; init; }

    /// <summary>
    /// Lists only the executions whose job key matches this, or every job's when null.
    /// </summary>
    public string? JobFilter { get; init; }

    /// <summary>
    /// Lists only the executions whose trigger key matches this, or every trigger's when null.
    /// </summary>
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
    /// <summary>
    /// The scheduler whose misfires to list. Required: the store keeps every scheduler's rows together.
    /// </summary>
    public required string SchedulerName { get; init; }

    /// <summary>
    /// The node whose misfires to list, or <see langword="null" /> for every node's.
    /// </summary>
    public string? SchedulerInstanceId { get; init; }

    /// <summary>
    /// Lists only the misfires whose trigger key matches this, or every trigger's when null.
    /// </summary>
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

/// <summary>
/// A job's identity, as the pages carry it.
/// </summary>
/// <param name="Group">The job's group.</param>
/// <param name="Name">The job's name, unique within the group.</param>
public sealed record JobKeyDto(string Group, string Name);

/// <summary>
/// One job group, and whether it is paused.
/// </summary>
/// <param name="Name">The group's name.</param>
/// <param name="Paused">Whether the group is paused, so what is added to it starts paused too.</param>
public sealed record JobGroupDto(string Name, bool Paused);

/// <summary>
/// One trigger group, and whether it is paused.
/// </summary>
/// <param name="Name">The group's name.</param>
/// <param name="Paused">Whether the group is paused, so what is added to it starts paused too.</param>
public sealed record TriggerGroupDto(string Name, bool Paused);

/// <summary>
/// A trigger's identity, as the pages carry it.
/// </summary>
/// <param name="Group">The trigger's group.</param>
/// <param name="Name">The trigger's name, unique within the group.</param>
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

/// <summary>
/// The calendar to store, and what to do about what is already there.
/// </summary>
/// <param name="CalendarName">The name to store it under.</param>
/// <param name="Calendar">The calendar itself, of whichever kind it is.</param>
/// <param name="Replace">Whether a calendar already under that name may be overwritten.</param>
/// <param name="UpdateTriggers">
/// Whether the triggers referring to that name are recomputed against the new calendar. Off, they keep
/// firing on the schedule the old one produced.
/// </param>
public sealed record AddCalendarRequest(string CalendarName, ICalendar Calendar, bool Replace, bool UpdateTriggers);

/// <summary>
/// The job to store, and what to do about what is already there.
/// </summary>
/// <param name="Job">The job's definition.</param>
/// <param name="Replace">Whether a job already under that key may be overwritten.</param>
/// <param name="StoreNonDurableWhileAwaitingScheduling">
/// Whether a non-durable job may be stored with no trigger yet, or <see langword="null" /> to leave the
/// scheduler's own default. A non-durable job stored this way is removed again if nothing schedules it.
/// </param>
public sealed record AddJobRequest(JobDetailDto Job, bool Replace, bool? StoreNonDurableWhileAwaitingScheduling);

/// <summary>
/// The execution limits a scheduler is running with, keyed by the spelling configuration and the HTTP
/// API use for a group: <c>_</c> for the bucket of triggers that carry no execution group, <c>*</c> for
/// the catch-all applied to groups with no limit of their own, and otherwise the group's name.
/// </summary>
/// <remarks>
/// The keys are the configuration spellings rather than display text because the overview joins them to
/// what is in flight, and a firing carries a group name or nothing at all. Rendering
/// <see cref="ExecutionGroupScope" />'s three cases as words is the panel's job.
/// </remarks>
/// <param name="Limits">Each group's limit, with the scope it is counted in. Empty when the scheduler
/// limits nothing, which leaves every group unlimited.</param>
/// <param name="UsesTriggerGroupWhenUnset">
/// Whether a trigger that carries no execution group is limited as though it belonged to a group named
/// after its own trigger group — <see cref="ExecutionLimits.UsesTriggerGroupWhenUnset" />. The overview
/// applies the same derivation when it counts what is in flight, or its counts and the scheduler's
/// would key the same firing differently.
/// </param>
/// <param name="CanReport">
/// Whether the scheduler could answer at all. <see langword="false" /> is not "nothing is limited": it
/// is "this scheduler does not implement execution limits", and the two must not be shown alike.
/// </param>
public sealed record ExecutionLimitsDto(
    Dictionary<string, DashboardExecutionLimit> Limits,
    bool UsesTriggerGroupWhenUnset = false,
    bool CanReport = true)
{
    /// <summary>
    /// The answer for a scheduler whose implementation refuses the question — an <see cref="IScheduler" />
    /// of an application's own may throw <see cref="NotSupportedException" /> rather than implement
    /// limits.
    /// </summary>
    public static ExecutionLimitsDto CannotReport { get; } = new([], UsesTriggerGroupWhenUnset: false, CanReport: false);
}

/// <summary>
/// One group's limit as the dashboard reads it.
/// </summary>
/// <param name="MaxConcurrent">The limit, or <see langword="null" /> when the group is explicitly
/// unlimited.</param>
/// <param name="Scope">Whether the number is what one node may run or what the cluster may run.</param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct DashboardExecutionLimit(int? MaxConcurrent, ExecutionLimitScope Scope);
