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

using Microsoft.AspNetCore.SignalR;

using Quartz.Dashboard.Services;

namespace Quartz.Dashboard.Hubs;

internal sealed class QuartzDashboardHub : Hub<IQuartzDashboardHubClient>
{
    private readonly SchedulerAuthorization authorization;

    public QuartzDashboardHub(SchedulerAuthorization authorization)
    {
        this.authorization = authorization;
    }

    /// <summary>
    /// Subscribes this connection to one scheduler's live events.
    /// </summary>
    /// <remarks>
    /// The group name is the scheduler's name — it is what the live-events plugin broadcasts to — so
    /// joining a group is reaching a scheduler, and it is checked as one. Refusing is a
    /// <see cref="HubException" /> rather than a silent no-op: a subscription that never delivers and
    /// never says why is indistinguishable from a scheduler that is idle.
    /// </remarks>
    public async Task JoinScheduler(string schedulerName)
    {
        ClaimsPrincipal user = Context.User ?? Anonymous;
        if (!await authorization.IsAuthorized(user, schedulerName, Context.ConnectionAborted).ConfigureAwait(false))
        {
            throw new HubException($"Not authorized for scheduler {schedulerName}");
        }

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
