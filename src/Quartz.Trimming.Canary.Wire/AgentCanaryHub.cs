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

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;

using Quartz.HttpApiContract;

namespace Quartz.Trimming.Canary.Wire;

/// <summary>
/// The dashboard's half of the agent protocol, as far as one agent needs it: where
/// <c>Quartz.Dashboard.Agent</c> arrives, registers, heartbeats, answers and streams events to.
/// </summary>
/// <remarks>
/// <para>
/// The real hub is <c>Quartz.Dashboard</c>'s, and Blazor keeps that package untrimmable, so there is no
/// end-to-end to publish natively. This hub stands in for it with the protocol's own records — the method
/// names are <see cref="AgentProtocol" />'s and the payloads are the contract's, read and written through
/// the same generated metadata the agent uses — so what runs on the agent's side is exactly what runs
/// against a dashboard, and what runs here is the SignalR server binding those records with reflection
/// switched off.
/// </para>
/// <para>
/// Untyped, as the dashboard's is, because a strongly typed hub is the one SignalR shape native AOT does
/// not support. The bearer token is read from the <c>Authorization</c> header and nowhere else, as the
/// dashboard reads it: the agent's token travels as a header on every transport, and a connection
/// without it is refused before any method can run.
/// </para>
/// </remarks>
internal sealed class AgentCanaryHub : Hub
{
    /// <summary>
    /// Where the hub is mapped, relative to the site root.
    /// </summary>
    internal const string Path = "/agents";

    /// <summary>
    /// The one token the hub accepts, which the agent is configured with.
    /// </summary>
    internal const string Token = "the-canary-agent-token";

    private readonly AgentCanaryHubState state;

    public AgentCanaryHub(AgentCanaryHubState state)
    {
        this.state = state;
    }

    public override Task OnConnectedAsync()
    {
        string? header = Context.GetHttpContext()?.Request.Headers.Authorization.ToString();
        if (!string.Equals(header, "Bearer " + Token, StringComparison.Ordinal))
        {
            state.Refused(string.IsNullOrEmpty(header) ? "no Authorization header" : "a token the hub does not hold");
            Context.Abort();
            return Task.CompletedTask;
        }

        return base.OnConnectedAsync();
    }

    /// <summary>
    /// The first call on every connection; the canary learns the connection id from it, which is what
    /// <see cref="AgentProtocol.Execute" /> is addressed to.
    /// </summary>
    public Task<AgentRegistered> Register(AgentRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);

        return Task.FromResult(state.Register(Context.ConnectionId, registration));
    }

    public Task Heartbeat(AgentHeartbeat heartbeat)
    {
        ArgumentNullException.ThrowIfNull(heartbeat);

        state.Heartbeat(heartbeat);
        return Task.CompletedTask;
    }

    public Task Answer(AgentAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        state.Answered(answer);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The agent's event stream, which it opens when told to <see cref="AgentProtocol.Watch" />: a
    /// client-to-server stream of the contract's event record.
    /// </summary>
    public async Task Events(IAsyncEnumerable<SchedulerEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        await foreach (SchedulerEvent schedulerEvent in events.WithCancellation(Context.ConnectionAborted).ConfigureAwait(false))
        {
            state.Event(schedulerEvent);
        }
    }

    public Task Unregister()
    {
        state.Unregister();
        return Task.CompletedTask;
    }
}

/// <summary>
/// What the hub has heard, for <see cref="AgentCheck" /> to wait on: one process-wide instance, since
/// there is one hub and one agent.
/// </summary>
internal sealed class AgentCanaryHubState
{
    /// <summary>
    /// What the hub tells the agent when it accepts it. A short heartbeat interval, so that a heartbeat
    /// arrives within the canary's patience rather than a quarter of a minute later.
    /// </summary>
    internal static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMilliseconds(500);

    internal const int MaxMessageBytes = 4 * 1024 * 1024;

    private readonly TaskCompletionSource<(string ConnectionId, AgentRegistration Registration)> registered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<AgentHeartbeat> heartbeat = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource unregistered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<AgentAnswer>> answers = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<SchedulerEvent> events = new();
    private readonly TaskCompletionSource<SchedulerEvent> jobExecuted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Why the hub refused a connection, when it did; the agent then never registers, and the canary
    /// says why rather than only that it waited.
    /// </summary>
    public string? Refusal { get; private set; }

    public Task<(string ConnectionId, AgentRegistration Registration)> Registered => registered.Task;

    public Task<AgentHeartbeat> FirstHeartbeat => heartbeat.Task;

    public Task Unregistered => unregistered.Task;

    /// <summary>
    /// The first <see cref="SchedulerEventKind.JobExecuted" /> the agent streamed up.
    /// </summary>
    public Task<SchedulerEvent> JobExecuted => jobExecuted.Task;

    /// <summary>
    /// Every event the agent streamed up, in the order they arrived.
    /// </summary>
    public ConcurrentQueue<SchedulerEvent> Events => events;

    /// <summary>
    /// The answer to the request <paramref name="id" />, once the agent sends it.
    /// </summary>
    public Task<AgentAnswer> AnswerTo(string id)
    {
        return answers.GetOrAdd(id, static _ => new TaskCompletionSource<AgentAnswer>(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
    }

    internal void Refused(string reason)
    {
        Refusal = reason;
    }

    internal AgentRegistered Register(string connectionId, AgentRegistration registration)
    {
        registered.TrySetResult((connectionId, registration));

        return new AgentRegistered
        {
            Accepted = true,
            MaxMessageBytes = MaxMessageBytes,
            HeartbeatInterval = HeartbeatInterval,
            OfflineAfterMissedHeartbeats = 3,
        };
    }

    internal void Heartbeat(AgentHeartbeat beat)
    {
        heartbeat.TrySetResult(beat);
    }

    internal void Answered(AgentAnswer answer)
    {
        answers.GetOrAdd(answer.Id, static _ => new TaskCompletionSource<AgentAnswer>(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult(answer);
    }

    internal void Event(SchedulerEvent schedulerEvent)
    {
        events.Enqueue(schedulerEvent);
        if (schedulerEvent.Kind == SchedulerEventKind.JobExecuted)
        {
            jobExecuted.TrySetResult(schedulerEvent);
        }
    }

    internal void Unregister()
    {
        unregistered.TrySetResult();
    }
}
