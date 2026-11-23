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
/// How a firing a caller waited for ended, or that it had not ended when the wait gave up.
/// </summary>
/// <remarks>
/// <para>
/// What <see cref="SchedulerWaitExtensions.TriggerJobAndWait(IScheduler, JobKey, TimeSpan, CancellationToken)" />
/// and <see cref="SchedulerWaitExtensions.WaitForFiring" /> answer with, the same in process and on a cluster:
/// <see cref="Execution" /> is the row the execution history recorded for the run, or the row it would record.
/// </para>
/// <para>
/// Never the job and never the exception: the history keeps neither, so a cluster could not answer with them.
/// The job's own message is <see cref="ExecutionHistoryEntry.ExceptionMessage" />.
/// </para>
/// </remarks>
public sealed record FiringResult
{
    /// <summary>
    /// The trigger whose firing was waited for: the one-off trigger <c>TriggerJobAndWait</c> stored, or the key
    /// <c>WaitForFiring</c> was given. The handle to wait again with after <see cref="FiringWaitStatus.Pending" />.
    /// </summary>
    public required TriggerKey TriggerKey { get; init; }

    /// <summary>
    /// Whether the firing ended, the wait ran out first, or nothing says how it ended.
    /// </summary>
    public required FiringWaitStatus Status { get; init; }

    /// <summary>
    /// How the firing ended, set when <see cref="Status" /> is <see cref="FiringWaitStatus.Ended" />.
    /// </summary>
    /// <remarks>
    /// What the scheduler saw: <see cref="ExecutionOutcome.Failed" /> when the job threw. A job that reported
    /// <see cref="JobRunReport.Failed" /> without throwing is <see cref="ExecutionOutcome.Succeeded" />; read
    /// <see cref="ExecutionHistoryEntry.EffectiveResult" /> for what the run achieved.
    /// </remarks>
    public ExecutionOutcome? Outcome { get; init; }

    /// <summary>
    /// The run, when the job ran: <see cref="Outcome" /> is <see cref="ExecutionOutcome.Succeeded" />,
    /// <see cref="ExecutionOutcome.Failed" /> or <see cref="ExecutionOutcome.Cancelled" />.
    /// </summary>
    /// <remarks>
    /// Its <see cref="ExecutionHistoryEntry.EntryId" /> is the history row's when the history recorded it, and
    /// <see langword="null" /> on a scheduler that keeps no history.
    /// </remarks>
    public ExecutionHistoryEntry? Execution { get; init; }

    /// <summary>
    /// The row that says why the job did not run, when one does: <see cref="Outcome" /> is
    /// <see cref="ExecutionOutcome.Vetoed" />, or <see cref="ExecutionOutcome.NotExecuted" /> because the
    /// trigger misfired or its overlap policy skipped it.
    /// </summary>
    public MisfireHistoryEntry? Misfire { get; init; }

    /// <summary>
    /// Why the job never ran, when <see cref="Outcome" /> is <see cref="ExecutionOutcome.NotExecuted" /> and the
    /// reason is known.
    /// </summary>
    public string? Message { get; init; }
}
