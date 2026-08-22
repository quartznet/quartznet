using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

using Quartz.AspNetCore.HttpApi.Util;
using Quartz.HttpApiContract;
using Quartz.Extensibility;

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

        yield return builder.MapGet(patternPrefix + "/{schedulerName}/execution-limits", GetExecutionLimits)
            .WithQuartzDefaults(nameof(GetExecutionLimits), "Get execution group limits");

        yield return builder.MapPost(patternPrefix + "/{schedulerName}/execution-limits", SetExecutionLimits)
            .WithQuartzDefaults(nameof(SetExecutionLimits), "Set execution group limits");

        yield return builder.MapDelete(patternPrefix + "/{schedulerName}/execution-limits", ClearExecutionLimits)
            .WithQuartzDefaults(nameof(ClearExecutionLimits), "Clear execution group limits");
    }

    [ProducesResponseType(typeof(SchedulerHeaderDto[]), StatusCodes.Status200OK)]
    private static Task<IResult> GetAllSchedulers(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        CancellationToken cancellationToken = default)
    {
        var schedulers = schedulerRepository.LookupAll();
        var result = schedulers.Select(SchedulerHeaderDto.Create).ToArray();
        return Task.FromResult(EndpointHelper.JsonResponse(result));
    }

    [ProducesResponseType(typeof(SchedulerDto), StatusCodes.Status200OK)]
    private static Task<IResult> GetSchedulerDetails(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var metadata = await scheduler.GetMetadata(cancellationToken).ConfigureAwait(false);
            var result = SchedulerDto.Create(scheduler, metadata);
            return result;
        });
    }

    [ProducesResponseType(typeof(SchedulerContextDto), StatusCodes.Status200OK)]
    private static Task<IResult> GetSchedulerContext(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, scheduler =>
        {
            var context = scheduler.Context;
            var result = SchedulerContextDto.Create(context);
            return Task.FromResult(result);
        });
    }

    /// <summary>
    /// Starts the scheduler, after <c>delay</c> when one is given — <c>?delay=00:00:30</c>, a
    /// <see cref="TimeSpan" /> like every other duration on the wire. A request that names none starts
    /// the scheduler immediately.
    /// </summary>
    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> Start(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        TimeSpan? delay,
        CancellationToken cancellationToken = default)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new BadHttpRequestException("delay must not be negative");
        }

        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, scheduler =>
        {
            if (delay.HasValue)
            {
                return scheduler.StartDelayed(delay.Value, cancellationToken).AsTask();
            }

            return scheduler.Start(cancellationToken).AsTask();
        });
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> Standby(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, scheduler => scheduler.Standby(cancellationToken).AsTask());
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> Shutdown(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        bool waitForJobsToComplete = false,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, scheduler => scheduler.Shutdown(waitForJobsToComplete, cancellationToken).AsTask());
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> Clear(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, scheduler => scheduler.Clear(cancellationToken).AsTask());
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> PauseAll(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, scheduler => scheduler.PauseAll(cancellationToken).AsTask());
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> ResumeAll(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, scheduler => scheduler.ResumeAll(cancellationToken).AsTask());
    }

    [ProducesResponseType(typeof(ExecutionLimitsResponse), StatusCodes.Status200OK)]
    private static Task<IResult> GetExecutionLimits(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
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
        });
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> SetExecutionLimits(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        [FromBody] SetExecutionLimitsRequest request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertIsValid(request);

        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            ExecutionLimits? limits = null;
            if (request.Limits is { Count: > 0 })
            {
                ExecutionLimitsBuilder builder = ExecutionLimitsBuilder.Create();
                foreach (KeyValuePair<string, ExecutionLimitDto> kvp in request.Limits)
                {
                    string key = kvp.Key.Trim();
                    int? maxConcurrent = kvp.Value.MaxConcurrent;
                    ExecutionLimitScope scope = kvp.Value.Scope;

                    if (key == ExecutionLimits.OtherGroups)
                    {
                        if (maxConcurrent.HasValue) builder.ForOtherGroups(maxConcurrent.Value, scope);
                    }
                    else if (ExecutionLimits.IsDefaultGroupAlias(key))
                    {
                        if (maxConcurrent.HasValue) builder.ForDefaultGroup(maxConcurrent.Value, scope);
                    }
                    else
                    {
                        if (maxConcurrent.HasValue) builder.ForGroup(key, maxConcurrent.Value, scope);
                        else builder.Unlimited(key);
                    }
                }

                if (request.UseTriggerGroupWhenUnset)
                {
                    builder.UseTriggerGroupWhenUnset();
                }

                limits = builder.Build();
            }

            await scheduler.SetExecutionLimits(limits, cancellationToken).ConfigureAwait(false);
        });
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> ClearExecutionLimits(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            await scheduler.SetExecutionLimits(null, cancellationToken).ConfigureAwait(false);
        });
    }
}