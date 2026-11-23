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

namespace Quartz.AspNetCore;

/// <summary>
/// The surface an operator acted through, which the dashboard's Action Log shows beside each entry.
/// </summary>
internal enum OperatorChannel
{
    /// <summary>A dashboard page's own button.</summary>
    Dashboard,

    /// <summary>A request to the HTTP API: a script, a pipeline, <c>HttpScheduler</c>.</summary>
    HttpApi,

    /// <summary>A tool an MCP client called.</summary>
    Mcp,
}

/// <summary>
/// One change an operator made, or reached for and was refused, through a surface other than the
/// dashboard's pages.
/// </summary>
/// <param name="Via">The surface the action came through.</param>
/// <param name="User">Who made it, or <c>(anonymous)</c> where nothing authenticated.</param>
/// <param name="SchedulerKey">The scheduler's key, <c>w1/QuartzScheduler</c> for one behind a target.</param>
/// <param name="Operation">The route's name: <c>PauseTrigger</c>, <c>Shutdown</c>.</param>
/// <param name="Subject">What it was done to — a job, a trigger, a calendar — or the scheduler's key.</param>
/// <param name="Succeeded">Whether it was done.</param>
internal sealed record OperatorAction(
    OperatorChannel Via,
    string User,
    string SchedulerKey,
    string Operation,
    string Subject,
    bool Succeeded)
{
    /// <summary>Whether a role rule refused it, so nothing reached the scheduler.</summary>
    public bool Refused { get; init; }

    /// <summary>The client's own name for itself, as it stated it, or <see langword="null" />.</summary>
    public string? Client { get; init; }

    /// <summary>What else to say about it: the role a refusal needed.</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Where an action taken outside the dashboard's pages is recorded besides the application's log.
/// </summary>
/// <remarks>
/// Internal, and in this package rather than the dashboard's, because the surfaces that record are here
/// and the dashboard is one of the things they record to: <c>AddQuartzDashboard</c> registers the
/// implementation that writes the Action Log, and a process with no dashboard has none and records only
/// the log line. A recorder is called on the request's path and must not block or throw.
/// </remarks>
internal interface IOperatorActionRecorder
{
    void Record(OperatorAction action);
}
