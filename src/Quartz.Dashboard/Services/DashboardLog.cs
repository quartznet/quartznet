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
    /// <remarks>
    /// <para>
    /// <c>{Origin}</c> and <c>{Node}</c> were added in 4.1, at the end so that everything the line said
    /// before still reads the same way and in the same order. Where the action landed is the part an
    /// operator cannot reconstruct afterwards: a scheduler reached over HTTP was driven in somebody
    /// else's process, and an action that is node-local — interrupt, start, stand-by, shutdown — reached
    /// the one node named here and no other. Both are <c>(unknown)</c> when the circuit's last listing
    /// said nothing about the scheduler.
    /// </para>
    /// </remarks>
    [LoggerMessage(EventId = 9100, Level = LogLevel.Information, Message = "Dashboard user {User} performed {Action} on {Target} of scheduler {SchedulerName}: {Outcome} (origin {Origin}, node {Node})")]
    public static partial void ActionPerformed(this ILogger logger, string user, string action, string target, string schedulerName, string outcome, string origin, string node);

    /// <inheritdoc cref="ActionPerformed" />
    [LoggerMessage(EventId = 9101, Level = LogLevel.Information, Message = "Dashboard user {User} attempted {Action} on {Target} of scheduler {SchedulerName} and it failed: {Reason} (origin {Origin}, node {Node})")]
    public static partial void ActionFailed(this ILogger logger, string user, string action, string target, string schedulerName, string? reason, string origin, string node);

    [LoggerMessage(EventId = 9102, Level = LogLevel.Debug, Message = "Dashboard connection {ConnectionId} opened for user {User}")]
    public static partial void HubConnected(this ILogger logger, string connectionId, string user);

    [LoggerMessage(EventId = 9103, Level = LogLevel.Debug, Message = "Dashboard connection {ConnectionId} closed for user {User}")]
    public static partial void HubDisconnected(this ILogger logger, string connectionId, string user);

    /// <remarks>
    /// Warning, and it says which scheduler: the hub goes on serving whoever is connected to it and they
    /// go on receiving nothing, which is the one shape of failure a live view cannot show for itself.
    /// Reaching it needs something other than a scheduler going away or a target that streams no events —
    /// the forwarder treats both of those as the endings they are.
    /// </remarks>
    [LoggerMessage(EventId = 9104, Level = LogLevel.Warning, Message = "Forwarding scheduler {SchedulerName} events to the dashboard hub stopped")]
    public static partial void HubForwardingStopped(this ILogger logger, string schedulerName, Exception exception);

    /// <remarks>
    /// <para>
    /// 9105–9109 are the fleet monitor's. A cluster appearing, changing membership or dissolving is
    /// Information, because each renames a row an operator may be looking at; a member that did not
    /// answer a round is Debug, because it stays in its cluster and the row says it is unreachable.
    /// </para>
    /// </remarks>
    [LoggerMessage(EventId = 9105, Level = LogLevel.Information, Message = "Cluster {Target} formed for scheduler {SchedulerName} from targets {Members}")]
    public static partial void ClusterFormed(this ILogger logger, string target, string schedulerName, string members);

    [LoggerMessage(EventId = 9106, Level = LogLevel.Information, Message = "Cluster {PreviousTarget} for scheduler {SchedulerName} is now {Target}: members {Members}")]
    public static partial void ClusterChanged(this ILogger logger, string previousTarget, string schedulerName, string target, string members);

    [LoggerMessage(EventId = 9107, Level = LogLevel.Information, Message = "Cluster {Target} for scheduler {SchedulerName} dissolved; its targets are listed on their own again")]
    public static partial void ClusterDissolved(this ILogger logger, string target, string schedulerName);

    [LoggerMessage(EventId = 9108, Level = LogLevel.Warning, Message = "Fleet detection round failed; the clusters stay as the last round left them")]
    public static partial void FleetRoundFailed(this ILogger logger, Exception exception);

    [LoggerMessage(EventId = 9109, Level = LogLevel.Debug, Message = "Member {Member} of cluster {Target} did not answer this round and stays in the cluster as an unreachable node")]
    public static partial void ClusterMemberUnreachable(this ILogger logger, string member, string target);

    /// <remarks>
    /// Debug, like 9109: a target that cannot be asked this round is a target left as it was, and the
    /// listing says it is unreachable. The exception says why, for whoever turns the level up.
    /// </remarks>
    [LoggerMessage(EventId = 9110, Level = LogLevel.Debug, Message = "Target {Target} did not answer the fleet detection round")]
    public static partial void FleetTargetUnanswered(this ILogger logger, string target, Exception exception);

    /// <remarks>
    /// <para>
    /// 9111–9117 are the agent hub's. A connection refused and a registration refused are Warning,
    /// because each is a worker that wanted to be on the dashboard and is not; an agent arriving,
    /// leaving and being forgotten is Information, because each changes a row an operator may be
    /// looking at; missed heartbeats are Warning, because a live socket whose process has gone quiet is
    /// the one failure the connection itself cannot report; a sweep that threw is Error, because it is
    /// the judge of every agent's liveness and a timer callback that throws has nowhere else to say so.
    /// </para>
    /// </remarks>
    [LoggerMessage(EventId = 9111, Level = LogLevel.Warning, Message = "Agent connection {ConnectionId} refused: {Reason}")]
    public static partial void AgentConnectionRefused(this ILogger logger, string connectionId, string reason);

    [LoggerMessage(EventId = 9112, Level = LogLevel.Information, Message = "Agent {Target} registered scheduler {SchedulerName} ({InstanceId}, Quartz {Version})")]
    public static partial void AgentRegistered(this ILogger logger, string target, string schedulerName, string instanceId, string version);

    [LoggerMessage(EventId = 9113, Level = LogLevel.Warning, Message = "Agent {Target} registration for {SchedulerName} refused: {Reason}")]
    public static partial void AgentRegistrationRefused(this ILogger logger, string target, string schedulerName, string reason);

    [LoggerMessage(EventId = 9114, Level = LogLevel.Information, Message = "Agent {Target} disconnected; scheduler {SchedulerName} is reported Unknown until it reconnects")]
    public static partial void AgentDisconnected(this ILogger logger, string target, string schedulerName);

    [LoggerMessage(EventId = 9115, Level = LogLevel.Information, Message = "Agent {Target} forgotten after {ForgetAfter} without reconnecting")]
    public static partial void AgentForgotten(this ILogger logger, string target, TimeSpan forgetAfter);

    [LoggerMessage(EventId = 9116, Level = LogLevel.Warning, Message = "Agent {Target} missed {Missed} heartbeats; scheduler {SchedulerName} is reported Unknown")]
    public static partial void AgentMissedHeartbeats(this ILogger logger, string target, int missed, string schedulerName);

    [LoggerMessage(EventId = 9117, Level = LogLevel.Error, Message = "The agent liveness sweep failed; the next one runs in {Interval}")]
    public static partial void AgentSweepFailed(this ILogger logger, TimeSpan interval, Exception exception);
}
