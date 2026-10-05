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

using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Serialization.SystemTextJson;

namespace Quartz.Impl;

/// <summary>
/// Reads the execution history of a scheduler in another process through the Quartz HTTP API.
/// </summary>
/// <remarks>
/// <para>
/// The reading half of <see cref="IExecutionHistoryStore" /> and nothing else: history is recorded where
/// the scheduler runs, by the recorder in that process, so the two writers raise
/// <see cref="NotSupportedException" /> rather than inventing a way to push rows over a wire.
/// </para>
/// <para>
/// A target whose API predates the history routes answers <c>404</c> with no problem details, which is
/// reported as <see cref="NotSupportedException" /> too — "this target serves no history" is a fact a
/// caller can render, where an exception about a missing route is not. It is not confused with the
/// <c>404</c> that names an unknown scheduler: that one carries problem details and arrives as
/// <see cref="HttpClientException" />, which still propagates.
/// </para>
/// <para>
/// A host before 4.4 ignores the query parameters it does not know, and would answer a filtered read with
/// rows the filter excludes. So before the first read that carries a 4.4 filter, the host's version is
/// read from <c>GET …/schedulers/{name}</c>, and a host older than 4.4 is refused with
/// <see cref="NotSupportedException" /> before anything filtered is sent.
/// </para>
/// <para>
/// A row from a newer host that carries a name this client does not know is read as far as it can be: a
/// result it cannot name is no result, as on a row written before 4.4, and a misfire reason or a status's
/// last result it cannot name leaves that one row out of the page, with a line in the log.
/// </para>
/// </remarks>
internal sealed class HttpExecutionHistoryStore : IExecutionHistoryStore
{
    /// <summary>
    /// The first version whose history routes read the 4.4 filters.
    /// </summary>
    private static readonly Version FirstFilteringVersion = new(4, 4);

    /// <summary>
    /// The most keys one status fetch may carry, as the host enforces it.
    /// </summary>
    private const int MaxKeysPerFetch = 1000;

    private readonly string schedulerName;
    private readonly WireClient wire;

    /// <summary>
    /// Whether the host has been seen to read the 4.4 filters.
    /// </summary>
    /// <remarks>
    /// Only that answer is kept. An older host is asked again on the next filtered read, so a host upgraded
    /// under a reader that keeps running is noticed; a 4.4 host is not downgraded in place.
    /// </remarks>
    private volatile bool hostFilters;

    /// <param name="schedulerName">The remote scheduler's name, which every request is addressed to.</param>
    /// <param name="httpClient">The client to call the remote scheduler with.</param>
    /// <param name="jsonSerializerOptions">
    /// Optional serializer options. A copy is taken and Quartz's own converters are added to the copy, so
    /// the instance passed in is left untouched.
    /// </param>
    /// <param name="logger">
    /// Where a row left out of a listing for a name this client does not know is reported;
    /// <see langword="null" /> for whatever <c>LogProvider</c> was given.
    /// </param>
    public HttpExecutionHistoryStore(
        string schedulerName,
        HttpClient httpClient,
        JsonSerializerOptions? jsonSerializerOptions = null,
        ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);
        ArgumentNullException.ThrowIfNull(httpClient);

        this.schedulerName = schedulerName;

        JsonSerializerOptions serializerOptions = jsonSerializerOptions is null
            ? new JsonSerializerOptions(JsonSerializerDefaults.Web)
            : new JsonSerializerOptions(jsonSerializerOptions);

        serializerOptions.ConfigureClientWireFormat(
            new SystemTextJsonSerializerRegistry(),
            new UnknownWireNames(logger ?? HttpClientLog.Fallback(), schedulerName));

        wire = new WireClient(new HttpWireTransport(httpClient), serializerOptions);
    }

    /// <summary>
    /// Not supported: history is recorded where the scheduler runs.
    /// </summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public ValueTask AddExecution(ExecutionHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        throw RecordedElsewhere(nameof(AddExecution));
    }

    /// <summary>
    /// Not supported: history is recorded where the scheduler runs.
    /// </summary>
    /// <exception cref="NotSupportedException">Always.</exception>
    public ValueTask AddMisfire(MisfireHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        throw RecordedElsewhere(nameof(AddMisfire));
    }

    /// <remarks>
    /// <see cref="ExecutionHistoryQuery.Job" />, <see cref="ExecutionHistoryQuery.FiredFrom" />,
    /// <see cref="ExecutionHistoryQuery.FiredBefore" /> and <see cref="ExecutionHistoryQuery.Results" /> need
    /// a host at 4.4 or later, and raise <see cref="NotSupportedException" /> against an older one. An
    /// empty <see cref="ExecutionHistoryQuery.Results" /> lists nothing, and is answered without asking.
    /// </remarks>
    public async ValueTask<PagedResult<ExecutionHistoryEntry>> QueryExecutions(ExecutionHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Results is { Count: 0 })
        {
            return Nothing<ExecutionHistoryEntry>(query);
        }

        if (query.Job is not null || query.FiredFrom is not null || query.FiredBefore is not null || query.Results is not null)
        {
            await RequireFilters("job, fire time and result", cancellationToken).ConfigureAwait(false);
        }

        QueryStringBuilder parameters = new();
        parameters.AddPaging(query);
        AddFilters(parameters, query.SchedulerInstanceId, query.TriggerContains);

        if (!string.IsNullOrWhiteSpace(query.JobContains))
        {
            parameters.Add("jobContains", query.JobContains);
        }

        // Sent only when the query asks a question: a route that is not given the parameter lists
        // everything, which is what a null filter means.
        if (query.FailedFinally is { } failedFinally)
        {
            parameters.Add("failedFinally", failedFinally);
        }

        AddJob(parameters, query.Job);

        if (query.FiredFrom is { } firedFrom)
        {
            parameters.Add("firedFrom", firedFrom.ToString("O", CultureInfo.InvariantCulture));
        }

        if (query.FiredBefore is { } firedBefore)
        {
            parameters.Add("firedBefore", firedBefore.ToString("O", CultureInfo.InvariantCulture));
        }

        foreach (JobRunResult wanted in query.Results ?? [])
        {
            parameters.Add("results", wanted.ToString());
        }

        PagedResultDto<ExecutionHistoryEntryDto> result = await Read<PagedResultDto<ExecutionHistoryEntryDto>>(
            At(SchedulerRoutes.QueryExecutionHistory).WithQuery(parameters.ToString()), cancellationToken).ConfigureAwait(false);

        List<ExecutionHistoryEntry> items = new(result.Items.Length);
        foreach (ExecutionHistoryEntryDto item in result.Items)
        {
            items.Add(item.AsExecutionHistoryEntry(schedulerName));
        }

        return new PagedResult<ExecutionHistoryEntry>(items, result.HasMore, result.TotalCount);
    }

    /// <remarks>
    /// <c>GET …/history/executions/{entryId}</c>, the one history route that carries the captured log. A
    /// row the target does not have answers <see langword="null" />; a target older than 4.3, which has no
    /// such route, answers <see cref="NotSupportedException" />, as a target with no history routes at
    /// all does.
    /// </remarks>
    public async ValueTask<ExecutionHistoryEntry?> GetExecution(string schedulerName, string entryId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);

        ExecutionHistoryEntryDto? result;
        try
        {
            result = await wire.SendAndReadOrNull<ExecutionHistoryEntryDto>(
                SchedulerRoutes.GetExecution.For(this.schedulerName, entryId), cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new NotSupportedException(
                $"The scheduler '{this.schedulerName}' is reached over HTTP and the target does not serve single executions: "
                + "it answered 404 without problem details for the route, which a Quartz HTTP API older than 4.3 does. "
                + "Upgrade the scheduler's host to read an execution's captured log.",
                exception);
        }

        return result?.AsExecutionHistoryEntry(this.schedulerName);
    }

    /// <remarks>
    /// <para>
    /// Every reason this client can read is asked for by name when <see cref="MisfireHistoryQuery.Reasons" />
    /// is null, because a 4.4 host that is asked for none leaves <see cref="MisfireReason.Vetoed" /> out for
    /// the sake of older clients. A host before 4.4 ignores the parameter and answers every reason, which is
    /// the same answer.
    /// </para>
    /// <para>
    /// <see cref="MisfireHistoryQuery.Job" /> and a <see cref="MisfireHistoryQuery.Reasons" /> of the
    /// caller's own need a host at 4.4 or later, and raise <see cref="NotSupportedException" /> against an
    /// older one. An empty set of reasons lists nothing, and is answered without asking.
    /// </para>
    /// </remarks>
    public async ValueTask<PagedResult<MisfireHistoryEntry>> QueryMisfires(MisfireHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Reasons is { Count: 0 })
        {
            return Nothing<MisfireHistoryEntry>(query);
        }

        if (query.Job is not null || query.Reasons is not null)
        {
            await RequireFilters("job and reason", cancellationToken).ConfigureAwait(false);
        }

        QueryStringBuilder parameters = new();
        parameters.AddPaging(query);
        AddFilters(parameters, query.SchedulerInstanceId, query.TriggerContains);
        AddJob(parameters, query.Job);

        foreach (MisfireReason reason in query.Reasons ?? Enum.GetValues<MisfireReason>())
        {
            parameters.Add("reasons", reason.ToString());
        }

        PagedResultDto<MisfireHistoryEntryDto> result = await Read<PagedResultDto<MisfireHistoryEntryDto>>(
            At(SchedulerRoutes.QueryMisfireHistory).WithQuery(parameters.ToString()), cancellationToken).ConfigureAwait(false);

        List<MisfireHistoryEntry> items = new(result.Items.Length);
        foreach (MisfireHistoryEntryDto item in result.Items)
        {
            items.Add(item.AsMisfireHistoryEntry(schedulerName));
        }

        return new PagedResult<MisfireHistoryEntry>(items, result.HasMore, result.TotalCount);
    }

    public async ValueTask<int> CountMisfires(string schedulerName, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        QueryStringBuilder parameters = new();
        parameters.Add("since", since.ToString("O", CultureInfo.InvariantCulture));

        MisfireCountResponse result = await Read<MisfireCountResponse>(
            At(SchedulerRoutes.CountMisfires).WithQuery(parameters.ToString()), cancellationToken).ConfigureAwait(false);

        return result.Count;
    }

    /// <summary>
    /// One page of the target's per-job run statuses: <c>GET …/history/job-status</c>, or, for a query
    /// that names its jobs, <c>POST …/history/job-status/fetch</c>.
    /// </summary>
    /// <remarks>
    /// The fetch answers every named job's status in one list, so a query that names its jobs is filtered
    /// by <see cref="JobRunStatusQuery.Failing" /> and paged here, in the order the host answers with. It
    /// is sent in batches of at most a thousand keys, the most the host takes at once.
    /// </remarks>
    /// <exception cref="NotSupportedException">
    /// The target keeps no per-job status: its host is older than 4.4, or its history store keeps rows only.
    /// </exception>
    public async ValueTask<PagedResult<JobRunStatus>> QueryJobRunStatuses(JobRunStatusQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.Jobs is null)
        {
            QueryStringBuilder parameters = new();
            parameters.AddPaging(query);
            if (query.Failing is { } failing)
            {
                parameters.Add("failing", failing);
            }

            PagedResultDto<JobRunStatusDto> page = await ReadStatus<PagedResultDto<JobRunStatusDto>>(
                    At(SchedulerRoutes.QueryJobRunStatuses).WithQuery(parameters.ToString()), body: null, cancellationToken)
                .ConfigureAwait(false) ?? throw new HttpClientException("Could not deserialize response");

            return new PagedResult<JobRunStatus>(page.Items.Select(x => x.AsJobRunStatus(schedulerName)).ToList(), page.HasMore, page.TotalCount);
        }

        List<JobRunStatus> statuses = [];
        KeyDto[] keys = query.Jobs.Distinct().Select(KeyDto.Create).ToArray();
        foreach (KeyDto[] batch in keys.Chunk(MaxKeysPerFetch))
        {
            JobRunStatusDto[] fetched = await ReadStatus<JobRunStatusDto[]>(
                    At(SchedulerRoutes.FetchJobRunStatuses), new JobKeySetRequest(batch), cancellationToken)
                .ConfigureAwait(false) ?? [];

            statuses.AddRange(fetched.Select(x => x.AsJobRunStatus(schedulerName)));
        }

        IEnumerable<JobRunStatus> filtered = statuses
            .OrderBy(static status => status.Job.Group, StringComparer.Ordinal)
            .ThenBy(static status => status.Job.Name, StringComparer.Ordinal);

        if (query.Failing is { } wanted)
        {
            filtered = filtered.Where(status => (status.ConsecutiveFailures > 0) == wanted);
        }

        List<JobRunStatus> ordered = filtered.ToList();
        int skip = Math.Min(query.Skip, ordered.Count);
        List<JobRunStatus> items = ordered.Skip(skip).Take(query.Take).ToList();
        return new PagedResult<JobRunStatus>(items, skip + items.Count < ordered.Count, ordered.Count);
    }

    /// <summary>
    /// One job's run status: <c>GET …/history/job-status/{jobGroup}/{jobName}</c>, or
    /// <see langword="null" /> when the target has recorded no run of it.
    /// </summary>
    /// <inheritdoc cref="QueryJobRunStatuses" path="/exception" />
    public async ValueTask<JobRunStatus?> GetJobRunStatus(string schedulerName, JobKey jobKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(jobKey);

        JobRunStatusDto? status = await ReadStatus<JobRunStatusDto>(
            SchedulerRoutes.GetJobRunStatus.For(this.schedulerName, jobKey.Group, jobKey.Name), body: null, cancellationToken).ConfigureAwait(false);

        return status?.AsJobRunStatus(this.schedulerName);
    }

    /// <summary>
    /// Sends one status read and reads its answer: <see langword="null" /> for the <c>404</c> that says
    /// there is no such status.
    /// </summary>
    /// <remarks>
    /// Two answers are the target saying it keeps no status, and both are raised as
    /// <see cref="NotSupportedException" />: the <c>404</c> without problem details of a host that predates
    /// the routes, and the <c>501</c> of a host whose history store keeps rows only, whose detail says so.
    /// </remarks>
    private async ValueTask<T?> ReadStatus<T>(WireRequest request, JobKeySetRequest? body, CancellationToken cancellationToken) where T : class
    {
        WireResponse response = body is null
            ? await wire.Exchange(request, cancellationToken).ConfigureAwait(false)
            : await wire.Exchange(request, body, cancellationToken).ConfigureAwait(false);

        if (response.Status == HttpStatusCode.NotImplemented)
        {
            throw new NotSupportedException(
                wire.ProblemDetail(response)
                ?? $"The scheduler '{schedulerName}' is reached over HTTP and its host keeps no per-job run status.");
        }

        try
        {
            return wire.EnsureSuccess(response, throwOnNotFound: false) ? wire.Read<T>(response) : null;
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new NotSupportedException(
                $"The scheduler '{schedulerName}' is reached over HTTP and the target serves no per-job run status: "
                + "it answered 404 without problem details for the route, which a Quartz HTTP API older than 4.4 does. "
                + "Upgrade the scheduler's host to read its jobs' run statuses.",
                exception);
        }
    }

    /// <summary>
    /// Refuses a read carrying a 4.4 filter before it is sent to a host that would ignore the filter.
    /// </summary>
    /// <param name="filters">The filters, as the refusal names them.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <exception cref="NotSupportedException">The host is older than 4.4.</exception>
    private async ValueTask RequireFilters(string filters, CancellationToken cancellationToken)
    {
        if (hostFilters)
        {
            return;
        }

        SchedulerDto details = await wire.SendAndRead<SchedulerDto>(
            SchedulerRoutes.GetSchedulerDetails.For(schedulerName), cancellationToken).ConfigureAwait(false);

        string? reported = details.Statistics?.Version;
        if (Version.TryParse(reported, out Version? version) && version >= FirstFilteringVersion)
        {
            hostFilters = true;
            return;
        }

        throw new NotSupportedException(
            $"The scheduler '{schedulerName}' is reached over HTTP and its host runs Quartz {reported ?? "(unknown)"}, "
            + $"whose history routes ignore the {filters} filters and would answer with rows they exclude. "
            + "Upgrade the scheduler's host to 4.4 or later to filter its history by them.");
    }

    private static void AddJob(QueryStringBuilder parameters, JobKey? job)
    {
        if (job is not null)
        {
            parameters.Add("jobGroup", job.Group);
            parameters.Add("jobName", job.Name);
        }
    }

    /// <summary>
    /// The answer to a query whose filter lists nothing, given without asking.
    /// </summary>
    private static PagedResult<T> Nothing<T>(PagedQuery query)
    {
        return new PagedResult<T>([], HasMore: false, query.IncludeTotalCount ? 0 : null);
    }

    /// <summary>
    /// A request to one of the history routes for this client's scheduler.
    /// </summary>
    /// <remarks>
    /// The name in the route is this client's own rather than the argument's: one registration is one
    /// remote scheduler, and a caller asking about another scheduler's misfires here is asking the wrong
    /// target.
    /// </remarks>
    private WireRequest At(WireRoute route) => route.For(schedulerName);

    private static void AddFilters(QueryStringBuilder parameters, string? schedulerInstanceId, string? triggerContains)
    {
        if (!string.IsNullOrWhiteSpace(schedulerInstanceId))
        {
            parameters.Add("schedulerInstanceId", schedulerInstanceId);
        }

        if (!string.IsNullOrWhiteSpace(triggerContains))
        {
            parameters.Add("triggerContains", triggerContains);
        }
    }

    /// <summary>
    /// Reads one history body, turning "this server has no such route" into "this target serves no
    /// history".
    /// </summary>
    /// <remarks>
    /// An unmatched route answers <c>404</c> with no body at all, which
    /// <c>HttpClientExtensions.EnsureSuccess</c> surfaces as <see cref="HttpRequestException" /> because
    /// there are no problem details to read. That is exactly what a host older than these routes answers, and it is a
    /// capability rather than a failure — the dashboard renders it as "the target serves no history"
    /// beside a misfire tile showing a dash rather than a zero.
    /// </remarks>
    private async ValueTask<T> Read<T>(WireRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return await wire.SendAndRead<T>(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            throw new NotSupportedException(
                $"The scheduler '{schedulerName}' is reached over HTTP and the target does not serve history: "
                + "it answered 404 for the history route, which a Quartz HTTP API older than 4.1 does. Upgrade the "
                + "scheduler's host to serve its execution history.",
                exception);
        }
    }

    private static NotSupportedException RecordedElsewhere(string member)
    {
        return new NotSupportedException(
            $"{nameof(HttpExecutionHistoryStore)}.{member} is not supported: history is recorded where the scheduler "
            + "runs, by the recorder in that process, and this is a reader of it.");
    }
}
