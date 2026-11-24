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

namespace Quartz.Mcp.Impl;

/// <summary>
/// What the MCP endpoint logs, under <see cref="Category" />, from the 9500–9599 range.
/// </summary>
/// <remarks>
/// <para>
/// The lines name the user, the client as it stated itself, the tool, the scheduler's key and the subject. They
/// never carry an argument: a confirmation token travels in the arguments, and so does a pause's reason, which
/// is written to the trigger and read back through the tools rather than logged.
/// </para>
/// <para>
/// A target that did not serve a call is logged with the HTTP API's own events, <c>9010</c> to <c>9014</c>, in
/// this category: one fault, one event id, whichever carrier met it.
/// </para>
/// </remarks>
internal static partial class McpLog
{
    internal const string Category = "Quartz.Mcp";

    /// <summary>
    /// A tool changed something — the record of an action an agent took, as <c>9007</c> is the HTTP API's.
    /// </summary>
    [LoggerMessage(EventId = 9500, Level = LogLevel.Information, Message = "MCP user {User} ({Client}) performed {Tool} on scheduler {SchedulerKey}: {Subject}")]
    public static partial void ToolChanged(this ILogger logger, string user, string client, string tool, string schedulerKey, string subject);

    /// <summary>
    /// A tool was refused before anything reached the scheduler: by <c>ReadOnly</c>, by a policy, by the target's
    /// ceiling, or for want of a stable identity.
    /// </summary>
    [LoggerMessage(EventId = 9501, Level = LogLevel.Warning, Message = "MCP user {User} was refused {Tool} on scheduler {SchedulerKey}: {Reason}")]
    public static partial void ToolRefused(this ILogger logger, string user, string tool, string schedulerKey, string reason);

    /// <summary>
    /// A confirmation token was refused, by the class of reason — expired, another caller's, the state changed —
    /// and never with the token.
    /// </summary>
    [LoggerMessage(EventId = 9502, Level = LogLevel.Warning, Message = "MCP user {User} was refused the confirmation of {Tool} on scheduler {SchedulerKey}: {Reason}")]
    public static partial void ConfirmationRefused(this ILogger logger, string user, string tool, string schedulerKey, string reason);

    /// <summary>
    /// A caller reached <c>MaxChangesPerMinute</c>.
    /// </summary>
    [LoggerMessage(EventId = 9503, Level = LogLevel.Warning, Message = "MCP user {User} was rate limited calling {Tool} on scheduler {SchedulerKey}: retry after {RetryAfterSeconds} s")]
    public static partial void RateLimited(this ILogger logger, string user, string tool, string schedulerKey, int retryAfterSeconds);

    /// <summary>
    /// The request guard refused a request before the MCP server read it: an <c>Origin</c> not allowed, or a body
    /// that is not JSON.
    /// </summary>
    [LoggerMessage(EventId = 9504, Level = LogLevel.Warning, Message = "MCP request refused: {Reason}")]
    public static partial void RequestRefused(this ILogger logger, string reason);
}
