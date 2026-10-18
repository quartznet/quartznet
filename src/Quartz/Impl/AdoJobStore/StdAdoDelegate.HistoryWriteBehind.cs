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

using System.Data.Common;

namespace Quartz.Impl.AdoJobStore;

// The statements a write-behind flush of AdoExecutionHistoryStore issues: every buffered execution's
// row, the buffered jobs' statuses read once, and each status written back once, guarded by the
// RUN_COUNT read. Internal, like the rest of the history's statements.
//
// The rows and the guarded updates go as DbBatch where the round batches - a delegate Quartz ships, on a
// connection that can - and one command each otherwise, which is Oracle, Firebird and SQLite. A batch
// that fails is not replayed a statement at a time: the flush is one transaction, which the failure has
// already doomed on most providers, so the store rolls it back and decides what happens next.
public partial class StdAdoDelegate
{
    /// <summary>Records the executions of one write-behind flush.</summary>
    /// <param name="conn">The flush's unit of work, on the history store's own connection.</param>
    /// <param name="entries">The executions, each with its <see cref="ExecutionHistoryEntry.EntryId" /> set.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual async ValueTask InsertExecutionHistories(
        ConnectionAndTransactionHolder conn,
        IReadOnlyList<ExecutionHistoryEntry> entries,
        CancellationToken cancellationToken = default)
    {
        if (entries.Count < 2 || !BatchesRounds(conn))
        {
            foreach (ExecutionHistoryEntry entry in entries)
            {
                await InsertExecutionHistory(conn, entry.EntryId!, entry, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        List<SqlStatement> statements = new(entries.Count);
        foreach (ExecutionHistoryEntry entry in entries)
        {
            statements.Add(DescribeExecutionHistoryInsert(entry.EntryId!, entry));
        }

        await ExecuteHistoryBatches(conn, statements, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the statuses of the jobs one write-behind flush holds runs of, keyed by job; a job with no
    /// recorded run has no entry.
    /// </summary>
    /// <param name="conn">The flush's unit of work.</param>
    /// <param name="schedulerName">The scheduler the jobs belong to.</param>
    /// <param name="jobs">The jobs.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual async ValueTask<Dictionary<JobKey, JobRunStatus>> SelectJobRunStatusesByKey(
        ConnectionAndTransactionHolder conn,
        string schedulerName,
        IReadOnlyCollection<JobKey> jobs,
        CancellationToken cancellationToken = default)
    {
        List<JobRunStatus> found = await ReadJobRunStatusesOf(
            conn, schedulerName, Deduplicate(jobs), failing: "", cancellationToken).ConfigureAwait(false);

        Dictionary<JobKey, JobRunStatus> statuses = new(found.Count);
        foreach (JobRunStatus status in found)
        {
            statuses[status.Job] = status;
        }

        return statuses;
    }

    /// <summary>
    /// Writes each status whose row still has the <c>RUN_COUNT</c> the flush read, and answers how many did.
    /// </summary>
    /// <remarks>
    /// Fewer than asked for means another writer — a node, or this node writing a run itself while its
    /// queue was full — changed a row since the read. The store then rolls the flush back.
    /// </remarks>
    /// <param name="conn">The flush's unit of work.</param>
    /// <param name="writes">Each job's folded status, and the run count read with it.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual async ValueTask<int> UpdateJobRunStatusesIfUnchanged(
        ConnectionAndTransactionHolder conn,
        IReadOnlyList<(JobRunStatus Status, long ReadRunCount)> writes,
        CancellationToken cancellationToken = default)
    {
        List<SqlStatement> statements = new(writes.Count);
        foreach ((JobRunStatus status, long readRunCount) in writes)
        {
            statements.Add(DescribeJobRunStatusWrite(status, readRunCount));
        }

        if (statements.Count < 2 || !BatchesRounds(conn))
        {
            int updated = 0;
            foreach (SqlStatement statement in statements)
            {
                updated += await ExecuteHistoryStatement(conn, statement, cancellationToken).ConfigureAwait(false);
            }

            return updated;
        }

        // The total is enough: each statement updates one row or none, and any short count sends the whole
        // flush round again, so which one missed does not matter.
        return await ExecuteHistoryBatches(conn, statements, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Issues statements as batches no larger than a round's, and answers the rows they affected in total.
    /// </summary>
    private async ValueTask<int> ExecuteHistoryBatches(
        ConnectionAndTransactionHolder conn,
        List<SqlStatement> statements,
        CancellationToken cancellationToken)
    {
        int affected = 0;
        int offset = 0;
        foreach (int length in RoundBatchLengths(statements))
        {
            DbCommand parameterFactory = conn.Connection.CreateCommand();
            await using (parameterFactory.ConfigureAwait(false))
            {
                DbBatch batch = CreateStatementBatch(conn, parameterFactory, statements, offset, length);
                await using (batch.ConfigureAwait(false))
                {
                    affected += await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            offset += length;
        }

        return affected;
    }
}
