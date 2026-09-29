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

internal static class JobEndpoints
{
    public static IEnumerable<RouteHandlerBuilder> MapEndpoints(IEndpointRouteBuilder builder, QuartzHttpApiOptions options)
    {
        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.QueryJobs), QueryJobs)
            .WithQuartzDefaults(SchedulerRoutes.QueryJobs, "Query jobs");

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.FetchJobs), FetchJobs)
            .WithQuartzDefaults(SchedulerRoutes.FetchJobs, "Fetch jobs by key");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.GetJobDetails), GetJobDetails)
            .WithQuartzDefaults(SchedulerRoutes.GetJobDetails, "Get job details");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.CheckJobExists), CheckJobExists)
            .WithQuartzDefaults(SchedulerRoutes.CheckJobExists, "Check job exists");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.GetJobTriggers), GetJobTriggers)
            .WithQuartzDefaults(SchedulerRoutes.GetJobTriggers, "Get job triggers");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.QueryFireInstances), QueryFireInstances)
            .WithQuartzDefaults(SchedulerRoutes.QueryFireInstances, "Query fire instances");

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.PauseJob), PauseJob)
            .WithQuartzDefaults(SchedulerRoutes.PauseJob, "Pause job")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.PauseJobs), PauseJobs)
            .WithQuartzDefaults(SchedulerRoutes.PauseJobs, "Pause jobs")
            .WithQuartzMutation(options);

        // The key-set forms live under "keys" because the collection-level "pause" and "resume"
        // already belong to the group-matcher forms, which select by query string rather than body.
        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.PauseJobKeys), PauseJobKeys)
            .WithQuartzDefaults(SchedulerRoutes.PauseJobKeys, "Pause jobs by key")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.ResumeJob), ResumeJob)
            .WithQuartzDefaults(SchedulerRoutes.ResumeJob, "Resume job")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.ResumeJobs), ResumeJobs)
            .WithQuartzDefaults(SchedulerRoutes.ResumeJobs, "Resume jobs")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.ResumeJobKeys), ResumeJobKeys)
            .WithQuartzDefaults(SchedulerRoutes.ResumeJobKeys, "Resume jobs by key")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.TriggerJob), TriggerJob)
            .WithQuartzDefaults(SchedulerRoutes.TriggerJob, "Trigger job")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.InterruptJob), InterruptJob)
            .WithQuartzDefaults(SchedulerRoutes.InterruptJob, "Interrupt job")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.InterruptJobInstance), InterruptJobInstance)
            .WithQuartzDefaults(SchedulerRoutes.InterruptJobInstance, "Interrupt job instance")
            .WithQuartzMutation(options);

        yield return builder.MapDelete(options.PatternFor(SchedulerRoutes.DeleteJob), DeleteJob)
            .WithQuartzDefaults(SchedulerRoutes.DeleteJob, "Delete job")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.DeleteJobs), DeleteJobs)
            .WithQuartzDefaults(SchedulerRoutes.DeleteJobs, "Delete jobs")
            .WithQuartzMutation(options);

        // "delete" was taken by the key-set form before there was a group form, so the group form
        // says so in its path rather than taking the plain one away from an endpoint that has it.
        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.DeleteJobsByGroup), DeleteJobsByGroup)
            .WithQuartzDefaults(SchedulerRoutes.DeleteJobsByGroup, "Delete jobs by group")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.AddJob), AddJob)
            .WithQuartzDefaults(SchedulerRoutes.AddJob, "Add job")
            .WithQuartzMutation(options)
            .ProducesJobTypeRefusal(options);

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.QueryJobGroups), QueryJobGroups)
            .WithQuartzDefaults(SchedulerRoutes.QueryJobGroups, "Query job groups");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.IsJobGroupPaused), IsJobGroupPaused)
            .WithQuartzDefaults(SchedulerRoutes.IsJobGroupPaused, "Is job group paused");
    }

    [ProducesResponseType(typeof(PagedResultDto<JobHeaderDto>), StatusCodes.Status200OK)]
    private static Task<IResult> QueryJobs(
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

        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.QueryJobs(scheduler, listing, cancellationToken));
    }

    [ProducesResponseType(typeof(JobDetailDto[]), StatusCodes.Status200OK)]
    private static Task<IResult> FetchJobs(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        KeyDto[] request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertKeysToFetch(request);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.FetchJobs(scheduler, request, cancellationToken));
    }

    [ProducesResponseType(typeof(JobDetailDto), StatusCodes.Status200OK)]
    private static Task<IResult> GetJobDetails(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string jobGroup,
        string jobName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            JobKey jobKey = new(jobName, jobGroup);
            return await SchedulerOperations.GetJobDetails(scheduler, jobKey, cancellationToken).ConfigureAwait(false)
                   ?? throw NotFoundException.ForJob(jobKey);
        });
    }

    [ProducesResponseType(typeof(ExistsResponse), StatusCodes.Status200OK)]
    private static Task<IResult> CheckJobExists(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string jobGroup,
        string jobName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.CheckJobExists(scheduler, new JobKey(jobName, jobGroup), cancellationToken));
    }

    [ProducesResponseType(typeof(OpenApi.Trigger[]), StatusCodes.Status200OK)]
    private static Task<IResult> GetJobTriggers(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string jobGroup,
        string jobName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.GetJobTriggers(scheduler, new JobKey(jobName, jobGroup), cancellationToken));
    }

    [ProducesResponseType(typeof(PagedResultDto<FireInstanceDto>), StatusCodes.Status200OK)]
    private static Task<IResult> QueryFireInstances(
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
        string? schedulerInstanceId = null,
        string? state = null,
        CancellationToken cancellationToken = default)
    {
        ListingParameters listing = endpointHelper.Listing(skip, take, includeTotalCount, state) with
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

        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.QueryFireInstances(scheduler, listing, jobName, jobGroup, schedulerInstanceId, cancellationToken));
    }

    /// <summary>
    /// Pauses a job's triggers, recording why and who asked.
    /// </summary>
    /// <remarks>
    /// The body is optional, and so is each of its members. No body — or one that says nothing — is the
    /// reasonless pause, made through the reasonless member as before 4.3; with a body, a missing
    /// <c>requestedBy</c> is the authenticated user.
    /// </remarks>
    [ProducesResponseType(typeof(OperationAppliedResponse), StatusCodes.Status200OK)]
    private static Task<IResult> PauseJob(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        HttpContext httpContext,
        string schedulerName,
        string jobGroup,
        string jobName,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PauseRequest? request,
        CancellationToken cancellationToken = default)
    {
        PauseDetails? details = EndpointHelper.PauseDetailsFor(request, httpContext);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.PauseJob(scheduler, new JobKey(jobName, jobGroup), details, cancellationToken));
    }

    /// <summary>
    /// Pauses the matching job groups, recording why and who asked.
    /// </summary>
    /// <remarks>
    /// The body is optional, as on the single-job pause.
    /// </remarks>
    [ProducesResponseType(typeof(AffectedGroupsResponse), StatusCodes.Status200OK)]
    private static Task<IResult> PauseJobs(
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
            scheduler => SchedulerOperations.PauseJobs(scheduler, groups, details, cancellationToken));
    }

    /// <summary>
    /// Pauses a set of jobs, recording why and who asked when the body says so.
    /// </summary>
    /// <remarks>
    /// As on the key-set trigger pause: a body with neither <c>reason</c> nor <c>requestedBy</c> is the
    /// reasonless pause, even from an authenticated caller.
    /// </remarks>
    [ProducesResponseType(typeof(AppliedJobKeysResponse), StatusCodes.Status200OK)]
    private static Task<IResult> PauseJobKeys(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        HttpContext httpContext,
        string schedulerName,
        JobKeySetPauseRequest request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertIsValid(request);
        PauseDetails? details = EndpointHelper.KeySetPauseDetailsFor(request, httpContext);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.PauseJobKeys(scheduler, request, details, cancellationToken));
    }

    [ProducesResponseType(typeof(OperationAppliedResponse), StatusCodes.Status200OK)]
    private static Task<IResult> ResumeJob(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string jobGroup,
        string jobName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.ResumeJob(scheduler, new JobKey(jobName, jobGroup), cancellationToken));
    }

    [ProducesResponseType(typeof(AffectedGroupsResponse), StatusCodes.Status200OK)]
    private static Task<IResult> ResumeJobs(
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
            scheduler => SchedulerOperations.ResumeJobs(scheduler, groups, cancellationToken));
    }

    [ProducesResponseType(typeof(AppliedJobKeysResponse), StatusCodes.Status200OK)]
    private static Task<IResult> ResumeJobKeys(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        JobKeySetRequest request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertIsValid(request);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.ResumeJobKeys(scheduler, request, cancellationToken));
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> TriggerJob(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string jobGroup,
        string jobName,
        TriggerJobRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.TriggerJob(scheduler, new JobKey(jobName, jobGroup), request, cancellationToken));
    }

    [ProducesResponseType(typeof(OperationAppliedResponse), StatusCodes.Status200OK)]
    private static Task<IResult> InterruptJob(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string jobGroup,
        string jobName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.InterruptJob(scheduler, new JobKey(jobName, jobGroup), cancellationToken));
    }

    [ProducesResponseType(typeof(OperationAppliedResponse), StatusCodes.Status200OK)]
    private static Task<IResult> InterruptJobInstance(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string fireInstanceId,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.InterruptJobInstance(scheduler, fireInstanceId, cancellationToken));
    }

    [ProducesResponseType(typeof(OperationAppliedResponse), StatusCodes.Status200OK)]
    private static Task<IResult> DeleteJob(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string jobGroup,
        string jobName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.DeleteJob(scheduler, new JobKey(jobName, jobGroup), cancellationToken));
    }

    /// <summary>
    /// Deletes a set of jobs, answering with the keys it deleted.
    /// </summary>
    /// <remarks>
    /// A partial hit deletes the jobs it found, so the answer is the keys rather than a flag: a key
    /// that names no job is absent from the list, and <c>jobs.length == request.jobs.length</c> is
    /// the "every key was found" question a caller used to have to take on trust.
    /// </remarks>
    [ProducesResponseType(typeof(AppliedJobKeysResponse), StatusCodes.Status200OK)]
    private static Task<IResult> DeleteJobs(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        DeleteJobsRequest request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertIsValid(request);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.DeleteJobs(scheduler, request, cancellationToken));
    }

    /// <summary>
    /// Deletes every job in the matching groups, answering with the keys it deleted.
    /// </summary>
    /// <remarks>
    /// The group matcher is the same one the pause and resume endpoints take, and the answer is the
    /// keys rather than the group names: a delete leaves nothing behind to remember about a group,
    /// so what a caller can use is what went.
    /// </remarks>
    [ProducesResponseType(typeof(AppliedJobKeysResponse), StatusCodes.Status200OK)]
    private static Task<IResult> DeleteJobsByGroup(
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
            scheduler => SchedulerOperations.DeleteJobsByGroup(scheduler, groups, cancellationToken));
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> AddJob(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        AddJobRequest request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertIsValid(request);
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.AddJob(scheduler, request, dto => RequestedJobDetail.From(dto, endpointHelper.IsJobTypeAllowed), cancellationToken));
    }

    [ProducesResponseType(typeof(PagedResultDto<JobGroupDto>), StatusCodes.Status200OK)]
    private static Task<IResult> QueryJobGroups(
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
            scheduler => SchedulerOperations.QueryJobGroups(scheduler, listing, paused, cancellationToken));
    }

    [ProducesResponseType(typeof(GroupPausedResponse), StatusCodes.Status200OK)]
    private static Task<IResult> IsJobGroupPaused(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string jobGroup,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository,
            scheduler => SchedulerOperations.IsJobGroupPaused(scheduler, jobGroup, cancellationToken));
    }
}
