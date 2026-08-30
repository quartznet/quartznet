using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using Quartz.AspNetCore.HttpApi.Util;
using Quartz.HttpApiContract;
using Quartz.Extensibility;

namespace Quartz.AspNetCore.HttpApi.Endpoints;

internal static class JobEndpoints
{
    public static IEnumerable<RouteHandlerBuilder> MapEndpoints(IEndpointRouteBuilder builder, QuartzHttpApiOptions options)
    {
        var patternPrefix = $"{options.TrimmedApiPath}/schedulers/{{schedulerName}}/jobs";

        yield return builder.MapGet(patternPrefix, QueryJobs)
            .WithQuartzDefaults(nameof(QueryJobs), "Query jobs");

        yield return builder.MapPost(patternPrefix + "/fetch", FetchJobs)
            .WithQuartzDefaults(nameof(FetchJobs), "Fetch jobs by key");

        yield return builder.MapGet(patternPrefix + "/{jobGroup}/{jobName}", GetJobDetails)
            .WithQuartzDefaults(nameof(GetJobDetails), "Get job details");

        yield return builder.MapGet(patternPrefix + "/{jobGroup}/{jobName}/exists", CheckJobExists)
            .WithQuartzDefaults(nameof(CheckJobExists), "Check job exists");

        yield return builder.MapGet(patternPrefix + "/{jobGroup}/{jobName}/triggers", GetJobTriggers)
            .WithQuartzDefaults(nameof(GetJobTriggers), "Get job triggers");

        yield return builder.MapGet(patternPrefix + "/fire-instances", QueryFireInstances)
            .WithQuartzDefaults(nameof(QueryFireInstances), "Query fire instances");

        yield return builder.MapPost(patternPrefix + "/{jobGroup}/{jobName}/pause", PauseJob)
            .WithQuartzDefaults(nameof(PauseJob), "Pause job");

        yield return builder.MapPost(patternPrefix + "/pause", PauseJobs)
            .WithQuartzDefaults(nameof(PauseJobs), "Pause jobs");

        // The key-set forms live under "keys" because the collection-level "pause" and "resume"
        // already belong to the group-matcher forms, which select by query string rather than body.
        yield return builder.MapPost(patternPrefix + "/keys/pause", PauseJobKeys)
            .WithQuartzDefaults(nameof(PauseJobKeys), "Pause jobs by key");

        yield return builder.MapPost(patternPrefix + "/{jobGroup}/{jobName}/resume", ResumeJob)
            .WithQuartzDefaults(nameof(ResumeJob), "Resume job");

        yield return builder.MapPost(patternPrefix + "/resume", ResumeJobs)
            .WithQuartzDefaults(nameof(ResumeJobs), "Resume jobs");

        yield return builder.MapPost(patternPrefix + "/keys/resume", ResumeJobKeys)
            .WithQuartzDefaults(nameof(ResumeJobKeys), "Resume jobs by key");

        yield return builder.MapPost(patternPrefix + "/{jobGroup}/{jobName}/trigger", TriggerJob)
            .WithQuartzDefaults(nameof(TriggerJob), "Trigger job");

        yield return builder.MapPost(patternPrefix + "/{jobGroup}/{jobName}/interrupt", InterruptJob)
            .WithQuartzDefaults(nameof(InterruptJob), "Interrupt job");

        yield return builder.MapPost(patternPrefix + "/interrupt/{fireInstanceId}", InterruptJobInstance)
            .WithQuartzDefaults(nameof(InterruptJobInstance), "Interrupt job instance");

        yield return builder.MapDelete(patternPrefix + "/{jobGroup}/{jobName}", DeleteJob)
            .WithQuartzDefaults(nameof(DeleteJob), "Delete job");

        yield return builder.MapPost(patternPrefix + "/delete", DeleteJobs)
            .WithQuartzDefaults(nameof(DeleteJobs), "Delete jobs");

        // "delete" was taken by the key-set form before there was a group form, so the group form
        // says so in its path rather than taking the plain one away from an endpoint that has it.
        yield return builder.MapPost(patternPrefix + "/delete-by-group", DeleteJobsByGroup)
            .WithQuartzDefaults(nameof(DeleteJobsByGroup), "Delete jobs by group");

        yield return builder.MapPost(patternPrefix, AddJob)
            .WithQuartzDefaults(nameof(AddJob), "Add job");

        yield return builder.MapGet(patternPrefix + "/groups", QueryJobGroups)
            .WithQuartzDefaults(nameof(QueryJobGroups), "Query job groups");

        yield return builder.MapGet(patternPrefix + "/groups/{jobGroup}/paused", IsJobGroupPaused)
            .WithQuartzDefaults(nameof(IsJobGroupPaused), "Is job group paused");
    }

    [ProducesResponseType(typeof(PagedResultDto<JobHeaderDto>), StatusCodes.Status200OK)]
    private static Task<IResult> QueryJobs(
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
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertPaging(skip, take);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            GroupMatcher<JobKey> matcher = EndpointHelper.GetGroupMatcher<JobKey>(groupContains, groupEndsWith, groupStartsWith, groupEquals);
            JobQuery query = new()
            {
                Group = matcher,
                Name = EndpointHelper.GetNameMatcher<JobKey>(nameContains, nameEndsWith, nameStartsWith, nameEquals),
                Skip = skip,
                IncludeTotalCount = includeTotalCount
            };

            // a request that names no take gets the query record's own default page size
            if (take.HasValue)
            {
                query = query with { Take = take.Value };
            }

            PagedResult<JobHeader> page = await scheduler.QueryJobs(query, cancellationToken).ConfigureAwait(false);
            return new PagedResultDto<JobHeaderDto>(page.Items.Select(JobHeaderDto.Create).ToArray(), page.HasMore, page.TotalCount);
        });
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            JobKey[] jobKeys = request.Select(x => x.AsJobKey()).ToArray();
            List<IJobDetail> jobDetails = await scheduler.GetJobDetails(jobKeys, cancellationToken).ConfigureAwait(false);
            return jobDetails.Select(JobDetailDto.Create).ToArray();
        });
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
            var jobDetail = await scheduler.GetJobDetailOrThrow(jobName, jobGroup, cancellationToken).ConfigureAwait(false);

            var result = JobDetailDto.Create(jobDetail);
            return result;
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var exists = await scheduler.Exists(new JobKey(jobName, jobGroup), cancellationToken).ConfigureAwait(false);
            return new ExistsResponse(exists);
        });
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var triggers = await scheduler.GetTriggersOfJob(new JobKey(jobName, jobGroup), cancellationToken).ConfigureAwait(false);
            return triggers;
        });
    }

    [ProducesResponseType(typeof(PagedResultDto<FireInstanceDto>), StatusCodes.Status200OK)]
    private static Task<IResult> QueryFireInstances(
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
        string? schedulerInstanceId = null,
        string? state = null,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertPaging(skip, take);

        // A state is parsed here rather than bound as a nullable enum, because null already means
        // something on the query record — every state — and an unnamed parameter must instead mean
        // "whatever the record defaults to".
        FireInstanceState? parsedState = null;
        bool anyState = false;
        if (state is not null)
        {
            if (string.Equals(state, HttpApiConstants.AnyFireInstanceState, StringComparison.OrdinalIgnoreCase))
            {
                anyState = true;
            }
            else if (Enum.TryParse(state, ignoreCase: true, out FireInstanceState value) && Enum.IsDefined(value))
            {
                parsedState = value;
            }
            else
            {
                throw new BadHttpRequestException($"Unknown fire instance state '{state}'");
            }
        }

        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            FireInstanceQuery query = new()
            {
                TriggerGroup = EndpointHelper.GetGroupMatcher<TriggerKey>(groupContains, groupEndsWith, groupStartsWith, groupEquals),
                TriggerName = EndpointHelper.GetNameMatcher<TriggerKey>(nameContains, nameEndsWith, nameStartsWith, nameEquals),
                Job = jobName is not null && jobGroup is not null ? new JobKey(jobName, jobGroup) : null,
                SchedulerInstanceId = schedulerInstanceId,
                Skip = skip,
                IncludeTotalCount = includeTotalCount
            };

            if (anyState || parsedState is not null)
            {
                query = query with { State = parsedState };
            }

            // a request that names no take gets the query record's own default page size
            if (take.HasValue)
            {
                query = query with { Take = take.Value };
            }

            PagedResult<FireInstance> page = await scheduler.QueryFireInstances(query, cancellationToken).ConfigureAwait(false);
            return new PagedResultDto<FireInstanceDto>(page.Items.Select(FireInstanceDto.Create).ToArray(), page.HasMore, page.TotalCount);
        });
    }

    [ProducesResponseType(typeof(OperationAppliedResponse), StatusCodes.Status200OK)]
    private static Task<IResult> PauseJob(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string jobGroup,
        string jobName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var applied = await scheduler.PauseJob(new JobKey(jobName, jobGroup), cancellationToken).ConfigureAwait(false);
            return new OperationAppliedResponse(applied);
        });
    }

    [ProducesResponseType(typeof(AffectedGroupsResponse), StatusCodes.Status200OK)]
    private static Task<IResult> PauseJobs(
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
            var matcher = EndpointHelper.GetGroupMatcher<JobKey>(groupContains, groupEndsWith, groupStartsWith, groupEquals);
            var pausedGroups = await scheduler.PauseJobs(matcher, cancellationToken).ConfigureAwait(false);
            return new AffectedGroupsResponse([.. pausedGroups]);
        });
    }

    [ProducesResponseType(typeof(AppliedJobKeysResponse), StatusCodes.Status200OK)]
    private static Task<IResult> PauseJobKeys(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        JobKeySetRequest request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertIsValid(request);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var jobKeys = request.Jobs.Select(x => x.AsJobKey()).ToArray();
            var paused = await scheduler.PauseJobs(jobKeys, cancellationToken).ConfigureAwait(false);
            return new AppliedJobKeysResponse([.. paused.Select(KeyDto.Create)]);
        });
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var applied = await scheduler.ResumeJob(new JobKey(jobName, jobGroup), cancellationToken).ConfigureAwait(false);
            return new OperationAppliedResponse(applied);
        });
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var matcher = EndpointHelper.GetGroupMatcher<JobKey>(groupContains, groupEndsWith, groupStartsWith, groupEquals);
            var resumedGroups = await scheduler.ResumeJobs(matcher, cancellationToken).ConfigureAwait(false);
            return new AffectedGroupsResponse([.. resumedGroups]);
        });
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var jobKeys = request.Jobs.Select(x => x.AsJobKey()).ToArray();
            var resumed = await scheduler.ResumeJobs(jobKeys, cancellationToken).ConfigureAwait(false);
            return new AppliedJobKeysResponse([.. resumed.Select(KeyDto.Create)]);
        });
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
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, scheduler => scheduler.TriggerJob(new JobKey(jobName, jobGroup), request?.JobData, cancellationToken).AsTask());
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var interrupted = await scheduler.Interrupt(new JobKey(jobName, jobGroup), cancellationToken).ConfigureAwait(false);
            return new OperationAppliedResponse(interrupted);
        });
    }

    [ProducesResponseType(typeof(OperationAppliedResponse), StatusCodes.Status200OK)]
    private static Task<IResult> InterruptJobInstance(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string fireInstanceId,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var interrupted = await scheduler.InterruptFireInstance(fireInstanceId, cancellationToken).ConfigureAwait(false);
            return new OperationAppliedResponse(interrupted);
        });
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var jobFound = await scheduler.DeleteJob(new JobKey(jobName, jobGroup), cancellationToken).ConfigureAwait(false);
            return new OperationAppliedResponse(jobFound);
        });
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var jobKeys = request.Jobs.Select(x => x.AsJobKey()).ToArray();
            var deleted = await scheduler.DeleteJobs(jobKeys, cancellationToken).ConfigureAwait(false);
            return new AppliedJobKeysResponse([.. deleted.Select(KeyDto.Create)]);
        });
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var matcher = EndpointHelper.GetGroupMatcher<JobKey>(groupContains, groupEndsWith, groupStartsWith, groupEquals);
            var deleted = await scheduler.DeleteJobs(matcher, cancellationToken).ConfigureAwait(false);
            return new AppliedJobKeysResponse([.. deleted.Select(KeyDto.Create)]);
        });
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
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            IJobDetail newJob = RequestedJobDetail.From(request.Job);
            var options = new AddJobOptions
            {
                Replace = request.Replace,
                StoreNonDurableWhileAwaitingScheduling = request.StoreNonDurableWhileAwaitingScheduling.GetValueOrDefault(),
            };

            await scheduler.AddJob(newJob, options, cancellationToken).ConfigureAwait(false);
        });
    }

    [ProducesResponseType(typeof(PagedResultDto<JobGroupDto>), StatusCodes.Status200OK)]
    private static Task<IResult> QueryJobGroups(
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            JobGroupQuery query = new()
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

            PagedResult<JobGroup> page = await scheduler.QueryJobGroups(query, cancellationToken).ConfigureAwait(false);
            return new PagedResultDto<JobGroupDto>(page.Items.Select(JobGroupDto.Create).ToArray(), page.HasMore, page.TotalCount);
        });
    }

    [ProducesResponseType(typeof(GroupPausedResponse), StatusCodes.Status200OK)]
    private static Task<IResult> IsJobGroupPaused(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string jobGroup,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            bool paused = await scheduler.IsJobGroupPaused(jobGroup, cancellationToken).ConfigureAwait(false);
            return new GroupPausedResponse(paused);
        });
    }
}