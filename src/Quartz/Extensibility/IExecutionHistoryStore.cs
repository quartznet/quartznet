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

namespace Quartz.Extensibility;

/// <summary>
/// Where what a scheduler has run and what it has missed is kept.
/// </summary>
/// <remarks>
/// <para>
/// A job store holds what is <em>scheduled</em>; this holds what <em>happened</em>. Nothing in Quartz
/// keeps that on its own — a fired trigger row is gone once the firing completes — so an operator
/// looking at a dashboard, an HTTP API or a report of the last hour is looking at this.
/// </para>
/// <para>
/// The shipped implementation is per-process and in-memory, bounded by
/// <see cref="ExecutionHistoryOptions" />; register your own before
/// <c>AddQuartzExecutionHistory()</c> to keep history somewhere that survives a restart. Both feeds
/// carry the node that produced each row, which is what makes an implementation shared by a whole
/// cluster readable.
/// </para>
/// <para>
/// Two feeds, one verb each way: an execution and a misfire are both written with <c>Add*</c> and both
/// read with <c>Query*</c>, and the paged reads are named as the scheduler's own queries are. A store
/// that records nowhere — one fronting a scheduler in another process, where the history is kept — is
/// entitled to raise <see cref="NotSupportedException" /> from the two writers.
/// </para>
/// </remarks>
public interface IExecutionHistoryStore
{
    /// <summary>
    /// Records one execution, which the history reads then return.
    /// </summary>
    /// <param name="entry">The execution that finished.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask AddExecution(ExecutionHistoryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of the recorded executions, newest first.
    /// </summary>
    /// <param name="query">Which executions to return, and how many.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<PagedResult<ExecutionHistoryEntry>> QueryExecutions(ExecutionHistoryQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records one misfire, which the misfire reads then return.
    /// </summary>
    /// <param name="entry">The firing that was missed.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask AddMisfire(MisfireHistoryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of the recorded misfires, newest first.
    /// </summary>
    /// <param name="query">Which misfires to return, and how many.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<PagedResult<MisfireHistoryEntry>> QueryMisfires(MisfireHistoryQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many misfires the scheduler has recorded since <paramref name="since" />.
    /// </summary>
    /// <remarks>
    /// A count rather than a page, because a summary asks "how bad is it right now" and a store that
    /// keeps history in a database can answer that with one <c>COUNT(*)</c> instead of loading rows it
    /// would throw away.
    /// </remarks>
    /// <param name="schedulerName">The scheduler whose misfires to count.</param>
    /// <param name="since">The instant to count from.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    ValueTask<int> CountMisfires(string schedulerName, DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one recorded execution, its <see cref="ExecutionHistoryEntry.Log" /> included, or
    /// <see langword="null" /> when the scheduler has no row by that <see cref="ExecutionHistoryEntry.EntryId" />
    /// — never recorded, or trimmed since.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The read a detail page makes, and the one read that carries the captured log: the listing need
    /// not, and the persistent store's does not.
    /// </para>
    /// <para>
    /// A default interface member, so a store written against an earlier 4.x keeps compiling. The
    /// default reads the scheduler's whole history through <see cref="QueryExecutions" /> and picks the
    /// row out — correct for any store, and a full read; a store that can find one row by its key
    /// overrides it.
    /// </para>
    /// </remarks>
    /// <param name="schedulerName">The scheduler the execution belongs to.</param>
    /// <param name="entryId">The row's <see cref="ExecutionHistoryEntry.EntryId" />.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    async ValueTask<ExecutionHistoryEntry?> GetExecution(string schedulerName, string entryId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);

        PagedResult<ExecutionHistoryEntry> all = await QueryExecutions(
            new ExecutionHistoryQuery { SchedulerName = schedulerName, Take = PagedQuery.All },
            cancellationToken).ConfigureAwait(false);

        foreach (ExecutionHistoryEntry entry in all.Items)
        {
            if (string.Equals(entry.EntryId, entryId, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        return null;
    }
}
