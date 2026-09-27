using System.ComponentModel;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;

using Quartz.AspNetCore.HttpApi.Util;
using Quartz.HttpApiContract;
using Quartz.Extensibility;

namespace Quartz.AspNetCore.HttpApi.Endpoints;

internal static class TriggerEndpoints
{
    public static IEnumerable<RouteHandlerBuilder> MapEndpoints(IEndpointRouteBuilder builder, QuartzHttpApiOptions options)
    {
        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.QueryTriggers), QueryTriggers)
            .WithQuartzDefaults(SchedulerRoutes.QueryTriggers, "Query triggers");

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.FetchTriggers), FetchTriggers)
            .WithQuartzDefaults(SchedulerRoutes.FetchTriggers, "Fetch triggers by key");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.GetTrigger), GetTrigger)
            .WithQuartzDefaults(SchedulerRoutes.GetTrigger, "Get trigger details");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.CheckTriggerExists), CheckTriggerExists)
            .WithQuartzDefaults(SchedulerRoutes.CheckTriggerExists, "Check trigger exists");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.GetTriggerState), GetTriggerState)
            .WithQuartzDefaults(SchedulerRoutes.GetTriggerState, "Get the current state of the trigger");

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.ResetTriggerFromErrorState), ResetTriggerFromErrorState)
            .WithQuartzDefaults(SchedulerRoutes.ResetTriggerFromErrorState, "Resets trigger from error state")
            .WithQuartzMutation(options);

        // The key-set forms live under "keys" because the collection-level "pause" and "resume"
        // already belong to the group-matcher forms, which select by query string rather than body.
        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.ResetTriggerKeysFromErrorState), ResetTriggerKeysFromErrorState)
            .WithQuartzDefaults(SchedulerRoutes.ResetTriggerKeysFromErrorState, "Resets triggers from error state by key")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.PauseTrigger), PauseTrigger)
            .WithQuartzDefaults(SchedulerRoutes.PauseTrigger, "Pause trigger")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.PauseTriggers), PauseTriggers)
            .WithQuartzDefaults(SchedulerRoutes.PauseTriggers, "Pause triggers")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.PauseTriggerKeys), PauseTriggerKeys)
            .WithQuartzDefaults(SchedulerRoutes.PauseTriggerKeys, "Pause triggers by key")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.ResumeTrigger), ResumeTrigger)
            .WithQuartzDefaults(SchedulerRoutes.ResumeTrigger, "Resume trigger")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.ResumeTriggers), ResumeTriggers)
            .WithQuartzDefaults(SchedulerRoutes.ResumeTriggers, "Resume triggers")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.ResumeTriggerKeys), ResumeTriggerKeys)
            .WithQuartzDefaults(SchedulerRoutes.ResumeTriggerKeys, "Resume triggers by key")
            .WithQuartzMutation(options);

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.QueryTriggerGroups), QueryTriggerGroups)
            .WithQuartzDefaults(SchedulerRoutes.QueryTriggerGroups, "Query trigger groups");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.IsTriggerGroupPaused), IsTriggerGroupPaused)
            .WithQuartzDefaults(SchedulerRoutes.IsTriggerGroupPaused, "Is trigger group paused");

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.ScheduleJob), ScheduleJob)
            .WithQuartzDefaults(SchedulerRoutes.ScheduleJob, "Schedule job")
            .WithQuartzMutation(options)
            .ProducesJobTypeRefusal(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.ScheduleJobs), ScheduleJobs)
            .WithQuartzDefaults(SchedulerRoutes.ScheduleJobs, "Schedule jobs")
            .WithQuartzMutation(options)
            .ProducesJobTypeRefusal(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.UnscheduleJob), UnscheduleJob)
            .WithQuartzDefaults(SchedulerRoutes.UnscheduleJob, "Unschedule job")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.UnscheduleJobs), UnscheduleJobs)
            .WithQuartzDefaults(SchedulerRoutes.UnscheduleJobs, "Unschedule jobs")
            .WithQuartzMutation(options);

        // "unschedule" was taken by the key-set form before there was a group form, so the group
        // form says so in its path rather than taking the plain one away from an endpoint that has it.
        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.UnscheduleJobsByGroup), UnscheduleJobsByGroup)
            .WithQuartzDefaults(SchedulerRoutes.UnscheduleJobsByGroup, "Unschedule jobs by group")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.RescheduleJob), RescheduleJob)
            .WithQuartzDefaults(SchedulerRoutes.RescheduleJob, "Reschedule job")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.UpdateTriggerDetails), UpdateTriggerDetails)
            .WithQuartzDefaults(SchedulerRoutes.UpdateTriggerDetails, "Update trigger details without rescheduling")
            .WithQuartzMutation(options);
    }

    [ProducesResponseType(typeof(PagedResultDto<TriggerHeaderDto>), StatusCodes.Status200OK)]
    private static Task<IResult> QueryTriggers(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        int skip = 0,
        [Description(EndpointHelper.TakeDescription)] string? take = null,
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
        [Description("Only triggers due to fire before this instant; a trigger with no next fire time never matches")]
        DateTimeOffset? nextFireTimeBefore = null,
        CancellationToken cancellationToken = default)
    {
        ListingParameters listing = endpointHelper.Listing(skip, take, includeTotalCount) with
        {
            GroupContains = groupContains,
            GroupEndsWith = groupEndsWith,
            GroupStartsWith = groupStartsWith,
            GroupEquals = groupEquals,
            NameContains = nameContains,
            NameEndsWith = nameEndsWith,
            NameStartsWith = nameStartsWith,
            NameEquals = nameEquals
        };

        bool hasJobName = !string.IsNullOrWhiteSpace(jobName);
        bool hasJobGroup = !string.IsNullOrWhiteSpace(jobGroup);
        if (hasJobName != hasJobGroup)
        {
            throw new BadHttpRequestException("Both jobName and jobGroup must be given to filter by job");
        }

        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, scheduler => SchedulerOperations.QueryTriggers(
            scheduler,
            listing,
            hasJobName ? new JobKey(jobName!, jobGroup!) : null,
            calendarName,
            state,
            nextFireTimeBefore,
            cancellationToken));
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.FetchTriggers(scheduler, request, cancellationToken));
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
            TriggerKey triggerKey = new(triggerName, triggerGroup);
            return await SchedulerOperations.GetTrigger(scheduler, triggerKey, cancellationToken).ConfigureAwait(false)
                   ?? throw NotFoundException.ForTrigger(triggerKey);
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.CheckTriggerExists(scheduler, new TriggerKey(triggerName, triggerGroup), cancellationToken));
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.GetTriggerState(scheduler, new TriggerKey(triggerName, triggerGroup), cancellationToken));
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.ResetTriggerFromErrorState(scheduler, new TriggerKey(triggerName, triggerGroup), cancellationToken));
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.ResetTriggerKeysFromErrorState(scheduler, request, cancellationToken));
    }

    /// <summary>
    /// Pauses a trigger, recording why and who asked.
    /// </summary>
    /// <remarks>
    /// The body is optional, and so is each of its members. No body — or one that says nothing — is the
    /// reasonless pause, made through the reasonless member as before 4.3; with a body, a missing
    /// <c>requestedBy</c> is the authenticated user.
    /// </remarks>
    [ProducesResponseType(typeof(OperationAppliedResponse), StatusCodes.Status200OK)]
    private static Task<IResult> PauseTrigger(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        HttpContext httpContext,
        string schedulerName,
        string triggerGroup,
        string triggerName,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PauseRequest? request,
        CancellationToken cancellationToken = default)
    {
        PauseDetails? details = EndpointHelper.PauseDetailsFor(request, httpContext);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.PauseTrigger(scheduler, new TriggerKey(triggerName, triggerGroup), details, cancellationToken));
    }

    /// <summary>
    /// Pauses the matching trigger groups, recording why and who asked.
    /// </summary>
    /// <remarks>
    /// The body is optional, as on the single-trigger pause.
    /// </remarks>
    [ProducesResponseType(typeof(AffectedGroupsResponse), StatusCodes.Status200OK)]
    private static Task<IResult> PauseTriggers(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        HttpContext httpContext,
        string schedulerName,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PauseRequest? request,
        string? groupContains = null,
        string? groupEndsWith = null,
        string? groupStartsWith = null,
        string? groupEquals = null,
        CancellationToken cancellationToken = default)
    {
        PauseDetails? details = EndpointHelper.PauseDetailsFor(request, httpContext);
        ListingParameters groups = ListingParameters.Groups(groupContains, groupEndsWith, groupStartsWith, groupEquals);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.PauseTriggers(scheduler, groups, details, cancellationToken));
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.PauseTriggerKeys(scheduler, request, cancellationToken));
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.ResumeTrigger(scheduler, new TriggerKey(triggerName, triggerGroup), cancellationToken));
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
        ListingParameters groups = ListingParameters.Groups(groupContains, groupEndsWith, groupStartsWith, groupEquals);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.ResumeTriggers(scheduler, groups, cancellationToken));
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.ResumeTriggerKeys(scheduler, request, cancellationToken));
    }

    [ProducesResponseType(typeof(PagedResultDto<TriggerGroupDto>), StatusCodes.Status200OK)]
    private static Task<IResult> QueryTriggerGroups(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        int skip = 0,
        [Description(EndpointHelper.TakeDescription)] string? take = null,
        bool includeTotalCount = false,
        bool? paused = null,
        string? nameContains = null,
        string? nameEndsWith = null,
        string? nameStartsWith = null,
        string? nameEquals = null,
        CancellationToken cancellationToken = default)
    {
        ListingParameters listing = endpointHelper.Listing(skip, take, includeTotalCount) with
        {
            NameContains = nameContains,
            NameEndsWith = nameEndsWith,
            NameStartsWith = nameStartsWith,
            NameEquals = nameEquals
        };

        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.QueryTriggerGroups(scheduler, listing, paused, cancellationToken));
    }

    [ProducesResponseType(typeof(GroupPausedResponse), StatusCodes.Status200OK)]
    private static Task<IResult> IsTriggerGroupPaused(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string triggerGroup,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.IsTriggerGroupPaused(scheduler, triggerGroup, cancellationToken));
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.ScheduleJob(scheduler, request, dto => RequestedJobDetail.From(dto, endpointHelper.IsJobTypeAllowed), cancellationToken));
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
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.ScheduleJobs(scheduler, request, dto => RequestedJobDetail.From(dto, endpointHelper.IsJobTypeAllowed), cancellationToken));
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.UnscheduleJob(scheduler, new TriggerKey(triggerName, triggerGroup), cancellationToken));
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.UnscheduleJobs(scheduler, request, cancellationToken));
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
        ListingParameters groups = ListingParameters.Groups(groupContains, groupEndsWith, groupStartsWith, groupEquals);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.UnscheduleJobsByGroup(scheduler, groups, cancellationToken));
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.RescheduleJob(scheduler, new TriggerKey(triggerName, triggerGroup), request, cancellationToken));
    }

    /// <summary>
    /// Edits a trigger's details in place — description, priority, job data, calendar, misfire
    /// instruction, node pin, execution group and retry policy — without touching its fire times or its
    /// state.
    /// </summary>
    /// <remarks>
    /// A body member that is absent leaves its value alone and one present as <c>null</c> clears it, so
    /// this is a patch rather than a replacement; <c>UpdateTriggerDetailsRequest</c> says why. A trigger
    /// the key does not resolve answers <c>{ "applied": false }</c> rather than <c>404</c>, which is
    /// what <see cref="IScheduler.UpdateTriggerDetails" /> answers and what every other single-trigger
    /// mutation on this route family answers.
    /// </remarks>
    [ProducesResponseType(typeof(OperationAppliedResponse), StatusCodes.Status200OK)]
    [Consumes(typeof(OpenApi.UpdateTriggerDetailsRequest), "application/json")]
    private static Task<IResult> UpdateTriggerDetails(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string triggerGroup,
        string triggerName,
        UpdateTriggerDetailsRequest request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertIsValid(request);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.UpdateTriggerDetails(scheduler, new TriggerKey(triggerName, triggerGroup), request, cancellationToken));
    }
}
