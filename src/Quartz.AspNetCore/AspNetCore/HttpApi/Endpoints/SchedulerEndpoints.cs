using System.ComponentModel;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;

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
            .WithQuartzDefaults(nameof(Start), "Start scheduler")
            .WithQuartzMutation(options);

        yield return builder.MapPost(patternPrefix + "/{schedulerName}/standby", Standby)
            .WithQuartzDefaults(nameof(Standby), "Set scheduler in stand-by mode")
            .WithQuartzMutation(options);

        yield return builder.MapPost(patternPrefix + "/{schedulerName}/shutdown", Shutdown)
            .WithQuartzDefaults(nameof(Shutdown), "Shutdown the scheduler")
            .WithQuartzMutation(options);

        yield return builder.MapPost(patternPrefix + "/{schedulerName}/clear", Clear)
            .WithQuartzDefaults(nameof(Clear), "Clear (delete!) all scheduling data")
            .WithQuartzMutation(options);

        yield return builder.MapPost(patternPrefix + "/{schedulerName}/pause-all", PauseAll)
            .WithQuartzDefaults(nameof(PauseAll), "Pause all triggers")
            .WithQuartzMutation(options);

        yield return builder.MapPost(patternPrefix + "/{schedulerName}/resume-all", ResumeAll)
            .WithQuartzDefaults(nameof(ResumeAll), "Resume (un-pause) all triggers")
            .WithQuartzMutation(options);

        yield return builder.MapGet(patternPrefix + "/{schedulerName}/nodes", GetClusterNodes)
            .WithQuartzDefaults(nameof(GetClusterNodes), "Get the scheduler's cluster nodes");

        yield return builder.MapGet(patternPrefix + "/{schedulerName}/history/executions", QueryExecutionHistory)
            .WithQuartzDefaults(nameof(QueryExecutionHistory), "Query the scheduler's execution history");

        yield return builder.MapGet(patternPrefix + "/{schedulerName}/history/misfires", QueryMisfireHistory)
            .WithQuartzDefaults(nameof(QueryMisfireHistory), "Query the scheduler's misfires");

        yield return builder.MapGet(patternPrefix + "/{schedulerName}/history/misfires/count", CountMisfires)
            .WithQuartzDefaults(nameof(CountMisfires), "Count the scheduler's misfires since an instant");

        yield return builder.MapGet(patternPrefix + "/{schedulerName}/execution-limits", GetExecutionLimits)
            .WithQuartzDefaults(nameof(GetExecutionLimits), "Get execution group limits");

        yield return builder.MapPost(patternPrefix + "/{schedulerName}/execution-limits", SetExecutionLimits)
            .WithQuartzDefaults(nameof(SetExecutionLimits), "Set execution group limits")
            .WithQuartzMutation(options);

        yield return builder.MapDelete(patternPrefix + "/{schedulerName}/execution-limits", ClearExecutionLimits)
            .WithQuartzDefaults(nameof(ClearExecutionLimits), "Clear execution group limits")
            .WithQuartzMutation(options);
    }

    /// <summary>
    /// Lists every scheduler the container knows about, ordered by name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The registrations rather than the repository: a repository holds the schedulers something has
    /// already built, so a tenant nobody has asked for was invisible here — and the caller could not tell
    /// that from "no such tenant". Such an entry is listed with a null status, and asking for it does not
    /// build it. The instance id of the schedulers that do exist comes off the registration, which asked
    /// each of them once, asynchronously and under a deadline.
    /// </para>
    /// <para>
    /// This route names no scheduler, so the endpoint filter has nothing to check and the listing filters
    /// itself: with <see cref="QuartzHttpApiOptions.SchedulerAuthorizationPolicy" /> set, a caller is told
    /// about the schedulers they may act on and no others.
    /// </para>
    /// </remarks>
    [ProducesResponseType(typeof(SchedulerHeaderDto[]), StatusCodes.Status200OK)]
    private static async Task<IResult> GetAllSchedulers(
        EndpointHelper endpointHelper,
        HttpContext httpContext,
        IOptions<QuartzHttpApiOptions> apiOptions,
        ISchedulerRegistry schedulerRegistry,
        CancellationToken cancellationToken = default)
    {
        List<SchedulerRegistration> registrations = await schedulerRegistry.QuerySchedulers(cancellationToken).ConfigureAwait(false);
        string? policyName = apiOptions.Value.SchedulerAuthorizationPolicy;

        List<SchedulerHeaderDto> result = new(registrations.Count);
        foreach (SchedulerRegistration registration in registrations)
        {
            if (!await SchedulerAuthorization.IsAuthorized(httpContext, policyName, registration.Name, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            result.Add(SchedulerHeaderDto.Create(registration));
        }

        return endpointHelper.JsonResponse(result.ToArray());
    }

    [ProducesResponseType(typeof(SchedulerDto), StatusCodes.Status200OK)]
    private static Task<IResult> GetSchedulerDetails(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            var metadata = await scheduler.GetMetadata(cancellationToken).ConfigureAwait(false);
            var result = SchedulerDto.Create(metadata);
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
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, scheduler =>
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

    /// <summary>
    /// The nodes of the cluster, this scheduler's own node first. A scheduler that is not clustered
    /// answers with the one node it is.
    /// </summary>
    [ProducesResponseType(typeof(ClusterNodeDto[]), StatusCodes.Status200OK)]
    private static Task<IResult> GetClusterNodes(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            List<ClusterNode> nodes = await scheduler.QueryClusterNodes(cancellationToken).ConfigureAwait(false);

            ClusterNodeDto[] result = new ClusterNodeDto[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                result[i] = ClusterNodeDto.Create(nodes[i]);
            }

            return result;
        });
    }

    /// <summary>
    /// One page of what this scheduler has run, newest first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The history is the process's rather than the scheduler's: a job store holds what is scheduled,
    /// and what happened is kept by the container's <see cref="IExecutionHistoryStore" /> — in memory and
    /// bounded unless the application registered a store of its own. The route names the scheduler
    /// because that is what the rows are keyed by, and because it is what per-scheduler authorization
    /// reads.
    /// </para>
    /// <para>
    /// <c>schedulerInstanceId</c> narrows to one node of a cluster; <c>jobContains</c> and
    /// <c>triggerContains</c> match a key's group, its name, or the two joined as <c>group.name</c>.
    /// </para>
    /// </remarks>
    [ProducesResponseType(typeof(PagedResultDto<ExecutionHistoryEntryDto>), StatusCodes.Status200OK)]
    private static Task<IResult> QueryExecutionHistory(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        IExecutionHistoryStore historyStore,
        string schedulerName,
        int skip = 0,
        [Description(EndpointHelper.TakeDescription)] string? take = null,
        bool includeTotalCount = false,
        string? schedulerInstanceId = null,
        string? jobContains = null,
        string? triggerContains = null,
        CancellationToken cancellationToken = default)
    {
        int? takeItems = endpointHelper.ParsePaging(skip, take);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            ExecutionHistoryQuery query = new()
            {
                // The scheduler's own spelling of its name, so a route that named it in another case
                // still reads the rows it recorded.
                SchedulerName = scheduler.SchedulerName,
                SchedulerInstanceId = schedulerInstanceId,
                JobContains = jobContains,
                TriggerContains = triggerContains,
                Skip = skip,
                IncludeTotalCount = includeTotalCount
            };

            // a request that names no take gets the query record's own default page size
            if (takeItems.HasValue)
            {
                query = query with { Take = takeItems.Value };
            }

            PagedResult<ExecutionHistoryEntry> page = await historyStore.QueryExecutions(query, cancellationToken).ConfigureAwait(false);

            ExecutionHistoryEntryDto[] items = new ExecutionHistoryEntryDto[page.Items.Count];
            for (int i = 0; i < page.Items.Count; i++)
            {
                items[i] = ExecutionHistoryEntryDto.Create(page.Items[i]);
            }

            return new PagedResultDto<ExecutionHistoryEntryDto>(items, page.HasMore, page.TotalCount);
        });
    }

    /// <summary>
    /// One page of the firings this scheduler missed, newest first.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="QueryExecutionHistory" path="/remarks" />
    /// </remarks>
    [ProducesResponseType(typeof(PagedResultDto<MisfireHistoryEntryDto>), StatusCodes.Status200OK)]
    private static Task<IResult> QueryMisfireHistory(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        IExecutionHistoryStore historyStore,
        string schedulerName,
        int skip = 0,
        [Description(EndpointHelper.TakeDescription)] string? take = null,
        bool includeTotalCount = false,
        string? schedulerInstanceId = null,
        string? triggerContains = null,
        CancellationToken cancellationToken = default)
    {
        int? takeItems = endpointHelper.ParsePaging(skip, take);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            MisfireHistoryQuery query = new()
            {
                SchedulerName = scheduler.SchedulerName,
                SchedulerInstanceId = schedulerInstanceId,
                TriggerContains = triggerContains,
                Skip = skip,
                IncludeTotalCount = includeTotalCount
            };

            if (takeItems.HasValue)
            {
                query = query with { Take = takeItems.Value };
            }

            PagedResult<MisfireHistoryEntry> page = await historyStore.QueryMisfires(query, cancellationToken).ConfigureAwait(false);

            MisfireHistoryEntryDto[] items = new MisfireHistoryEntryDto[page.Items.Count];
            for (int i = 0; i < page.Items.Count; i++)
            {
                items[i] = MisfireHistoryEntryDto.Create(page.Items[i]);
            }

            return new PagedResultDto<MisfireHistoryEntryDto>(items, page.HasMore, page.TotalCount);
        });
    }

    /// <summary>
    /// How many firings this scheduler has missed since <c>since</c> — <c>?since=2026-09-11T12:00:00Z</c>.
    /// </summary>
    /// <remarks>
    /// A count rather than a page, because a summary tile asks "how bad is it right now" and a store
    /// that keeps history in a database answers that with one <c>COUNT(*)</c> instead of sending rows the
    /// caller would throw away.
    /// </remarks>
    [ProducesResponseType(typeof(MisfireCountResponse), StatusCodes.Status200OK)]
    private static Task<IResult> CountMisfires(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        IExecutionHistoryStore historyStore,
        string schedulerName,
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        if (since is null)
        {
            throw new BadHttpRequestException("since is required: a count with no window is a count of everything the store still holds");
        }

        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
        {
            int count = await historyStore.CountMisfires(scheduler.SchedulerName, since.Value, cancellationToken).ConfigureAwait(false);
            return new MisfireCountResponse(count);
        });
    }

    [ProducesResponseType(typeof(ExecutionLimitsResponse), StatusCodes.Status200OK)]
    private static Task<IResult> GetExecutionLimits(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, schedulerRepository, async scheduler =>
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

            // A request that names no group and asks for no derivation is the one that clears the limits.
            // Asking for the derivation alone still configures something - every trigger is then limited
            // as though its trigger group were its execution group - so it is built rather than dropped.
            if (request.Limits is { Count: > 0 } || request.UseTriggerGroupWhenUnset)
            {
                ExecutionLimitsBuilder builder = ExecutionLimitsBuilder.Create();
                foreach (KeyValuePair<string, ExecutionLimitDto> kvp in request.Limits ?? [])
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