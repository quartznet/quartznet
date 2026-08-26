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

using Quartz.Dashboard.Services;

namespace Quartz.Dashboard.Hubs;

/// <summary>
/// The events the dashboard hub pushes to a connected browser.
/// </summary>
/// <remarks>
/// Nothing outside this assembly calls this, and its DTOs are of no use to anyone else either, so it
/// would be internal if it could be. It cannot: SignalR reaches a typed client by emitting a proxy
/// into a dynamic assembly of its own, which no <c>InternalsVisibleTo</c> can name — a strong-named
/// assembly may only grant one to a friend it names by public key — and the proxy then fails to load
/// with "attempting to implement an inaccessible interface". <c>QuartzDashboardHubClientProxyTest</c>
/// is the guard.
/// <para>
/// The <see cref="Task" /> return types are dictated by SignalR for the same reason: the typed-client
/// proxy only implements members returning <see cref="Task" /> or <see cref="Task{TResult}" />.
/// </para>
/// <para>
/// Every payload leads with the <c>SchedulerInstanceId</c> of the node that raised the event. A browser
/// joins a group named after a scheduler, and in a cluster that group is fed by every node running that
/// scheduler — so without the id a live view cannot say which machine an event came from, which is what
/// <see href="https://github.com/quartznet/quartznet/issues/3422" /> reported. A pause and a resume get
/// a payload of their own for it rather than a <c>JobKeyDto</c> / <c>TriggerKeyDto</c>: those are keys,
/// used by <see cref="Services.IQuartzApiClient" /> everywhere, and a key does not belong to a node.
/// </para>
/// </remarks>
public interface IQuartzDashboardHubClient
{
    Task JobExecuting(JobEventDto jobEvent);

    Task JobExecuted(JobExecutionResultDto result);

    Task TriggerFired(TriggerEventDto triggerEvent);

    Task TriggerCompleted(TriggerEventDto triggerEvent);

    Task TriggerMisfired(TriggerEventDto triggerEvent);

    Task TriggerPaused(TriggerLifecycleDto triggerEvent);

    Task TriggerResumed(TriggerLifecycleDto triggerEvent);

    Task JobPaused(JobLifecycleDto jobEvent);

    Task JobResumed(JobLifecycleDto jobEvent);

    Task SchedulerStateChanged(SchedulerStateDto state);

    Task SchedulerError(SchedulerErrorDto schedulerError);
}

public sealed record JobEventDto(
    string SchedulerInstanceId,
    JobKeyDto JobKey,
    TriggerKeyDto TriggerKey,
    DateTimeOffset FireTimeUtc,
    string? FireInstanceId);

/// <remarks>
/// <see cref="RunTime" /> is a <see cref="TimeSpan" />, as every other duration on the wire is — it
/// carries <see cref="IJobExecutionContext.JobRunTime" /> unrounded, where the milliseconds it used to
/// be threw away everything below one.
/// </remarks>
public sealed record JobExecutionResultDto(
    string SchedulerInstanceId,
    JobKeyDto JobKey,
    TriggerKeyDto TriggerKey,
    DateTimeOffset FireTimeUtc,
    TimeSpan RunTime,
    bool Vetoed,
    string? ExceptionMessage);

public sealed record TriggerEventDto(
    string SchedulerInstanceId,
    TriggerKeyDto TriggerKey,
    JobKeyDto? JobKey,
    DateTimeOffset? FireTimeUtc);

/// <summary>
/// A trigger that was paused or resumed, and the node that did it.
/// </summary>
public sealed record TriggerLifecycleDto(string SchedulerInstanceId, TriggerKeyDto TriggerKey);

/// <summary>
/// A job that was paused or resumed, and the node that did it.
/// </summary>
public sealed record JobLifecycleDto(string SchedulerInstanceId, JobKeyDto JobKey);

/// <summary>
/// The state a scheduler is now in, pushed whenever it changes.
/// </summary>
/// <remarks>
/// A <see cref="SchedulerStatus" /> rather than free text: this used to be a phrase chosen at each
/// call site, which is how a running scheduler came to be announced as <c>"Started"</c> here while the
/// same state was called <c>"Running"</c> everywhere else. It carries the state the scheduler is in,
/// so an event that is not a state — a scheduler that is starting — is not one of these at all.
/// <para>
/// The state is one node's. A cluster is a scheduler running in several processes at once, each with a
/// lifecycle of its own, so a node going into standby says nothing about its peers.
/// </para>
/// </remarks>
public sealed record SchedulerStateDto(string SchedulerName, string SchedulerInstanceId, SchedulerStatus Status);

/// <remarks>
/// <see cref="TriggerKey" /> and <see cref="JobKey" /> are null when the scheduler could not say what
/// the error was about — a store retrying a failed operation names neither.
/// </remarks>
public sealed record SchedulerErrorDto(
    string SchedulerName,
    string SchedulerInstanceId,
    string Message,
    string? Cause,
    TriggerKeyDto? TriggerKey,
    JobKeyDto? JobKey);
