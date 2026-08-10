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

        yield return builder.MapPost(patternPrefix + "/{triggerGroup}/{triggerName}/pause", PauseTrigger)
            .WithQuartzDefaults(nameof(PauseTrigger), "Pause trigger");

        yield return builder.MapPost(patternPrefix + "/pause", PauseTriggers)
            .WithQuartzDefaults(nameof(PauseTriggers), "Pause triggers");

        yield return builder.MapPost(patternPrefix + "/{triggerGroup}/{triggerName}/resume", ResumeTrigger)
            .WithQuartzDefaults(nameof(ResumeTrigger), "Resume trigger");

        yield return builder.MapPost(patternPrefix + "/resume", ResumeTriggers)
            .WithQuartzDefaults(nameof(ResumeTriggers), "Resume triggers");

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

        yield return builder.MapPost(patternPrefix + "/{triggerGroup}/{triggerName}/reschedule", RescheduleJob)
            .WithQuartzDefaults(nameof(RescheduleJob), "Reschedule job");
    }

    [ProducesResponseType(typeof(PagedResultDto<TriggerHeaderDto>), StatusCodes.Status200OK)]
    private static Task<IResult> QueryTriggers(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        int skip = 0,
        int? take = null,
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
        EndpointHelper.AssertPaging(skip, take);

        bool hasJobName = !string.IsNullOrWhiteSpace(jobName);
        bool hasJobGroup = !string.IsNullOrWhiteSpace(jobGroup);
        if (hasJobName != hasJobGroup)
        {
            throw new BadHttpRequestException("Both jobName and jobGroup must be given to filter by job");
        }

        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
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
            if (take.HasValue)
            {
                query = query with { Take = take.Value };
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
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
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
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
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
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
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
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
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
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var applied = await scheduler.ResetTriggerFromErrorState(new TriggerKey(triggerName, triggerGroup), cancellationToken).ConfigureAwait(false);
            return new OperationAppliedResponse(applied);
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
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
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
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var matcher = EndpointHelper.GetGroupMatcher<TriggerKey>(groupContains, groupEndsWith, groupStartsWith, groupEquals);
            var pausedGroups = await scheduler.PauseTriggers(matcher, cancellationToken).ConfigureAwait(false);
            return new AffectedGroupsResponse([.. pausedGroups]);
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
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
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
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var matcher = EndpointHelper.GetGroupMatcher<TriggerKey>(groupContains, groupEndsWith, groupStartsWith, groupEquals);
            var resumedGroups = await scheduler.ResumeTriggers(matcher, cancellationToken).ConfigureAwait(false);
            return new AffectedGroupsResponse([.. resumedGroups]);
        });
    }

    [ProducesResponseType(typeof(PagedResultDto<TriggerGroupDto>), StatusCodes.Status200OK)]
    private static Task<IResult> QueryTriggerGroups(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        int skip = 0,
        int? take = null,
        bool includeTotalCount = false,
        bool? paused = null,
        string? name = null,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertPaging(skip, take);
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            TriggerGroupQuery query = new()
            {
                Name = name,
                Paused = paused,
                Skip = skip,
                IncludeTotalCount = includeTotalCount
            };

            // a request that names no take gets the query record's own default page size
            if (take.HasValue)
            {
                query = query with { Take = take.Value };
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
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
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
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            if (request.Job is null)
            {
                var firstFireTime = await scheduler.ScheduleJob(request.Trigger, cancellationToken).ConfigureAwait(false);
                return new ScheduleJobResponse(firstFireTime);
            }

            var jobDetail = request.Job.AsIJobDetail().JobDetail!;
            var firstFireTimeWithJob = await scheduler.ScheduleJob(jobDetail, request.Trigger, cancellationToken).ConfigureAwait(false);
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
                var jobDetail = jobDetailDto.AsIJobDetail().JobDetail!;
                jobsAndTriggers.Add(jobDetail, triggers);
            }

            await scheduler.ScheduleJobs(jobsAndTriggers, request.Replace, cancellationToken).ConfigureAwait(false);
        });
    }

    [ProducesResponseType(typeof(UnscheduleJobResponse), StatusCodes.Status200OK)]
    private static Task<IResult> UnscheduleJob(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string triggerGroup,
        string triggerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var triggerFound = await scheduler.UnscheduleJob(new TriggerKey(triggerName, triggerGroup), cancellationToken).ConfigureAwait(false);
            return new UnscheduleJobResponse(triggerFound);
        });
    }

    [ProducesResponseType(typeof(UnscheduleJobsResponse), StatusCodes.Status200OK)]
    private static Task<IResult> UnscheduleJobs(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        UnscheduleJobsRequest request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertIsValid(request);
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var triggerKeys = request.Triggers.Select(x => x.AsTriggerKey()).ToArray();
            var allTriggersFound = await scheduler.UnscheduleJobs(triggerKeys, cancellationToken).ConfigureAwait(false);
            return new UnscheduleJobsResponse(allTriggersFound);
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
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var firstFireTimeUtc = await scheduler.RescheduleJob(new TriggerKey(triggerName, triggerGroup), request.NewTrigger, cancellationToken).ConfigureAwait(false);
            return new RescheduleJobResponse(firstFireTimeUtc);
        });
    }
}