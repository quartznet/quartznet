using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using Quartz.AspNetCore.HttpApi.Util;
using Quartz.HttpApiContract;
using Quartz.Extensibility;

namespace Quartz.AspNetCore.HttpApi.Endpoints;

internal static class TriggerEndpoints
{
    public static IEnumerable<RouteHandlerBuilder> MapEndpoints(IEndpointRouteBuilder builder, QuartzHttpApiOptions options)
    {
        var patternPrefix = $"{options.TrimmedApiPath}/schedulers/{{schedulerName}}/triggers";

        yield return builder.MapGet(patternPrefix, QueryTriggers)
            .WithQuartzDefaults(nameof(QueryTriggers), "Query triggers");

        yield return builder.MapPost(patternPrefix + "/fetch", FetchTriggers)
            .WithQuartzDefaults(nameof(FetchTriggers), "Fetch triggers by key");

        yield return builder.MapGet(patternPrefix + "/{triggerGroup}/{triggerName}", GetTrigger)
            .WithQuartzDefaults(nameof(GetTrigger), "Get trigger details");

        yield return builder.MapGet(patternPrefix + "/{triggerGroup}/{triggerName}/exists", CheckTriggerExists)
            .WithQuartzDefaults(nameof(CheckTriggerExists), "Check trigger exists");

        yield return builder.MapGet(patternPrefix + "/{triggerGroup}/{triggerName}/state", GetTriggerState)
            .WithQuartzDefaults(nameof(GetTriggerState), "Get the current state of the trigger");

        yield return builder.MapPost(patternPrefix + "/{triggerGroup}/{triggerName}/reset-from-error-state", ResetTriggerFromErrorState)
            .WithQuartzDefaults(nameof(ResetTriggerFromErrorState), "Resets trigger from error state");

        // The key-set forms live under "keys" because the collection-level "pause" and "resume"
        // already belong to the group-matcher forms, which select by query string rather than body.
        yield return builder.MapPost(patternPrefix + "/keys/reset-from-error-state", ResetTriggerKeysFromErrorState)
            .WithQuartzDefaults(nameof(ResetTriggerKeysFromErrorState), "Resets triggers from error state by key");

        yield return builder.MapPost(patternPrefix + "/{triggerGroup}/{triggerName}/pause", PauseTrigger)
            .WithQuartzDefaults(nameof(PauseTrigger), "Pause trigger");

        yield return builder.MapPost(patternPrefix + "/pause", PauseTriggers)
            .WithQuartzDefaults(nameof(PauseTriggers), "Pause triggers");

        yield return builder.MapPost(patternPrefix + "/keys/pause", PauseTriggerKeys)
            .WithQuartzDefaults(nameof(PauseTriggerKeys), "Pause triggers by key");

        yield return builder.MapPost(patternPrefix + "/{triggerGroup}/{triggerName}/resume", ResumeTrigger)
            .WithQuartzDefaults(nameof(ResumeTrigger), "Resume trigger");

        yield return builder.MapPost(patternPrefix + "/resume", ResumeTriggers)
            .WithQuartzDefaults(nameof(ResumeTriggers), "Resume triggers");

        yield return builder.MapPost(patternPrefix + "/keys/resume", ResumeTriggerKeys)
            .WithQuartzDefaults(nameof(ResumeTriggerKeys), "Resume triggers by key");

        yield return builder.MapGet(patternPrefix + "/groups", QueryTriggerGroups)
            .WithQuartzDefaults(nameof(QueryTriggerGroups), "Query trigger groups");

        yield return builder.MapGet(patternPrefix + "/groups/{triggerGroup}/paused", IsTriggerGroupPaused)
            .WithQuartzDefaults(nameof(IsTriggerGroupPaused), "Is trigger group paused");

        yield return builder.MapPost(patternPrefix + "/schedule", ScheduleJob)
            .WithQuartzDefaults(nameof(ScheduleJob), "Schedule job");

        yield return builder.MapPost(patternPrefix + "/schedule-multiple", ScheduleJobs)
            .WithQuartzDefaults(nameof(ScheduleJobs), "Schedule jobs");

        yield return builder.MapPost(patternPrefix + "/{triggerGroup}/{triggerName}/unschedule", UnscheduleJob)
            .WithQuartzDefaults(nameof(UnscheduleJob), "Unschedule job");

        yield return builder.MapPost(patternPrefix + "/unschedule", UnscheduleJobs)
            .WithQuartzDefaults(nameof(UnscheduleJobs), "Unschedule jobs");

        // "unschedule" was taken by the key-set form before there was a group form, so the group
        // form says so in its path rather than taking the plain one away from an endpoint that has it.
        yield return builder.MapPost(patternPrefix + "/unschedule-by-group", UnscheduleJobsByGroup)
            .WithQuartzDefaults(nameof(UnscheduleJobsByGroup), "Unschedule jobs by group");

        yield return builder.MapPost(patternPrefix + "/{triggerGroup}/{triggerName}/reschedule", RescheduleJob)
            .WithQuartzDefaults(nameof(RescheduleJob), "Reschedule job");
    }

    [ProducesResponseType(typeof(PagedResultDto<TriggerHeaderDto>), StatusCodes.Status200OK)]
    private static Task<IResult> QueryTriggers(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        int skip = 0,
        string? take = null,
        bool includeTotalCount = false,
        string? groupContains = null,
        string? groupEndsWith = null,
        string? groupStartsWith = null,
        string? groupEquals = null,
        string? nameContains = null,
        string? nameEndsWith = null,
        string? nameStartsWith = null,
        string? nameEquals = null,
        string? jobName = null,
        string? jobGroup = null,
        string? calendarName = null,
        TriggerState? state = null,
        CancellationToken cancellationToken = default)
    {
        int? takeItems = EndpointHelper.ParsePaging(skip, take);

        bool hasJobName = !string.IsNullOrWhiteSpace(jobName);
        bool hasJobGroup = !string.IsNullOrWhiteSpace(jobGroup);
        if (hasJobName != hasJobGroup)
        {
            throw new BadHttpRequestException("Both jobName and jobGroup must be given to filter by job");
        }

        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            GroupMatcher<TriggerKey> matcher = EndpointHelper.GetGroupMatcher<TriggerKey>(groupContains, groupEndsWith, groupStartsWith, groupEquals);
            TriggerQuery query = new()
            {
                Group = matcher,
                Name = EndpointHelper.GetNameMatcher<TriggerKey>(nameContains, nameEndsWith, nameStartsWith, nameEquals),
                Job = hasJobName ? new JobKey(jobName!, jobGroup!) : null,
                CalendarName = calendarName,
                State = state,
                Skip = skip,
                IncludeTotalCount = includeTotalCount
            };

            // a request that names no take gets the query record's own default page size
            if (takeItems.HasValue)
            {
                query = query with { Take = takeItems.Value };
            }

            PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(query, cancellationToken).ConfigureAwait(false);
            return new PagedResultDto<TriggerHeaderDto>(page.Items.Select(TriggerHeaderDto.Create).ToArray(), page.HasMore, page.TotalCount);
        });
    }

    [ProducesResponseType(typeof(OpenApi.Trigger[]), StatusCodes.Status200OK)]
    private static Task<IResult> FetchTriggers(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        KeyDto[] request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertKeysToFetch(request);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            TriggerKey[] triggerKeys = request.Select(x => x.AsTriggerKey()).ToArray();
            List<ITrigger> triggers = await scheduler.GetTriggers(triggerKeys, cancellationToken).ConfigureAwait(false);
            return triggers;
        });
    }

    [ProducesResponseType(typeof(OpenApi.Trigger), StatusCodes.Status200OK)]
    private static Task<IResult> GetTrigger(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string triggerGroup,
        string triggerName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var trigger = await scheduler.GetTriggerOrThrow(triggerName, triggerGroup, cancellationToken).ConfigureAwait(false);
            return trigger;
        });
    }

    [ProducesResponseType(typeof(ExistsResponse), StatusCodes.Status200OK)]
    private static Task<IResult> CheckTriggerExists(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string triggerGroup,
        string triggerName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var exists = await scheduler.Exists(new TriggerKey(triggerName, triggerGroup), cancellationToken).ConfigureAwait(false);
            return new ExistsResponse(exists);
        });
    }

    [ProducesResponseType(typeof(TriggerStateDto), StatusCodes.Status200OK)]
    private static Task<IResult> GetTriggerState(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string triggerGroup,
        string triggerName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var state = await scheduler.GetTriggerState(new TriggerKey(triggerName, triggerGroup), cancellationToken).ConfigureAwait(false);
            return new TriggerStateDto(state);
        });
    }

    [ProducesResponseType(typeof(OperationAppliedResponse), StatusCodes.Status200OK)]
    private static Task<IResult> ResetTriggerFromErrorState(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string triggerGroup,
        string triggerName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var applied = await scheduler.ResetTriggerFromErrorState(new TriggerKey(triggerName, triggerGroup), cancellationToken).ConfigureAwait(false);
            return new OperationAppliedResponse(applied);
        });
    }

    [ProducesResponseType(typeof(AppliedTriggerKeysResponse), StatusCodes.Status200OK)]
    private static Task<IResult> ResetTriggerKeysFromErrorState(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        TriggerKeySetRequest request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertIsValid(request);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var triggerKeys = request.Triggers.Select(x => x.AsTriggerKey()).ToArray();
            var reset = await scheduler.ResetTriggersFromErrorState(triggerKeys, cancellationToken).ConfigureAwait(false);
            return new AppliedTriggerKeysResponse([.. reset.Select(KeyDto.Create)]);
        });
    }

    [ProducesResponseType(typeof(OperationAppliedResponse), StatusCodes.Status200OK)]
    private static Task<IResult> PauseTrigger(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string triggerGroup,
        string triggerName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var applied = await scheduler.PauseTrigger(new TriggerKey(triggerName, triggerGroup), cancellationToken).ConfigureAwait(false);
            return new OperationAppliedResponse(applied);
        });
    }

    [ProducesResponseType(typeof(AffectedGroupsResponse), StatusCodes.Status200OK)]
    private static Task<IResult> PauseTriggers(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string? groupContains = null,
        string? groupEndsWith = null,
        string? groupStartsWith = null,
        string? groupEquals = null,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var matcher = EndpointHelper.GetGroupMatcher<TriggerKey>(groupContains, groupEndsWith, groupStartsWith, groupEquals);
            var pausedGroups = await scheduler.PauseTriggerGroups(matcher, cancellationToken).ConfigureAwait(false);
            return new AffectedGroupsResponse([.. pausedGroups]);
        });
    }

    [ProducesResponseType(typeof(AppliedTriggerKeysResponse), StatusCodes.Status200OK)]
    private static Task<IResult> PauseTriggerKeys(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        TriggerKeySetRequest request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertIsValid(request);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var triggerKeys = request.Triggers.Select(x => x.AsTriggerKey()).ToArray();
            var paused = await scheduler.PauseTriggers(triggerKeys, cancellationToken).ConfigureAwait(false);
            return new AppliedTriggerKeysResponse([.. paused.Select(KeyDto.Create)]);
        });
    }

    [ProducesResponseType(typeof(OperationAppliedResponse), StatusCodes.Status200OK)]
    private static Task<IResult> ResumeTrigger(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string triggerGroup,
        string triggerName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var applied = await scheduler.ResumeTrigger(new TriggerKey(triggerName, triggerGroup), cancellationToken).ConfigureAwait(false);
            return new OperationAppliedResponse(applied);
        });
    }

    [ProducesResponseType(typeof(AffectedGroupsResponse), StatusCodes.Status200OK)]
    private static Task<IResult> ResumeTriggers(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string? groupContains = null,
        string? groupEndsWith = null,
        string? groupStartsWith = null,
        string? groupEquals = null,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var matcher = EndpointHelper.GetGroupMatcher<TriggerKey>(groupContains, groupEndsWith, groupStartsWith, groupEquals);
            var resumedGroups = await scheduler.ResumeTriggerGroups(matcher, cancellationToken).ConfigureAwait(false);
            return new AffectedGroupsResponse([.. resumedGroups]);
        });
    }

    [ProducesResponseType(typeof(AppliedTriggerKeysResponse), StatusCodes.Status200OK)]
    private static Task<IResult> ResumeTriggerKeys(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        TriggerKeySetRequest request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertIsValid(request);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var triggerKeys = request.Triggers.Select(x => x.AsTriggerKey()).ToArray();
            var resumed = await scheduler.ResumeTriggers(triggerKeys, cancellationToken).ConfigureAwait(false);
            return new AppliedTriggerKeysResponse([.. resumed.Select(KeyDto.Create)]);
        });
    }

    [ProducesResponseType(typeof(PagedResultDto<TriggerGroupDto>), StatusCodes.Status200OK)]
    private static Task<IResult> QueryTriggerGroups(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        int skip = 0,
        string? take = null,
        bool includeTotalCount = false,
        bool? paused = null,
        string? nameContains = null,
        string? nameEndsWith = null,
        string? nameStartsWith = null,
        string? nameEquals = null,
        CancellationToken cancellationToken = default)
    {
        int? takeItems = EndpointHelper.ParsePaging(skip, take);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            TriggerGroupQuery query = new()
            {
                Name = EndpointHelper.GetNameMatcher(nameContains, nameEndsWith, nameStartsWith, nameEquals),
                Paused = paused,
                Skip = skip,
                IncludeTotalCount = includeTotalCount
            };

            // a request that names no take gets the query record's own default page size
            if (takeItems.HasValue)
            {
                query = query with { Take = takeItems.Value };
            }

            PagedResult<TriggerGroup> page = await scheduler.QueryTriggerGroups(query, cancellationToken).ConfigureAwait(false);
            return new PagedResultDto<TriggerGroupDto>(page.Items.Select(TriggerGroupDto.Create).ToArray(), page.HasMore, page.TotalCount);
        });
    }

    [ProducesResponseType(typeof(GroupPausedResponse), StatusCodes.Status200OK)]
    private static Task<IResult> IsTriggerGroupPaused(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string triggerGroup,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            bool paused = await scheduler.IsTriggerGroupPaused(triggerGroup, cancellationToken).ConfigureAwait(false);
            return new GroupPausedResponse(paused);
        });
    }

    [ProducesResponseType(typeof(ScheduleJobResponse), StatusCodes.Status200OK)]
    [Consumes(typeof(OpenApi.ScheduleJobRequest), "application/json")]
    private static Task<IResult> ScheduleJob(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        ScheduleJobRequest request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertIsValid(request);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            ScheduleJobOptions options = new() { Replace = request.Replace };

            if (request.Job is null)
            {
                var firstFireTime = await scheduler.ScheduleJob(request.Trigger, options, cancellationToken).ConfigureAwait(false);
                return new ScheduleJobResponse(firstFireTime);
            }

            IJobDetail jobDetail = RequestedJobDetail.From(request.Job);
            var firstFireTimeWithJob = await scheduler.ScheduleJob(jobDetail, request.Trigger, options, cancellationToken).ConfigureAwait(false);
            return new ScheduleJobResponse(firstFireTimeWithJob);
        });
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    [Consumes(typeof(OpenApi.ScheduleJobsRequest), "application/json")]
    private static Task<IResult> ScheduleJobs(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        ScheduleJobsRequest request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertIsValid(request);
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var jobsAndTriggers = new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>>();
            foreach (var (jobDetailDto, triggers) in request.JobsAndTriggers)
            {
                IJobDetail jobDetail = RequestedJobDetail.From(jobDetailDto);
                jobsAndTriggers.Add(jobDetail, triggers);
            }

            await scheduler.ScheduleJobs(jobsAndTriggers, new ScheduleJobOptions { Replace = request.Replace }, cancellationToken).ConfigureAwait(false);
        });
    }

    [ProducesResponseType(typeof(OperationAppliedResponse), StatusCodes.Status200OK)]
    private static Task<IResult> UnscheduleJob(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string triggerGroup,
        string triggerName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var triggerFound = await scheduler.UnscheduleJob(new TriggerKey(triggerName, triggerGroup), cancellationToken).ConfigureAwait(false);
            return new OperationAppliedResponse(triggerFound);
        });
    }

    /// <summary>
    /// Unschedules a set of triggers, answering with the keys it removed.
    /// </summary>
    /// <remarks>
    /// A partial hit unschedules the triggers it found, so the answer is the keys rather than a flag:
    /// a key that names no trigger is absent from the list, and
    /// <c>triggers.length == request.triggers.length</c> is the "every key was found" question a
    /// caller used to have to take on trust.
    /// </remarks>
    [ProducesResponseType(typeof(AppliedTriggerKeysResponse), StatusCodes.Status200OK)]
    private static Task<IResult> UnscheduleJobs(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        UnscheduleJobsRequest request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertIsValid(request);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var triggerKeys = request.Triggers.Select(x => x.AsTriggerKey()).ToArray();
            var unscheduled = await scheduler.UnscheduleJobs(triggerKeys, cancellationToken).ConfigureAwait(false);
            return new AppliedTriggerKeysResponse([.. unscheduled.Select(KeyDto.Create)]);
        });
    }

    /// <summary>
    /// Removes every trigger in the matching groups, answering with the keys it removed.
    /// </summary>
    /// <remarks>
    /// The group matcher is the same one the pause and resume endpoints take, and the answer is the
    /// keys rather than the group names: unscheduling leaves nothing behind to remember about a
    /// group, so what a caller can use is what went. A job left with no triggers and no durability
    /// goes with them, and is not named — the answer is about triggers.
    /// </remarks>
    [ProducesResponseType(typeof(AppliedTriggerKeysResponse), StatusCodes.Status200OK)]
    private static Task<IResult> UnscheduleJobsByGroup(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string? groupContains = null,
        string? groupEndsWith = null,
        string? groupStartsWith = null,
        string? groupEquals = null,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var matcher = EndpointHelper.GetGroupMatcher<TriggerKey>(groupContains, groupEndsWith, groupStartsWith, groupEquals);
            var unscheduled = await scheduler.UnscheduleJobs(matcher, cancellationToken).ConfigureAwait(false);
            return new AppliedTriggerKeysResponse([.. unscheduled.Select(KeyDto.Create)]);
        });
    }

    [ProducesResponseType(typeof(RescheduleJobResponse), StatusCodes.Status200OK)]
    [Consumes(typeof(OpenApi.RescheduleJobRequest), "application/json")]
    private static Task<IResult> RescheduleJob(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string triggerGroup,
        string triggerName,
        RescheduleJobRequest request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertIsValid(request);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var firstFireTimeUtc = await scheduler.RescheduleJob(new TriggerKey(triggerName, triggerGroup), request.NewTrigger, cancellationToken).ConfigureAwait(false);
            return new RescheduleJobResponse(firstFireTimeUtc);
        });
    }
}