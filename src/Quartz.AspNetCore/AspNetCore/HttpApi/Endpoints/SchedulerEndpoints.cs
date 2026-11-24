using System.ComponentModel;
using System.Net.ServerSentEvents;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.AspNetCore.HttpApi.Util;
using Quartz.Configuration;
using Quartz.HttpApiContract;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.AspNetCore.HttpApi.Endpoints;

internal static class SchedulerEndpoints
{
    public static IEnumerable<RouteHandlerBuilder> MapEndpoints(IEndpointRouteBuilder builder, QuartzHttpApiOptions options)
    {
        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.GetAllSchedulers), GetAllSchedulers)
            .WithQuartzDefaults(SchedulerRoutes.GetAllSchedulers, "Get all schedulers");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.GetSchedulerDetails), GetSchedulerDetails)
            .WithQuartzDefaults(SchedulerRoutes.GetSchedulerDetails, "Get scheduler details");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.GetSchedulerContext), GetSchedulerContext)
            .WithQuartzDefaults(SchedulerRoutes.GetSchedulerContext, "Get scheduler context");

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.Start), Start)
            .WithQuartzDefaults(SchedulerRoutes.Start, "Start scheduler")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.Standby), Standby)
            .WithQuartzDefaults(SchedulerRoutes.Standby, "Set scheduler in stand-by mode")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.Shutdown), Shutdown)
            .WithQuartzDefaults(SchedulerRoutes.Shutdown, "Shutdown the scheduler")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.Clear), Clear)
            .WithQuartzDefaults(SchedulerRoutes.Clear, "Clear (delete!) all scheduling data")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.PauseAll), PauseAll)
            .WithQuartzDefaults(SchedulerRoutes.PauseAll, "Pause all triggers")
            .WithQuartzMutation(options);

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.ResumeAll), ResumeAll)
            .WithQuartzDefaults(SchedulerRoutes.ResumeAll, "Resume (un-pause) all triggers")
            .WithQuartzMutation(options);

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.GetClusterNodes), GetClusterNodes)
            .WithQuartzDefaults(SchedulerRoutes.GetClusterNodes, "Get the scheduler's cluster nodes");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.StreamEvents), StreamEvents)
            .WithQuartzDefaults(SchedulerRoutes.StreamEvents, "Stream the scheduler's events as they happen");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.QueryExecutionHistory), QueryExecutionHistory)
            .WithQuartzDefaults(SchedulerRoutes.QueryExecutionHistory, "Query the scheduler's execution history");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.GetExecution), GetExecution)
            .WithQuartzDefaults(SchedulerRoutes.GetExecution, "Get one execution, with its captured log");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.QueryMisfireHistory), QueryMisfireHistory)
            .WithQuartzDefaults(SchedulerRoutes.QueryMisfireHistory, "Query the scheduler's misfires");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.CountMisfires), CountMisfires)
            .WithQuartzDefaults(SchedulerRoutes.CountMisfires, "Count the scheduler's misfires since an instant");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.QueryJobRunStatuses), QueryJobRunStatuses)
            .WithQuartzDefaults(SchedulerRoutes.QueryJobRunStatuses, "Query the scheduler's per-job run statuses");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.GetJobRunStatus), GetJobRunStatus)
            .WithQuartzDefaults(SchedulerRoutes.GetJobRunStatus, "Get one job's run status");

        // A read that takes a body, as the bulk fetches are: not a mutation, so a read-only API serves it.
        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.FetchJobRunStatuses), FetchJobRunStatuses)
            .WithQuartzDefaults(SchedulerRoutes.FetchJobRunStatuses, "Get the run statuses of a set of jobs");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.QueryTriggerRunStatuses), QueryTriggerRunStatuses)
            .WithQuartzDefaults(SchedulerRoutes.QueryTriggerRunStatuses, "Query the scheduler's per-trigger run statuses");

        // A read that takes a body, as the job statuses' fetch is.
        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.FetchTriggerRunStatuses), FetchTriggerRunStatuses)
            .WithQuartzDefaults(SchedulerRoutes.FetchTriggerRunStatuses, "Get the run statuses of a set of triggers");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.QueryExecutionStatistics), QueryExecutionStatistics)
            .WithQuartzDefaults(SchedulerRoutes.QueryExecutionStatistics, "Count and time the scheduler's runs in buckets of fire time");

        yield return builder.MapGet(options.PatternFor(SchedulerRoutes.GetExecutionLimits), GetExecutionLimits)
            .WithQuartzDefaults(SchedulerRoutes.GetExecutionLimits, "Get execution group limits");

        yield return builder.MapPost(options.PatternFor(SchedulerRoutes.SetExecutionLimits), SetExecutionLimits)
            .WithQuartzDefaults(SchedulerRoutes.SetExecutionLimits, "Set execution group limits")
            .WithQuartzMutation(options);

        yield return builder.MapDelete(options.PatternFor(SchedulerRoutes.ClearExecutionLimits), ClearExecutionLimits)
            .WithQuartzDefaults(SchedulerRoutes.ClearExecutionLimits, "Clear execution group limits")
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
    /// about the schedulers they may act on and no others, and with
    /// <see cref="QuartzHttpApiOptions.OperationAuthorizationPolicy" /> set, about the ones they may view.
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
        string? policyName = apiOptions.Value.SchedulerAuthorizationPolicy;
        string? operationPolicyName = apiOptions.Value.OperationAuthorizationPolicy;

        SchedulerHeaderDto[] result = await SchedulerOperations.GetAllSchedulers(
            schedulerRegistry,
            (key, token) => IsListed(httpContext, policyName, operationPolicyName, key, token),
            cancellationToken).ConfigureAwait(false);

        return endpointHelper.JsonResponse(result);
    }

    /// <summary>
    /// Whether the listing names <paramref name="key" /> to this caller: one they may see, and may view under
    /// the operation policy when there is one.
    /// </summary>
    /// <remarks>
    /// Asked with the registration's target, so that a policy deciding by target lists what it would let a
    /// request with <c>?target=</c> reach.
    /// </remarks>
    private static async ValueTask<bool> IsListed(
        HttpContext httpContext,
        string? policyName,
        string? operationPolicyName,
        SchedulerRef key,
        CancellationToken cancellationToken)
    {
        return await SchedulerAuthorization.IsAuthorized(httpContext, policyName, key, cancellationToken).ConfigureAwait(false)
               && await OperationAuthorization.IsAllowed(
                   httpContext, operationPolicyName, key, SchedulerRoutes.GetAllSchedulers, SchedulerAccess.View, cancellationToken).ConfigureAwait(false);
    }

    [ProducesResponseType(typeof(SchedulerDto), StatusCodes.Status200OK)]
    private static Task<IResult> GetSchedulerDetails(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        HttpContext httpContext,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, target, httpContext, schedulerRepository,
            scheduler => SchedulerOperations.GetSchedulerDetails(scheduler, cancellationToken));
    }

    [ProducesResponseType(typeof(SchedulerContextDto), StatusCodes.Status200OK)]
    private static Task<IResult> GetSchedulerContext(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        HttpContext httpContext,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, target, httpContext, schedulerRepository,
            scheduler => ValueTask.FromResult(SchedulerOperations.GetSchedulerContext(scheduler)));
    }

    /// <summary>
    /// Starts the scheduler, after <c>delay</c> when one is given — <c>?delay=00:00:30</c>, a
    /// <see cref="TimeSpan" /> like every other duration on the wire. A request that names none starts
    /// the scheduler immediately.
    /// </summary>
    /// <remarks>
    /// A store-attached window is refused — see <see cref="EnsureNotAWindow" /> — before a delayed start
    /// is queued, since a start refused on the task that waits out the delay would already have answered
    /// <c>200</c>. It is refused alike by its bare name and through <c>?target=</c>, which resolve to the
    /// same window.
    /// </remarks>
    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> Start(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        HttpContext httpContext,
        string schedulerName,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        TimeSpan? delay,
        CancellationToken cancellationToken = default)
    {
        if (delay < TimeSpan.Zero)
        {
            throw new BadHttpRequestException("delay must not be negative");
        }

        return EndpointHelper.ExecuteWithResolution(schedulerName, target, httpContext, schedulerRepository, async resolution =>
        {
            EnsureNotAWindow(resolution, "start a scheduler");
            await SchedulerOperations.Start(resolution.Scheduler, delay, cancellationToken).ConfigureAwait(false);
            return Results.Ok();
        });
    }

    /// <remarks>
    /// A store-attached window is refused: see <see cref="EnsureNotAWindow" />.
    /// </remarks>
    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> Standby(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        HttpContext httpContext,
        string schedulerName,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithResolution(schedulerName, target, httpContext, schedulerRepository, async resolution =>
        {
            EnsureNotAWindow(resolution, "stand a scheduler down");
            await SchedulerOperations.Standby(resolution.Scheduler, cancellationToken).ConfigureAwait(false);
            return Results.Ok();
        });
    }

    /// <remarks>
    /// A store-attached window is refused: see <see cref="EnsureNotAWindow" />.
    /// </remarks>
    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> Shutdown(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        HttpContext httpContext,
        string schedulerName,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        bool waitForJobsToComplete = false,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithResolution(schedulerName, target, httpContext, schedulerRepository, async resolution =>
        {
            EnsureNotAWindow(resolution, "shut a scheduler down");
            await SchedulerOperations.Shutdown(resolution.Scheduler, waitForJobsToComplete, cancellationToken).ConfigureAwait(false);
            return Results.Ok();
        });
    }

    /// <summary>
    /// Refuses a verb that belongs to the process running a scheduler, when this process only has a
    /// window onto it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A store-attached window is a scheduler this process built over a database other processes run,
    /// bound in the repository like any other — so the lookup every route makes reaches it, and nothing
    /// about the object says this process does not run it. Starting, standing down and shutting down
    /// would all act on that object and reach none of the nodes the caller meant: a start is refused by
    /// the window's store as well, but a standby would report a cluster stood down that is running, and a
    /// shutdown would unbind the window for good, since its name has already been decided about and is
    /// never rediscovered. The dashboard's client refuses the same three for the same reason, and this
    /// decides "window" the way it does: <see cref="SchedulerWindowRegistry" /> is the one place that
    /// fact is recorded, and the resolution reads it from there for a bare name and a targeted key alike.
    /// </para>
    /// <para>
    /// A <see cref="SchedulerException" />, so the answer is the <c>400</c> a scheduler's own refusal is
    /// and <c>HttpScheduler</c> rethrows it as the exception an in-process caller would get. A container
    /// whose registry records no window — nothing attached a store — resolves a scheduler of this process,
    /// as it always did.
    /// </para>
    /// </remarks>
    private static void EnsureNotAWindow(SchedulerResolution resolution, string what)
    {
        if (resolution.IsWindow)
        {
            string schedulerName = resolution.Scheduler.SchedulerName;
            throw new SchedulerException(
                $"Cannot {what} through a window: '{schedulerName}' is read through the store attached as '{resolution.Ref.Target}', "
                + "not run by this process, and nothing in a shared database carries an instruction to a node. Reach "
                + "the node itself — its own HTTP API, or an agent — for anything that belongs to one process.");
        }
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> Clear(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        HttpContext httpContext,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, target, httpContext, schedulerRepository,
            scheduler => SchedulerOperations.Clear(scheduler, cancellationToken));
    }

    /// <summary>
    /// Pauses every trigger group, recording why and who asked.
    /// </summary>
    /// <remarks>
    /// The body is optional, and so is each of its members. No body — or one that says nothing — is the
    /// reasonless pause, made through the reasonless member as before 4.3; with a body, a missing
    /// <c>requestedBy</c> is the authenticated user.
    /// </remarks>
    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> PauseAll(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        HttpContext httpContext,
        string schedulerName,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PauseRequest? request,
        CancellationToken cancellationToken = default)
    {
        PauseDetails? details = EndpointHelper.PauseDetailsFor(request, httpContext);
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, target, httpContext, schedulerRepository,
            scheduler => SchedulerOperations.PauseAll(scheduler, details, cancellationToken));
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> ResumeAll(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        HttpContext httpContext,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, target, httpContext, schedulerRepository,
            scheduler => SchedulerOperations.ResumeAll(scheduler, cancellationToken));
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
        HttpContext httpContext,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, target, httpContext, schedulerRepository,
            scheduler => SchedulerOperations.GetClusterNodes(scheduler, cancellationToken));
    }

    /// <summary>
    /// This scheduler's events as they happen, as a server-sent event stream.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One frame per event, its <c>event:</c> type the event's kind and its <c>data:</c> the event as
    /// JSON — the same JSON every other body on this API is written as, because the result resolves the
    /// application's <see cref="Microsoft.AspNetCore.Http.Json.JsonOptions" /> that
    /// <see cref="QuartzJsonOptionsSetup" /> taught the wire contract. A stream with nothing to say emits
    /// a <c>Heartbeat</c> every
    /// <see cref="QuartzHttpApiOptions.EventStreamHeartbeatInterval" />.
    /// </para>
    /// <para>
    /// The scheduler is looked up before the stream opens, so an unknown name is the same <c>404</c> it is
    /// on every other route rather than an empty stream — and per-scheduler authorization runs in front of
    /// the handler, as it does for every route naming <c>{schedulerName}</c>, so a caller who may not see
    /// this scheduler is refused before a single frame is written.
    /// </para>
    /// <para>
    /// Without <c>target</c> the events are this process's: they come from the broker the schedulers here
    /// publish into, which is where <c>AddQuartzSchedulerEvents()</c> points them. From 4.9 a request with
    /// <c>target</c> reads the target's own stream — an HTTP target's event route, an agent's feed — as the
    /// dashboard's Live Logs page does, through <see cref="SchedulerEventLookup" />. A window has none, since
    /// a database carries a schedule rather than a feed of what happened to it, and is answered <c>501</c>.
    /// </para>
    /// <para>
    /// There is no replay and no <c>Last-Event-ID</c> handling: a subscription carries what happens after
    /// it is made. What a scheduler <em>has</em> done is
    /// <see cref="QueryExecutionHistory" />'s question.
    /// </para>
    /// </remarks>
    [ProducesResponseType(typeof(SchedulerEvent), StatusCodes.Status200OK, "text/event-stream")]
    private static Task<IResult> StreamEvents(
        ISchedulerRepository schedulerRepository,
        ISchedulerEventSource eventSource,
        IOptions<QuartzHttpApiOptions> apiOptions,
        TimeProvider timeProvider,
        HttpContext httpContext,
        string schedulerName,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithResolution(schedulerName, target, httpContext, schedulerRepository, resolution =>
        {
            IScheduler scheduler = resolution.Scheduler;

            // The scheduler's own spelling of its name, so a route that named it in another case still
            // subscribes to the events it publishes.
            IAsyncEnumerable<SseItem<SchedulerEvent>> frames = SchedulerEventStream.Read(
                EventsFor(httpContext, eventSource, target, resolution),
                scheduler.SchedulerName,
                apiOptions.Value.EventStreamHeartbeatInterval,
                timeProvider,
                httpContext.RequestAborted);

            return ValueTask.FromResult<IResult>(TypedResults.ServerSentEvents(frames));
        });
    }

    /// <summary>
    /// The source a request's events are read from: this process's broker for a bare key, as before 4.9,
    /// and the target's own stream for a key with a target.
    /// </summary>
    /// <exception cref="NotServedException">The target has no event stream: a window onto a store.</exception>
    private static ISchedulerEventSource EventsFor(HttpContext httpContext, ISchedulerEventSource broker, string? target, SchedulerResolution resolution)
    {
        if (target is null)
        {
            return broker;
        }

        ISchedulerEventSource? source = resolution.IsWindow ? null : SchedulerEventLookup.ForTargeted(httpContext.RequestServices, resolution.Ref);
        return source
               ?? throw new NotServedException(new NotSupportedException(
                   $"The scheduler '{resolution.Ref.Key}' has no event stream here: a window onto a store attached to this "
                   + "host reads a database, which carries a schedule rather than a feed of what happened to it. Stream the "
                   + "events of a node through its own HTTP API or agent."));
    }

    /// <summary>
    /// One page of what this scheduler has run, newest first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A job store holds what is scheduled; what happened is kept by an <see cref="IExecutionHistoryStore" />,
    /// and which one is <see cref="HistoryFor" />'s decision: the scheduler's own when it has one —
    /// <c>UseExecutionHistory()</c> keeps it in its database, keyed by its name — and the container's
    /// shared store, in memory and bounded unless the application registered its own, otherwise. The
    /// route names the scheduler because that is what the rows are keyed by, and because it is what
    /// per-scheduler authorization reads.
    /// </para>
    /// <para>
    /// <c>schedulerInstanceId</c> narrows to one node of a cluster; <c>jobContains</c> and
    /// <c>triggerContains</c> match a key's group, its name, or the two joined as <c>group.name</c>.
    /// <c>failedFinally=true</c> narrows to the executions that failed and were not retried — the
    /// occurrences that gave up — and <c>failedFinally=false</c> to everything else.
    /// </para>
    /// <para>
    /// From 4.4, <c>jobGroup</c> with <c>jobName</c> narrows to one job exactly, <c>firedFrom</c> (inclusive)
    /// and <c>firedBefore</c> (exclusive) to a window, and <c>results</c> to a set of results, repeated or
    /// comma-separated. A host before 4.4 ignores all five, which is why the HTTP client asks the host's
    /// version before it sends one.
    /// </para>
    /// <para>
    /// From 4.6, <c>triggerGroup</c> with <c>triggerName</c> narrows to one trigger exactly, and
    /// <c>fireInstanceId</c> to one firing. A host before 4.6 ignores all three.
    /// </para>
    /// </remarks>
    [ProducesResponseType(typeof(PagedResultDto<ExecutionHistoryEntryDto>), StatusCodes.Status200OK)]
    private static Task<IResult> QueryExecutionHistory(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        IExecutionHistoryStore historyStore,
        HttpContext httpContext,
        string schedulerName,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        int skip = 0,
        [Description(EndpointHelper.TakeDescription)] string? take = null,
        bool includeTotalCount = false,
        string? schedulerInstanceId = null,
        string? jobContains = null,
        string? triggerContains = null,
        bool? failedFinally = null,
        string? jobGroup = null,
        string? jobName = null,
        [Description("Only executions fired at or after this instant")] DateTimeOffset? firedFrom = null,
        [Description("Only executions fired before this instant")] DateTimeOffset? firedBefore = null,
        [FromQuery, Description(ResultsDescription)] string[]? results = null,
        [Description(TriggerGroupDescription)] string? triggerGroup = null,
        [Description(TriggerNameDescription)] string? triggerName = null,
        [Description(FireInstanceIdDescription)] string? fireInstanceId = null,
        CancellationToken cancellationToken = default)
    {
        ListingParameters listing = endpointHelper.Listing(skip, take, includeTotalCount);
        HistoryParameters filters = new()
        {
            JobGroup = jobGroup,
            JobName = jobName,
            TriggerGroup = triggerGroup,
            TriggerName = triggerName,
            FireInstanceId = fireInstanceId,
            FiredFrom = firedFrom,
            FiredBefore = firedBefore,
            Results = results
        };

        return endpointHelper.ExecuteWithJsonResponse(schedulerName, target, httpContext, schedulerRepository, scheduler => SchedulerOperations.QueryExecutionHistory(
            scheduler,
            HistoryFor(httpContext, historyStore, target, scheduler),
            listing,
            schedulerInstanceId,
            jobContains,
            triggerContains,
            failedFinally,
            filters,
            cancellationToken));
    }

    private const string ResultsDescription = "Only these results: Succeeded, Failed, Cancelled, Skipped. Repeated or comma-separated";

    private const string TriggerGroupDescription = "The trigger's group, matched exactly; needs triggerName";

    private const string TriggerNameDescription = "The trigger's name, matched exactly; needs triggerGroup";

    private const string FireInstanceIdDescription = "Only the executions of this firing, matched exactly";

    private const string ReasonsDescription = "Only these reasons: Missed, Overlap, Vetoed. Repeated or comma-separated; "
                                              + "without it, Missed and Overlap, which every client can read";

    /// <summary>
    /// One execution this scheduler ran, named by the <c>entryId</c> its listing row carries, with the
    /// lines its job logged when the scheduler captures them and the input it was given when the history
    /// records inputs.
    /// </summary>
    /// <remarks>
    /// The one history read that carries <c>log</c> and <c>input</c>: the listing leaves both out of every
    /// row. A row that was never recorded, or has since been trimmed, answers <c>404</c>.
    /// </remarks>
    [ProducesResponseType(typeof(ExecutionHistoryEntryDto), StatusCodes.Status200OK)]
    private static Task<IResult> GetExecution(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        IExecutionHistoryStore historyStore,
        HttpContext httpContext,
        string schedulerName,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        string entryId,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, target, httpContext, schedulerRepository, async scheduler =>
            await SchedulerOperations.GetExecution(scheduler, HistoryFor(httpContext, historyStore, target, scheduler), entryId, cancellationToken).ConfigureAwait(false)
            ?? throw NotFoundException.ForExecution(entryId));
    }

    /// <summary>
    /// One page of the firings this scheduler missed, newest first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read from the store <see cref="QueryExecutionHistory" /> reads. <c>schedulerInstanceId</c> and
    /// <c>triggerContains</c> narrow as they do there; from 4.4, <c>jobGroup</c> with <c>jobName</c>
    /// narrows to one job, and from 4.6 <c>triggerGroup</c> with <c>triggerName</c> to one trigger.
    /// </para>
    /// <para>
    /// <c>reasons</c> names the reasons to list. Without it the listing leaves out <c>Vetoed</c>: a 4.3
    /// client reads the reason through an enum that has no such name, and one such row fails its whole
    /// listing. A client that can read it asks for it.
    /// </para>
    /// </remarks>
    [ProducesResponseType(typeof(PagedResultDto<MisfireHistoryEntryDto>), StatusCodes.Status200OK)]
    private static Task<IResult> QueryMisfireHistory(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        IExecutionHistoryStore historyStore,
        HttpContext httpContext,
        string schedulerName,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        int skip = 0,
        [Description(EndpointHelper.TakeDescription)] string? take = null,
        bool includeTotalCount = false,
        string? schedulerInstanceId = null,
        string? triggerContains = null,
        string? jobGroup = null,
        string? jobName = null,
        [FromQuery, Description(ReasonsDescription)] string[]? reasons = null,
        [Description(TriggerGroupDescription)] string? triggerGroup = null,
        [Description(TriggerNameDescription)] string? triggerName = null,
        CancellationToken cancellationToken = default)
    {
        ListingParameters listing = endpointHelper.Listing(skip, take, includeTotalCount);
        HistoryParameters filters = new()
        {
            JobGroup = jobGroup,
            JobName = jobName,
            TriggerGroup = triggerGroup,
            TriggerName = triggerName,
            Reasons = reasons
        };

        return endpointHelper.ExecuteWithJsonResponse(schedulerName, target, httpContext, schedulerRepository, scheduler => SchedulerOperations.QueryMisfireHistory(
            scheduler,
            HistoryFor(httpContext, historyStore, target, scheduler),
            listing,
            schedulerInstanceId,
            triggerContains,
            filters,
            cancellationToken));
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
        HttpContext httpContext,
        string schedulerName,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        DateTimeOffset? since = null,
        CancellationToken cancellationToken = default)
    {
        if (since is null)
        {
            throw new BadHttpRequestException("since is required: a count with no window is a count of everything the store still holds");
        }

        return endpointHelper.ExecuteWithJsonResponse(schedulerName, target, httpContext, schedulerRepository, scheduler => SchedulerOperations.CountMisfires(
            scheduler,
            HistoryFor(httpContext, historyStore, target, scheduler),
            since.Value,
            cancellationToken));
    }

    /// <summary>
    /// One page of this scheduler's per-job run statuses, by job group and then name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Kept by the history store beside the rows, from every execution it records, so a status outlives
    /// the rows it was folded from. <c>failing=true</c> lists the jobs whose latest occurrences failed for
    /// good, and <c>failing=false</c> the rest.
    /// </para>
    /// <para>
    /// A store that keeps no status answers <c>501</c>, naming <see cref="NotSupportedException" />. A host
    /// older than 4.4 has no such route and answers <c>404</c> without problem details.
    /// </para>
    /// </remarks>
    [ProducesResponseType(typeof(PagedResultDto<JobRunStatusDto>), StatusCodes.Status200OK)]
    private static Task<IResult> QueryJobRunStatuses(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        IExecutionHistoryStore historyStore,
        HttpContext httpContext,
        string schedulerName,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        int skip = 0,
        [Description(EndpointHelper.TakeDescription)] string? take = null,
        bool includeTotalCount = false,
        [Description("true for the jobs failing now, false for the rest")] bool? failing = null,
        CancellationToken cancellationToken = default)
    {
        ListingParameters listing = endpointHelper.Listing(skip, take, includeTotalCount);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, target, httpContext, schedulerRepository, scheduler => Served(() => SchedulerOperations.QueryJobRunStatuses(
            scheduler,
            HistoryFor(httpContext, historyStore, target, scheduler),
            listing,
            failing,
            cancellationToken)));
    }

    /// <summary>
    /// One job's run status; <c>404</c> when the store has recorded no run of it.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="QueryJobRunStatuses" path="/remarks" />
    /// </remarks>
    [ProducesResponseType(typeof(JobRunStatusDto), StatusCodes.Status200OK)]
    private static Task<IResult> GetJobRunStatus(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        IExecutionHistoryStore historyStore,
        HttpContext httpContext,
        string schedulerName,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        string jobGroup,
        string jobName,
        CancellationToken cancellationToken = default)
    {
        JobKey jobKey = new(jobName, jobGroup);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, target, httpContext, schedulerRepository, async scheduler =>
            await Served(() => SchedulerOperations.GetJobRunStatus(
                scheduler,
                HistoryFor(httpContext, historyStore, target, scheduler),
                jobKey,
                cancellationToken)).ConfigureAwait(false)
            ?? throw NotFoundException.ForJobRunStatus(jobKey));
    }

    /// <summary>
    /// The run statuses of the jobs the body names, by job group and then name — at most
    /// <see cref="EndpointHelper.MaxKeysToFetch" /> of them. A job with no recorded run is absent.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="QueryJobRunStatuses" path="/remarks" />
    /// </remarks>
    [ProducesResponseType(typeof(JobRunStatusDto[]), StatusCodes.Status200OK)]
    private static Task<IResult> FetchJobRunStatuses(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        IExecutionHistoryStore historyStore,
        HttpContext httpContext,
        string schedulerName,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        JobKeySetRequest request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertIsValid(request);
        EndpointHelper.AssertKeysToFetch(request.Jobs);

        return endpointHelper.ExecuteWithJsonResponse(schedulerName, target, httpContext, schedulerRepository, scheduler => Served(() => SchedulerOperations.FetchJobRunStatuses(
            scheduler,
            HistoryFor(httpContext, historyStore, target, scheduler),
            request,
            cancellationToken)));
    }

    /// <summary>
    /// One page of this scheduler's per-trigger run statuses, by trigger group and then name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// From 4.9. Kept by the history store beside the job statuses, folded by their rule from the firings of each
    /// trigger's own key, other than one after which the trigger is finalized. <c>failing=true</c> lists the
    /// triggers whose latest occurrences failed for good, and <c>failing=false</c> the rest.
    /// </para>
    /// <para>
    /// A store that keeps no trigger status answers <c>501</c>, naming <see cref="NotSupportedException" />. A host
    /// older than 4.9 has no such route and answers <c>404</c> without problem details.
    /// </para>
    /// </remarks>
    [ProducesResponseType(typeof(PagedResultDto<TriggerRunStatusDto>), StatusCodes.Status200OK)]
    private static Task<IResult> QueryTriggerRunStatuses(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        IExecutionHistoryStore historyStore,
        HttpContext httpContext,
        string schedulerName,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        int skip = 0,
        [Description(EndpointHelper.TakeDescription)] string? take = null,
        bool includeTotalCount = false,
        [Description("true for the triggers failing now, false for the rest")] bool? failing = null,
        CancellationToken cancellationToken = default)
    {
        ListingParameters listing = endpointHelper.Listing(skip, take, includeTotalCount);
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, target, httpContext, schedulerRepository, scheduler => Served(() => SchedulerOperations.QueryTriggerRunStatuses(
            scheduler,
            HistoryFor(httpContext, historyStore, target, scheduler),
            listing,
            failing,
            cancellationToken)));
    }

    /// <summary>
    /// The run statuses of the triggers the body names, by trigger group and then name — at most
    /// <see cref="EndpointHelper.MaxKeysToFetch" /> of them. A trigger with no recorded run is absent.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="QueryTriggerRunStatuses" path="/remarks" />
    /// </remarks>
    [ProducesResponseType(typeof(TriggerRunStatusDto[]), StatusCodes.Status200OK)]
    private static Task<IResult> FetchTriggerRunStatuses(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        IExecutionHistoryStore historyStore,
        HttpContext httpContext,
        string schedulerName,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        TriggerKeySetRequest request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertIsValid(request);
        EndpointHelper.AssertKeysToFetch(request.Triggers);

        return endpointHelper.ExecuteWithJsonResponse(schedulerName, target, httpContext, schedulerRepository, scheduler => Served(() => SchedulerOperations.FetchTriggerRunStatuses(
            scheduler,
            HistoryFor(httpContext, historyStore, target, scheduler),
            request,
            cancellationToken)));
    }

    /// <summary>
    /// This scheduler's runs, counted by result and timed, one bucket per stretch of fire time that holds a
    /// run, oldest first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// From 4.4. Narrowed as <see cref="QueryExecutionHistory" /> narrows its page, so a chart beside it counts
    /// the rows it lists — except that <c>jobGroup</c> alone counts a whole group. <c>bucket</c> is a
    /// <c>TimeSpan</c>, <c>01:00:00</c> when absent and at least a minute; buckets start at whole multiples of
    /// it, so an hour starts on the hour and a day at midnight, UTC.
    /// </para>
    /// <para>
    /// A store that cannot count answers <c>501</c>, naming <see cref="NotSupportedException" />. A host older
    /// than 4.4 has no such route and answers <c>404</c> without problem details.
    /// </para>
    /// </remarks>
    [ProducesResponseType(typeof(ExecutionStatisticsDto), StatusCodes.Status200OK)]
    private static Task<IResult> QueryExecutionStatistics(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        IExecutionHistoryStore historyStore,
        HttpContext httpContext,
        string schedulerName,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        string? schedulerInstanceId = null,
        string? jobContains = null,
        string? triggerContains = null,
        bool? failedFinally = null,
        [Description("The job's group; alone, the whole group")] string? jobGroup = null,
        [Description("The job's name; needs jobGroup")] string? jobName = null,
        [Description("Only executions fired at or after this instant")] DateTimeOffset? firedFrom = null,
        [Description("Only executions fired before this instant")] DateTimeOffset? firedBefore = null,
        [FromQuery, Description(ResultsDescription)] string[]? results = null,
        [Description("How much fire time a bucket covers, as a TimeSpan: 01:00:00 when absent, at least 00:01:00")] TimeSpan? bucket = null,
        [Description(TriggerGroupDescription)] string? triggerGroup = null,
        [Description(TriggerNameDescription)] string? triggerName = null,
        [Description(FireInstanceIdDescription)] string? fireInstanceId = null,
        CancellationToken cancellationToken = default)
    {
        HistoryParameters filters = new()
        {
            JobGroup = jobGroup,
            JobName = jobName,
            TriggerGroup = triggerGroup,
            TriggerName = triggerName,
            FireInstanceId = fireInstanceId,
            FiredFrom = firedFrom,
            FiredBefore = firedBefore,
            Results = results
        };

        return endpointHelper.ExecuteWithJsonResponse(schedulerName, target, httpContext, schedulerRepository, scheduler => Served(() => SchedulerOperations.QueryExecutionStatistics(
            scheduler,
            HistoryFor(httpContext, historyStore, target, scheduler),
            schedulerInstanceId,
            jobContains,
            triggerContains,
            failedFinally,
            filters,
            bucket,
            cancellationToken)));
    }

    /// <summary>
    /// A status or statistics read, with a store's <see cref="NotSupportedException" /> answered as the
    /// <c>501</c> it is rather than as a server fault.
    /// </summary>
    private static async ValueTask<T> Served<T>(Func<ValueTask<T>> read)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (NotSupportedException e)
        {
            throw new NotServedException(e);
        }
    }

    /// <summary>
    /// The store that holds one scheduler's history, decided as the dashboard decides it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule is <see cref="ExecutionHistoryLookup" />'s, shared with the dashboard's client so that a
    /// scheduler has one history whichever of them is asked: a window reads the database it is a window
    /// onto, a scheduler with a store of its own reads that one, and everything else reads
    /// <paramref name="shared" />. Asked with the scheduler's own spelling of its name, which is the key
    /// its store is registered under, and from 4.9 with the request's target, which names the target's
    /// history: what the target recorded, read where it keeps it.
    /// </para>
    /// <para>
    /// A window whose store keeps no history here is refused rather than answered with this process's
    /// history, with the dashboard's own words — as a <see cref="SchedulerException" />, which is the
    /// <c>400</c> the API answers a scheduler's refusals with.
    /// </para>
    /// </remarks>
    private static IExecutionHistoryStore HistoryFor(HttpContext httpContext, IExecutionHistoryStore shared, string? target, IScheduler scheduler)
    {
        // A scheduler reached through a target's proxy keeps its history where the target does, read by the
        // name and the target as a pair; everything else — a scheduler of this process, a window, whether or not
        // the request named the window's target — is read by its name, as before 4.9. MCP's history tools read by
        // the same rule.
        return ExecutionHistoryLookup.ForResolved(httpContext.RequestServices, shared, scheduler, targeted: target is not null, out string? refusal)
            ?? throw new SchedulerException(refusal!);
    }

    [ProducesResponseType(typeof(ExecutionLimitsResponse), StatusCodes.Status200OK)]
    private static Task<IResult> GetExecutionLimits(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        HttpContext httpContext,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        CancellationToken cancellationToken = default)
    {
        return endpointHelper.ExecuteWithJsonResponse(schedulerName, target, httpContext, schedulerRepository,
            scheduler => SchedulerOperations.GetExecutionLimits(scheduler, cancellationToken));
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> SetExecutionLimits(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        HttpContext httpContext,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        [FromBody] SetExecutionLimitsRequest request,
        CancellationToken cancellationToken = default)
    {
        EndpointHelper.AssertIsValid(request);

        return EndpointHelper.ExecuteWithOkResponse(schedulerName, target, httpContext, schedulerRepository,
            scheduler => SchedulerOperations.SetExecutionLimits(scheduler, request, cancellationToken));
    }

    [ProducesResponseType(StatusCodes.Status200OK)]
    private static Task<IResult> ClearExecutionLimits(
        EndpointHelper endpointHelper,
        ISchedulerRepository schedulerRepository,
        string schedulerName,
        HttpContext httpContext,
        [FromQuery(Name = HttpApiConstants.TargetQueryParameter)] string? target,
        CancellationToken cancellationToken = default)
    {
        return EndpointHelper.ExecuteWithOkResponse(schedulerName, target, httpContext, schedulerRepository,
            scheduler => SchedulerOperations.ClearExecutionLimits(scheduler, cancellationToken));
    }
}