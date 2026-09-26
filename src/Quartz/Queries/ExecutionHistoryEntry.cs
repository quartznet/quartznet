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

namespace Quartz;

/// <summary>
/// One execution that has finished, as the scheduler that ran it reported.
/// </summary>
/// <remarks>
/// <see cref="Duration" /> is a <see cref="TimeSpan" />, as every other duration Quartz reports is.
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
public sealed record ExecutionHistoryEntry(
    string SchedulerName,
    string SchedulerInstanceId,
    string JobGroup,
    string JobName,
    string TriggerGroup,
    string TriggerName,
    DateTimeOffset FiredAtUtc,
    TimeSpan Duration,
    bool Succeeded,
    string? ExceptionMessage)
{
    /// <summary>
    /// Which attempt at the occurrence this execution was: <c>0</c> on the regular fire, <c>n</c> on
    /// the <c>n</c>-th retry under the trigger's <see cref="ITrigger.RetryPolicy" />.
    /// </summary>
    /// <remarks>
    /// A non-positional <c>init</c> property, so the record's constructor is unchanged and a store
    /// written against 4.1 still compiles. <c>0</c> for every execution of a trigger with no policy,
    /// which is the default.
    /// </remarks>
    public int RetryAttempt { get; init; }

    /// <summary>
    /// Whether the trigger answered this failure with another attempt, so the occurrence was not
    /// finished when this row was written.
    /// </summary>
    /// <remarks>
    /// This is what tells a row that says "failed, and will be tried again" from one that says
    /// "failed, and that was the last word". A row with <see cref="Succeeded" /> false and this false
    /// is a <em>final</em> failure — which is what <see cref="ExecutionHistoryQuery.FailedFinally" /> selects
    /// and what the dashboard offers to run again.
    /// </remarks>
    public bool RetryScheduled { get; init; }

    /// <summary>
    /// What names this row among the scheduler's history, and what
    /// <see cref="Extensibility.IExecutionHistoryStore.GetExecution" /> reads it back by; or
    /// <see langword="null" /> on a row nothing has named.
    /// </summary>
    /// <remarks>
    /// The recorder names every row it writes, and the shipped stores name one that arrives without, so
    /// every row they return carries one. A store written against 4.2 may return rows without, and a
    /// reader then has no way to ask for one of them alone.
    /// </remarks>
    public string? EntryId { get; init; }

    /// <summary>
    /// The lines the job logged while it ran, oldest first, or <see langword="null" /> when nothing was
    /// captured.
    /// </summary>
    /// <remarks>
    /// Kept only for a scheduler that calls <c>UseExecutionLogCapture()</c>, bounded by
    /// <see cref="ExecutionLogCaptureOptions" />. A listing is entitled to leave it out — the persistent
    /// store and the HTTP API do, so that a page of history does not carry every row's log — and
    /// <see cref="Extensibility.IExecutionHistoryStore.GetExecution" /> always carries it.
    /// </remarks>
    public string? Log { get; init; }
}
