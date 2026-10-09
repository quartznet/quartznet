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

using Quartz.Diagnostics;

namespace Quartz.Impl;

/// <summary>
/// Every event the dashboard agent logs, as source-generated methods with a pinned event id.
/// </summary>
/// <remarks>
/// Event ids 9300-9399 belong to this package. An id, once given out, is what an operator filters and
/// alerts on, so it is never reused for a different event and never renumbered; the
/// <c>LogEventCatalogTest</c> in <c>Quartz.Tests.Unit</c> makes a change to one a reviewed diff.
/// </remarks>
internal static partial class DashboardAgentLog
{
    /// <summary>
    /// The one category the agent logs under.
    /// </summary>
    internal const string Category = "Quartz.Dashboard.Agent";

    /// <summary>
    /// The agent's logger when no container handed it one: whatever
    /// <see cref="LogProvider.SetLogProvider" /> was given, or nothing.
    /// </summary>
    internal static ILogger Fallback() => LogProvider.CreateLogger(Category);

    [LoggerMessage(EventId = 9300, Level = LogLevel.Information, Message = "Registered with dashboard {Endpoint} as {Target}/{SchedulerName}")]
    public static partial void Registered(this ILogger logger, Uri endpoint, string target, string schedulerName);

    /// <remarks>
    /// Once per outage: the agent keeps dialling, and a line per attempt would say nothing the first did
    /// not. The registration line says when it ended.
    /// </remarks>
    [LoggerMessage(EventId = 9301, Level = LogLevel.Warning, Message = "Dashboard {Endpoint} unreachable since {Since}; retrying")]
    public static partial void Unreachable(this ILogger logger, Uri endpoint, DateTimeOffset since, Exception exception);

    /// <remarks>
    /// Once per reason: the dashboard may be holding the target for an instance that has gone, in which
    /// case the registration is retried until it is accepted, and the refusal does not change in between.
    /// </remarks>
    [LoggerMessage(EventId = 9302, Level = LogLevel.Warning, Message = "Dashboard {Endpoint} refused registration: {Reason}")]
    public static partial void RegistrationRefused(this ILogger logger, Uri endpoint, string reason);

    [LoggerMessage(EventId = 9303, Level = LogLevel.Information, Message = "Connection to {Endpoint} closed: {Reason}; reconnecting")]
    public static partial void ConnectionClosed(this ILogger logger, Uri endpoint, string reason);

    /// <remarks>
    /// Warning: the dashboard asked for something the agent's own configuration refuses — read-only, an
    /// operation outside the allow-list, a job type outside it, or an answer too large for the channel.
    /// An operator who narrowed the agent wants to see what was turned away.
    /// </remarks>
    [LoggerMessage(EventId = 9304, Level = LogLevel.Warning, Message = "Operation {Operation} refused: {Reason}")]
    public static partial void OperationRefused(this ILogger logger, string operation, string reason);

    [LoggerMessage(EventId = 9305, Level = LogLevel.Error, Message = "Operation {Operation} failed")]
    public static partial void OperationFailed(this ILogger logger, string operation, Exception exception);
}
