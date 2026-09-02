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

using Microsoft.Extensions.Logging;

namespace Quartz.Dashboard.Services;

/// <summary>
/// Every event the dashboard logs, as source-generated methods with a pinned event id.
/// </summary>
/// <remarks>
/// Event ids 9100-9199 belong to the dashboard; 9000-9099 are the HTTP API's. The dashboard used to
/// raise none at all: every mutating action a visitor took went into an in-memory list bounded at 250
/// entries and reachable only from the dashboard's own Action Log page, so the record of who paused a
/// trigger in production lived in one process's memory and was gone when it restarted. These are the
/// same events, on the way to whatever the application logs to.
/// </remarks>
internal static partial class DashboardLog
{
    [LoggerMessage(EventId = 9100, Level = LogLevel.Information, Message = "Dashboard user {User} performed {Action} on {Target} of scheduler {SchedulerName}: {Outcome}")]
    public static partial void ActionPerformed(this ILogger logger, string user, string action, string target, string schedulerName, string outcome);

    [LoggerMessage(EventId = 9101, Level = LogLevel.Information, Message = "Dashboard user {User} attempted {Action} on {Target} of scheduler {SchedulerName} and it failed: {Reason}")]
    public static partial void ActionFailed(this ILogger logger, string user, string action, string target, string schedulerName, string? reason);

    [LoggerMessage(EventId = 9102, Level = LogLevel.Debug, Message = "Dashboard connection {ConnectionId} opened for user {User}")]
    public static partial void HubConnected(this ILogger logger, string connectionId, string user);

    [LoggerMessage(EventId = 9103, Level = LogLevel.Debug, Message = "Dashboard connection {ConnectionId} closed for user {User}")]
    public static partial void HubDisconnected(this ILogger logger, string connectionId, string user);
}
