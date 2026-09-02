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

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace Quartz.Dashboard.Services;

/// <summary>
/// What a page calls when a visitor changes something: the process-wide Action Log, and the
/// application's own logger.
/// </summary>
/// <remarks>
/// <para>
/// Scoped, because the one thing the store cannot know is who did it — the visitor is a circuit's, and
/// <see cref="DashboardActionLogService" /> is a singleton shared by every circuit in the process. So
/// the pages talk to this, and it talks to both.
/// </para>
/// <para>
/// The Action Log alone was the whole record: 250 entries, in one process's memory, readable only from
/// the dashboard's own page and gone at the next restart. An operator asking who paused a trigger last
/// Tuesday had nowhere to look. These are the same events on the way to whatever the application logs
/// to, at Information, and the page is unchanged.
/// </para>
/// </remarks>
internal sealed class DashboardActionLog
{
    private readonly DashboardActionLogService store;
    private readonly ILogger<DashboardActionLog> logger;
    private readonly AuthenticationStateProvider authenticationStateProvider;

    public DashboardActionLog(
        DashboardActionLogService store,
        ILogger<DashboardActionLog> logger,
        AuthenticationStateProvider authenticationStateProvider)
    {
        this.store = store;
        this.logger = logger;
        this.authenticationStateProvider = authenticationStateProvider;
    }

    /// <summary>
    /// Records one mutating action, in the page's own log and in the application's.
    /// </summary>
    public void Record(
        string schedulerName,
        string action,
        string target,
        bool succeeded,
        string? message = null)
    {
        store.Record(schedulerName, action, target, succeeded, message);

        string user = UserName();
        if (succeeded)
        {
            logger.ActionPerformed(user, action, target, schedulerName, message ?? "done");
        }
        else
        {
            logger.ActionFailed(user, action, target, schedulerName, message);
        }
    }

    /// <inheritdoc cref="DashboardActionLogService.GetLatest" />
    public IReadOnlyList<DashboardActionLogEntry> GetLatest(int maxCount = 25) => store.GetLatest(maxCount);

    /// <summary>
    /// Who the circuit belongs to, or a placeholder when nothing has said.
    /// </summary>
    /// <remarks>
    /// Read from the already-completed task rather than awaited: every caller here is a synchronous
    /// UI event handler, a circuit's authentication state is settled long before a visitor can click
    /// anything, and blocking on a task inside a render is the one way this could go wrong. An
    /// application that authenticates nobody logs the action under <c>(anonymous)</c>, which is the
    /// truth about it.
    /// </remarks>
    private string UserName()
    {
        Task<AuthenticationState> state = authenticationStateProvider.GetAuthenticationStateAsync();
        if (!state.IsCompletedSuccessfully)
        {
            return "(unknown)";
        }

        string? name = state.Result.User.Identity?.Name;
        return string.IsNullOrWhiteSpace(name) ? "(anonymous)" : name;
    }
}
