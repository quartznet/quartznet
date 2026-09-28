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

using Quartz.Extensibility;

namespace Quartz.HttpApiContract;

/// <summary>
/// What each route of the wire contract does: one method per route in <see cref="SchedulerRoutes" />,
/// from the scheduler and the request's shapes to the response's.
/// </summary>
/// <remarks>
/// <para>
/// The HTTP API's endpoints are one carrier of these and bind the request from a URL and a body; a
/// carrier with no HTTP binds the same shapes from the same route and calls the same method, so there is
/// one mapping of the contract onto <see cref="IScheduler" /> however a request arrives. A carrier keeps
/// what belongs to how it was reached: finding the scheduler, authorizing the caller, refusing what its
/// options refuse, and writing the answer.
/// </para>
/// <para>
/// Three things stay with the carrier on purpose. A read whose subject is absent answers
/// <see langword="null" />, and the carrier says <c>404</c> in its own terms. A job carried by a request is
/// turned into an <see cref="IJobDetail" /> by a function the carrier passes in, because
/// <see cref="JobDetailDto.AsIJobDetail" /> is <c>[RequiresUnreferencedCode]</c> and the carrier is where
/// that statement stops travelling. And the scheduler listing is filtered by a predicate the carrier
/// passes in, because who may see which scheduler is the carrier's to decide.
/// </para>
/// <para>
/// A request the contract refuses as malformed raises <see cref="InvalidRequestException" />; everything
/// else a method raises is the scheduler's own.
/// </para>
/// </remarks>
internal static class SchedulerOperations
{
    // --- Schedulers ------------------------------------------------------------------------------------

    /// <summary>
    /// Every scheduler the container knows about that <paramref name="isVisible" /> lets the caller see,
    /// ordered by name. A registration nothing has built is listed with a null status.
    /// </summary>
    public static async ValueTask<SchedulerHeaderDto[]> GetAllSchedulers(
        ISchedulerRegistry schedulerRegistry,
        Func<string, CancellationToken, ValueTask<bool>> isVisible,
        CancellationToken cancellationToken)
    {
        List<SchedulerRegistration> registrations = await schedulerRegistry.QuerySchedulers(cancellationToken).ConfigureAwait(false);

        List<SchedulerHeaderDto> result = new(registrations.Count);
        foreach (SchedulerRegistration registration in registrations)
        {
            if (!await isVisible(registration.Name, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            result.Add(SchedulerHeaderDto.Create(registration));
        }

        return result.ToArray();
    }

    public static async ValueTask<SchedulerDto> GetSchedulerDetails(IScheduler scheduler, CancellationToken cancellationToken)
    {
        SchedulerMetadata metadata = await scheduler.GetMetadata(cancellationToken).ConfigureAwait(false);
        return SchedulerDto.Create(metadata);
    }

    public static SchedulerContextDto GetSchedulerContext(IScheduler scheduler)
    {
        return SchedulerContextDto.Create(scheduler.Context);
    }

    /// <summary>
    /// Starts the scheduler, after <paramref name="delay" /> when there is one.
    /// </summary>
    public static ValueTask Start(IScheduler scheduler, TimeSpan? delay, CancellationToken cancellationToken)
    {
        if (delay.HasValue)
        {
            return scheduler.StartDelayed(delay.Value, cancellationToken);
        }

        return scheduler.Start(cancellationToken);
    }

    public static ValueTask Standby(IScheduler scheduler, CancellationToken cancellationToken)
    {
        return scheduler.Standby(cancellationToken);
    }

    public static ValueTask Shutdown(IScheduler scheduler, bool waitForJobsToComplete, CancellationToken cancellationToken)
    {
        return scheduler.Shutdown(waitForJobsToComplete, cancellationToken);
    }

    public static ValueTask Clear(IScheduler scheduler, CancellationToken cancellationToken)
    {
        return scheduler.Clear(cancellationToken);
    }

    /// <summary>
    /// Pauses every trigger group. Details that say nothing are the reasonless pause, made through the
    /// reasonless member as before 4.3.
    /// </summary>
    public static ValueTask PauseAll(IScheduler scheduler, PauseDetails? details, CancellationToken cancellationToken)
    {
        return PauseDetails.SaysNothing(details)
            ? scheduler.PauseAll(cancellationToken)
            : scheduler.PauseAllWith(details, cancellationToken);
    }

    public static ValueTask ResumeAll(IScheduler scheduler, CancellationToken cancellationToken)
    {
        return scheduler.ResumeAll(cancellationToken);
    }

    /// <summary>
    /// The nodes of the cluster, this scheduler's own node first.
    /// </summary>
    public static async ValueTask<ClusterNodeDto[]> GetClusterNodes(IScheduler scheduler, CancellationToken cancellationToken)
    {
        List<ClusterNode> nodes = await scheduler.QueryClusterNodes(cancellationToken).ConfigureAwait(false);

        ClusterNodeDto[] result = new ClusterNodeDto[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            result[i] = ClusterNodeDto.Create(nodes[i]);
        }

        return result;
    }

    /// <summary>
    /// One page of what <paramref name="scheduler" /> has run, newest first, read from
    /// <paramref name="history" /> — the store the carrier decided holds this scheduler's history.
    /// </summary>
    public static async ValueTask<PagedResultDto<ExecutionHistoryEntryDto>> QueryExecutionHistory(
        IScheduler scheduler,
        IExecutionHistoryStore history,
        ListingParameters listing,
        string? schedulerInstanceId,
        string? jobContains,
        string? triggerContains,
        bool? failedFinally,
        CancellationToken cancellationToken)
    {
        ExecutionHistoryQuery query = listing.Page(new ExecutionHistoryQuery
        {
            // The scheduler's own spelling of its name, so a route that named it in another case still
            // reads the rows it recorded.
            SchedulerName = scheduler.SchedulerName,
            SchedulerInstanceId = schedulerInstanceId,
            JobContains = jobContains,
            TriggerContains = triggerContains,
            FailedFinally = failedFinally
        });

        PagedResult<ExecutionHistoryEntry> page = await history.QueryExecutions(query, cancellationToken).ConfigureAwait(false);

        ExecutionHistoryEntryDto[] items = new ExecutionHistoryEntryDto[page.Items.Count];
        for (int i = 0; i < page.Items.Count; i++)
        {
            items[i] = ExecutionHistoryEntryDto.Create(page.Items[i]);
        }

        return new PagedResultDto<ExecutionHistoryEntryDto>(items, page.HasMore, page.TotalCount);
    }

    /// <summary>
    /// One execution with its captured log, or <see langword="null" /> when it was never recorded or has
    /// since been trimmed.
    /// </summary>
    public static async ValueTask<ExecutionHistoryEntryDto?> GetExecution(
        IScheduler scheduler,
        IExecutionHistoryStore history,
        string entryId,
        CancellationToken cancellationToken)
    {
        ExecutionHistoryEntry? entry = await history.GetExecution(scheduler.SchedulerName, entryId, cancellationToken).ConfigureAwait(false);
        return entry is null ? null : ExecutionHistoryEntryDto.Create(entry, includeLog: true);
    }

    /// <summary>
    /// One page of the firings <paramref name="scheduler" /> missed, newest first.
    /// </summary>
    public static async ValueTask<PagedResultDto<MisfireHistoryEntryDto>> QueryMisfireHistory(
        IScheduler scheduler,
        IExecutionHistoryStore history,
        ListingParameters listing,
        string? schedulerInstanceId,
        string? triggerContains,
        CancellationToken cancellationToken)
    {
        MisfireHistoryQuery query = listing.Page(new MisfireHistoryQuery
        {
            SchedulerName = scheduler.SchedulerName,
            SchedulerInstanceId = schedulerInstanceId,
            TriggerContains = triggerContains
        });

        PagedResult<MisfireHistoryEntry> page = await history.QueryMisfires(query, cancellationToken).ConfigureAwait(false);

        MisfireHistoryEntryDto[] items = new MisfireHistoryEntryDto[page.Items.Count];
        for (int i = 0; i < page.Items.Count; i++)
        {
            items[i] = MisfireHistoryEntryDto.Create(page.Items[i]);
        }

        return new PagedResultDto<MisfireHistoryEntryDto>(items, page.HasMore, page.TotalCount);
    }

    public static async ValueTask<MisfireCountResponse> CountMisfires(
        IScheduler scheduler,
        IExecutionHistoryStore history,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        int count = await history.CountMisfires(scheduler.SchedulerName, since, cancellationToken).ConfigureAwait(false);
        return new MisfireCountResponse(count);
    }

    public static async ValueTask<ExecutionLimitsResponse> GetExecutionLimits(IScheduler scheduler, CancellationToken cancellationToken)
    {
        ExecutionLimits? limits = await scheduler.GetExecutionLimits(cancellationToken).ConfigureAwait(false);
        Dictionary<string, ExecutionLimitDto>? dict = null;
        if (limits is not null && !limits.IsEmpty)
        {
            dict = new Dictionary<string, ExecutionLimitDto>();
            foreach (ExecutionGroupLimit limit in limits.Groups)
            {
                dict[limit.Group.ToConfigurationKey()] = new ExecutionLimitDto(limit.MaxConcurrent, limit.Scope);
            }
        }

        return new ExecutionLimitsResponse(dict, limits?.UsesTriggerGroupWhenUnset ?? false);
    }

    public static ValueTask SetExecutionLimits(IScheduler scheduler, SetExecutionLimitsRequest request, CancellationToken cancellationToken)
    {
        ExecutionLimits? limits = null;

        // A request that names no group and asks for no derivation is the one that clears the limits.
        // Asking for the derivation alone still configures something - every trigger is then limited
        // as though its trigger group were its execution group - so it is built rather than dropped.
        if (request.Limits is { Count: > 0 } || request.UseTriggerGroupWhenUnset)
        {
            ExecutionLimitsBuilder builder = ExecutionLimitsBuilder.Create();
            foreach (KeyValuePair<string, ExecutionLimitDto> kvp in request.Limits ?? [])
            {
                // The same reading the property bridge uses, prefix keys such as "tenant:*" included;
                // the request's validation has already refused what this would throw on.
                builder.ForConfigurationKey(kvp.Key, kvp.Value.MaxConcurrent, kvp.Value.Scope);
            }

            if (request.UseTriggerGroupWhenUnset)
            {
                builder.UseTriggerGroupWhenUnset();
            }

            limits = builder.Build();
        }

        return scheduler.SetExecutionLimits(limits, cancellationToken);
    }

    public static ValueTask ClearExecutionLimits(IScheduler scheduler, CancellationToken cancellationToken)
    {
        return scheduler.SetExecutionLimits(null, cancellationToken);
    }

    // --- Jobs ------------------------------------------------------------------------------------------

    public static async ValueTask<PagedResultDto<JobHeaderDto>> QueryJobs(IScheduler scheduler, ListingParameters listing, CancellationToken cancellationToken)
    {
        JobQuery query = listing.Page(new JobQuery
        {
            Group = listing.GroupFilter<JobKey>(),
            Name = listing.NameFilter<JobKey>()
        });

        PagedResult<JobHeader> page = await scheduler.QueryJobs(query, cancellationToken).ConfigureAwait(false);
        return new PagedResultDto<JobHeaderDto>(page.Items.Select(JobHeaderDto.Create).ToArray(), page.HasMore, page.TotalCount);
    }

    public static async ValueTask<JobDetailDto[]> FetchJobs(IScheduler scheduler, KeyDto[] keys, CancellationToken cancellationToken)
    {
        JobKey[] jobKeys = keys.Select(x => x.AsJobKey()).ToArray();
        List<IJobDetail> jobDetails = await scheduler.GetJobDetails(jobKeys, cancellationToken).ConfigureAwait(false);
        return jobDetails.Select(JobDetailDto.Create).ToArray();
    }

    /// <summary>
    /// The job, or <see langword="null" /> when there is none under <paramref name="jobKey" />.
    /// </summary>
    public static async ValueTask<JobDetailDto?> GetJobDetails(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken)
    {
        IJobDetail? jobDetail = await scheduler.GetJobDetail(jobKey, cancellationToken).ConfigureAwait(false);
        return jobDetail is null ? null : JobDetailDto.Create(jobDetail);
    }

    public static async ValueTask<ExistsResponse> CheckJobExists(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken)
    {
        bool exists = await scheduler.Exists(jobKey, cancellationToken).ConfigureAwait(false);
        return new ExistsResponse(exists);
    }

    public static ValueTask<List<ITrigger>> GetJobTriggers(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken)
    {
        return scheduler.GetTriggersOfJob(jobKey, cancellationToken);
    }

    /// <summary>
    /// One page of firings. The job filter applies only when both halves of the key are given.
    /// </summary>
    public static async ValueTask<PagedResultDto<FireInstanceDto>> QueryFireInstances(
        IScheduler scheduler,
        ListingParameters listing,
        string? jobName,
        string? jobGroup,
        string? schedulerInstanceId,
        CancellationToken cancellationToken)
    {
        FireInstanceQuery query = listing.Page(new FireInstanceQuery
        {
            TriggerGroup = listing.GroupFilter<TriggerKey>(),
            TriggerName = listing.NameFilter<TriggerKey>(),
            Job = jobName is not null && jobGroup is not null ? new JobKey(jobName, jobGroup) : null,
            SchedulerInstanceId = schedulerInstanceId
        });

        if (listing.StateNamed)
        {
            query = query with { State = listing.State };
        }

        PagedResult<FireInstance> page = await scheduler.QueryFireInstances(query, cancellationToken).ConfigureAwait(false);
        return new PagedResultDto<FireInstanceDto>(page.Items.Select(FireInstanceDto.Create).ToArray(), page.HasMore, page.TotalCount);
    }

    /// <summary>
    /// Pauses a job's triggers. Details that say nothing are the reasonless pause.
    /// </summary>
    public static async ValueTask<OperationAppliedResponse> PauseJob(IScheduler scheduler, JobKey jobKey, PauseDetails? details, CancellationToken cancellationToken)
    {
        bool applied = PauseDetails.SaysNothing(details)
            ? await scheduler.PauseJob(jobKey, cancellationToken).ConfigureAwait(false)
            : await scheduler.PauseJobWith(jobKey, details, cancellationToken).ConfigureAwait(false);
        return new OperationAppliedResponse(applied);
    }

    /// <summary>
    /// Pauses the matching job groups. Details that say nothing are the reasonless pause.
    /// </summary>
    public static async ValueTask<AffectedGroupsResponse> PauseJobs(IScheduler scheduler, ListingParameters groups, PauseDetails? details, CancellationToken cancellationToken)
    {
        GroupMatcher<JobKey> matcher = groups.GroupFilter<JobKey>();
        List<string> pausedGroups = PauseDetails.SaysNothing(details)
            ? await scheduler.PauseJobGroups(matcher, cancellationToken).ConfigureAwait(false)
            : await scheduler.PauseJobGroupsWith(matcher, details, cancellationToken).ConfigureAwait(false);
        return new AffectedGroupsResponse([.. pausedGroups]);
    }

    public static async ValueTask<AppliedJobKeysResponse> PauseJobKeys(IScheduler scheduler, JobKeySetRequest request, CancellationToken cancellationToken)
    {
        JobKey[] jobKeys = request.Jobs.Select(x => x.AsJobKey()).ToArray();
        List<JobKey> paused = await scheduler.PauseJobs(jobKeys, cancellationToken).ConfigureAwait(false);
        return new AppliedJobKeysResponse([.. paused.Select(KeyDto.Create)]);
    }

    public static async ValueTask<OperationAppliedResponse> ResumeJob(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken)
    {
        bool applied = await scheduler.ResumeJob(jobKey, cancellationToken).ConfigureAwait(false);
        return new OperationAppliedResponse(applied);
    }

    public static async ValueTask<AffectedGroupsResponse> ResumeJobs(IScheduler scheduler, ListingParameters groups, CancellationToken cancellationToken)
    {
        GroupMatcher<JobKey> matcher = groups.GroupFilter<JobKey>();
        List<string> resumedGroups = await scheduler.ResumeJobGroups(matcher, cancellationToken).ConfigureAwait(false);
        return new AffectedGroupsResponse([.. resumedGroups]);
    }

    public static async ValueTask<AppliedJobKeysResponse> ResumeJobKeys(IScheduler scheduler, JobKeySetRequest request, CancellationToken cancellationToken)
    {
        JobKey[] jobKeys = request.Jobs.Select(x => x.AsJobKey()).ToArray();
        List<JobKey> resumed = await scheduler.ResumeJobs(jobKeys, cancellationToken).ConfigureAwait(false);
        return new AppliedJobKeysResponse([.. resumed.Select(KeyDto.Create)]);
    }

    public static ValueTask TriggerJob(IScheduler scheduler, JobKey jobKey, TriggerJobRequest? request, CancellationToken cancellationToken)
    {
        return scheduler.TriggerJob(jobKey, request?.JobData, cancellationToken);
    }

    public static async ValueTask<OperationAppliedResponse> InterruptJob(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken)
    {
        bool interrupted = await scheduler.Interrupt(jobKey, cancellationToken).ConfigureAwait(false);
        return new OperationAppliedResponse(interrupted);
    }

    public static async ValueTask<OperationAppliedResponse> InterruptJobInstance(IScheduler scheduler, string fireInstanceId, CancellationToken cancellationToken)
    {
        bool interrupted = await scheduler.InterruptFireInstance(fireInstanceId, cancellationToken).ConfigureAwait(false);
        return new OperationAppliedResponse(interrupted);
    }

    public static async ValueTask<OperationAppliedResponse> DeleteJob(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken)
    {
        bool jobFound = await scheduler.DeleteJob(jobKey, cancellationToken).ConfigureAwait(false);
        return new OperationAppliedResponse(jobFound);
    }

    /// <summary>
    /// Deletes a set of jobs, answering with the keys it deleted: a key that names no job is absent.
    /// </summary>
    public static async ValueTask<AppliedJobKeysResponse> DeleteJobs(IScheduler scheduler, DeleteJobsRequest request, CancellationToken cancellationToken)
    {
        JobKey[] jobKeys = request.Jobs.Select(x => x.AsJobKey()).ToArray();
        List<JobKey> deleted = await scheduler.DeleteJobs(jobKeys, cancellationToken).ConfigureAwait(false);
        return new AppliedJobKeysResponse([.. deleted.Select(KeyDto.Create)]);
    }

    /// <summary>
    /// Deletes every job in the matching groups, answering with the keys it deleted.
    /// </summary>
    public static async ValueTask<AppliedJobKeysResponse> DeleteJobsByGroup(IScheduler scheduler, ListingParameters groups, CancellationToken cancellationToken)
    {
        GroupMatcher<JobKey> matcher = groups.GroupFilter<JobKey>();
        List<JobKey> deleted = await scheduler.DeleteJobs(matcher, cancellationToken).ConfigureAwait(false);
        return new AppliedJobKeysResponse([.. deleted.Select(KeyDto.Create)]);
    }

    /// <summary>
    /// Adds the job the request carries, turned into a job detail by the carrier's
    /// <paramref name="toJobDetail" />.
    /// </summary>
    public static ValueTask AddJob(
        IScheduler scheduler,
        AddJobRequest request,
        Func<JobDetailDto, IJobDetail> toJobDetail,
        CancellationToken cancellationToken)
    {
        IJobDetail newJob = toJobDetail(request.Job);
        AddJobOptions options = new()
        {
            Replace = request.Replace,
            StoreNonDurableWhileAwaitingScheduling = request.StoreNonDurableWhileAwaitingScheduling.GetValueOrDefault(),
        };

        return scheduler.AddJob(newJob, options, cancellationToken);
    }

    public static async ValueTask<PagedResultDto<JobGroupDto>> QueryJobGroups(IScheduler scheduler, ListingParameters listing, bool? paused, CancellationToken cancellationToken)
    {
        JobGroupQuery query = listing.Page(new JobGroupQuery
        {
            Name = listing.NameFilter(),
            Paused = paused
        });

        PagedResult<JobGroup> page = await scheduler.QueryJobGroups(query, cancellationToken).ConfigureAwait(false);
        return new PagedResultDto<JobGroupDto>(page.Items.Select(JobGroupDto.Create).ToArray(), page.HasMore, page.TotalCount);
    }

    /// <summary>
    /// Whether the job group is paused, and what its pause recorded.
    /// </summary>
    public static async ValueTask<GroupPausedResponse> IsJobGroupPaused(IScheduler scheduler, string jobGroup, CancellationToken cancellationToken)
    {
        bool paused = await scheduler.IsJobGroupPaused(jobGroup, cancellationToken).ConfigureAwait(false);
        PauseInfo? pause = paused ? await scheduler.GetJobGroupPause(jobGroup, cancellationToken).ConfigureAwait(false) : null;
        return new GroupPausedResponse(paused, PauseDto.Create(pause));
    }

    // --- Triggers --------------------------------------------------------------------------------------

    public static async ValueTask<PagedResultDto<TriggerHeaderDto>> QueryTriggers(
        IScheduler scheduler,
        ListingParameters listing,
        JobKey? job,
        string? calendarName,
        TriggerState? state,
        DateTimeOffset? nextFireTimeBefore,
        CancellationToken cancellationToken)
    {
        TriggerQuery query = listing.Page(new TriggerQuery
        {
            Group = listing.GroupFilter<TriggerKey>(),
            Name = listing.NameFilter<TriggerKey>(),
            Job = job,
            CalendarName = calendarName,
            State = state,
            NextFireTimeBefore = nextFireTimeBefore
        });

        PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(query, cancellationToken).ConfigureAwait(false);
        return new PagedResultDto<TriggerHeaderDto>(page.Items.Select(TriggerHeaderDto.Create).ToArray(), page.HasMore, page.TotalCount);
    }

    public static ValueTask<List<ITrigger>> FetchTriggers(IScheduler scheduler, KeyDto[] keys, CancellationToken cancellationToken)
    {
        TriggerKey[] triggerKeys = keys.Select(x => x.AsTriggerKey()).ToArray();
        return scheduler.GetTriggers(triggerKeys, cancellationToken);
    }

    /// <summary>
    /// The trigger, or <see langword="null" /> when there is none under <paramref name="triggerKey" />.
    /// </summary>
    public static ValueTask<ITrigger?> GetTrigger(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken)
    {
        return scheduler.GetTrigger(triggerKey, cancellationToken);
    }

    public static async ValueTask<ExistsResponse> CheckTriggerExists(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken)
    {
        bool exists = await scheduler.Exists(triggerKey, cancellationToken).ConfigureAwait(false);
        return new ExistsResponse(exists);
    }

    /// <summary>
    /// The trigger's state, and what its pause recorded when it is paused.
    /// </summary>
    public static async ValueTask<TriggerStateDto> GetTriggerState(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken)
    {
        TriggerState state = await scheduler.GetTriggerState(triggerKey, cancellationToken).ConfigureAwait(false);

        // Asked only of a paused trigger: nothing else has a pause to report.
        PauseInfo? pause = state == TriggerState.Paused
            ? await scheduler.GetTriggerPause(triggerKey, cancellationToken).ConfigureAwait(false)
            : null;

        return new TriggerStateDto(state, PauseDto.Create(pause));
    }

    public static async ValueTask<OperationAppliedResponse> ResetTriggerFromErrorState(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken)
    {
        bool applied = await scheduler.ResetTriggerFromErrorState(triggerKey, cancellationToken).ConfigureAwait(false);
        return new OperationAppliedResponse(applied);
    }

    public static async ValueTask<AppliedTriggerKeysResponse> ResetTriggerKeysFromErrorState(IScheduler scheduler, TriggerKeySetRequest request, CancellationToken cancellationToken)
    {
        TriggerKey[] triggerKeys = request.Triggers.Select(x => x.AsTriggerKey()).ToArray();
        List<TriggerKey> reset = await scheduler.ResetTriggersFromErrorState(triggerKeys, cancellationToken).ConfigureAwait(false);
        return new AppliedTriggerKeysResponse([.. reset.Select(KeyDto.Create)]);
    }

    /// <summary>
    /// Pauses a trigger. Details that say nothing are the reasonless pause.
    /// </summary>
    public static async ValueTask<OperationAppliedResponse> PauseTrigger(IScheduler scheduler, TriggerKey triggerKey, PauseDetails? details, CancellationToken cancellationToken)
    {
        bool applied = PauseDetails.SaysNothing(details)
            ? await scheduler.PauseTrigger(triggerKey, cancellationToken).ConfigureAwait(false)
            : await scheduler.PauseTriggerWith(triggerKey, details, cancellationToken).ConfigureAwait(false);
        return new OperationAppliedResponse(applied);
    }

    /// <summary>
    /// Pauses the matching trigger groups. Details that say nothing are the reasonless pause.
    /// </summary>
    public static async ValueTask<AffectedGroupsResponse> PauseTriggers(IScheduler scheduler, ListingParameters groups, PauseDetails? details, CancellationToken cancellationToken)
    {
        GroupMatcher<TriggerKey> matcher = groups.GroupFilter<TriggerKey>();
        List<string> pausedGroups = PauseDetails.SaysNothing(details)
            ? await scheduler.PauseTriggerGroups(matcher, cancellationToken).ConfigureAwait(false)
            : await scheduler.PauseTriggerGroupsWith(matcher, details, cancellationToken).ConfigureAwait(false);
        return new AffectedGroupsResponse([.. pausedGroups]);
    }

    public static async ValueTask<AppliedTriggerKeysResponse> PauseTriggerKeys(IScheduler scheduler, TriggerKeySetRequest request, CancellationToken cancellationToken)
    {
        TriggerKey[] triggerKeys = request.Triggers.Select(x => x.AsTriggerKey()).ToArray();
        List<TriggerKey> paused = await scheduler.PauseTriggers(triggerKeys, cancellationToken).ConfigureAwait(false);
        return new AppliedTriggerKeysResponse([.. paused.Select(KeyDto.Create)]);
    }

    public static async ValueTask<OperationAppliedResponse> ResumeTrigger(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken)
    {
        bool applied = await scheduler.ResumeTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
        return new OperationAppliedResponse(applied);
    }

    public static async ValueTask<AffectedGroupsResponse> ResumeTriggers(IScheduler scheduler, ListingParameters groups, CancellationToken cancellationToken)
    {
        GroupMatcher<TriggerKey> matcher = groups.GroupFilter<TriggerKey>();
        List<string> resumedGroups = await scheduler.ResumeTriggerGroups(matcher, cancellationToken).ConfigureAwait(false);
        return new AffectedGroupsResponse([.. resumedGroups]);
    }

    public static async ValueTask<AppliedTriggerKeysResponse> ResumeTriggerKeys(IScheduler scheduler, TriggerKeySetRequest request, CancellationToken cancellationToken)
    {
        TriggerKey[] triggerKeys = request.Triggers.Select(x => x.AsTriggerKey()).ToArray();
        List<TriggerKey> resumed = await scheduler.ResumeTriggers(triggerKeys, cancellationToken).ConfigureAwait(false);
        return new AppliedTriggerKeysResponse([.. resumed.Select(KeyDto.Create)]);
    }

    public static async ValueTask<PagedResultDto<TriggerGroupDto>> QueryTriggerGroups(IScheduler scheduler, ListingParameters listing, bool? paused, CancellationToken cancellationToken)
    {
        TriggerGroupQuery query = listing.Page(new TriggerGroupQuery
        {
            Name = listing.NameFilter(),
            Paused = paused
        });

        PagedResult<TriggerGroup> page = await scheduler.QueryTriggerGroups(query, cancellationToken).ConfigureAwait(false);
        return new PagedResultDto<TriggerGroupDto>(page.Items.Select(TriggerGroupDto.Create).ToArray(), page.HasMore, page.TotalCount);
    }

    /// <summary>
    /// Whether the trigger group is paused, and what its pause recorded.
    /// </summary>
    public static async ValueTask<GroupPausedResponse> IsTriggerGroupPaused(IScheduler scheduler, string triggerGroup, CancellationToken cancellationToken)
    {
        bool paused = await scheduler.IsTriggerGroupPaused(triggerGroup, cancellationToken).ConfigureAwait(false);
        PauseInfo? pause = paused ? await scheduler.GetTriggerGroupPause(triggerGroup, cancellationToken).ConfigureAwait(false) : null;
        return new GroupPausedResponse(paused, PauseDto.Create(pause));
    }

    /// <summary>
    /// Schedules the request's trigger, with the job it carries when it carries one — turned into a job
    /// detail by the carrier's <paramref name="toJobDetail" /> — and under its conflict mode when it names
    /// one.
    /// </summary>
    public static async ValueTask<ScheduleJobResponse> ScheduleJob(
        IScheduler scheduler,
        ScheduleJobRequest request,
        Func<JobDetailDto, IJobDetail> toJobDetail,
        CancellationToken cancellationToken)
    {
        // Validation has refused onConflict beside a job, so this is a trigger scheduled on its own.
        if (request.OnConflict is { } onConflict)
        {
            ScheduleTriggerResult result = await scheduler.ScheduleTrigger(request.Trigger, onConflict, cancellationToken).ConfigureAwait(false);
            return new ScheduleJobResponse(result.NextFireTimeUtc) { Outcome = result.Outcome };
        }

        ScheduleJobOptions options = new() { Replace = request.Replace };

        if (request.Job is null)
        {
            DateTimeOffset firstFireTime = await scheduler.ScheduleJob(request.Trigger, options, cancellationToken).ConfigureAwait(false);
            return new ScheduleJobResponse(firstFireTime);
        }

        IJobDetail jobDetail = toJobDetail(request.Job);
        DateTimeOffset firstFireTimeWithJob = await scheduler.ScheduleJob(jobDetail, request.Trigger, options, cancellationToken).ConfigureAwait(false);
        return new ScheduleJobResponse(firstFireTimeWithJob);
    }

    /// <summary>
    /// Schedules every job the request carries with its triggers. Every job is converted before any of
    /// them is stored, so one refused type name refuses the whole batch rather than half of it.
    /// </summary>
    public static ValueTask ScheduleJobs(
        IScheduler scheduler,
        ScheduleJobsRequest request,
        Func<JobDetailDto, IJobDetail> toJobDetail,
        CancellationToken cancellationToken)
    {
        Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> jobsAndTriggers = new();
        foreach ((JobDetailDto jobDetailDto, ITrigger[] triggers) in request.JobsAndTriggers)
        {
            IJobDetail jobDetail = toJobDetail(jobDetailDto);
            jobsAndTriggers.Add(jobDetail, triggers);
        }

        return scheduler.ScheduleJobs(jobsAndTriggers, new ScheduleJobOptions { Replace = request.Replace }, cancellationToken);
    }

    public static async ValueTask<OperationAppliedResponse> UnscheduleJob(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken)
    {
        bool triggerFound = await scheduler.UnscheduleJob(triggerKey, cancellationToken).ConfigureAwait(false);
        return new OperationAppliedResponse(triggerFound);
    }

    /// <summary>
    /// Unschedules a set of triggers, answering with the keys it removed: a key that names no trigger is
    /// absent.
    /// </summary>
    public static async ValueTask<AppliedTriggerKeysResponse> UnscheduleJobs(IScheduler scheduler, UnscheduleJobsRequest request, CancellationToken cancellationToken)
    {
        TriggerKey[] triggerKeys = request.Triggers.Select(x => x.AsTriggerKey()).ToArray();
        List<TriggerKey> unscheduled = await scheduler.UnscheduleJobs(triggerKeys, cancellationToken).ConfigureAwait(false);
        return new AppliedTriggerKeysResponse([.. unscheduled.Select(KeyDto.Create)]);
    }

    /// <summary>
    /// Removes every trigger in the matching groups, answering with the keys it removed.
    /// </summary>
    public static async ValueTask<AppliedTriggerKeysResponse> UnscheduleJobsByGroup(IScheduler scheduler, ListingParameters groups, CancellationToken cancellationToken)
    {
        GroupMatcher<TriggerKey> matcher = groups.GroupFilter<TriggerKey>();
        List<TriggerKey> unscheduled = await scheduler.UnscheduleJobs(matcher, cancellationToken).ConfigureAwait(false);
        return new AppliedTriggerKeysResponse([.. unscheduled.Select(KeyDto.Create)]);
    }

    public static async ValueTask<RescheduleJobResponse> RescheduleJob(IScheduler scheduler, TriggerKey triggerKey, RescheduleJobRequest request, CancellationToken cancellationToken)
    {
        DateTimeOffset? firstFireTimeUtc = await scheduler.RescheduleJob(triggerKey, request.NewTrigger, cancellationToken).ConfigureAwait(false);
        return new RescheduleJobResponse(firstFireTimeUtc);
    }

    /// <summary>
    /// Edits a trigger's details in place, without touching its fire times or its state.
    /// </summary>
    public static async ValueTask<OperationAppliedResponse> UpdateTriggerDetails(IScheduler scheduler, TriggerKey triggerKey, UpdateTriggerDetailsRequest request, CancellationToken cancellationToken)
    {
        bool applied = await scheduler.UpdateTriggerDetails(triggerKey, request.AsUpdate(), cancellationToken).ConfigureAwait(false);
        return new OperationAppliedResponse(applied);
    }

    /// <summary>
    /// Backfills the trigger over the request's range, as
    /// <see cref="SchedulerBackfillExtensions.Backfill" /> does, or answers <see langword="null" /> when there
    /// is no trigger under <paramref name="triggerKey" />.
    /// </summary>
    /// <remarks>
    /// The extension's own run, which answers its refusals — an empty range, one reaching past the
    /// scheduler's current time, more slots than the options allow, an option out of range — rather than
    /// throwing them. Each is the request's, decided before anything is written, and is raised as
    /// <see cref="InvalidRequestException" /> with the extension's words. The scheduler's own failures while
    /// scheduling stay its own.
    /// </remarks>
    public static async ValueTask<BackfillResponse?> BackfillTrigger(
        IScheduler scheduler,
        TriggerKey triggerKey,
        BackfillRequest request,
        CancellationToken cancellationToken)
    {
        BackfillOutcome outcome = await Backfilling.Run(
            scheduler,
            triggerKey,
            request.From.GetValueOrDefault(),
            request.To.GetValueOrDefault(),
            request.AsOptions(),
            cancellationToken).ConfigureAwait(false);

        if (outcome.Refusal is { } refusal)
        {
            throw new InvalidRequestException(refusal.Reason);
        }

        return outcome.Result is { } result ? BackfillResponse.Create(result) : null;
    }

    // --- Calendars -------------------------------------------------------------------------------------

    public static async ValueTask<PagedResultDto<string>> QueryCalendarNames(IScheduler scheduler, ListingParameters listing, CancellationToken cancellationToken)
    {
        CalendarQuery query = listing.Page(new CalendarQuery
        {
            Name = listing.NameFilter()
        });

        PagedResult<string> page = await scheduler.QueryCalendarNames(query, cancellationToken).ConfigureAwait(false);
        return new PagedResultDto<string>(page.Items.ToArray(), page.HasMore, page.TotalCount);
    }

    /// <summary>
    /// The calendar, or <see langword="null" /> when there is none under <paramref name="calendarName" />.
    /// </summary>
    public static ValueTask<ICalendar?> GetCalendar(IScheduler scheduler, string calendarName, CancellationToken cancellationToken)
    {
        return scheduler.GetCalendar(calendarName, cancellationToken);
    }

    public static async ValueTask<ExistsResponse> CheckCalendarExists(IScheduler scheduler, string calendarName, CancellationToken cancellationToken)
    {
        bool exists = await scheduler.Exists(calendarName, cancellationToken).ConfigureAwait(false);
        return new ExistsResponse(exists);
    }

    public static ValueTask AddCalendar(IScheduler scheduler, AddCalendarRequest request, CancellationToken cancellationToken)
    {
        return scheduler.AddCalendar(
            request.CalendarName,
            request.Calendar,
            new AddCalendarOptions { Replace = request.Replace, UpdateTriggers = request.UpdateTriggers },
            cancellationToken);
    }

    public static async ValueTask<OperationAppliedResponse> DeleteCalendar(IScheduler scheduler, string calendarName, CancellationToken cancellationToken)
    {
        bool calendarFound = await scheduler.DeleteCalendar(calendarName, cancellationToken).ConfigureAwait(false);
        return new OperationAppliedResponse(calendarFound);
    }
}
