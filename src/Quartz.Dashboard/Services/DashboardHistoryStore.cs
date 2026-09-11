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

namespace Quartz.Dashboard.Services;

/// <remarks>
/// <see cref="Duration" /> is a <see cref="TimeSpan" />, as every other duration the dashboard shows
/// is; it used to be a count of whole milliseconds, which lost every execution shorter than one.
/// </remarks>
/// <param name="SchedulerName">The scheduler the execution belongs to.</param>
/// <param name="SchedulerInstanceId">
/// The node that ran it. Every node of a cluster keeps its own history of its own executions, so
/// without this a row cannot say which machine it came from — and a store shared across the cluster
/// cannot say it either.
/// </param>
/// <param name="JobGroup">The group of the job that ran.</param>
/// <param name="JobName">The name of the job that ran.</param>
/// <param name="TriggerGroup">The group of the trigger that fired it.</param>
/// <param name="TriggerName">The name of the trigger that fired it.</param>
/// <param name="FiredAtUtc">When the execution fired.</param>
/// <param name="Duration">How long the job took.</param>
/// <param name="Succeeded">Whether the job completed without throwing.</param>
/// <param name="ExceptionMessage">What it threw, or <see langword="null" /> when it succeeded.</param>
public sealed record DashboardHistoryEntry(
    string SchedulerName,
    string SchedulerInstanceId,
    string JobGroup,
    string JobName,
    string TriggerGroup,
    string TriggerName,
    DateTimeOffset FiredAtUtc,
    TimeSpan Duration,
    bool Succeeded,
    string? ExceptionMessage);

/// <summary>
/// One trigger that missed its scheduled firing, as the scheduler reported it.
/// </summary>
/// <remarks>
/// A misfire is not an execution — nothing ran — so it is recorded beside the executions rather than
/// among them, under the same bounds.
/// </remarks>
/// <param name="SchedulerName">The scheduler the trigger belongs to.</param>
/// <param name="SchedulerInstanceId">The node that noticed the misfire.</param>
/// <param name="TriggerGroup">The group of the trigger that missed its firing.</param>
/// <param name="TriggerName">The name of the trigger that missed its firing.</param>
/// <param name="JobKey">
/// The job the trigger points at, or <see langword="null" /> when the trigger names none.
/// </param>
/// <param name="MisfiredAtUtc">When the misfire was noticed, on the scheduler's clock.</param>
/// <param name="ScheduledFireTimeUtc">
/// The firing that was missed, or <see langword="null" /> when the trigger had no next firing left to
/// name. The scheduler reports a misfire before it applies the trigger's misfire instruction, so this
/// is the time the trigger was still due at.
/// </param>
public sealed record DashboardMisfireEntry(
    string SchedulerName,
    string SchedulerInstanceId,
    string TriggerGroup,
    string TriggerName,
    JobKeyDto? JobKey,
    DateTimeOffset MisfiredAtUtc,
    DateTimeOffset? ScheduledFireTimeUtc);

/// <summary>
/// Where the dashboard's execution history and misfire feed live.
/// </summary>
/// <remarks>
/// <para>
/// The dashboard's 4.0 seam, and it still works: register one of your own before
/// <c>AddQuartzDashboard()</c> and the recorder's rows are written into it, the dashboard's pages read
/// out of it, and the HTTP API's history routes answer out of it. What it is now is one side of a pair —
/// <see cref="Extensibility.IExecutionHistoryStore" /> is where Quartz itself keeps history, an adapter
/// joins the two, and a new implementation is better written against that one, which no surface but the
/// dashboard's depends on.
/// </para>
/// <para>
/// Both feeds carry the node that produced each row, which is what makes an implementation shared by a
/// whole cluster readable.
/// </para>
/// <para>
/// Two feeds, one verb each way: an execution and a misfire are both written with <c>Add*</c> and both
/// read with <c>Query*</c>, and the paged reads are named as <see cref="IQuartzApiClient" />'s are.
/// </para>
/// </remarks>
public interface IDashboardHistoryStore
{
    /// <summary>
    /// Records one execution, which the history page then reads back.
    /// </summary>
    ValueTask AddExecution(DashboardHistoryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of the recorded executions, newest first.
    /// </summary>
    ValueTask<PagedResult<DashboardHistoryEntry>> QueryExecutions(DashboardHistoryQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records one misfire, which the misfires page and the overview's tile then read back.
    /// </summary>
    ValueTask AddMisfire(DashboardMisfireEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of the recorded misfires, newest first.
    /// </summary>
    ValueTask<PagedResult<DashboardMisfireEntry>> QueryMisfires(DashboardMisfireQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many misfires the scheduler has recorded since <paramref name="since" />.
    /// </summary>
    /// <remarks>
    /// A count rather than a page, because a summary asks "how bad is it right now" and a store that
    /// keeps history in a database can answer that with one <c>COUNT(*)</c> instead of loading rows it
    /// would throw away.
    /// </remarks>
    ValueTask<int> CountMisfires(string schedulerName, DateTimeOffset since, CancellationToken cancellationToken = default);
}
