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

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Dashboard.Services;
using Quartz.HttpApiContract;

namespace Quartz.Dashboard.Hubs;

/// <summary>
/// Where agents arrive: the hub a scheduler's process dials out to, mapped at <c>{DashboardPath}/agents</c>
/// when the dashboard accepts agents.
/// </summary>
/// <remarks>
/// <para>
/// Untyped and internal: the methods take the contract's own records, nothing about it is public for
/// SignalR's sake, and the dashboard's one <c>Task</c>-shaped public interface stays the browser hub's.
/// The names of the methods are <see cref="AgentProtocol" />'s, which the agent speaks too.
/// </para>
/// <para>
/// The bearer token is read from the <c>Authorization</c> header and from nowhere else: a token in a URL
/// ends up in access logs, and the .NET client sends its token as a header on every transport. A
/// connection with no header, or one carrying a token the dashboard does not hold, is aborted before any
/// method can run, and the refusal is logged.
/// </para>
/// </remarks>
internal sealed class DashboardAgentHub : Hub
{
    private const string AuthenticatedItem = "Quartz.Agent.Authenticated";

    private readonly AgentRegistry registry;
    private readonly IOptions<QuartzDashboardOptions> options;
    private readonly ILogger<DashboardAgentHub> logger;

    public DashboardAgentHub(AgentRegistry registry, IOptions<QuartzDashboardOptions> options, ILogger<DashboardAgentHub> logger)
    {
        this.registry = registry;
        this.options = options;
        this.logger = logger;
    }

    public override Task OnConnectedAsync()
    {
        DashboardAgentHubOptions? agents = options.Value.Agents;
        if (agents is null)
        {
            logger.AgentConnectionRefused(Context.ConnectionId, "the dashboard does not accept agents");
            Context.Abort();
            return Task.CompletedTask;
        }

        if (agents.Tokens.HasAny)
        {
            string? header = Context.GetHttpContext()?.Request.Headers.Authorization.ToString();
            if (string.IsNullOrEmpty(header))
            {
                logger.AgentConnectionRefused(Context.ConnectionId, "no Authorization header; the token travels as a bearer header, never in the URL");
                Context.Abort();
                return Task.CompletedTask;
            }

            if (!agents.Tokens.Matches(header))
            {
                logger.AgentConnectionRefused(Context.ConnectionId, "the bearer token matches neither the primary nor the secondary agent token");
                Context.Abort();
                return Task.CompletedTask;
            }
        }

        Context.Items[AuthenticatedItem] = true;
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        registry.Disconnected(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// The first call on every connection. The connection that held the target before, when the same
    /// instance took it over, is told to close.
    /// </summary>
    public async Task<AgentRegistered> Register(AgentRegistration registration)
    {
        EnsureAuthenticated();
        ArgumentNullException.ThrowIfNull(registration);

        AgentRegistered answer = registry.Register(Context.ConnectionId, registration, out string? superseded);

        if (superseded is not null)
        {
            await Clients.Client(superseded).SendAsync(AgentProtocol.Close, "superseded", Context.ConnectionAborted).ConfigureAwait(false);
        }

        if (answer.Accepted)
        {
            await registry.Rewatch(Context.ConnectionId).ConfigureAwait(false);
        }

        return answer;
    }

    public Task Heartbeat(AgentHeartbeat heartbeat)
    {
        EnsureAuthenticated();
        ArgumentNullException.ThrowIfNull(heartbeat);

        return registry.Heartbeat(Context.ConnectionId, Context.ConnectionAborted).AsTask();
    }

    public Task Answer(AgentAnswer answer)
    {
        EnsureAuthenticated();
        registry.Answer(Context.ConnectionId, answer);
        return Task.CompletedTask;
    }

    /// <summary>
    /// The agent's event stream, open while the dashboard watches its scheduler.
    /// </summary>
    public async Task Events(IAsyncEnumerable<SchedulerEvent> events)
    {
        EnsureAuthenticated();
        ArgumentNullException.ThrowIfNull(events);

        await foreach (SchedulerEvent schedulerEvent in events.WithCancellation(Context.ConnectionAborted).ConfigureAwait(false))
        {
            registry.Push(Context.ConnectionId, schedulerEvent);
        }
    }

    public Task Unregister()
    {
        EnsureAuthenticated();
        registry.Unregistered(Context.ConnectionId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// A method on a connection the token check did not pass is refused, whatever the abort did: the
    /// check is the one thing between an agent's credentials and every scheduler on the dashboard.
    /// </summary>
    private void EnsureAuthenticated()
    {
        if (!Context.Items.TryGetValue(AuthenticatedItem, out object? value) || value is not true)
        {
            throw new HubException("The connection was not authenticated.");
        }
    }
}
