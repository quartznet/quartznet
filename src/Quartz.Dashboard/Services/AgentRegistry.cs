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

using System.Collections.Concurrent;
using System.Net;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Dashboard.Hubs;
using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Serialization.SystemTextJson;

namespace Quartz.Dashboard.Services;

/// <summary>
/// One agent as the dashboard holds it: the registration, the connection it is on, when it was last heard
/// from, the answers it still owes, and the scheduler, history reader and event source built over it.
/// </summary>
internal sealed class AgentEntry
{
    public AgentEntry(string target, string schedulerName, string schedulerInstanceId, string quartzVersion, HashSet<string> routes, bool readOnly)
    {
        Target = target;
        SchedulerName = schedulerName;
        SchedulerInstanceId = schedulerInstanceId;
        QuartzVersion = quartzVersion;
        Routes = routes;
        ReadOnly = readOnly;
    }

    public string Target { get; }

    public string SchedulerName { get; }

    public string SchedulerInstanceId { get; }

    public string QuartzVersion { get; private set; }

    public HashSet<string> Routes { get; private set; }

    public bool ReadOnly { get; private set; }

    /// <summary>The connection the agent is on, or <see langword="null" /> while it is disconnected.</summary>
    public string? ConnectionId { get; set; }

    /// <summary>When the agent last registered, heartbeat or answered.</summary>
    public DateTimeOffset LastSeenUtc { get; set; }

    /// <summary>When the agent disconnected or said goodbye, or <see langword="null" /> while it is connected.</summary>
    public DateTimeOffset? OfflineSinceUtc { get; set; }

    /// <summary>Whether the agent said goodbye, which lists it as shut down rather than unreachable.</summary>
    public bool Unregistered { get; set; }

    /// <summary>Whether the missed heartbeats have been logged for the current silence.</summary>
    public bool OverdueLogged { get; set; }

    public ConcurrentDictionary<string, TaskCompletionSource<WireResponse>> Pending { get; } = new(StringComparer.Ordinal);

    public AgentEventSource Events { get; set; } = null!;

    public HttpScheduler Scheduler { get; set; } = null!;

    public IExecutionHistoryStore History { get; set; } = null!;

    public bool Serves(WireRoute route) => Routes.Contains(route.Name);

    /// <summary>
    /// Takes a fresh registration from the same instance over: its routes, its read-only flag and the
    /// Quartz it runs may have changed with a redeploy.
    /// </summary>
    public void Refresh(HashSet<string> routes, bool readOnly, string quartzVersion)
    {
        Routes = routes;
        ReadOnly = readOnly;
        QuartzVersion = quartzVersion;
    }

    /// <summary>
    /// Whether the agent is silent for longer than its heartbeats allow.
    /// </summary>
    public bool IsOverdue(DateTimeOffset now, DashboardAgentHubOptions options)
    {
        return now - LastSeenUtc > options.HeartbeatInterval * options.OfflineAfterMissedHeartbeats;
    }

    /// <summary>
    /// Whether the agent is not to be asked anything: disconnected, gone, or silent.
    /// </summary>
    public bool IsOffline(DateTimeOffset now, DashboardAgentHubOptions options)
    {
        return ConnectionId is null || Unregistered || IsOverdue(now, options);
    }

    /// <summary>
    /// What the listing is told without asking: shut down after a goodbye, unknown while disconnected or
    /// silent, and nothing — ask — while the agent is live.
    /// </summary>
    public TargetLiveness Liveness(DateTimeOffset now, DashboardAgentHubOptions options)
    {
        SchedulerStatus? status = null;
        if (Unregistered)
        {
            status = SchedulerStatus.Shutdown;
        }
        else if (ConnectionId is null || IsOverdue(now, options))
        {
            status = SchedulerStatus.Unknown;
        }

        return new TargetLiveness(status, SchedulerInstanceId, LastSeenUtc);
    }

    /// <summary>
    /// Fails every answer still owed, for a connection that will not answer them.
    /// </summary>
    public void FailPending(string reason)
    {
        foreach (KeyValuePair<string, TaskCompletionSource<WireResponse>> pending in Pending)
        {
            if (Pending.TryRemove(pending.Key, out TaskCompletionSource<WireResponse>? answer))
            {
                answer.TrySetException(new HttpRequestException(reason));
            }
        }
    }
}

/// <summary>
/// Every agent that has dialled this dashboard, by target: registers one, judges its liveness, routes
/// its answers and events, and forgets it when it has been gone long enough.
/// </summary>
/// <remarks>
/// <para>
/// Registering an agent is what puts its scheduler on the dashboard: an <see cref="HttpScheduler" />
/// over an <see cref="AgentWireTransport" /> is bound in the repository under the agent's target, and a
/// <see cref="SchedulerTarget" /> of that name is added, whose history and events are the agent's own and
/// whose liveness is heard rather than asked. The fleet monitor hears the target change and runs a
/// detection round, which is how three agents on one clustered store become one row.
/// </para>
/// <para>
/// A target is held by one scheduler instance at a time. The same instance on a new connection takes its
/// own entry over, and the old connection is told to close; a different instance claiming a live target
/// is refused, and claiming one that is disconnected or silent takes it over, because that is an
/// operator who redeployed. Everything here is in this process's memory, which is what makes the
/// dashboard single-instance while it accepts agents.
/// </para>
/// </remarks>
internal sealed class AgentRegistry
{
    private readonly ISchedulerRepository repository;
    private readonly SchedulerTargets targets;
    private readonly IHubContext<DashboardAgentHub> hub;
    private readonly IOptions<QuartzDashboardOptions> dashboardOptions;
    private readonly TimeProvider timeProvider;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<AgentRegistry> logger;
    private readonly SystemTextJsonSerializerRegistry serializerRegistry;

    private readonly Dictionary<string, AgentEntry> byTarget = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AgentEntry> byConnection = new(StringComparer.Ordinal);
    private readonly Lock gate = new();

    public AgentRegistry(
        IServiceProvider services,
        IHubContext<DashboardAgentHub> hub,
        IOptions<QuartzDashboardOptions> dashboardOptions,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(services);

        repository = services.GetRequiredService<ISchedulerRepository>();
        targets = services.GetRequiredService<SchedulerTargets>();
        timeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;
        serializerRegistry = services.GetService<SystemTextJsonSerializerRegistry>() ?? new SystemTextJsonSerializerRegistry();
        this.hub = hub;
        this.dashboardOptions = dashboardOptions;
        this.loggerFactory = loggerFactory;
        logger = loggerFactory.CreateLogger<AgentRegistry>();
    }

    /// <summary>
    /// The hub's options, read when they are asked for; a dashboard that accepts no agents has defaults.
    /// </summary>
    public DashboardAgentHubOptions Options => dashboardOptions.Value.Agents ?? Defaults;

    private static readonly DashboardAgentHubOptions Defaults = new();

    public DateTimeOffset Now => timeProvider.GetUtcNow();

    /// <summary>The dashboard's clock, which every timeout about an agent runs on.</summary>
    public TimeProvider TimeProvider => timeProvider;

    /// <summary>
    /// The entry under <paramref name="target" />, or <see langword="null" />.
    /// </summary>
    public AgentEntry? Find(string target)
    {
        lock (gate)
        {
            return byTarget.GetValueOrDefault(target);
        }
    }

    /// <summary>
    /// Every entry, for the sweep.
    /// </summary>
    public List<AgentEntry> Snapshot()
    {
        lock (gate)
        {
            return [.. byTarget.Values];
        }
    }

    /// <summary>
    /// Registers an agent on <paramref name="connectionId" />, answering what the agent is told — and
    /// naming the connection it superseded, which the hub then tells to close.
    /// </summary>
    public AgentRegistered Register(string connectionId, AgentRegistration registration, out string? superseded)
    {
        ArgumentNullException.ThrowIfNull(registration);

        superseded = null;
        DashboardAgentHubOptions options = Options;

        if (Malformed(registration) is { } malformed)
        {
            return Refuse(registration, malformed);
        }

        DateTimeOffset now = Now;
        HashSet<string> routes = new(registration.Routes ?? [], StringComparer.Ordinal);

        AgentEntry? replaced = null;
        AgentEntry entry;
        lock (gate)
        {
            // One connection is one agent is one target. A second target on a connection that already
            // holds one would leave the first unmapped the moment either was removed.
            if (byConnection.TryGetValue(connectionId, out AgentEntry? held)
                && !string.Equals(held.Target, registration.Target, StringComparison.OrdinalIgnoreCase))
            {
                return Refuse(registration, $"the connection already registered target '{held.Target}'; one connection registers one target");
            }

            AgentEntry? existing = byTarget.GetValueOrDefault(registration.Target);
            if (existing is not null && IsSameScheduler(existing, registration))
            {
                superseded = TakeOver(existing, connectionId, now, routes, registration);
                return Accept(options);
            }

            if (existing is not null)
            {
                if (!existing.IsOffline(now, options))
                {
                    return Refuse(registration, $"target '{existing.Target}' is held by instance '{existing.SchedulerInstanceId}' of scheduler '{existing.SchedulerName}'");
                }

                // The holder is gone: the operator redeployed under the same target.
                replaced = existing;
                Remove(existing);
            }

            entry = new AgentEntry(registration.Target, registration.SchedulerName, registration.SchedulerInstanceId, registration.QuartzVersion ?? "", routes, registration.ReadOnly)
            {
                ConnectionId = connectionId,
                LastSeenUtc = now,
            };

            if (Bind(entry) is { } refusal)
            {
                return Refuse(registration, refusal);
            }

            byTarget[entry.Target] = entry;
            byConnection[connectionId] = entry;
        }

        replaced?.Events.Complete();

        logger.AgentRegistered(entry.Target, entry.SchedulerName, entry.SchedulerInstanceId, entry.QuartzVersion);
        return Accept(options);
    }

    /// <summary>
    /// What is wrong with a registration before any entry is looked at, or <see langword="null" />.
    /// </summary>
    private static string? Malformed(AgentRegistration registration)
    {
        if (registration.ProtocolVersion != AgentProtocol.Version)
        {
            return $"protocol {AgentProtocol.Version} required, the agent speaks {registration.ProtocolVersion}";
        }

        if (string.IsNullOrWhiteSpace(registration.SchedulerName) || string.IsNullOrWhiteSpace(registration.SchedulerInstanceId))
        {
            return "the registration names no scheduler";
        }

        return SchedulerTargets.Problem(registration.Target);
    }

    private static bool IsSameScheduler(AgentEntry existing, AgentRegistration registration)
    {
        return string.Equals(existing.SchedulerName, registration.SchedulerName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.SchedulerInstanceId, registration.SchedulerInstanceId, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same instance on a new connection takes its own entry over, under the lock.
    /// </summary>
    /// <returns>The connection it superseded, which the hub tells to close, or <see langword="null" />.</returns>
    private string? TakeOver(AgentEntry existing, string connectionId, DateTimeOffset now, HashSet<string> routes, AgentRegistration registration)
    {
        string? superseded = existing.ConnectionId is { } old && !string.Equals(old, connectionId, StringComparison.Ordinal) ? old : null;
        if (superseded is not null)
        {
            byConnection.Remove(superseded);
            existing.FailPending($"Agent '{existing.Target}' reconnected; the request was lost with the old connection.");
        }

        existing.ConnectionId = connectionId;
        existing.LastSeenUtc = now;
        existing.OfflineSinceUtc = null;
        existing.Unregistered = false;
        existing.OverdueLogged = false;
        existing.Refresh(routes, registration.ReadOnly, registration.QuartzVersion ?? existing.QuartzVersion);
        byConnection[connectionId] = existing;

        logger.AgentRegistered(existing.Target, existing.SchedulerName, existing.SchedulerInstanceId, existing.QuartzVersion);
        return superseded;
    }

    /// <summary>
    /// Builds what the dashboard reaches the agent's scheduler through, and claims the target.
    /// </summary>
    /// <returns>Why the target could not be claimed, or <see langword="null" /> when it was.</returns>
    private string? Bind(AgentEntry entry)
    {
        AgentWireTransport transport = new(this, entry);
        entry.Events = new AgentEventSource(watch => Watch(entry, watch));
        entry.Scheduler = new HttpScheduler(
            entry.SchedulerName,
            transport,
            jsonSerializerOptions: null,
            serializerRegistry,
            loggerFactory.CreateLogger(HttpClientLog.Category),
            entry.Target,
            SchedulerOrigin.Agent);
        entry.History = new HttpExecutionHistoryStore(entry.SchedulerName, transport, jsonSerializerOptions: null, loggerFactory.CreateLogger(HttpClientLog.Category));

        try
        {
            targets.Add(new SchedulerTarget
            {
                Name = entry.Target,
                Origin = SchedulerOrigin.Agent,
                Events = (_, _) => entry.Events,
                History = (_, _) => entry.History,
                Liveness = () => entry.Liveness(Now, Options),
            });
        }
        catch (SchedulerConfigException exception)
        {
            return exception.Message;
        }

        try
        {
            repository.Bind(entry.Scheduler, entry.Target);
        }
        catch (SchedulerException exception)
        {
            targets.Remove(entry.Target);
            return exception.Message;
        }

        return null;
    }

    private AgentRegistered Refuse(AgentRegistration registration, string reason)
    {
        logger.AgentRegistrationRefused(registration.Target ?? "(none)", registration.SchedulerName ?? "(none)", reason);
        return new AgentRegistered { Accepted = false, Reason = reason };
    }

    private static AgentRegistered Accept(DashboardAgentHubOptions options)
    {
        return new AgentRegistered
        {
            Accepted = true,
            MaxMessageBytes = options.MaxMessageBytes,
            HeartbeatInterval = options.HeartbeatInterval,
            OfflineAfterMissedHeartbeats = options.OfflineAfterMissedHeartbeats,
        };
    }

    /// <summary>
    /// A heartbeat from <paramref name="connectionId" />: the agent is alive. One from a connection no
    /// entry is on any more is told to close, so the agent registers again.
    /// </summary>
    public async ValueTask Heartbeat(string connectionId, CancellationToken cancellationToken = default)
    {
        AgentEntry? entry;
        lock (gate)
        {
            entry = byConnection.GetValueOrDefault(connectionId);
            if (entry is not null)
            {
                entry.LastSeenUtc = Now;
                entry.OverdueLogged = false;
            }
        }

        if (entry is null)
        {
            await hub.Clients.Client(connectionId).SendAsync(AgentProtocol.Close, "forgotten", cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// An answer from <paramref name="connectionId" />, handed to whoever is waiting for it.
    /// </summary>
    public void Answer(string connectionId, AgentAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        AgentEntry? entry;
        lock (gate)
        {
            entry = byConnection.GetValueOrDefault(connectionId);
            if (entry is not null)
            {
                entry.LastSeenUtc = Now;
            }
        }

        if (entry is not null && entry.Pending.TryRemove(answer.Id, out TaskCompletionSource<WireResponse>? waiting))
        {
            waiting.TrySetResult(new WireResponse((HttpStatusCode) answer.Status, answer.Body ?? []));
        }
    }

    /// <summary>
    /// An event the agent on <paramref name="connectionId" /> streamed up.
    /// </summary>
    public void Push(string connectionId, SchedulerEvent schedulerEvent)
    {
        AgentEntry? entry;
        lock (gate)
        {
            entry = byConnection.GetValueOrDefault(connectionId);
        }

        entry?.Events.Push(schedulerEvent);
    }

    /// <summary>
    /// The agent on <paramref name="connectionId" /> said goodbye: its scheduler is listed as shut down
    /// until it is forgotten.
    /// </summary>
    public void Unregistered(string connectionId)
    {
        lock (gate)
        {
            if (byConnection.GetValueOrDefault(connectionId) is { } entry)
            {
                entry.Unregistered = true;
                entry.LastSeenUtc = Now;
                entry.OfflineSinceUtc = Now;
                entry.FailPending($"Agent '{entry.Target}' is shutting down.");
            }
        }
    }

    /// <summary>
    /// The connection <paramref name="connectionId" /> went: its agent is listed as unknown until it
    /// reconnects or is forgotten. A connection that was superseded belongs to nobody and changes nothing.
    /// </summary>
    public void Disconnected(string connectionId)
    {
        AgentEntry? entry;
        lock (gate)
        {
            if (!byConnection.Remove(connectionId, out entry))
            {
                return;
            }

            entry.ConnectionId = null;
            entry.OfflineSinceUtc = Now;
            entry.FailPending($"Agent '{entry.Target}' disconnected.");
        }

        if (!entry.Unregistered)
        {
            logger.AgentDisconnected(entry.Target, entry.SchedulerName);
        }
    }

    /// <summary>
    /// One pass of the judge: logs an agent that has gone quiet, and forgets one that has been gone for
    /// <see cref="DashboardAgentHubOptions.ForgetAfter" />.
    /// </summary>
    public void Sweep()
    {
        DashboardAgentHubOptions options = Options;
        DateTimeOffset now = Now;
        List<AgentEntry> forgotten = [];

        lock (gate)
        {
            foreach (AgentEntry entry in byTarget.Values.ToList())
            {
                bool overdue = entry.IsOverdue(now, options);
                if (entry.ConnectionId is not null && !entry.Unregistered && overdue && !entry.OverdueLogged)
                {
                    entry.OverdueLogged = true;
                    logger.AgentMissedHeartbeats(entry.Target, options.OfflineAfterMissedHeartbeats, entry.SchedulerName);
                }

                if (entry.IsOffline(now, options) && now - entry.LastSeenUtc > options.ForgetAfter)
                {
                    Remove(entry);
                    forgotten.Add(entry);
                }
            }
        }

        foreach (AgentEntry entry in forgotten)
        {
            entry.Events.Complete();
            logger.AgentForgotten(entry.Target, options.ForgetAfter);
        }
    }

    /// <summary>
    /// Takes an entry out of everything, under the lock: the tables, the repository and the targets.
    /// </summary>
    private void Remove(AgentEntry entry)
    {
        byTarget.Remove(entry.Target);
        if (entry.ConnectionId is { } connectionId)
        {
            byConnection.Remove(connectionId);
        }

        entry.FailPending($"Agent '{entry.Target}' was forgotten.");
        repository.Remove(entry.SchedulerName, entry.Target);
        targets.Remove(entry.Target);
    }

    // --- What goes down the connection ---------------------------------------------------------------

    public Task Execute(AgentEntry entry, AgentRequest request, CancellationToken cancellationToken)
    {
        string? connectionId = entry.ConnectionId;
        return connectionId is null
            ? Task.FromException(new HttpRequestException($"Agent '{entry.Target}' is disconnected."))
            : hub.Clients.Client(connectionId).SendAsync(AgentProtocol.Execute, request, cancellationToken);
    }

    public async ValueTask Cancel(AgentEntry entry, string id)
    {
        if (entry.ConnectionId is not { } connectionId)
        {
            return;
        }

        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5), timeProvider);
            await hub.Clients.Client(connectionId).SendAsync(AgentProtocol.Cancel, id, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A cancellation the connection did not carry: the agent's own timeout ends the operation.
        }
    }

    private async ValueTask Watch(AgentEntry entry, bool watch)
    {
        if (entry.ConnectionId is not { } connectionId)
        {
            return;
        }

        try
        {
            await hub.Clients.Client(connectionId).SendAsync(AgentProtocol.Watch, watch).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The connection went between the check and the send; the reconnection asks again.
        }
    }

    /// <summary>
    /// Asks an agent that has just registered to stream again when something is still watching it.
    /// </summary>
    public ValueTask Rewatch(string connectionId)
    {
        AgentEntry? entry;
        lock (gate)
        {
            entry = byConnection.GetValueOrDefault(connectionId);
        }

        return entry?.Events.Rewatch() ?? default;
    }
}
