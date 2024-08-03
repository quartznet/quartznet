using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using Quartz.AspNetCore.HttpApi.Util;
using Quartz.HttpApiContract;
using Quartz.Spi;

namespace Quartz.AspNetCore.HttpApi.Endpoints;

internal static class JobEndpoints
{
    public static IEnumerable<RouteHandlerBuilder> MapEndpoints(IEndpointRouteBuilder builder, QuartzHttpApiOptions options)
    {
        var patternPrefix = $"{options.TrimmedApiPath}/schedulers/{{schedulerName}}/jobs";

        yield return builder.MapGet(patternPrefix, GetJobKeys)
            .WithQuartzDefaults(nameof(GetJobKeys), "Get job keys");

        yield return builder.MapGet(patternPrefix + "/{jobGroup}/{jobName}", GetJobDetails)
            .WithQuartzDefaults(nameof(GetJobDetails), "Get job details");

        yield return builder.MapGet(patternPrefix + "/{jobGroup}/{jobName}/exists", CheckJobExists)
            .WithQuartzDefaults(nameof(CheckJobExists), "Check job exists");

        yield return builder.MapGet(patternPrefix + "/{jobGroup}/{jobName}/triggers", GetJobTriggers)
            .WithQuartzDefaults(nameof(GetJobTriggers), "Get job triggers");

        yield return builder.MapGet(patternPrefix + "/currently-executing", CurrentlyExecutingJobs)
            .WithQuartzDefaults(nameof(CurrentlyExecutingJobs), "Get currently executing jobs");

        yield return builder.MapPost(patternPrefix + "/{jobGroup}/{jobName}/pause", PauseJob)
            .WithQuartzDefaults(nameof(PauseJob), "Pause job");

        yield return builder.MapPost(patternPrefix + "/pause", PauseJobs)
            .WithQuartzDefaults(nameof(PauseJobs), "Pause jobs");

        yield return builder.MapPost(patternPrefix + "/{jobGroup}/{jobName}/resume", ResumeJob)
            .WithQuartzDefaults(nameof(ResumeJob), "Resume job");

        yield return builder.MapPost(patternPrefix + "/resume", ResumeJobs)
            .WithQuartzDefaults(nameof(ResumeJobs), "Resume jobs");

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

        yield return builder.MapPost(patternPrefix, AddJob)
            .WithQuartzDefaults(nameof(AddJob), "Add job");

        yield return builder.MapGet(patternPrefix + "/groups", GetJobGroupNames)
            .WithQuartzDefaults(nameof(GetJobGroupNames), "Get all job group names");

        yield return builder.MapGet(patternPrefix + "/groups/{jobGroup}/paused", IsJobGroupPaused)
            .WithQuartzDefaults(nameof(IsJobGroupPaused), "Is job group paused");
    }

    [ProducesResponseType(typeof(KeyDto[]), StatusCodes.Status200OK)]
    private static Task<IResult> GetJobKeys(
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
            var matcher = EndpointHelper.GetGroupMatcher<JobKey>(groupContains, groupEndsWith, groupStartsWith, groupEquals);
            var jobKeys = await scheduler.GetJobKeys(matcher, cancellationToken).ConfigureAwait(false);

            var result = jobKeys.Select(KeyDto.Create).ToArray();
            return result;
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
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
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
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var exists = await scheduler.CheckExists(new JobKey(jobName, jobGroup), cancellationToken).ConfigureAwait(false);
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
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var triggers = await scheduler.GetTriggersOfJob(new JobKey(jobName, jobGroup), cancellationToken).ConfigureAwait(false);
            return triggers;
        });
    }

    [ProducesResponseType(typeof(OpenApi.CurrentlyExecutingJobDto[]), StatusCodes.Status200OK)]
    private static Task<IResult> CurrentlyExecutingJobs(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var currentlyExecutingJobs = await scheduler.GetCurrentlyExecutingJobs(cancellationToken).ConfigureAwait(false);
            var result = currentlyExecutingJobs.Select(CurrentlyExecutingJobDto.Create).ToArray();
            return result;
        });
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> PauseJob(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string jobGroup,
        string jobName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, scheduler => scheduler.PauseJob(new JobKey(jobName, jobGroup), cancellationToken).AsTask());
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
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
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var matcher = EndpointHelper.GetGroupMatcher<JobKey>(groupContains, groupEndsWith, groupStartsWith, groupEquals);
            await scheduler.PauseJobs(matcher, cancellationToken).ConfigureAwait(false);
        });
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> ResumeJob(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string jobGroup,
        string jobName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, scheduler => scheduler.ResumeJob(new JobKey(jobName, jobGroup), cancellationToken).AsTask());
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
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
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var matcher = EndpointHelper.GetGroupMatcher<JobKey>(groupContains, groupEndsWith, groupStartsWith, groupEquals);
            await scheduler.ResumeJobs(matcher, cancellationToken).ConfigureAwait(false);
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
        if (request?.JobData is not null)
        {
            return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, scheduler => scheduler.TriggerJob(new JobKey(jobName, jobGroup), request.JobData, cancellationToken).AsTask());
        }

        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, scheduler => scheduler.TriggerJob(new JobKey(jobName, jobGroup), cancellationToken).AsTask());
    }

    [ProducesResponseType(typeof(InterruptResponse), StatusCodes.Status200OK)]
    private static Task<IResult> InterruptJob(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string jobGroup,
        string jobName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var interrupted = await scheduler.Interrupt(new JobKey(jobName, jobGroup), cancellationToken).ConfigureAwait(false);
            return new InterruptResponse(interrupted);
        });
    }

    [ProducesResponseType(typeof(InterruptResponse), StatusCodes.Status200OK)]
    private static Task<IResult> InterruptJobInstance(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string fireInstanceId,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var interrupted = await scheduler.Interrupt(fireInstanceId, cancellationToken).ConfigureAwait(false);
            return new InterruptResponse(interrupted);
        });
    }

    [ProducesResponseType(typeof(DeleteJobResponse), StatusCodes.Status200OK)]
    private static Task<IResult> DeleteJob(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string jobGroup,
        string jobName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var jobFound = await scheduler.DeleteJob(new JobKey(jobName, jobGroup), cancellationToken).ConfigureAwait(false);
            return new DeleteJobResponse(jobFound);
        });
    }

    [ProducesResponseType(typeof(DeleteJobsResponse), StatusCodes.Status200OK)]
    private static Task<IResult> DeleteJobs(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        DeleteJobsRequest request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertIsValid(request);
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var jobKeys = request.Jobs.Select(x => x.AsJobKey()).ToArray();
            var allJobsFound = await scheduler.DeleteJobs(jobKeys, cancellationToken).ConfigureAwait(false);
            return new DeleteJobsResponse(allJobsFound);
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
            var newJob = request.Job.AsIJobDetail().JobDetail!;
            if (!request.StoreNonDurableWhileAwaitingScheduling.HasValue)
            {
                await scheduler.AddJob(newJob, request.Replace, cancellationToken).ConfigureAwait(false);
                return;
            }

            await scheduler.AddJob(newJob, request.Replace, request.StoreNonDurableWhileAwaitingScheduling.Value, cancellationToken).ConfigureAwait(false);
        });
    }

    [ProducesResponseType(typeof(NamesDto), StatusCodes.Status200OK)]
    private static Task<IResult> GetJobGroupNames(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var groupNames = await scheduler.GetJobGroupNames(cancellationToken).ConfigureAwait(false);
            return new NamesDto(groupNames);
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
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var paused = await scheduler.IsJobGroupPaused(jobGroup, cancellationToken).ConfigureAwait(false);
            return new GroupPausedResponse(paused);
        });
    }
}