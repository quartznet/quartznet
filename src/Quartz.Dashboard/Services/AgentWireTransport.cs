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

namespace Quartz.Dashboard.Services;

/// <summary>
/// Carries wire requests down an agent's connection and brings its answers back, which is what makes an
/// agent's scheduler an ordinary <c>HttpScheduler</c> to the dashboard.
/// </summary>
/// <remarks>
/// <para>
/// A request goes down as <see cref="AgentRequest" /> with an id of its own, and the answer comes back
/// through the hub as <see cref="AgentAnswer" /> with the same id; the entry keeps the table of answers
/// still owed. An agent that is offline — disconnected, or silent for longer than its heartbeats allow —
/// is not asked at all: the send throws at once, the way an unreachable HTTP target throws, so the listing
/// reports <see cref="SchedulerStatus.Unknown" /> without waiting out a timeout nobody will answer.
/// </para>
/// <para>
/// A route the agent did not say it serves is answered <c>404</c> with no body, which is what the HTTP
/// client already treats as "this host has no such route": a 4.5 dashboard against a newer or older
/// agent degrades the way it degrades against a newer or older HTTP API.
/// </para>
/// </remarks>
internal sealed class AgentWireTransport : IWireTransport
{
    private readonly AgentRegistry registry;
    private readonly AgentEntry entry;

    public AgentWireTransport(AgentRegistry registry, AgentEntry entry)
    {
        this.registry = registry;
        this.entry = entry;
    }

    public async ValueTask<WireResponse> Send(WireRequest request, CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = registry.Now;
        if (entry.IsOffline(now, registry.Options))
        {
            throw new HttpRequestException(
                $"Agent '{entry.Target}' has been offline since {(entry.OfflineSinceUtc ?? entry.LastSeenUtc).ToString("O", CultureInfo.InvariantCulture)}.");
        }

        if (!entry.Serves(request.Route))
        {
            return new WireResponse(HttpStatusCode.NotFound, []);
        }

        if (registry.Options.IsJobTypeAllowed is { } isJobTypeAllowed && RefusedJobType(request, isJobTypeAllowed) is { } refused)
        {
            ProblemDetailsDto problem = ProblemDetailsFactory.Create(HttpStatusCode.Forbidden, $"Job type {refused} is not allowed");
            return new WireResponse(HttpStatusCode.Forbidden, JsonSerializer.SerializeToUtf8Bytes(problem, HttpApiJsonContext.Default.ProblemDetailsDto));
        }

        string id = Guid.NewGuid().ToString("N");
        AgentRequest down = new()
        {
            Id = id,
            Method = request.Route.Method,
            Path = request.Path,
            Body = request.Body,
            Timeout = registry.Options.OperationTimeout,
        };

        TaskCompletionSource<WireResponse> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        entry.Pending[id] = answer;

        try
        {
            // The timeout runs on the dashboard's clock, as the agent's copy of it runs on the worker's.
            using CancellationTokenSource expiry = new(registry.Options.OperationTimeout, registry.TimeProvider);
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, expiry.Token);

            await registry.Execute(entry, down, timeout.Token).ConfigureAwait(false);

            using CancellationTokenRegistration registration = timeout.Token.Register(
                static (state, token) => ((TaskCompletionSource<WireResponse>) state!).TrySetCanceled(token), answer);

            return await answer.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException cancelled)
        {
            entry.Pending.TryRemove(id, out _);
            await registry.Cancel(entry, id).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            throw new OperationCanceledException(
                $"Agent '{entry.Target}' did not answer {request.Route.Name} within {registry.Options.OperationTimeout}.",
                cancelled);
        }
        finally
        {
            entry.Pending.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// The first job type name in a request that stores a job the dashboard's own predicate does not
    /// allow, or <see langword="null" />. Read out of the body as JSON without the wire format: the names
    /// are strings at known places, and the trigger beside them is left unread.
    /// </summary>
    /// <remarks>
    /// Best-effort and case-sensitive by design: it reads the two shapes the catalogue's add-job routes
    /// carry, and nothing it misses is let through — the worker's own <c>IsJobTypeAllowed</c> is the
    /// enforcement, and this check exists so an operator can stop a type at the dashboard first.
    /// </remarks>
    private static string? RefusedJobType(WireRequest request, Func<string, bool> isJobTypeAllowed)
    {
        if (request.Body is null)
        {
            return null;
        }

        bool addsJobs = ReferenceEquals(request.Route, SchedulerRoutes.AddJob)
            || ReferenceEquals(request.Route, SchedulerRoutes.ScheduleJob)
            || ReferenceEquals(request.Route, SchedulerRoutes.ScheduleJobs);
        if (!addsJobs)
        {
            return null;
        }

        try
        {
            using JsonDocument body = JsonDocument.Parse(request.Body);
            return JobTypesIn(body.RootElement).FirstOrDefault(jobType => !isJobTypeAllowed(jobType));
        }
        catch (JsonException)
        {
            // A body that is not JSON is the agent's to refuse.
            return null;
        }
    }

    private static IEnumerable<string> JobTypesIn(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (root.TryGetProperty("job", out JsonElement job) && JobTypeOf(job) is { } single)
        {
            yield return single;
        }

        if (root.TryGetProperty("jobsAndTriggers", out JsonElement items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("job", out JsonElement itemJob) && JobTypeOf(itemJob) is { } each)
                {
                    yield return each;
                }
            }
        }
    }

    private static string? JobTypeOf(JsonElement job)
    {
        return job.ValueKind == JsonValueKind.Object && job.TryGetProperty("jobType", out JsonElement jobType) && jobType.ValueKind == JsonValueKind.String
            ? jobType.GetString()
            : null;
    }
}
