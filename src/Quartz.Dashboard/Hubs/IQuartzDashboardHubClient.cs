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
/// </remarks>
public interface IQuartzDashboardHubClient
{
    Task JobExecuting(JobEventDto jobEvent);

    Task JobExecuted(JobExecutionResultDto result);

    Task TriggerFired(TriggerEventDto triggerEvent);

    Task TriggerCompleted(TriggerEventDto triggerEvent);

    Task TriggerMisfired(TriggerEventDto triggerEvent);

    Task TriggerPaused(TriggerKeyDto triggerKey);

    Task TriggerResumed(TriggerKeyDto triggerKey);

    Task JobPaused(JobKeyDto jobKey);

    Task JobResumed(JobKeyDto jobKey);

    Task SchedulerStateChanged(SchedulerStateDto state);

    Task SchedulerError(SchedulerErrorDto schedulerError);
}

public sealed record JobEventDto(
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
    JobKeyDto JobKey,
    TriggerKeyDto TriggerKey,
    DateTimeOffset FireTimeUtc,
    TimeSpan RunTime,
    bool Vetoed,
    string? ExceptionMessage);

public sealed record TriggerEventDto(
    TriggerKeyDto TriggerKey,
    JobKeyDto? JobKey,
    DateTimeOffset? FireTimeUtc);

/// <summary>
/// The state a scheduler is now in, pushed whenever it changes.
/// </summary>
/// <remarks>
/// A <see cref="SchedulerStatus" /> rather than free text: this used to be a phrase chosen at each
/// call site, which is how a running scheduler came to be announced as <c>"Started"</c> here while the
/// same state was called <c>"Running"</c> everywhere else. It carries the state the scheduler is in,
/// so an event that is not a state — a scheduler that is starting — is not one of these at all.
/// </remarks>
public sealed record SchedulerStateDto(string SchedulerName, SchedulerStatus Status);

public sealed record SchedulerErrorDto(string SchedulerName, string Message, string? Cause);
