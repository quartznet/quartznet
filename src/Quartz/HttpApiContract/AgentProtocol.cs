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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Quartz.HttpApiContract;

/// <summary>
/// What a dashboard agent and the dashboard's agent hub say to each other: the method names of the
/// SignalR hub both ends speak, and the version of the exchange.
/// </summary>
/// <remarks>
/// <para>
/// The agent is a scheduler's process dialling out to a dashboard, and the hub is where it arrives. The
/// requests that then flow down the connection are the HTTP API's own — a route, a path, a body — carried
/// as <see cref="AgentRequest" /> rather than as HTTP, and answered as <see cref="AgentAnswer" />. The
/// dashboard drives the agent's scheduler through the same <c>HttpScheduler</c> it drives an HTTP target
/// with, over a transport that sends these instead of requests.
/// </para>
/// <para>
/// Operations are correlated explicitly, by <see cref="AgentRequest.Id" />, rather than through the hub's
/// own client results: an explicit answer carries cancellation (<see cref="Cancel" />) and survives
/// whatever the hub's dispatch does with a result nobody is waiting for.
/// </para>
/// </remarks>
internal static class AgentProtocol
{
    /// <summary>
    /// The version of this exchange, which an agent states when it registers.
    /// </summary>
    public const int Version = 1;

    /// <summary>
    /// Where the hub is mapped, under the dashboard's path.
    /// </summary>
    public const string HubPath = "/agents";

    // --- Agent to hub ----------------------------------------------------------------------------------

    /// <summary>The first call on every connection; answered with <see cref="AgentRegistered" />.</summary>
    public const string Register = "Register";

    /// <summary>Sent every <see cref="AgentRegistered.HeartbeatInterval" />.</summary>
    public const string Heartbeat = "Heartbeat";

    /// <summary>One per <see cref="Execute" />, carrying the <see cref="AgentAnswer" />.</summary>
    public const string Answer = "Answer";

    /// <summary>A client-to-server stream of <see cref="SchedulerEvent" />, open while watched.</summary>
    public const string Events = "Events";

    /// <summary>A graceful shutdown: the hub keeps the target listed as shut down until it is forgotten.</summary>
    public const string Unregister = "Unregister";

    // --- Hub to agent ----------------------------------------------------------------------------------

    /// <summary>A request to run, correlated by <see cref="AgentRequest.Id" />.</summary>
    public const string Execute = "Execute";

    /// <summary>The caller of a request went away, or the request timed out.</summary>
    public const string Cancel = "Cancel";

    /// <summary>Start or stop the event stream.</summary>
    public const string Watch = "Watch";

    /// <summary>The hub is dropping this connection, and says why.</summary>
    public const string Close = "Close";

    /// <summary>
    /// Teaches <paramref name="options" /> how the protocol's payloads are written, which both ends have to
    /// agree on: the two enums a payload carries as their names, and the contract's generated metadata
    /// in front of whatever resolver the options already had.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only what the hub's payloads need. The bodies inside an <see cref="AgentRequest" /> and an
    /// <see cref="AgentAnswer" /> are bytes, written and read by the wire format each end already has for
    /// the HTTP API; the hub protocol never looks inside them.
    /// </para>
    /// <para>
    /// The resolver insertion changes which code answers for the five records above and for
    /// <see cref="SchedulerEvent" /> — the generated metadata rather than reflection, so the agent survives
    /// trimming — and nothing about their shape: the metadata takes its naming policy and enum spelling
    /// from the options, as the wire-format tests pin. SignalR has one JSON protocol per application, so
    /// on the dashboard this reaches the browser hub's payloads too, which name none of those types; a
    /// per-hub protocol would need an <c>IHubProtocol</c> of its own, which is not worth a resolver that
    /// changes nothing.
    /// </para>
    /// </remarks>
    public static void ConfigureHubPayloads(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        AddConverterOnce<JsonStringEnumConverter<SchedulerStatus>>(options);
        AddConverterOnce<JsonStringEnumConverter<SchedulerEventKind>>(options);

        if (!options.TypeInfoResolverChain.Contains(HttpApiJsonContext.Default))
        {
            options.TypeInfoResolverChain.Insert(0, HttpApiJsonContext.Default);
        }
    }

    /// <summary>
    /// The dashboard already names <see cref="SchedulerStatus" />'s converter for its browser hub; a
    /// second copy of a converter is a second lookup for nothing.
    /// </summary>
    private static void AddConverterOnce<TConverter>(JsonSerializerOptions options)
        where TConverter : JsonConverter, new()
    {
        if (!options.Converters.Any(static converter => converter is TConverter))
        {
            options.Converters.Add(new TConverter());
        }
    }
}

/// <summary>
/// What an agent says about itself when it connects.
/// </summary>
internal sealed record AgentRegistration
{
    public int ProtocolVersion { get; init; } = AgentProtocol.Version;

    /// <summary>The target the agent registers under: the first half of its scheduler's key.</summary>
    public required string Target { get; init; }

    public required string SchedulerName { get; init; }

    public required string SchedulerInstanceId { get; init; }

    public required string QuartzVersion { get; init; }

    /// <summary>
    /// The names of the routes this agent serves, which is how a dashboard newer or older than the agent
    /// learns what it may ask: a route absent from here is answered <c>404</c> without being sent.
    /// </summary>
    public required string[] Routes { get; init; }

    public bool ReadOnly { get; init; }

    public TimeSpan HeartbeatInterval { get; init; }
}

/// <summary>
/// The hub's answer to a registration.
/// </summary>
internal sealed record AgentRegistered
{
    public bool Accepted { get; init; }

    /// <summary>Why not, when not: a refused token, a target another instance holds, a version the hub cannot speak.</summary>
    public string? Reason { get; init; }

    /// <summary>The most bytes one answer may carry; the agent refuses a larger page itself.</summary>
    public int MaxMessageBytes { get; init; }

    /// <summary>How often the hub expects a heartbeat, which the agent adopts.</summary>
    public TimeSpan HeartbeatInterval { get; init; }

    /// <summary>How many heartbeats may be missed before the hub reports the scheduler as unknown.</summary>
    public int OfflineAfterMissedHeartbeats { get; init; }
}

/// <summary>
/// One heartbeat: the scheduler's state, and when it was sent on the agent's clock.
/// </summary>
internal sealed record AgentHeartbeat
{
    public SchedulerStatus Status { get; init; }

    public DateTimeOffset SentAtUtc { get; init; }
}

/// <summary>
/// One request down the tunnel: an HTTP API call without the HTTP.
/// </summary>
internal sealed record AgentRequest
{
    public required string Id { get; init; }

    public required string Method { get; init; }

    public required string Path { get; init; }

    public byte[]? Body { get; init; }

    /// <summary>How long the dashboard waits for the answer before it cancels the request.</summary>
    public TimeSpan Timeout { get; init; }
}

/// <summary>
/// The answer to one <see cref="AgentRequest" />: the status and the body an HTTP response would have had.
/// </summary>
internal sealed record AgentAnswer
{
    public required string Id { get; init; }

    public int Status { get; init; }

    public byte[] Body { get; init; } = [];
}
