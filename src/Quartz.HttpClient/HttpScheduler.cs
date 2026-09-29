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

using System.Globalization;
using System.Net;
using System.Text.Json;

using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Serialization.SystemTextJson;
using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz;

/// <summary>
/// An <see cref="IScheduler" /> that reaches a scheduler in another process through the Quartz HTTP API.
/// </summary>
/// <remarks>
/// <para>
/// Everything is a round trip: this type holds no scheduler state of its own, and every member calls
/// the endpoint that answers it. Construct one with the remote scheduler's name and an
/// <see cref="HttpClient" /> whose <see cref="HttpClient.BaseAddress" /> is where
/// <c>MapQuartzHttpApi</c> is mapped, or register it with <c>AddQuartzHttpClient</c>.
/// </para>
/// <para>
/// Every request is built from the route the server maps for it, out of the one table both ends read,
/// and goes through a transport that HTTP is one implementation of — so a scheduler reached some other
/// way is this same client over another transport, not a second mapping of <see cref="IScheduler" />.
/// </para>
/// <para>
/// What the server rejects arrives as the exception it named: <see cref="SchedulerException" /> and its
/// subclasses come back as themselves, and anything else — a request the endpoint refused before it
/// reached a scheduler, or a server that is not this API — as <see cref="HttpClientException" />, which
/// derives from <see cref="SchedulerException" /> so one <c>catch</c> covers both.
/// </para>
/// <para>
/// Two members cannot be honoured over a wire and raise <see cref="NotSupportedException" /> rather
/// than pretending: <see cref="Context" /> and <see cref="ListenerManager" />, each of which explains
/// itself. Both are physical limits rather than missing routes. <see cref="DisposeAsync" /> pointedly
/// does not shut the remote scheduler down — see its own remarks.
/// </para>
/// <para>
/// Quartz recognises this type as standing for a scheduler elsewhere, which is what keeps its two
/// blocking properties off the paths that must not block: the scheduler repository never reads
/// <see cref="Status" /> to decide whether an entry is dead, and a scheduler listing asks
/// <see cref="GetStatus" /> and <see cref="GetSchedulerInstanceId" /> under a deadline of its own. A
/// listing reports such a scheduler as <see cref="SchedulerOrigin.Remote" />.
/// </para>
/// </remarks>
public sealed class HttpScheduler : IScheduler, IProxyScheduler, IBackfillingScheduler
{
    private readonly WireClient wire;

    /// <param name="schedulerName">Name of the scheduler, must be same as the remote scheduler.</param>
    /// <param name="httpClient">The client to call the remote scheduler with.</param>
    /// <param name="jsonSerializerOptions">
    /// Optional serializer options. A copy is taken and Quartz's own converters are added to the copy,
    /// so the instance passed in is left untouched.
    /// </param>
    /// <param name="serializerRegistry">
    /// The trigger and calendar serializers to understand. Custom types are only readable over HTTP when
    /// their serializers are given here — the remote scheduler's own registrations are not visible in this
    /// process. Defaults to the built-in types.
    /// </param>
    public HttpScheduler(
        string schedulerName,
        HttpClient httpClient,
        JsonSerializerOptions? jsonSerializerOptions = null,
        SystemTextJsonSerializerRegistry? serializerRegistry = null)
        : this(RequireName(schedulerName), OverHttp(httpClient), jsonSerializerOptions, serializerRegistry)
    {
    }

    /// <param name="schedulerName">Name of the scheduler, must be same as the remote scheduler.</param>
    /// <param name="transport">What carries the requests to the remote scheduler and its answers back.</param>
    /// <param name="jsonSerializerOptions">Optional serializer options, copied as the public constructor copies them.</param>
    /// <param name="serializerRegistry">The trigger and calendar serializers to understand.</param>
    internal HttpScheduler(
        string schedulerName,
        IWireTransport transport,
        JsonSerializerOptions? jsonSerializerOptions,
        SystemTextJsonSerializerRegistry? serializerRegistry)
    {
        SchedulerName = RequireName(schedulerName);
        ArgumentNullException.ThrowIfNull(transport);

        // The caller's options are borrowed, not owned: adding our converters to their instance would
        // throw once those options had been used for anything (they are read-only from then on), and
        // would add the converters a second time when two clients share one instance.
        JsonSerializerOptions serializerOptions = jsonSerializerOptions is null
            ? new JsonSerializerOptions(JsonSerializerDefaults.Web)
            : new JsonSerializerOptions(jsonSerializerOptions);

        serializerOptions.ConfigureWireFormat(serializerRegistry ?? new SystemTextJsonSerializerRegistry());

        wire = new WireClient(transport, serializerOptions);
    }

    private static string RequireName(string schedulerName)
    {
        if (string.IsNullOrWhiteSpace(schedulerName))
        {
            throw new ArgumentException("Scheduler name required", nameof(schedulerName));
        }

        return schedulerName;
    }

    private static HttpWireTransport OverHttp(HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (!httpClient.BaseAddress?.ToString().EndsWith('/') == true)
        {
            throw new ArgumentException("HttpClient's BaseAddress must end in /", nameof(httpClient));
        }

        return new HttpWireTransport(httpClient);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The name this client was constructed with, which every request is addressed to. It is not read
    /// back from the server, so a name no scheduler goes by is reported when a member is called rather
    /// than here.
    /// </remarks>
    public string SchedulerName { get; }

    /// <inheritdoc />
    /// <remarks>
    /// One round trip, and a blocking one: <see cref="IScheduler" /> declares this a property, and a
    /// property cannot be awaited. Prefer <see cref="GetSchedulerInstanceId" />, which asks the same
    /// question in the same one request without holding a thread while it is answered, or
    /// <see cref="GetMetadata" /> where the whole of the scheduler's details is wanted.
    /// </remarks>
    public string SchedulerInstanceId => GetSchedulerDetailsSync().SchedulerInstanceId;

    /// <summary>
    /// The remote scheduler's instance Id, read over the network without blocking the calling thread.
    /// </summary>
    /// <remarks>
    /// One round trip, the same one <see cref="SchedulerInstanceId" /> makes and the same one
    /// <see cref="GetMetadata" /> makes. This is the member to call from a request path.
    /// </remarks>
    public async ValueTask<string> GetSchedulerInstanceId(CancellationToken cancellationToken = default)
    {
        var schedulerDto = await GetSchedulerDetails(cancellationToken).ConfigureAwait(false);
        return schedulerDto.SchedulerInstanceId;
    }

    /// <summary>
    /// The system clock, which is the only honest answer a proxy can give.
    /// </summary>
    /// <remarks>
    /// The clock that decides when a trigger fires is the remote scheduler's, and this process cannot
    /// read it: the HTTP API reports times, not a <see cref="System.TimeProvider" />, and a
    /// <see cref="System.TimeProvider" /> is not something that can be fetched over a wire. So a client
    /// building a trigger for a remote scheduler measures "now" against its own clock, exactly as it
    /// would have done writing <c>DateTimeOffset.UtcNow</c> by hand — the two machines agreeing about
    /// the time is the assumption the whole wire format already makes.
    /// </remarks>
    public TimeProvider TimeProvider => TimeProvider.System;

    /// <summary>
    /// The remote scheduler's lifecycle state, read over the network.
    /// </summary>
    /// <remarks>
    /// One round trip, where the three booleans this replaces were three - each asking the same endpoint
    /// the same question and reading a different field of the answer. A blocking round trip, though:
    /// prefer <see cref="GetStatus" />, which asks the same question without holding a thread while it
    /// is answered.
    /// </remarks>
    public SchedulerStatus Status => GetSchedulerDetailsSync().Status;

    /// <summary>
    /// The remote scheduler's lifecycle state, read over the network without blocking the calling
    /// thread.
    /// </summary>
    /// <remarks>
    /// One round trip, the same one <see cref="Status" /> makes. This is the member to call from a
    /// request path.
    /// </remarks>
    public async ValueTask<SchedulerStatus> GetStatus(CancellationToken cancellationToken = default)
    {
        var schedulerDto = await GetSchedulerDetails(cancellationToken).ConfigureAwait(false);
        return schedulerDto.Status;
    }

    /// <summary>
    /// Not supported. The context belongs to the scheduler's own process: it is a live, writable
    /// object there, and a snapshot fetched over HTTP would be neither.
    /// </summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public SchedulerContext Context => throw NotSupportedRemotely(
        nameof(Context),
        "the context is a live object in the scheduler's own process, and a copy fetched over HTTP could not be written back");

    /// <summary>
    /// Not supported. Listeners run where the jobs run, which is the scheduler's process rather than
    /// this one.
    /// </summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public IListenerManager ListenerManager => throw NotSupportedRemotely(
        nameof(ListenerManager),
        "listeners run in the process the jobs run in, which is not this one");

    /// <summary>
    /// The one message every "a remote scheduler cannot do this" throw carries, so that all of them
    /// name the member and say why.
    /// </summary>
    private static NotSupportedException NotSupportedRemotely(string member, string reason)
    {
        return new NotSupportedException($"{nameof(HttpScheduler)}.{member} is not supported: {reason}.");
    }

    /// <summary>
    /// Hands back a page, unless the caller asked for every match and the server answered fewer.
    /// </summary>
    /// <remarks>
    /// <see cref="PagedQuery.All" /> travels as the API's <c>all</c>, which a server with
    /// <c>QuartzHttpApiOptions.MaxPageSize</c> set answers with that many rows and a <c>hasMore</c> of
    /// true. Handing those back as if they were everything is the one thing worse than not answering:
    /// the 3.x-compatible listings — <c>GetJobKeys</c>, <c>GetTriggerKeys</c>, <c>GetCalendarNames</c> —
    /// return a bare list, so a truncated page would read as the whole store. Below the cap there is
    /// nothing to say and this costs a comparison.
    /// </remarks>
    private static PagedResult<T> WholeAnswer<T>(PagedQuery query, PagedResult<T> result)
    {
        if (query.Take == PagedQuery.All && result.HasMore)
        {
            throw new HttpClientException(
                "The request asked for every match and the server answered a bounded page: it has "
                + "QuartzHttpApiOptions.MaxPageSize set below the number of matches. Read the result a page "
                + "at a time with a Take of your own, or raise MaxPageSize on the server.");
        }

        return result;
    }

    /// <inheritdoc />
    public async ValueTask<SchedulerMetadata> GetMetadata(CancellationToken cancellationToken = default)
    {
        var schedulerDto = await GetSchedulerDetails(cancellationToken).ConfigureAwait(false);
        return new SchedulerMetadata
        {
            SchedulerName = schedulerDto.Name,
            SchedulerInstanceId = schedulerDto.SchedulerInstanceId,
            SchedulerTypeName = GetType().AssemblyQualifiedNameWithoutVersion(),
            IsProxy = true,
            Status = schedulerDto.Status,
            RunningSince = schedulerDto.Statistics.RunningSince,
            JobsExecuted = schedulerDto.Statistics.JobsExecuted,
            // the remote node's own count, which is what the member means everywhere
            LocalExecutingJobs = schedulerDto.Statistics.LocalExecutingJobs,
            // names pass through as strings: the remote types need not exist in this process
            JobStoreTypeName = schedulerDto.JobStore.Type,
            JobStorePersistent = schedulerDto.JobStore.Persistent,
            JobStoreClustered = schedulerDto.JobStore.Clustered,
            ThreadPoolTypeName = schedulerDto.ThreadPool.Type,
            ThreadPoolSize = schedulerDto.ThreadPool.Size,
            Version = schedulerDto.Statistics.Version,
        };
    }

    /// <inheritdoc />
    public async ValueTask<PagedResult<FireInstance>> QueryFireInstances(FireInstanceQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        QueryStringBuilder parameters = new();
        parameters.AddPaging(query);
        parameters.AddGroupMatcher(query.TriggerGroup);
        parameters.AddNameMatcher(query.TriggerName);

        if (query.Job is not null)
        {
            parameters.Add("jobName", query.Job.Name);
            parameters.Add("jobGroup", query.Job.Group);
        }

        if (query.SchedulerInstanceId is not null)
        {
            parameters.Add("schedulerInstanceId", query.SchedulerInstanceId);
        }

        // Always sent, because the query's own default is Executing rather than "everything": omitting
        // the parameter would have to mean "every state", and then the default could not travel.
        parameters.Add("state", query.State?.ToString() ?? HttpApiConstants.AnyFireInstanceState);

        PagedResultDto<FireInstanceDto> result = await wire
            .SendAndRead<PagedResultDto<FireInstanceDto>>(At(SchedulerRoutes.QueryFireInstances).WithQuery(parameters.ToString()), cancellationToken)
            .ConfigureAwait(false);

        return WholeAnswer(query, new PagedResult<FireInstance>(result.Items.Select(x => x.AsFireInstance()).ToList(), result.HasMore, result.TotalCount));
    }

    /// <inheritdoc />
    public async ValueTask<List<ClusterNode>> QueryClusterNodes(CancellationToken cancellationToken = default)
    {
        ClusterNodeDto[] result = await wire
            .SendAndRead<ClusterNodeDto[]>(At(SchedulerRoutes.GetClusterNodes), cancellationToken)
            .ConfigureAwait(false);

        // The server has already put the current node first, so the order travels rather than being
        // recomputed here: "current" means current on the node that answered, not on the one reading.
        List<ClusterNode> nodes = new(result.Length);
        foreach (ClusterNodeDto node in result)
        {
            nodes.Add(node.AsClusterNode());
        }

        return nodes;
    }

    /// <inheritdoc />
    public ValueTask Start(CancellationToken cancellationToken = default)
    {
        return wire.Send(At(SchedulerRoutes.Start), cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask StartDelayed(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        // "c" is the invariant round-trip form, which is what the endpoint parses the parameter with.
        return wire.Send(At(SchedulerRoutes.Start).WithQuery($"?delay={delay.ToString("c")}"), cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask Standby(CancellationToken cancellationToken = default)
    {
        return wire.Send(At(SchedulerRoutes.Standby), cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask Shutdown(bool waitForJobsToComplete = false, CancellationToken cancellationToken = default)
    {
        return wire.Send(At(SchedulerRoutes.Shutdown).WithQuery($"?waitForJobsToComplete={waitForJobsToComplete}"), cancellationToken);
    }

    /// <summary>
    /// Releases what this client owns, which is nothing — and in particular does <b>not</b> shut the
    /// remote scheduler down.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Disposing an <see cref="IScheduler" /> releases what that instance owns. A local scheduler owns
    /// the execution it drives, so disposing it stops it. This one owns only a connection to a scheduler
    /// running somewhere else, which other clients are using and which outlives this process: a client
    /// going away is not an instruction to stop scheduling for everybody. Call
    /// <see cref="Shutdown(bool, CancellationToken)" /> to stop the remote scheduler, deliberately.
    /// </para>
    /// <para>
    /// The <see cref="System.Net.Http.HttpClient" /> is not disposed either — it belongs to whoever made
    /// it, an <see cref="System.Net.Http.IHttpClientFactory" /> or the caller, and disposing something
    /// handed in is how a client shared with the rest of an application stops working.
    /// </para>
    /// </remarks>
    public ValueTask DisposeAsync()
    {
        return default;
    }

    /// <inheritdoc />
    public ValueTask<DateTimeOffset> ScheduleJob(IJobDetail jobDetail, ITrigger trigger, ScheduleJobOptions options = default, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobDetail);

        return DoScheduleJob(jobDetail, trigger, options.Replace, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<DateTimeOffset> ScheduleJob(ITrigger trigger, ScheduleJobOptions options = default, CancellationToken cancellationToken = default)
    {
        return DoScheduleJob(null, trigger, options.Replace, cancellationToken);
    }

    /// <inheritdoc cref="IScheduler.ScheduleTrigger" path="/summary|/param|/returns|/exception" />
    /// <remarks>
    /// The host decides under its store's lock, as a local scheduler does. A host older than 4.3 ignores
    /// the conflict mode: it is sent <c>replace</c> as well for <see cref="TriggerConflict.Replace" />, so
    /// that one still replaces, while <see cref="TriggerConflict.Keep" /> and
    /// <see cref="TriggerConflict.KeepEarlier" /> throw <see cref="ObjectAlreadyExistsException" /> on a
    /// conflict there, and an outcome it does not report is read as <see cref="ScheduleOutcome.Created" />.
    /// </remarks>
    public async ValueTask<ScheduleTriggerResult> ScheduleTrigger(ITrigger trigger, TriggerConflict onConflict, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        TriggerConflictResolution.RequireDefined(onConflict, nameof(onConflict));

        ScheduleJobResponse result = await wire.SendAndRead<ScheduleJobRequest, ScheduleJobResponse>(
            At(SchedulerRoutes.ScheduleJob),
            new ScheduleJobRequest(trigger, Job: null, Replace: onConflict == TriggerConflict.Replace) { OnConflict = onConflict },
            cancellationToken
        ).ConfigureAwait(false);

        return new ScheduleTriggerResult(result.FirstFireTimeUtc, result.Outcome ?? ScheduleOutcome.Created);
    }

    private async ValueTask<DateTimeOffset> DoScheduleJob(IJobDetail? jobDetail, ITrigger trigger, bool replace, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        var jobDetailsDto = jobDetail is not null ? JobDetailDto.Create(jobDetail) : null;
        var result = await wire.SendAndRead<ScheduleJobRequest, ScheduleJobResponse>(
            At(SchedulerRoutes.ScheduleJob),
            new ScheduleJobRequest(trigger, jobDetailsDto, replace),
            cancellationToken
        ).ConfigureAwait(false);

        return result.FirstFireTimeUtc;
    }

    /// <inheritdoc />
    public ValueTask ScheduleJobs(IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJobs, ScheduleJobOptions options = default, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggersAndJobs);

        var requestItems = triggersAndJobs.Select(CreateRequestItem).ToArray();
        var request = new ScheduleJobsRequest(requestItems, options.Replace);

        return wire.Send(At(SchedulerRoutes.ScheduleJobs), request, cancellationToken);

        static ScheduleJobsRequestItem CreateRequestItem(KeyValuePair<IJobDetail, IReadOnlyCollection<ITrigger>> triggersAndJob)
        {
            var (job, triggers) = (triggersAndJob.Key, triggersAndJob.Value);
            return new ScheduleJobsRequestItem(JobDetailDto.Create(job), triggers.ToArray());
        }
    }

    /// <inheritdoc />
    public ValueTask ScheduleJob(IJobDetail jobDetail, IReadOnlyCollection<ITrigger> triggersForJob, ScheduleJobOptions options = default, CancellationToken cancellationToken = default)
    {
        var triggersAndJobs = new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>>
        {
            { jobDetail, triggersForJob }
        };

        return ScheduleJobs(triggersAndJobs, options, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<bool> UnscheduleJob(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var result = await wire.SendAndRead<OperationAppliedResponse>(
            At(SchedulerRoutes.UnscheduleJob, triggerKey),
            cancellationToken
        ).ConfigureAwait(false);

        return result.Applied;
    }

    /// <inheritdoc />
    public async ValueTask<List<TriggerKey>> UnscheduleJobs(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggerKeys);

        var result = await wire.SendAndRead<UnscheduleJobsRequest, AppliedTriggerKeysResponse>(
            At(SchedulerRoutes.UnscheduleJobs),
            new UnscheduleJobsRequest(triggerKeys.Select(KeyDto.Create).ToArray()),
            cancellationToken
        ).ConfigureAwait(false);

        return [.. result.Triggers.Select(x => x.AsTriggerKey())];
    }

    /// <inheritdoc />
    public async ValueTask<List<TriggerKey>> UnscheduleJobs(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matcher);

        var urlParams = matcher.ToUrlParameters();
        var result = await wire.SendAndRead<AppliedTriggerKeysResponse>(
            At(SchedulerRoutes.UnscheduleJobsByGroup).WithQuery($"?{urlParams}"),
            cancellationToken
        ).ConfigureAwait(false);

        return [.. result.Triggers.Select(x => x.AsTriggerKey())];
    }

    /// <inheritdoc />
    public async ValueTask<DateTimeOffset?> RescheduleJob(TriggerKey triggerKey, ITrigger newTrigger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(newTrigger);

        var result = await wire.SendAndRead<RescheduleJobRequest, RescheduleJobResponse>(
            At(SchedulerRoutes.RescheduleJob, triggerKey),
            new RescheduleJobRequest(newTrigger),
            cancellationToken
        ).ConfigureAwait(false);

        return result.FirstFireTimeUtc;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The body carries only what the update set, so a member the caller never touched is not sent and
    /// the trigger keeps it. The schedule family of a misfire instruction travels too, which is what
    /// keeps <c>WithMisfireInstruction</c> rejected by the store for a trigger of another family here
    /// exactly as it is in process.
    /// </remarks>
    public async ValueTask<bool> UpdateTriggerDetails(TriggerKey triggerKey, TriggerDetailsUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var result = await wire.SendAndRead<UpdateTriggerDetailsRequest, OperationAppliedResponse>(
            At(SchedulerRoutes.UpdateTriggerDetails, triggerKey),
            UpdateTriggerDetailsRequest.Create(update),
            cancellationToken
        ).ConfigureAwait(false);

        return result.Applied;
    }

    /// <summary>
    /// A backfill in one request: the host's <c>backfill</c> route runs it, against the host's clock, and
    /// audits it once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The answers are an in-process call's: a refusal the host made comes back as the
    /// <see cref="ArgumentException" /> it would have been, with the host's words, and a trigger the host does
    /// not hold as <see cref="ObjectDoesNotExistException" />.
    /// </para>
    /// <para>
    /// A host older than 4.3 has no such route and answers <c>404</c> without problem details. That host is
    /// backfilled the way any scheduler is, through the reads and writes it has: a request per slot.
    /// </para>
    /// </remarks>
    async ValueTask<BackfillResult> IBackfillingScheduler.Backfill(
        TriggerKey triggerKey,
        DateTimeOffset from,
        DateTimeOffset until,
        BackfillOptions options,
        CancellationToken cancellationToken)
    {
        WireResponse response = await wire.Exchange(
            At(SchedulerRoutes.BackfillTrigger, triggerKey),
            BackfillRequest.Create(from, until, options),
            cancellationToken).ConfigureAwait(false);

        if (wire.TryReadRequestRefusal(response, out string? refusal))
        {
            throw Backfilling.RefusalOf(refusal);
        }

        bool found;
        try
        {
            found = wire.EnsureSuccess(response, throwOnNotFound: false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return await Backfilling.Compose(this, triggerKey, from, until, options, cancellationToken).ConfigureAwait(false);
        }

        if (!found)
        {
            throw Backfilling.MissingTrigger(triggerKey);
        }

        return wire.Read<BackfillResponse>(response).AsResult();
    }

    /// <inheritdoc />
    public async ValueTask SetExecutionLimits(ExecutionLimits? limits, CancellationToken cancellationToken = default)
    {
        if (limits is null)
        {
            await wire.Send(At(SchedulerRoutes.ClearExecutionLimits), cancellationToken).ConfigureAwait(false);
        }
        else
        {
            Dictionary<string, ExecutionLimitDto> dict = new();
            foreach (ExecutionGroupLimit limit in limits.Groups)
            {
                dict[limit.Group.ToConfigurationKey()] = new ExecutionLimitDto(limit.MaxConcurrent, limit.Scope);
            }
            await wire.Send(
                At(SchedulerRoutes.SetExecutionLimits),
                new SetExecutionLimitsRequest(dict, limits.UsesTriggerGroupWhenUnset),
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask<ExecutionLimits?> GetExecutionLimits(CancellationToken cancellationToken = default)
    {
        ExecutionLimitsResponse response = await wire.SendAndRead<ExecutionLimitsResponse>(At(SchedulerRoutes.GetExecutionLimits), cancellationToken).ConfigureAwait(false);

        // No groups and no derivation is the one answer that means "nothing is configured". A scheduler
        // that limits nothing but asked for the trigger group to stand in for an unset execution group
        // has said something, and saying it back as null would lose it.
        if (response.Limits is not { Count: > 0 } && !response.UseTriggerGroupWhenUnset)
        {
            return null;
        }

        ExecutionLimitsBuilder builder = ExecutionLimitsBuilder.Create();
        foreach (KeyValuePair<string, ExecutionLimitDto> kvp in response.Limits ?? [])
        {
            // The same reading the host's configuration and endpoint use, prefix keys included.
            builder.ForConfigurationKey(kvp.Key, kvp.Value.MaxConcurrent, kvp.Value.Scope);
        }

        if (response.UseTriggerGroupWhenUnset)
        {
            builder.UseTriggerGroupWhenUnset();
        }

        return builder.Build();
    }

    /// <inheritdoc />
    public ValueTask AddJob(IJobDetail jobDetail, AddJobOptions options = default, CancellationToken cancellationToken = default)
    {
        var request = new AddJobRequest(
            Job: JobDetailDto.Create(jobDetail),
            Replace: options.Replace,
            StoreNonDurableWhileAwaitingScheduling: options.StoreNonDurableWhileAwaitingScheduling
        );

        return wire.Send(At(SchedulerRoutes.AddJob), request, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var result = await wire.SendAndRead<OperationAppliedResponse>(At(SchedulerRoutes.DeleteJob, jobKey), cancellationToken).ConfigureAwait(false);
        return result.Applied;
    }

    /// <inheritdoc />
    public async ValueTask<List<JobKey>> DeleteJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobKeys);

        var result = await wire.SendAndRead<DeleteJobsRequest, AppliedJobKeysResponse>(
            At(SchedulerRoutes.DeleteJobs),
            new DeleteJobsRequest(jobKeys.Select(KeyDto.Create).ToArray()),
            cancellationToken
        ).ConfigureAwait(false);

        return [.. result.Jobs.Select(x => x.AsJobKey())];
    }

    /// <inheritdoc />
    public async ValueTask<List<JobKey>> DeleteJobs(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matcher);

        var urlParams = matcher.ToUrlParameters();
        var result = await wire.SendAndRead<AppliedJobKeysResponse>(
            At(SchedulerRoutes.DeleteJobsByGroup).WithQuery($"?{urlParams}"),
            cancellationToken
        ).ConfigureAwait(false);

        return [.. result.Jobs.Select(x => x.AsJobKey())];
    }

    /// <inheritdoc />
    public ValueTask TriggerJob(JobKey jobKey, JobDataMap? data = null, CancellationToken cancellationToken = default)
    {
        if (data is null)
        {
            return wire.Send(At(SchedulerRoutes.TriggerJob, jobKey), cancellationToken);
        }

        var request = new TriggerJobRequest(data);
        return wire.Send(At(SchedulerRoutes.TriggerJob, jobKey), request, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<bool> PauseJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var result = await wire.SendAndRead<OperationAppliedResponse>(At(SchedulerRoutes.PauseJob, jobKey), cancellationToken).ConfigureAwait(false);
        return result.Applied;
    }

    /// <inheritdoc />
    public async ValueTask<List<string>> PauseJobGroups(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        var urlParams = matcher.ToUrlParameters();
        var result = await wire.SendAndRead<AffectedGroupsResponse>(At(SchedulerRoutes.PauseJobs).WithQuery($"?{urlParams}"), cancellationToken).ConfigureAwait(false);
        return [.. result.Groups];
    }

    /// <inheritdoc />
    public async ValueTask<List<JobKey>> PauseJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobKeys);

        var request = new JobKeySetRequest([.. jobKeys.Select(KeyDto.Create)]);
        var result = await wire.SendAndRead<JobKeySetRequest, AppliedJobKeysResponse>(At(SchedulerRoutes.PauseJobKeys), request, cancellationToken).ConfigureAwait(false);
        return [.. result.Jobs.Select(x => x.AsJobKey())];
    }

    /// <inheritdoc />
    public async ValueTask<bool> PauseTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var result = await wire.SendAndRead<OperationAppliedResponse>(At(SchedulerRoutes.PauseTrigger, triggerKey), cancellationToken).ConfigureAwait(false);
        return result.Applied;
    }

    /// <inheritdoc />
    public async ValueTask<List<string>> PauseTriggerGroups(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        var urlParams = matcher.ToUrlParameters();
        var result = await wire.SendAndRead<AffectedGroupsResponse>(At(SchedulerRoutes.PauseTriggers).WithQuery($"?{urlParams}"), cancellationToken).ConfigureAwait(false);
        return [.. result.Groups];
    }

    /// <inheritdoc />
    public async ValueTask<List<TriggerKey>> PauseTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggerKeys);

        var request = new TriggerKeySetRequest([.. triggerKeys.Select(KeyDto.Create)]);
        var result = await wire.SendAndRead<TriggerKeySetRequest, AppliedTriggerKeysResponse>(At(SchedulerRoutes.PauseTriggerKeys), request, cancellationToken).ConfigureAwait(false);
        return [.. result.Triggers.Select(x => x.AsTriggerKey())];
    }

    /// <inheritdoc />
    public async ValueTask<bool> ResumeJob(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var result = await wire.SendAndRead<OperationAppliedResponse>(At(SchedulerRoutes.ResumeJob, jobKey), cancellationToken).ConfigureAwait(false);
        return result.Applied;
    }

    /// <inheritdoc />
    public async ValueTask<List<string>> ResumeJobGroups(GroupMatcher<JobKey> matcher, CancellationToken cancellationToken = default)
    {
        var urlParams = matcher.ToUrlParameters();
        var result = await wire.SendAndRead<AffectedGroupsResponse>(At(SchedulerRoutes.ResumeJobs).WithQuery($"?{urlParams}"), cancellationToken).ConfigureAwait(false);
        return [.. result.Groups];
    }

    /// <inheritdoc />
    public async ValueTask<List<JobKey>> ResumeJobs(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobKeys);

        var request = new JobKeySetRequest([.. jobKeys.Select(KeyDto.Create)]);
        var result = await wire.SendAndRead<JobKeySetRequest, AppliedJobKeysResponse>(At(SchedulerRoutes.ResumeJobKeys), request, cancellationToken).ConfigureAwait(false);
        return [.. result.Jobs.Select(x => x.AsJobKey())];
    }

    /// <inheritdoc />
    public async ValueTask<bool> ResumeTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var result = await wire.SendAndRead<OperationAppliedResponse>(At(SchedulerRoutes.ResumeTrigger, triggerKey), cancellationToken).ConfigureAwait(false);
        return result.Applied;
    }

    /// <inheritdoc />
    public async ValueTask<List<string>> ResumeTriggerGroups(GroupMatcher<TriggerKey> matcher, CancellationToken cancellationToken = default)
    {
        var urlParams = matcher.ToUrlParameters();
        var result = await wire.SendAndRead<AffectedGroupsResponse>(At(SchedulerRoutes.ResumeTriggers).WithQuery($"?{urlParams}"), cancellationToken).ConfigureAwait(false);
        return [.. result.Groups];
    }

    /// <inheritdoc />
    public async ValueTask<List<TriggerKey>> ResumeTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggerKeys);

        var request = new TriggerKeySetRequest([.. triggerKeys.Select(KeyDto.Create)]);
        var result = await wire.SendAndRead<TriggerKeySetRequest, AppliedTriggerKeysResponse>(At(SchedulerRoutes.ResumeTriggerKeys), request, cancellationToken).ConfigureAwait(false);
        return [.. result.Triggers.Select(x => x.AsTriggerKey())];
    }

    /// <inheritdoc />
    public ValueTask PauseAll(CancellationToken cancellationToken = default)
    {
        return wire.Send(At(SchedulerRoutes.PauseAll), cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask ResumeAll(CancellationToken cancellationToken = default)
    {
        return wire.Send(At(SchedulerRoutes.ResumeAll), cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The details go as the pause route's optional body. A host older than 4.3 ignores it and pauses
    /// without them, which is what <see cref="IScheduler.PauseTriggerWith" /> promises of a scheduler that
    /// records nothing. Details that say nothing are <see cref="PauseTrigger" />, which sends no body, so
    /// the host makes the pause it made before a pause could say anything.
    /// </remarks>
    public async ValueTask<bool> PauseTriggerWith(TriggerKey triggerKey, PauseDetails? details, CancellationToken cancellationToken = default)
    {
        if (PauseDetails.SaysNothing(details))
        {
            return await PauseTrigger(triggerKey, cancellationToken).ConfigureAwait(false);
        }

        var result = await wire.SendAndRead<PauseRequest, OperationAppliedResponse>(
            At(SchedulerRoutes.PauseTrigger, triggerKey), PauseBody(details), cancellationToken).ConfigureAwait(false);
        return result.Applied;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Details that say nothing are <see cref="PauseJob" />, which sends no body.
    /// </remarks>
    public async ValueTask<bool> PauseJobWith(JobKey jobKey, PauseDetails? details, CancellationToken cancellationToken = default)
    {
        if (PauseDetails.SaysNothing(details))
        {
            return await PauseJob(jobKey, cancellationToken).ConfigureAwait(false);
        }

        var result = await wire.SendAndRead<PauseRequest, OperationAppliedResponse>(
            At(SchedulerRoutes.PauseJob, jobKey), PauseBody(details), cancellationToken).ConfigureAwait(false);
        return result.Applied;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Details that say nothing are <see cref="PauseTriggerGroups" />, which sends no body.
    /// </remarks>
    public async ValueTask<List<string>> PauseTriggerGroupsWith(GroupMatcher<TriggerKey> matcher, PauseDetails? details, CancellationToken cancellationToken = default)
    {
        if (PauseDetails.SaysNothing(details))
        {
            return await PauseTriggerGroups(matcher, cancellationToken).ConfigureAwait(false);
        }

        ArgumentNullException.ThrowIfNull(matcher);

        var urlParams = matcher.ToUrlParameters();
        var result = await wire.SendAndRead<PauseRequest, AffectedGroupsResponse>(
            At(SchedulerRoutes.PauseTriggers).WithQuery($"?{urlParams}"), PauseBody(details), cancellationToken).ConfigureAwait(false);
        return [.. result.Groups];
    }

    /// <inheritdoc />
    /// <remarks>
    /// Details that say nothing are <see cref="PauseJobGroups" />, which sends no body.
    /// </remarks>
    public async ValueTask<List<string>> PauseJobGroupsWith(GroupMatcher<JobKey> matcher, PauseDetails? details, CancellationToken cancellationToken = default)
    {
        if (PauseDetails.SaysNothing(details))
        {
            return await PauseJobGroups(matcher, cancellationToken).ConfigureAwait(false);
        }

        ArgumentNullException.ThrowIfNull(matcher);

        var urlParams = matcher.ToUrlParameters();
        var result = await wire.SendAndRead<PauseRequest, AffectedGroupsResponse>(
            At(SchedulerRoutes.PauseJobs).WithQuery($"?{urlParams}"), PauseBody(details), cancellationToken).ConfigureAwait(false);
        return [.. result.Groups];
    }

    /// <inheritdoc />
    /// <remarks>
    /// Details that say nothing are <see cref="PauseAll" />, which sends no body.
    /// </remarks>
    public ValueTask PauseAllWith(PauseDetails? details, CancellationToken cancellationToken = default)
    {
        if (PauseDetails.SaysNothing(details))
        {
            return PauseAll(cancellationToken);
        }

        return wire.Send(At(SchedulerRoutes.PauseAll), PauseBody(details), cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// One request to the key-set pause route, with the details beside the keys. A host older than 4.4
    /// reads the keys alone and pauses without the details, which is what
    /// <see cref="IScheduler.PauseTriggersWith" /> promises of a scheduler that records nothing. Details
    /// that say nothing are <see cref="PauseTriggers" />, which sends the keys alone.
    /// </remarks>
    public async ValueTask<List<TriggerKey>> PauseTriggersWith(IReadOnlyCollection<TriggerKey> triggerKeys, PauseDetails? details, CancellationToken cancellationToken = default)
    {
        if (PauseDetails.SaysNothing(details))
        {
            return await PauseTriggers(triggerKeys, cancellationToken).ConfigureAwait(false);
        }

        ArgumentNullException.ThrowIfNull(triggerKeys);

        TriggerKeySetPauseRequest request = new([.. triggerKeys.Select(KeyDto.Create)], details.Reason, details.RequestedBy);
        AppliedTriggerKeysResponse result = await wire.SendAndRead<TriggerKeySetPauseRequest, AppliedTriggerKeysResponse>(At(SchedulerRoutes.PauseTriggerKeys), request, cancellationToken).ConfigureAwait(false);
        return [.. result.Triggers.Select(x => x.AsTriggerKey())];
    }

    /// <inheritdoc />
    /// <remarks>
    /// One request to the key-set pause route, as <see cref="PauseTriggersWith" /> sends. Details that say
    /// nothing are <see cref="PauseJobs" />.
    /// </remarks>
    public async ValueTask<List<JobKey>> PauseJobsWith(IReadOnlyCollection<JobKey> jobKeys, PauseDetails? details, CancellationToken cancellationToken = default)
    {
        if (PauseDetails.SaysNothing(details))
        {
            return await PauseJobs(jobKeys, cancellationToken).ConfigureAwait(false);
        }

        ArgumentNullException.ThrowIfNull(jobKeys);

        JobKeySetPauseRequest request = new([.. jobKeys.Select(KeyDto.Create)], details.Reason, details.RequestedBy);
        AppliedJobKeysResponse result = await wire.SendAndRead<JobKeySetPauseRequest, AppliedJobKeysResponse>(At(SchedulerRoutes.PauseJobKeys), request, cancellationToken).ConfigureAwait(false);
        return [.. result.Jobs.Select(x => x.AsJobKey())];
    }

    /// <inheritdoc />
    /// <remarks>
    /// Read off the trigger's state route, which answers the pause beside the state. A host older than
    /// 4.3 answers the state alone, which reads as no record.
    /// </remarks>
    public async ValueTask<PauseInfo?> GetTriggerPause(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var result = await wire.SendAndRead<TriggerStateDto>(At(SchedulerRoutes.GetTriggerState, triggerKey), cancellationToken).ConfigureAwait(false);
        return result.Pause?.AsPauseInfo();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Read off the group's paused route, which answers the pause beside whether it is paused.
    /// </remarks>
    public async ValueTask<PauseInfo?> GetTriggerGroupPause(string groupName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);

        var result = await wire.SendAndRead<GroupPausedResponse>(
            SchedulerRoutes.IsTriggerGroupPaused.For(SchedulerName, groupName), cancellationToken).ConfigureAwait(false);
        return result.Pause?.AsPauseInfo();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Read off the group's paused route, which answers the pause beside whether it is paused.
    /// </remarks>
    public async ValueTask<PauseInfo?> GetJobGroupPause(string groupName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);

        var result = await wire.SendAndRead<GroupPausedResponse>(
            SchedulerRoutes.IsJobGroupPaused.For(SchedulerName, groupName), cancellationToken).ConfigureAwait(false);
        return result.Pause?.AsPauseInfo();
    }

    private static PauseRequest PauseBody(PauseDetails details) => new(details.Reason, details.RequestedBy);

    /// <inheritdoc />
    public async ValueTask<PagedResult<JobHeader>> QueryJobs(JobQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        QueryStringBuilder parameters = new();
        parameters.AddPaging(query);
        parameters.AddGroupMatcher(query.Group);
        parameters.AddNameMatcher(query.Name);

        PagedResultDto<JobHeaderDto> result = await wire
            .SendAndRead<PagedResultDto<JobHeaderDto>>(At(SchedulerRoutes.QueryJobs).WithQuery(parameters.ToString()), cancellationToken)
            .ConfigureAwait(false);

        return WholeAnswer(query, new PagedResult<JobHeader>(result.Items.Select(x => x.AsJobHeader()).ToList(), result.HasMore, result.TotalCount));
    }

    /// <inheritdoc />
    public async ValueTask<PagedResult<TriggerHeader>> QueryTriggers(TriggerQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        QueryStringBuilder parameters = new();
        parameters.AddPaging(query);
        parameters.AddGroupMatcher(query.Group);
        parameters.AddNameMatcher(query.Name);

        if (query.Job is not null)
        {
            parameters.Add("jobName", query.Job.Name);
            parameters.Add("jobGroup", query.Job.Group);
        }

        if (query.CalendarName is not null)
        {
            parameters.Add("calendarName", query.CalendarName);
        }

        if (query.State is not null)
        {
            parameters.Add("state", query.State.Value.ToString());
        }

        if (query.NextFireTimeBefore is { } nextFireTimeBefore)
        {
            // Round-trip, so the offset survives the wire and the server reads the same instant.
            parameters.Add("nextFireTimeBefore", nextFireTimeBefore.ToString("O", CultureInfo.InvariantCulture));
        }

        PagedResultDto<TriggerHeaderDto> result = await wire
            .SendAndRead<PagedResultDto<TriggerHeaderDto>>(At(SchedulerRoutes.QueryTriggers).WithQuery(parameters.ToString()), cancellationToken)
            .ConfigureAwait(false);

        return WholeAnswer(query, new PagedResult<TriggerHeader>(result.Items.Select(x => x.AsTriggerHeader()).ToList(), result.HasMore, result.TotalCount));
    }

    /// <inheritdoc />
    public async ValueTask<PagedResult<JobGroup>> QueryJobGroups(JobGroupQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        QueryStringBuilder parameters = new();
        parameters.AddPaging(query);
        parameters.AddNameMatcher(query.Name);

        if (query.Paused is not null)
        {
            parameters.Add("paused", query.Paused.Value);
        }

        PagedResultDto<JobGroupDto> result = await wire
            .SendAndRead<PagedResultDto<JobGroupDto>>(At(SchedulerRoutes.QueryJobGroups).WithQuery(parameters.ToString()), cancellationToken)
            .ConfigureAwait(false);

        return WholeAnswer(query, new PagedResult<JobGroup>(result.Items.Select(x => x.AsJobGroup()).ToList(), result.HasMore, result.TotalCount));
    }

    /// <inheritdoc />
    public async ValueTask<PagedResult<TriggerGroup>> QueryTriggerGroups(TriggerGroupQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        QueryStringBuilder parameters = new();
        parameters.AddPaging(query);
        parameters.AddNameMatcher(query.Name);

        if (query.Paused is not null)
        {
            parameters.Add("paused", query.Paused.Value);
        }

        PagedResultDto<TriggerGroupDto> result = await wire
            .SendAndRead<PagedResultDto<TriggerGroupDto>>(At(SchedulerRoutes.QueryTriggerGroups).WithQuery(parameters.ToString()), cancellationToken)
            .ConfigureAwait(false);

        return WholeAnswer(query, new PagedResult<TriggerGroup>(result.Items.Select(x => x.AsTriggerGroup()).ToList(), result.HasMore, result.TotalCount));
    }

    /// <inheritdoc />
    public async ValueTask<PagedResult<string>> QueryCalendarNames(CalendarQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        QueryStringBuilder parameters = new();
        parameters.AddPaging(query);
        parameters.AddNameMatcher(query.Name);

        PagedResultDto<string> result = await wire
            .SendAndRead<PagedResultDto<string>>(At(SchedulerRoutes.QueryCalendarNames).WithQuery(parameters.ToString()), cancellationToken)
            .ConfigureAwait(false);

        return WholeAnswer(query, new PagedResult<string>([..result.Items], result.HasMore, result.TotalCount));
    }

    /// <inheritdoc />
    public async ValueTask<List<IJobDetail>> GetJobDetails(IReadOnlyCollection<JobKey> jobKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobKeys);

        if (jobKeys.Count == 0)
        {
            return [];
        }

        JobDetailDto[] dtos = await wire.SendAndRead<KeyDto[], JobDetailDto[]>(
            At(SchedulerRoutes.FetchJobs),
            jobKeys.Select(KeyDto.Create).ToArray(),
            cancellationToken
        ).ConfigureAwait(false);

        List<IJobDetail> result = new(dtos.Length);
        foreach (JobDetailDto dto in dtos)
        {
            var (jobDetail, errorReason) = dto.AsIJobDetail();
            if (jobDetail is null)
            {
                throw new HttpClientException("Could not create IJobDetail from JobDetailDto: " + errorReason);
            }

            result.Add(jobDetail);
        }

        return result;
    }

    /// <inheritdoc />
    public async ValueTask<List<ITrigger>> GetTriggers(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggerKeys);

        if (triggerKeys.Count == 0)
        {
            return [];
        }

        return await wire.SendAndRead<KeyDto[], List<ITrigger>>(
            At(SchedulerRoutes.FetchTriggers),
            triggerKeys.Select(KeyDto.Create).ToArray(),
            cancellationToken
        ).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<IJobDetail?> GetJobDetail(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var result = await wire.SendAndReadOrNull<JobDetailDto>(At(SchedulerRoutes.GetJobDetails, jobKey), cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return null;
        }

        var (jobDetail, errorReason) = result.AsIJobDetail();
        if (jobDetail is null)
        {
            throw new HttpClientException("Could not create IJobDetail from JobDetailDto: " + errorReason);
        }

        return jobDetail;
    }

    /// <inheritdoc />
    public async ValueTask<ITrigger?> GetTrigger(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var result = await wire.SendAndReadOrNull<ITrigger>(At(SchedulerRoutes.GetTrigger, triggerKey), cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    public async ValueTask<TriggerState> GetTriggerState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var result = await wire.SendAndRead<TriggerStateDto>(At(SchedulerRoutes.GetTriggerState, triggerKey), cancellationToken).ConfigureAwait(false);
        return result.State;
    }

    /// <inheritdoc />
    public async ValueTask<bool> ResetTriggerFromErrorState(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var result = await wire.SendAndRead<OperationAppliedResponse>(At(SchedulerRoutes.ResetTriggerFromErrorState, triggerKey), cancellationToken).ConfigureAwait(false);
        return result.Applied;
    }

    /// <inheritdoc />
    public async ValueTask<List<TriggerKey>> ResetTriggersFromErrorState(IReadOnlyCollection<TriggerKey> triggerKeys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(triggerKeys);

        var request = new TriggerKeySetRequest([.. triggerKeys.Select(KeyDto.Create)]);
        var result = await wire.SendAndRead<TriggerKeySetRequest, AppliedTriggerKeysResponse>(At(SchedulerRoutes.ResetTriggerKeysFromErrorState), request, cancellationToken).ConfigureAwait(false);
        return [.. result.Triggers.Select(x => x.AsTriggerKey())];
    }

    /// <inheritdoc />
    public ValueTask AddCalendar(string calendarName, ICalendar calendar, AddCalendarOptions options = default, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(calendarName))
        {
            throw new ArgumentException("Calendar name required", nameof(calendarName));
        }

        ArgumentNullException.ThrowIfNull(calendar);

        var requestContent = new AddCalendarRequest(calendarName, calendar, options.Replace, options.UpdateTriggers);
        return wire.Send(At(SchedulerRoutes.AddCalendar), requestContent, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<bool> DeleteCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        var result = await wire.SendAndRead<OperationAppliedResponse>(AtCalendar(SchedulerRoutes.DeleteCalendar, calendarName), cancellationToken).ConfigureAwait(false);
        return result.Applied;
    }

    /// <inheritdoc />
    public ValueTask<ICalendar?> GetCalendar(string calendarName, CancellationToken cancellationToken = default)
    {
        return wire.SendAndReadOrNull<ICalendar>(AtCalendar(SchedulerRoutes.GetCalendar, calendarName), cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask<bool> Interrupt(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var response = await wire.SendAndRead<OperationAppliedResponse>(At(SchedulerRoutes.InterruptJob, jobKey), cancellationToken).ConfigureAwait(false);
        return response.Applied;
    }

    /// <inheritdoc />
    public async ValueTask<bool> InterruptFireInstance(string fireInstanceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fireInstanceId))
        {
            throw new ArgumentException("Fire instance id required", nameof(fireInstanceId));
        }

        var response = await wire.SendAndRead<OperationAppliedResponse>(
            SchedulerRoutes.InterruptJobInstance.For(SchedulerName, fireInstanceId),
            cancellationToken
        ).ConfigureAwait(false);

        return response.Applied;
    }

    /// <inheritdoc />
    public async ValueTask<bool> Exists(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        var result = await wire.SendAndRead<ExistsResponse>(At(SchedulerRoutes.CheckJobExists, jobKey), cancellationToken).ConfigureAwait(false);
        return result.Exists;
    }

    /// <inheritdoc />
    public async ValueTask<bool> Exists(TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        var result = await wire.SendAndRead<ExistsResponse>(At(SchedulerRoutes.CheckTriggerExists, triggerKey), cancellationToken).ConfigureAwait(false);
        return result.Exists;
    }

    /// <inheritdoc />
    public async ValueTask<bool> Exists(string calendarName, CancellationToken cancellationToken = default)
    {
        var result = await wire.SendAndRead<ExistsResponse>(AtCalendar(SchedulerRoutes.CheckCalendarExists, calendarName), cancellationToken).ConfigureAwait(false);
        return result.Exists;
    }

    /// <inheritdoc />
    public ValueTask Clear(CancellationToken cancellationToken = default)
    {
        return wire.Send(At(SchedulerRoutes.Clear), cancellationToken);
    }

    /// <summary>
    /// A request to <paramref name="route" /> for this client's scheduler.
    /// </summary>
    private WireRequest At(WireRoute route) => route.For(SchedulerName);

    private WireRequest At(WireRoute route, JobKey job)
    {
        if (job is null)
        {
            throw new ArgumentNullException(nameof(job), "JobKey required");
        }

        return route.For(SchedulerName, job.Group, job.Name);
    }

    private WireRequest At(WireRoute route, TriggerKey trigger)
    {
        if (trigger is null)
        {
            throw new ArgumentNullException(nameof(trigger), "TriggerKey required");
        }

        return route.For(SchedulerName, trigger.Group, trigger.Name);
    }

    private WireRequest AtCalendar(WireRoute route, string calendarName)
    {
        if (string.IsNullOrWhiteSpace(calendarName))
        {
            throw new ArgumentException("Calendar name required", nameof(calendarName));
        }

        return route.For(SchedulerName, calendarName);
    }

    private SchedulerDto GetSchedulerDetailsSync()
    {
#pragma warning disable CA2012
        var schedulerDto = GetSchedulerDetails(CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
#pragma warning restore CA2012
        return schedulerDto;
    }

    private ValueTask<SchedulerDto> GetSchedulerDetails(CancellationToken cancellationToken)
    {
        return wire.SendAndRead<SchedulerDto>(At(SchedulerRoutes.GetSchedulerDetails), cancellationToken);
    }
}