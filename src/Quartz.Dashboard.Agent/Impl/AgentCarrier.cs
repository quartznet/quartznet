#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Serialization.SystemTextJson;

namespace Quartz.Impl;

/// <summary>
/// The HTTP API's contract without the HTTP: answers an <see cref="AgentRequest" /> as the API's endpoint
/// for that route would have, against the one scheduler this agent serves.
/// </summary>
/// <remarks>
/// <para>
/// One method per route of <see cref="SchedulerRoutes" />, each binding what the endpoint binds — route
/// values, the query, the body — and calling the same <see cref="SchedulerOperations" /> member, so a
/// request means the same thing whichever carrier it arrived through. What is the carrier's own stays
/// here: finding the scheduler, which is the one it was built for; refusing what the agent's options
/// refuse; turning a failure into problem details; and writing the answer.
/// </para>
/// <para>
/// The refusals are the agent's and are made in its process, which is what makes them hold against a
/// dashboard that has been taken over: <see cref="DashboardAgentOptions.ReadOnly" /> before any body is
/// read, <see cref="DashboardAgentOptions.IsOperationAllowed" /> by route name, and
/// <see cref="DashboardAgentOptions.IsJobTypeAllowed" /> — unset meaning every job type is refused —
/// where a job type name becomes a job. A page larger than the hub's message cap is refused here too,
/// rather than sent and dropped by the channel.
/// </para>
/// </remarks>
internal sealed class AgentCarrier
{
    /// <summary>
    /// What a read-only refusal says: the agent's sibling of the HTTP API's constant.
    /// </summary>
    public const string ReadOnlyDetail = "The Quartz dashboard agent is configured as read-only.";

    /// <summary>
    /// What a page too large for the channel says.
    /// </summary>
    public const string TooLargeDetail = "The page is too large for the agent channel; lower take.";

    /// <summary>
    /// The most keys one bulk fetch request may carry, as the HTTP API enforces it.
    /// </summary>
    private const int MaxKeysToFetch = 1000;

    // The route values and query parameters the catalogue names, as the HTTP API's handlers bind them.
    private const string SchedulerInstanceIdParameter = "schedulerInstanceId";
    private const string JobNameValue = "jobName";
    private const string JobGroupValue = "jobGroup";
    private const string CalendarNameValue = "calendarName";

    private static readonly Dictionary<WireRoute, Func<Call, ValueTask<Payload>>> Handlers = BuildHandlers();

    /// <summary>
    /// The routes this carrier answers: every route of the catalogue but the event stream, which goes
    /// up the tunnel as a stream of its own rather than as a request.
    /// </summary>
    public static readonly IReadOnlyCollection<WireRoute> ServedRoutes = Handlers.Keys.ToList().AsReadOnly();

    private readonly IScheduler scheduler;
    private readonly IServiceProvider services;
    private readonly DashboardAgentOptions options;
    private readonly JsonSerializerOptions wire;
    private readonly ILogger logger;

    private volatile int maxMessageBytes;

    /// <param name="scheduler">The one scheduler this agent serves.</param>
    /// <param name="services">The scheduler's container, for the history store and the serializer registry.</param>
    /// <param name="options">What the agent accepts.</param>
    /// <param name="logger">Where a refusal and a fault are recorded.</param>
    public AgentCarrier(IScheduler scheduler, IServiceProvider services, DashboardAgentOptions options, ILogger logger)
    {
        this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // The wire format the HTTP API writes, so a body reads back on the dashboard exactly as one from
        // an HTTP target does: the container's registry answers for custom triggers and calendars.
        wire = new JsonSerializerOptions(JsonSerializerDefaults.Web).ConfigureWireFormat(
            services.GetService<SystemTextJsonSerializerRegistry>() ?? new SystemTextJsonSerializerRegistry());
    }

    /// <summary>
    /// The names of the routes this carrier serves, as the registration states them.
    /// </summary>
    public static readonly string[] RouteNames = ServedRoutes.Select(route => route.Name).ToArray();

    /// <summary>
    /// The most bytes one answer may carry, as the hub stated it; zero until the hub has.
    /// </summary>
    public int MaxMessageBytes
    {
        get => maxMessageBytes;
        set => maxMessageBytes = value;
    }

    /// <summary>
    /// Answers one request, with the status and body the HTTP API would have answered with.
    /// </summary>
    public async ValueTask<AgentAnswer> Handle(AgentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        (WireRoute Route, Dictionary<string, string> Values)? match = SchedulerRoutes.Match(request.Method, request.Path);
        if (match is null || !Handlers.TryGetValue(match.Value.Route, out Func<Call, ValueTask<Payload>>? handler))
        {
            // What an unknown route answers over HTTP: a 404 with no problem details, which the client reads
            // as "this target has no such route".
            return new AgentAnswer { Id = request.Id, Status = (int) HttpStatusCode.NotFound };
        }

        (WireRoute route, Dictionary<string, string> values) = match.Value;

        try
        {
            Refuse(route, values);

            Call call = new(this, route, values, AgentQuery.Parse(request.Path), request.Body, cancellationToken);
            Payload payload = await handler(call).ConfigureAwait(false);

            int cap = maxMessageBytes;
            if (cap > 0 && payload.Body.Length > cap)
            {
                logger.OperationRefused(route.Name, TooLargeDetail);
                return Problem(request.Id, ProblemDetailsFactory.Create(HttpStatusCode.RequestEntityTooLarge, TooLargeDetail));
            }

            return new AgentAnswer { Id = request.Id, Status = (int) payload.Status, Body = payload.Body };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ProblemClassification problem = ProblemDetailsFactory.Classify(exception, includeStackTrace: false);
            if (problem.Kind == ProblemKind.Forbidden)
            {
                logger.OperationRefused(route.Name, problem.Detail);
            }
            else if (problem.Kind == ProblemKind.Fault)
            {
                logger.OperationFailed(route.Name, exception);
            }

            return Problem(request.Id, ProblemDetailsFactory.Create(problem));
        }
    }

    /// <summary>
    /// The refusals decided by the route and the options alone, before a body is read or a scheduler
    /// looked up — and the one scheduler this agent has, which every route has to name.
    /// </summary>
    private void Refuse(WireRoute route, Dictionary<string, string> values)
    {
        if (options.IsOperationAllowed is { } allowed && !allowed(route.Name))
        {
            throw new ForbiddenException($"The agent does not accept {route.Name}.");
        }

        if (route.Mutates && options.ReadOnly)
        {
            throw new ForbiddenException(ReadOnlyDetail);
        }

        if (values.TryGetValue("schedulerName", out string? schedulerName)
            && !string.Equals(schedulerName, scheduler.SchedulerName, StringComparison.OrdinalIgnoreCase))
        {
            throw NotFoundException.ForScheduler(schedulerName);
        }
    }

    /// <summary>
    /// The one place a job type name given to this agent becomes a job, and therefore the one place
    /// <see cref="DashboardAgentOptions.IsJobTypeAllowed" /> is enforced — and refuses every name while
    /// it is unset.
    /// </summary>
    /// <remarks>
    /// <see cref="JobDetailDto.AsIJobDetail" /> is <c>[RequiresUnreferencedCode]</c>, because the job type
    /// on the wire is a string, and this is where that statement stops travelling: it is recorded in
    /// <c>TrimAnalysisBaseline.cs</c>, which says the rest.
    /// </remarks>
    private IJobDetail ToJobDetail(JobDetailDto dto)
    {
        if (dto.JobType is { Length: > 0 } jobType)
        {
            if (options.IsJobTypeAllowed is null)
            {
                throw new ForbiddenException(
                    $"Job type {jobType} is not allowed: the agent refuses every job type until "
                    + $"{nameof(DashboardAgentOptions)}.{nameof(DashboardAgentOptions.IsJobTypeAllowed)} names the ones it accepts.");
            }

            if (!options.IsJobTypeAllowed(jobType))
            {
                throw ForbiddenException.ForJobType(jobType);
            }
        }

        (IJobDetail? jobDetail, string? errorReason) = dto.AsIJobDetail();
        return jobDetail ?? throw new InvalidRequestException("Request validation failed: " + errorReason);
    }

    /// <summary>
    /// The store that holds this scheduler's history, decided as the HTTP API decides it.
    /// </summary>
    private IExecutionHistoryStore History()
    {
        IExecutionHistoryStore shared = services.GetRequiredService<IExecutionHistoryStore>();
        return ExecutionHistoryLookup.Find(services, attachedStores: null, shared, scheduler.SchedulerName, out _, out string? refusal)
            ?? throw new SchedulerException(refusal!);
    }

    private static AgentAnswer Problem(string id, ProblemDetailsDto problem)
    {
        return new AgentAnswer
        {
            Id = id,
            Status = problem.Status ?? (int) HttpStatusCode.InternalServerError,
            Body = JsonSerializer.SerializeToUtf8Bytes(problem, HttpApiJsonContext.Default.ProblemDetailsDto),
        };
    }

    /// <summary>
    /// A refusal decided before any route is matched, in the words the HTTP API uses for one.
    /// </summary>
    internal static AgentAnswer Problem(string id, HttpStatusCode status, string detail)
    {
        return Problem(id, ProblemDetailsFactory.Create(status, detail));
    }

    private JsonTypeInfo<T> Format<T>() => (JsonTypeInfo<T>) wire.GetTypeInfo(typeof(T));

    // --- The table ------------------------------------------------------------------------------------

    private static Dictionary<WireRoute, Func<Call, ValueTask<Payload>>> BuildHandlers()
    {
        Dictionary<WireRoute, Func<Call, ValueTask<Payload>>> handlers = new();

        // Schedulers
        handlers[SchedulerRoutes.GetAllSchedulers] = call => call.Json(call.Carrier.ListSelf());
        handlers[SchedulerRoutes.GetSchedulerDetails] = call => call.Json(SchedulerOperations.GetSchedulerDetails(call.Scheduler, call.Token));
        handlers[SchedulerRoutes.GetSchedulerContext] = call => call.Json(SchedulerOperations.GetSchedulerContext(call.Scheduler));
        handlers[SchedulerRoutes.Start] = call =>
        {
            TimeSpan? delay = call.Query.GetTimeSpan("delay");
            if (delay < TimeSpan.Zero)
            {
                throw new InvalidRequestException("delay must not be negative");
            }

            return Call.Ok(SchedulerOperations.Start(call.Scheduler, delay, call.Token));
        };
        handlers[SchedulerRoutes.Standby] = call => Call.Ok(SchedulerOperations.Standby(call.Scheduler, call.Token));
        handlers[SchedulerRoutes.Shutdown] = call => Call.Ok(SchedulerOperations.Shutdown(call.Scheduler, call.Query.GetBool("waitForJobsToComplete", false), call.Token));
        handlers[SchedulerRoutes.Clear] = call => Call.Ok(SchedulerOperations.Clear(call.Scheduler, call.Token));
        handlers[SchedulerRoutes.PauseAll] = call => Call.Ok(SchedulerOperations.PauseAll(call.Scheduler, call.PauseDetails(), call.Token));
        handlers[SchedulerRoutes.ResumeAll] = call => Call.Ok(SchedulerOperations.ResumeAll(call.Scheduler, call.Token));
        handlers[SchedulerRoutes.GetClusterNodes] = call => call.Json(SchedulerOperations.GetClusterNodes(call.Scheduler, call.Token));

        // History
        handlers[SchedulerRoutes.QueryExecutionHistory] = call => call.Json(SchedulerOperations.QueryExecutionHistory(
            call.Scheduler, call.Carrier.History(), call.Listing(), call.Query.Get(SchedulerInstanceIdParameter), call.Query.Get("jobContains"),
            call.Query.Get("triggerContains"), call.Query.GetNullableBool("failedFinally"), call.HistoryFilters(), call.Token));
        handlers[SchedulerRoutes.GetExecution] = call => call.JsonOrNotFound(
            SchedulerOperations.GetExecution(call.Scheduler, call.Carrier.History(), call.Values["entryId"], call.Token),
            () => NotFoundException.ForExecution(call.Values["entryId"]));
        handlers[SchedulerRoutes.QueryMisfireHistory] = call => call.Json(SchedulerOperations.QueryMisfireHistory(
            call.Scheduler, call.Carrier.History(), call.Listing(), call.Query.Get(SchedulerInstanceIdParameter), call.Query.Get("triggerContains"),
            call.HistoryFilters(), call.Token));
        handlers[SchedulerRoutes.CountMisfires] = call =>
        {
            DateTimeOffset since = call.Query.GetDateTimeOffset("since")
                ?? throw new InvalidRequestException("since is required: a count with no window is a count of everything the store still holds");
            return call.Json(SchedulerOperations.CountMisfires(call.Scheduler, call.Carrier.History(), since, call.Token));
        };
        handlers[SchedulerRoutes.QueryJobRunStatuses] = call => call.Json(Served(() => SchedulerOperations.QueryJobRunStatuses(
            call.Scheduler, call.Carrier.History(), call.Listing(), call.Query.GetNullableBool("failing"), call.Token)));
        handlers[SchedulerRoutes.GetJobRunStatus] = call => call.JsonOrNotFound(
            Served(() => SchedulerOperations.GetJobRunStatus(call.Scheduler, call.Carrier.History(), call.JobKey(), call.Token)),
            () => NotFoundException.ForJobRunStatus(call.JobKey()));
        handlers[SchedulerRoutes.FetchJobRunStatuses] = call =>
        {
            JobKeySetRequest request = call.Body<JobKeySetRequest>();
            AssertKeysToFetch(request.Jobs);
            return call.Json(Served(() => SchedulerOperations.FetchJobRunStatuses(call.Scheduler, call.Carrier.History(), request, call.Token)));
        };
        handlers[SchedulerRoutes.QueryExecutionStatistics] = call => call.Json(Served(() => SchedulerOperations.QueryExecutionStatistics(
            call.Scheduler, call.Carrier.History(), call.Query.Get(SchedulerInstanceIdParameter), call.Query.Get("jobContains"),
            call.Query.Get("triggerContains"), call.Query.GetNullableBool("failedFinally"), call.HistoryFilters(),
            call.Query.GetTimeSpan("bucket"), call.Token)));
        handlers[SchedulerRoutes.GetExecutionLimits] = call => call.Json(SchedulerOperations.GetExecutionLimits(call.Scheduler, call.Token));
        handlers[SchedulerRoutes.SetExecutionLimits] = call => Call.Ok(SchedulerOperations.SetExecutionLimits(call.Scheduler, call.Body<SetExecutionLimitsRequest>(), call.Token));
        handlers[SchedulerRoutes.ClearExecutionLimits] = call => Call.Ok(SchedulerOperations.ClearExecutionLimits(call.Scheduler, call.Token));

        // Jobs
        handlers[SchedulerRoutes.QueryJobs] = call => call.Json(SchedulerOperations.QueryJobs(call.Scheduler, call.Listing().WithMatchers(call.Query), call.Token));
        handlers[SchedulerRoutes.FetchJobs] = call =>
        {
            KeyDto[] keys = call.Body<KeyDto[]>();
            AssertKeysToFetch(keys);
            return call.Json(SchedulerOperations.FetchJobs(call.Scheduler, keys, call.Token));
        };
        handlers[SchedulerRoutes.GetJobDetails] = call => call.JsonOrNotFound(
            SchedulerOperations.GetJobDetails(call.Scheduler, call.JobKey(), call.Token),
            () => NotFoundException.ForJob(call.JobKey()));
        handlers[SchedulerRoutes.CheckJobExists] = call => call.Json(SchedulerOperations.CheckJobExists(call.Scheduler, call.JobKey(), call.Token));
        handlers[SchedulerRoutes.GetJobTriggers] = call => call.Json(SchedulerOperations.GetJobTriggers(call.Scheduler, call.JobKey(), call.Token));
        handlers[SchedulerRoutes.QueryFireInstances] = call => call.Json(SchedulerOperations.QueryFireInstances(
            call.Scheduler, call.Listing(call.Query.Get("state")).WithMatchers(call.Query), call.Query.Get(JobNameValue), call.Query.Get(JobGroupValue),
            call.Query.Get(SchedulerInstanceIdParameter), call.Token));
        handlers[SchedulerRoutes.PauseJob] = call => call.Json(SchedulerOperations.PauseJob(call.Scheduler, call.JobKey(), call.PauseDetails(), call.Token));
        handlers[SchedulerRoutes.PauseJobs] = call => call.Json(SchedulerOperations.PauseJobs(call.Scheduler, call.Groups(), call.PauseDetails(), call.Token));
        handlers[SchedulerRoutes.PauseJobKeys] = call =>
        {
            JobKeySetPauseRequest request = call.Body<JobKeySetPauseRequest>();
            return call.Json(SchedulerOperations.PauseJobKeys(call.Scheduler, request, request.AsPauseDetails(authenticatedUser: null), call.Token));
        };
        handlers[SchedulerRoutes.ResumeJob] = call => call.Json(SchedulerOperations.ResumeJob(call.Scheduler, call.JobKey(), call.Token));
        handlers[SchedulerRoutes.ResumeJobs] = call => call.Json(SchedulerOperations.ResumeJobs(call.Scheduler, call.Groups(), call.Token));
        handlers[SchedulerRoutes.ResumeJobKeys] = call => call.Json(SchedulerOperations.ResumeJobKeys(call.Scheduler, call.Body<JobKeySetRequest>(), call.Token));
        handlers[SchedulerRoutes.TriggerJob] = call => Call.Ok(SchedulerOperations.TriggerJob(call.Scheduler, call.JobKey(), call.OptionalBody<TriggerJobRequest>(), call.Token));
        handlers[SchedulerRoutes.InterruptJob] = call => call.Json(SchedulerOperations.InterruptJob(call.Scheduler, call.JobKey(), call.Token));
        handlers[SchedulerRoutes.InterruptJobInstance] = call => call.Json(SchedulerOperations.InterruptJobInstance(call.Scheduler, call.Values["fireInstanceId"], call.Token));
        handlers[SchedulerRoutes.DeleteJob] = call => call.Json(SchedulerOperations.DeleteJob(call.Scheduler, call.JobKey(), call.Token));
        handlers[SchedulerRoutes.DeleteJobs] = call => call.Json(SchedulerOperations.DeleteJobs(call.Scheduler, call.Body<DeleteJobsRequest>(), call.Token));
        handlers[SchedulerRoutes.DeleteJobsByGroup] = call => call.Json(SchedulerOperations.DeleteJobsByGroup(call.Scheduler, call.Groups(), call.Token));
        handlers[SchedulerRoutes.AddJob] = call => Call.Ok(SchedulerOperations.AddJob(call.Scheduler, call.Body<AddJobRequest>(), call.Carrier.ToJobDetail, call.Token));
        handlers[SchedulerRoutes.QueryJobGroups] = call => call.Json(SchedulerOperations.QueryJobGroups(
            call.Scheduler, call.Listing().WithMatchers(call.Query), call.Query.GetNullableBool("paused"), call.Token));
        handlers[SchedulerRoutes.IsJobGroupPaused] = call => call.Json(SchedulerOperations.IsJobGroupPaused(call.Scheduler, call.Values[JobGroupValue], call.Token));

        // Triggers
        handlers[SchedulerRoutes.QueryTriggers] = call =>
        {
            string? jobName = call.Query.Get(JobNameValue);
            string? jobGroup = call.Query.Get(JobGroupValue);
            bool hasJobName = !string.IsNullOrWhiteSpace(jobName);
            bool hasJobGroup = !string.IsNullOrWhiteSpace(jobGroup);
            if (hasJobName != hasJobGroup)
            {
                throw new InvalidRequestException("Both jobName and jobGroup must be given to filter by job");
            }

            return call.Json(SchedulerOperations.QueryTriggers(
                call.Scheduler,
                call.Listing().WithMatchers(call.Query),
                hasJobName ? new JobKey(jobName!, jobGroup!) : null,
                call.Query.Get(CalendarNameValue),
                call.Query.GetEnum<TriggerState>("state"),
                call.Query.GetDateTimeOffset("nextFireTimeBefore"),
                call.Token));
        };
        handlers[SchedulerRoutes.FetchTriggers] = call =>
        {
            KeyDto[] keys = call.Body<KeyDto[]>();
            AssertKeysToFetch(keys);
            return call.Json(SchedulerOperations.FetchTriggers(call.Scheduler, keys, call.Token));
        };
        handlers[SchedulerRoutes.GetTrigger] = call => call.JsonOrNotFound(
            SchedulerOperations.GetTrigger(call.Scheduler, call.TriggerKey(), call.Token),
            () => NotFoundException.ForTrigger(call.TriggerKey()));
        handlers[SchedulerRoutes.CheckTriggerExists] = call => call.Json(SchedulerOperations.CheckTriggerExists(call.Scheduler, call.TriggerKey(), call.Token));
        handlers[SchedulerRoutes.GetTriggerState] = call => call.Json(SchedulerOperations.GetTriggerState(call.Scheduler, call.TriggerKey(), call.Token));
        handlers[SchedulerRoutes.ResetTriggerFromErrorState] = call => call.Json(SchedulerOperations.ResetTriggerFromErrorState(call.Scheduler, call.TriggerKey(), call.Token));
        handlers[SchedulerRoutes.ResetTriggerKeysFromErrorState] = call => call.Json(SchedulerOperations.ResetTriggerKeysFromErrorState(call.Scheduler, call.Body<TriggerKeySetRequest>(), call.Token));
        handlers[SchedulerRoutes.PauseTrigger] = call => call.Json(SchedulerOperations.PauseTrigger(call.Scheduler, call.TriggerKey(), call.PauseDetails(), call.Token));
        handlers[SchedulerRoutes.PauseTriggers] = call => call.Json(SchedulerOperations.PauseTriggers(call.Scheduler, call.Groups(), call.PauseDetails(), call.Token));
        handlers[SchedulerRoutes.PauseTriggerKeys] = call =>
        {
            TriggerKeySetPauseRequest request = call.Body<TriggerKeySetPauseRequest>();
            return call.Json(SchedulerOperations.PauseTriggerKeys(call.Scheduler, request, request.AsPauseDetails(authenticatedUser: null), call.Token));
        };
        handlers[SchedulerRoutes.ResumeTrigger] = call => call.Json(SchedulerOperations.ResumeTrigger(call.Scheduler, call.TriggerKey(), call.Token));
        handlers[SchedulerRoutes.ResumeTriggers] = call => call.Json(SchedulerOperations.ResumeTriggers(call.Scheduler, call.Groups(), call.Token));
        handlers[SchedulerRoutes.ResumeTriggerKeys] = call => call.Json(SchedulerOperations.ResumeTriggerKeys(call.Scheduler, call.Body<TriggerKeySetRequest>(), call.Token));
        handlers[SchedulerRoutes.QueryTriggerGroups] = call => call.Json(SchedulerOperations.QueryTriggerGroups(
            call.Scheduler, call.Listing().WithMatchers(call.Query), call.Query.GetNullableBool("paused"), call.Token));
        handlers[SchedulerRoutes.IsTriggerGroupPaused] = call => call.Json(SchedulerOperations.IsTriggerGroupPaused(call.Scheduler, call.Values["triggerGroup"], call.Token));
        handlers[SchedulerRoutes.ScheduleJob] = call => call.Json(SchedulerOperations.ScheduleJob(
            call.Scheduler, call.Body<ScheduleJobRequest>(), call.Carrier.ToJobDetail, authenticatedUser: null, call.Token));
        handlers[SchedulerRoutes.ScheduleJobs] = call => Call.Ok(SchedulerOperations.ScheduleJobs(
            call.Scheduler, call.Body<ScheduleJobsRequest>(), call.Carrier.ToJobDetail, authenticatedUser: null, call.Token));
        handlers[SchedulerRoutes.UnscheduleJob] = call => call.Json(SchedulerOperations.UnscheduleJob(call.Scheduler, call.TriggerKey(), call.Token));
        handlers[SchedulerRoutes.UnscheduleJobs] = call => call.Json(SchedulerOperations.UnscheduleJobs(call.Scheduler, call.Body<UnscheduleJobsRequest>(), call.Token));
        handlers[SchedulerRoutes.UnscheduleJobsByGroup] = call => call.Json(SchedulerOperations.UnscheduleJobsByGroup(call.Scheduler, call.Groups(), call.Token));
        handlers[SchedulerRoutes.RescheduleJob] = call => call.Json(SchedulerOperations.RescheduleJob(call.Scheduler, call.TriggerKey(), call.Body<RescheduleJobRequest>(), call.Token));
        handlers[SchedulerRoutes.UpdateTriggerDetails] = call => call.Json(SchedulerOperations.UpdateTriggerDetails(call.Scheduler, call.TriggerKey(), call.Body<UpdateTriggerDetailsRequest>(), call.Token));
        handlers[SchedulerRoutes.BackfillTrigger] = call => call.JsonOrNotFound(
            SchedulerOperations.BackfillTrigger(call.Scheduler, call.TriggerKey(), call.Body<BackfillRequest>(), call.Token),
            () => NotFoundException.ForTrigger(call.TriggerKey()));

        // Calendars
        handlers[SchedulerRoutes.QueryCalendarNames] = call => call.Json(SchedulerOperations.QueryCalendarNames(call.Scheduler, call.Listing().WithMatchers(call.Query), call.Token));
        handlers[SchedulerRoutes.GetCalendar] = call => call.JsonOrNotFound(
            SchedulerOperations.GetCalendar(call.Scheduler, call.Values[CalendarNameValue], call.Token),
            () => NotFoundException.ForCalendar(call.Values[CalendarNameValue]));
        handlers[SchedulerRoutes.CheckCalendarExists] = call => call.Json(SchedulerOperations.CheckCalendarExists(call.Scheduler, call.Values[CalendarNameValue], call.Token));
        handlers[SchedulerRoutes.AddCalendar] = call => Call.Ok(SchedulerOperations.AddCalendar(call.Scheduler, call.Body<AddCalendarRequest>(), call.Token));
        handlers[SchedulerRoutes.DeleteCalendar] = call => call.Json(SchedulerOperations.DeleteCalendar(call.Scheduler, call.Values[CalendarNameValue], call.Token));

        return handlers;
    }

    /// <summary>
    /// The listing of this agent's one scheduler, which is what its process would answer the listing
    /// route with: no registration is listed that nothing built, because the agent is the scheduler.
    /// </summary>
    private SchedulerHeaderDto[] ListSelf()
    {
        return [new SchedulerHeaderDto(scheduler.SchedulerName, scheduler.SchedulerInstanceId, scheduler.Status, SchedulerOrigin.Container)];
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

    private static void AssertKeysToFetch(KeyDto[]? keys)
    {
        if (keys is null)
        {
            throw new InvalidRequestException("Keys to fetch are required");
        }

        if (keys.Length > MaxKeysToFetch)
        {
            throw new InvalidRequestException($"Too many keys given, at most {MaxKeysToFetch} can be fetched at once");
        }

        foreach (KeyDto key in keys)
        {
            AssertIsValid(key);
        }
    }

    private static void AssertIsValid<T>(T toValidate) where T : IValidatable
    {
        string[] errors = toValidate.Validate().Distinct().ToArray();
        if (errors.Length > 0)
        {
            throw new InvalidRequestException($"Request validation failed: {string.Join(", ", errors)}");
        }
    }

    /// <summary>
    /// One answer: the status, and the body as the bytes the wire carries.
    /// </summary>
    private readonly record struct Payload(HttpStatusCode Status, byte[] Body);

    /// <summary>
    /// One request as a handler sees it: the carrier, the route it matched and the values it read, the
    /// query, the body, and the cancellation.
    /// </summary>
    private sealed class Call
    {
        private readonly AgentCarrier carrier;
        private readonly WireRoute route;
        private readonly byte[]? body;

        public Call(AgentCarrier carrier, WireRoute route, Dictionary<string, string> values, AgentQuery query, byte[]? body, CancellationToken token)
        {
            this.carrier = carrier;
            this.route = route;
            this.body = body;
            Values = values;
            Query = query;
            Token = token;
        }

        public AgentCarrier Carrier => carrier;

        public IScheduler Scheduler => carrier.scheduler;

        public Dictionary<string, string> Values { get; }

        public AgentQuery Query { get; }

        public CancellationToken Token { get; }

        public JobKey JobKey() => new(Values[JobNameValue], Values[JobGroupValue]);

        public TriggerKey TriggerKey() => new(Values["triggerName"], Values["triggerGroup"]);

        /// <summary>
        /// The paging the request carried, with this agent's page cap, as the API reads it before any
        /// scheduler is looked up.
        /// </summary>
        public ListingParameters Listing(string? state = null)
        {
            return ListingParameters.Read(Query.GetInt("skip", 0), Query.Get("take"), Query.GetBool("includeTotalCount", false), carrier.options.MaxPageSize, state);
        }

        public ListingParameters Groups()
        {
            return ListingParameters.Groups(Query.Get("groupContains"), Query.Get("groupEndsWith"), Query.Get("groupStartsWith"), Query.Get("groupEquals"));
        }

        public HistoryParameters HistoryFilters()
        {
            return new HistoryParameters
            {
                JobGroup = Query.Get(JobGroupValue),
                JobName = Query.Get(JobNameValue),
                FiredFrom = Query.GetDateTimeOffset("firedFrom"),
                FiredBefore = Query.GetDateTimeOffset("firedBefore"),
                Results = Query.GetAll("results"),
                Reasons = Query.GetAll("reasons"),
            };
        }

        /// <summary>
        /// What a pause request's optional body says, or <see langword="null" /> for no body at all: the
        /// reasonless pause, as before 4.3. No user is filled in — the dashboard's visitor is not this
        /// process's caller, and a body that names a requester names one.
        /// </summary>
        public PauseDetails? PauseDetails()
        {
            return OptionalBody<PauseRequest>()?.AsPauseDetails(authenticatedUser: null);
        }

        /// <summary>
        /// The body, read with the wire format and validated as the endpoint validates it.
        /// </summary>
        /// <exception cref="InvalidRequestException">There is none, it is not JSON, or it fails validation.</exception>
        public T Body<T>()
        {
            if (body is null || body.Length == 0)
            {
                throw new InvalidRequestException("Request body is required");
            }

            T value = Read<T>(body) ?? throw new InvalidRequestException("Request body is required");
            if (value is IValidatable validatable)
            {
                AssertIsValid(validatable);
            }

            return value;
        }

        /// <summary>
        /// The body when there is one, which the endpoints that take an optional body allow to be absent.
        /// </summary>
        public T? OptionalBody<T>() where T : class
        {
            if (body is null || body.Length == 0)
            {
                return null;
            }

            T? value = Read<T>(body);
            if (value is IValidatable validatable)
            {
                AssertIsValid(validatable);
            }

            return value;
        }

        private T? Read<T>(byte[] bytes)
        {
            try
            {
                return JsonSerializer.Deserialize(bytes, carrier.Format<T>());
            }
            catch (JsonException exception)
            {
                throw new InvalidRequestException($"Failed to read parameter \"{typeof(T).Name}\" from the request body as JSON.", exception);
            }
        }

        public async ValueTask<Payload> Json<T>(ValueTask<T> result) where T : notnull
        {
            T value = await result.ConfigureAwait(false);
            return Serialize(value);
        }

        public ValueTask<Payload> Json<T>(T value) where T : notnull
        {
            return new ValueTask<Payload>(Serialize(value));
        }

        private Payload Serialize<T>(T value) where T : notnull
        {
            return new Payload(HttpStatusCode.OK, JsonSerializer.SerializeToUtf8Bytes(value, carrier.Format<T>()));
        }

        /// <summary>
        /// A read whose subject may be absent: the body when it is there, and the carrier's <c>404</c>
        /// otherwise.
        /// </summary>
        public async ValueTask<Payload> JsonOrNotFound<T>(ValueTask<T?> result, Func<NotFoundException> notFound) where T : class
        {
            T value = await result.ConfigureAwait(false) ?? throw notFound();
            return new Payload(HttpStatusCode.OK, JsonSerializer.SerializeToUtf8Bytes(value, carrier.Format<T>()));
        }

        public static async ValueTask<Payload> Ok(ValueTask operation)
        {
            await operation.ConfigureAwait(false);
            return new Payload(HttpStatusCode.OK, []);
        }

        public override string ToString() => route.Name;
    }
}

/// <summary>
/// The matchers a listing request carried, read onto the parameters as the endpoints read them.
/// </summary>
internal static class AgentListingExtensions
{
    public static ListingParameters WithMatchers(this ListingParameters listing, AgentQuery query)
    {
        return listing with
        {
            GroupContains = query.Get("groupContains"),
            GroupEndsWith = query.Get("groupEndsWith"),
            GroupStartsWith = query.Get("groupStartsWith"),
            GroupEquals = query.Get("groupEquals"),
            NameContains = query.Get("nameContains"),
            NameEndsWith = query.Get("nameEndsWith"),
            NameStartsWith = query.Get("nameStartsWith"),
            NameEquals = query.Get("nameEquals"),
        };
    }
}
