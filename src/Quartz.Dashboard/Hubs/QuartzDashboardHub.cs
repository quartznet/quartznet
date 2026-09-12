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

using System.Security.Claims;

using Microsoft.Extensions.Logging;

using Microsoft.AspNetCore.SignalR;

using Quartz.Dashboard.Services;

namespace Quartz.Dashboard.Hubs;

internal sealed class QuartzDashboardHub : Hub<IQuartzDashboardHubClient>
{
    private readonly SchedulerAuthorization authorization;
    private readonly DashboardHubForwarder forwarder;
    private readonly ILogger<QuartzDashboardHub> logger;

    public QuartzDashboardHub(
        SchedulerAuthorization authorization,
        DashboardHubForwarder forwarder,
        ILogger<QuartzDashboardHub> logger)
    {
        this.authorization = authorization;
        this.forwarder = forwarder;
        this.logger = logger;
    }

    /// <remarks>
    /// Debug rather than Information: a circuit opens and closes on every navigation and every reload,
    /// so this is diagnostic detail an operator turns on while looking at something, not a record of
    /// what was done. What was done is <see cref="DashboardActionLog" />.
    /// </remarks>
    public override Task OnConnectedAsync()
    {
        logger.HubConnected(Context.ConnectionId, UserName());
        return base.OnConnectedAsync();
    }

    /// <inheritdoc cref="OnConnectedAsync" />
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        logger.HubDisconnected(Context.ConnectionId, UserName());
        return base.OnDisconnectedAsync(exception);
    }

    private string UserName()
    {
        string? name = Context.User?.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? "(anonymous)" : name;
    }

    /// <summary>
    /// Subscribes this connection to one scheduler's live events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The group name is the scheduler's name — it is what the forwarder sends to — so joining a group is
    /// reaching a scheduler, and it is checked as one. Refusing is a <see cref="HubException" /> rather
    /// than a silent no-op: a subscription that never delivers and never says why is indistinguishable
    /// from a scheduler that is idle.
    /// </para>
    /// <para>
    /// The join is also what starts the forwarding. Quartz builds a scheduler's events only while
    /// something is subscribed to them, so a hub nobody has connected to costs its schedulers nothing —
    /// and the check above has already run, so nothing is subscribed to on behalf of a caller who may not
    /// see it.
    /// </para>
    /// </remarks>
    public async Task JoinScheduler(string schedulerName)
    {
        ClaimsPrincipal user = Context.User ?? Anonymous;
        if (!await authorization.IsAuthorized(user, schedulerName, Context.ConnectionAborted).ConfigureAwait(false))
        {
            throw new HubException($"Not authorized for scheduler {schedulerName}");
        }

        forwarder.Forward(schedulerName);

        await Groups.AddToGroupAsync(Context.ConnectionId, schedulerName, Context.ConnectionAborted).ConfigureAwait(false);
    }

    public Task LeaveScheduler(string schedulerName)
    {
        return Groups.RemoveFromGroupAsync(Context.ConnectionId, schedulerName, Context.ConnectionAborted);
    }

    /// <summary>
    /// What a connection that authenticated as nobody is: an empty principal, so the policy decides it
    /// rather than the hub deciding for it.
    /// </summary>
    private static readonly ClaimsPrincipal Anonymous = new();
}
