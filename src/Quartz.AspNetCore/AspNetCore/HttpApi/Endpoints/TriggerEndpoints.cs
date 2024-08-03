using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using Quartz.AspNetCore.HttpApi.Util;
using Quartz.HttpApiContract;
using Quartz.Spi;

namespace Quartz.AspNetCore.HttpApi.Endpoints;

internal static class TriggerEndpoints
{
    public static IEnumerable<RouteHandlerBuilder> MapEndpoints(IEndpointRouteBuilder builder, QuartzHttpApiOptions options)
    {
        var patternPrefix = $"{options.TrimmedApiPath}/schedulers/{{schedulerName}}/triggers";

        yield return builder.MapGet(patternPrefix, GetTriggerKeys)
            .WithQuartzDefaults(nameof(GetTriggerKeys), "Get all trigger keys");

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

        yield return builder.MapGet(patternPrefix + "/groups", GetTriggerGroupNames)
            .WithQuartzDefaults(nameof(GetTriggerGroupNames), "Get all trigger group names");

        yield return builder.MapGet(patternPrefix + "/groups/paused", GetPausedTriggerGroups)
            .WithQuartzDefaults(nameof(GetPausedTriggerGroups), "Get all paused trigger group names");

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

    [ProducesResponseType(typeof(KeyDto[]), StatusCodes.Status200OK)]
    private static Task<IResult> GetTriggerKeys(
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
            var triggerKeys = await scheduler.GetTriggerKeys(matcher, cancellationToken).ConfigureAwait(false);

            var result = triggerKeys.Select(KeyDto.Create).ToArray();
            return result;
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
            var exists = await scheduler.CheckExists(new TriggerKey(triggerName, triggerGroup), cancellationToken).ConfigureAwait(false);
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

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> ResetTriggerFromErrorState(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string triggerGroup,
        string triggerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, scheduler => scheduler.ResetTriggerFromErrorState(new TriggerKey(triggerName, triggerGroup), cancellationToken).AsTask());
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> PauseTrigger(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string triggerGroup,
        string triggerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, scheduler => scheduler.PauseTrigger(new TriggerKey(triggerName, triggerGroup), cancellationToken).AsTask());
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
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
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var matcher = EndpointHelper.GetGroupMatcher<TriggerKey>(groupContains, groupEndsWith, groupStartsWith, groupEquals);
            await scheduler.PauseTriggers(matcher, cancellationToken).ConfigureAwait(false);
        });
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> ResumeTrigger(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        string triggerGroup,
        string triggerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, scheduler => scheduler.ResumeTrigger(new TriggerKey(triggerName, triggerGroup), cancellationToken).AsTask());
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
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
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var matcher = EndpointHelper.GetGroupMatcher<TriggerKey>(groupContains, groupEndsWith, groupStartsWith, groupEquals);
            await scheduler.ResumeTriggers(matcher, cancellationToken).ConfigureAwait(false);
        });
    }

    [ProducesResponseType(typeof(NamesDto), StatusCodes.Status200OK)]
    private static Task<IResult> GetTriggerGroupNames(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var groupNames = await scheduler.GetTriggerGroupNames(cancellationToken).ConfigureAwait(false);
            return new NamesDto(groupNames);
        });
    }

    [ProducesResponseType(typeof(NamesDto), StatusCodes.Status200OK)]
    private static Task<IResult> GetPausedTriggerGroups(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var result = await scheduler.GetPausedTriggerGroups(cancellationToken).ConfigureAwait(false);
            return new NamesDto(result);
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
            var paused = await scheduler.IsTriggerGroupPaused(triggerGroup, cancellationToken).ConfigureAwait(false);
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