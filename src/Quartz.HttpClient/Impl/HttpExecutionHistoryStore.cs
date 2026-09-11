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
/// </remarks>
internal sealed class HttpExecutionHistoryStore : IExecutionHistoryStore
{
    private readonly string schedulerName;
    private readonly HttpClient httpClient;
    private readonly JsonSerializerOptions jsonSerializerOptions;

    /// <param name="schedulerName">The remote scheduler's name, which every request is addressed to.</param>
    /// <param name="httpClient">The client to call the remote scheduler with.</param>
    /// <param name="jsonSerializerOptions">
    /// Optional serializer options. A copy is taken and Quartz's own converters are added to the copy, so
    /// the instance passed in is left untouched.
    /// </param>
    public HttpExecutionHistoryStore(
        string schedulerName,
        HttpClient httpClient,
        JsonSerializerOptions? jsonSerializerOptions = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);
        ArgumentNullException.ThrowIfNull(httpClient);

        this.schedulerName = schedulerName;
        this.httpClient = httpClient;

        this.jsonSerializerOptions = jsonSerializerOptions is null
            ? new JsonSerializerOptions(JsonSerializerDefaults.Web)
            : new JsonSerializerOptions(jsonSerializerOptions);

        this.jsonSerializerOptions.ConfigureWireFormat(new SystemTextJsonSerializerRegistry());
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

    public async ValueTask<PagedResult<ExecutionHistoryEntry>> QueryExecutions(ExecutionHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        QueryStringBuilder parameters = new();
        parameters.AddPaging(query);
        AddFilters(parameters, query.SchedulerInstanceId, query.TriggerContains);

        if (!string.IsNullOrWhiteSpace(query.JobContains))
        {
            parameters.Add("jobContains", query.JobContains);
        }

        PagedResultDto<ExecutionHistoryEntryDto> result = await Read<PagedResultDto<ExecutionHistoryEntryDto>>(
            $"{HistoryUrl}/executions{parameters}", cancellationToken).ConfigureAwait(false);

        List<ExecutionHistoryEntry> items = new(result.Items.Length);
        foreach (ExecutionHistoryEntryDto item in result.Items)
        {
            items.Add(item.AsExecutionHistoryEntry(schedulerName));
        }

        return new PagedResult<ExecutionHistoryEntry>(items, result.HasMore, result.TotalCount);
    }

    public async ValueTask<PagedResult<MisfireHistoryEntry>> QueryMisfires(MisfireHistoryQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        QueryStringBuilder parameters = new();
        parameters.AddPaging(query);
        AddFilters(parameters, query.SchedulerInstanceId, query.TriggerContains);

        PagedResultDto<MisfireHistoryEntryDto> result = await Read<PagedResultDto<MisfireHistoryEntryDto>>(
            $"{HistoryUrl}/misfires{parameters}", cancellationToken).ConfigureAwait(false);

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
            $"{HistoryUrl}/misfires/count{parameters}", cancellationToken).ConfigureAwait(false);

        return result.Count;
    }

    /// <remarks>
    /// The name in the route is this client's own rather than the argument's: one registration is one
    /// remote scheduler, and a caller asking about another scheduler's misfires here is asking the wrong
    /// target.
    /// </remarks>
    private string HistoryUrl => $"schedulers/{schedulerName}/history";

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
    /// <c>HttpClientExtensions</c> surfaces as <see cref="HttpRequestException" /> because there are no
    /// problem details to read. That is exactly what a host older than these routes answers, and it is a
    /// capability rather than a failure — the dashboard renders it as "the target serves no history"
    /// beside a misfire tile showing a dash rather than a zero.
    /// </remarks>
    private async ValueTask<T> Read<T>(string url, CancellationToken cancellationToken)
    {
        try
        {
            return await httpClient.Get<T>(url, jsonSerializerOptions, cancellationToken).ConfigureAwait(false);
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
