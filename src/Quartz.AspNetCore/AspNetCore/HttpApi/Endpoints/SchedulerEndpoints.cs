using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using Quartz.AspNetCore.HttpApi.Util;
using Quartz.HttpApiContract;
using Quartz.Impl;

namespace Quartz.AspNetCore.HttpApi.Endpoints;

internal static class SchedulerEndpoints
{
    public static IEnumerable<RouteHandlerBuilder> MapEndpoints(IEndpointRouteBuilder builder, QuartzHttpApiOptions options)
    {
        var patternPrefix = $"{options.TrimmedApiPath}/schedulers";

        yield return builder.MapGet(patternPrefix, GetAllSchedulers)
            .WithQuartzDefaults(nameof(GetAllSchedulers), "Get all schedulers");

        yield return builder.MapGet(patternPrefix + "/{schedulerName}", GetSchedulerDetails)
            .WithQuartzDefaults(nameof(GetSchedulerDetails), "Get scheduler details");

        yield return builder.MapGet(patternPrefix + "/{schedulerName}/context", GetSchedulerContext)
            .WithQuartzDefaults(nameof(GetSchedulerContext), "Get scheduler context");

        yield return builder.MapPost(patternPrefix + "/{schedulerName}/start", Start)
            .WithQuartzDefaults(nameof(Start), "Start scheduler");

        yield return builder.MapPost(patternPrefix + "/{schedulerName}/standby", Standby)
            .WithQuartzDefaults(nameof(Standby), "Set scheduler in stand-by mode");

        yield return builder.MapPost(patternPrefix + "/{schedulerName}/shutdown", Shutdown)
            .WithQuartzDefaults(nameof(Shutdown), "Shutdown the scheduler");

        yield return builder.MapPost(patternPrefix + "/{schedulerName}/clear", Clear)
            .WithQuartzDefaults(nameof(Clear), "Clear (delete!) all scheduling data");

        yield return builder.MapPost(patternPrefix + "/{schedulerName}/pause-all", PauseAll)
            .WithQuartzDefaults(nameof(PauseAll), "Pause all triggers");

        yield return builder.MapPost(patternPrefix + "/{schedulerName}/resume-all", ResumeAll)
            .WithQuartzDefaults(nameof(ResumeAll), "Resume (un-pause) all triggers");
    }

    [ProducesResponseType(typeof(SchedulerHeaderDto[]), StatusCodes.Status200OK)]
    private static async Task<IResult> GetAllSchedulers(
        EndpointHelper endpointHelper,
        CancellationToken cancellationToken = default)
    {
        var schedulers = await SchedulerRepository.Instance.LookupAll(cancellationToken).ConfigureAwait(false);
        var result = schedulers.Select(SchedulerHeaderDto.Create).ToArray();
        return EndpointHelper.JsonResponse(result);
    }

    [ProducesResponseType(typeof(SchedulerDto), StatusCodes.Status200OK)]
    private static Task<IResult> GetSchedulerDetails(
        EndpointHelper endpointHelper,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, async scheduler =>
        {
            var metaData = await scheduler.GetMetaData(cancellationToken).ConfigureAwait(false);
            var result = SchedulerDto.Create(scheduler, metaData);
            return result;
        });
    }

    [ProducesResponseType(typeof(SchedulerContextDto), StatusCodes.Status200OK)]
    private static Task<IResult> GetSchedulerContext(
        EndpointHelper endpointHelper,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, scheduler =>
        {
            var context = scheduler.Context;
            var result = SchedulerContextDto.Create(context);
            return Task.FromResult(result);
        });
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> Start(
        EndpointHelper endpointHelper,
        string schedulerName,
        long? delayMilliseconds,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, scheduler =>
        {
            if (delayMilliseconds.HasValue)
            {
                return scheduler.StartDelayed(TimeSpan.FromMilliseconds(delayMilliseconds.Value), cancellationToken).AsTask();
            }

            return scheduler.Start(cancellationToken).AsTask();
        });
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> Standby(
        EndpointHelper endpointHelper,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, scheduler => scheduler.Standby(cancellationToken).AsTask());
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> Shutdown(
        EndpointHelper endpointHelper,
        string schedulerName,
        bool waitForJobsToComplete = false,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, scheduler => scheduler.Shutdown(waitForJobsToComplete, cancellationToken).AsTask());
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> Clear(
        EndpointHelper endpointHelper,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, scheduler => scheduler.Clear(cancellationToken).AsTask());
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> PauseAll(
        EndpointHelper endpointHelper,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, scheduler => scheduler.PauseAll(cancellationToken).AsTask());
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> ResumeAll(
        EndpointHelper endpointHelper,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, scheduler => scheduler.ResumeAll(cancellationToken).AsTask());
    }
}