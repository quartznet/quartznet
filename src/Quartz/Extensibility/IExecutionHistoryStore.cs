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

using Quartz.Impl;

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
/// <para>
/// Beside the feeds, a store may keep a <see cref="JobRunStatus" /> per job, read with
/// <see cref="QueryJobRunStatuses" /> and <see cref="GetJobRunStatus" />. Every store counts its runs over
/// time with <see cref="QueryExecutionStatistics" />.
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
    /// would throw away. Only <see cref="MisfireReason.Missed" /> rows count: a firing the overlap
    /// policy skipped, or a listener vetoed, is recorded beside the misfires, and is not one.
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

    /// <summary>
    /// Returns one page of the per-job run statuses the store keeps, ordered by job group and then name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A status is folded from every execution the store records, as it records it, so it outlives the
    /// rows. The shipped in-memory store keeps one per job, up to
    /// <see cref="ExecutionHistoryOptions.MaxEntriesPerScheduler" /> per scheduler.
    /// </para>
    /// <para>
    /// A default interface member, so a store written against an earlier 4.x keeps compiling. The default
    /// throws <see cref="NotSupportedException" />: the rows it could read are trimmed, so counts built
    /// from them would be wrong.
    /// </para>
    /// </remarks>
    /// <param name="query">Which statuses to return, and how many.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <exception cref="NotSupportedException">The store keeps no per-job status.</exception>
    ValueTask<PagedResult<JobRunStatus>> QueryJobRunStatuses(JobRunStatusQuery query, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            $"{GetType().Name} keeps no per-job run status. Implement {nameof(IExecutionHistoryStore)}."
            + $"{nameof(QueryJobRunStatuses)} to keep one beside the rows it records.");
    }

    /// <summary>
    /// Returns one job's run status, or <see langword="null" /> when the store has recorded no run of it.
    /// </summary>
    /// <remarks>
    /// A default interface member. The default asks <see cref="QueryJobRunStatuses" /> for that one job,
    /// so it answers wherever that does and throws <see cref="NotSupportedException" /> where it throws.
    /// </remarks>
    /// <param name="schedulerName">The scheduler the job belongs to.</param>
    /// <param name="jobKey">The job.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <exception cref="NotSupportedException">The store keeps no per-job status.</exception>
    async ValueTask<JobRunStatus?> GetJobRunStatus(string schedulerName, JobKey jobKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);
        ArgumentNullException.ThrowIfNull(jobKey);

        PagedResult<JobRunStatus> page = await QueryJobRunStatuses(
            new JobRunStatusQuery { SchedulerName = schedulerName, Jobs = [jobKey], Take = 1 },
            cancellationToken).ConfigureAwait(false);

        return page.Items.Count > 0 ? page.Items[0] : null;
    }

    /// <summary>
    /// Counts a scheduler's runs by result and times them, in buckets of fire time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What a chart of runs over time reads: one answer for a whole window, however many rows it holds,
    /// where a page of <see cref="QueryExecutions" /> holds a few.
    /// </para>
    /// <para>
    /// A default interface member, so a store written against an earlier 4.x keeps compiling. The default
    /// reads <see cref="QueryExecutions" /> a thousand rows at a time, newest first, and counts them here. It
    /// stops after <see cref="ExecutionStatistics.DefaultRowLimit" /> rows and sets
    /// <see cref="ExecutionStatistics.Truncated" />, so its oldest buckets may then be short. A store that can
    /// count where it keeps the rows overrides it; the shipped ones do.
    /// </para>
    /// </remarks>
    /// <param name="query">Which runs to count, and the bucket size.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    async ValueTask<ExecutionStatistics> QueryExecutionStatistics(ExecutionStatisticsQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        const int PageSize = 1000;

        ExecutionStatisticsBuilder builder = new(query.BucketSize);
        HashSet<string> counted = new(StringComparer.Ordinal);
        int read = 0;

        while (true)
        {
            // A group alone is narrowed by the key filter, which finds every row of the group and a few more;
            // the group is then matched exactly here.
            ExecutionHistoryQuery page = query.AsHistoryQuery(read, PageSize);
            if (query.JobGroup is not null && query.JobContains is null)
            {
                page = page with { JobContains = query.JobGroup };
            }

            PagedResult<ExecutionHistoryEntry> rows = await QueryExecutions(page, cancellationToken).ConfigureAwait(false);

            foreach (ExecutionHistoryEntry row in rows.Items)
            {
                // A row recorded while this reads pushes the older ones down a page, and is then read twice.
                bool seen = row.EntryId is { } entryId && !counted.Add(entryId);
                if (!seen && (query.JobGroup is null || string.Equals(row.JobGroup, query.JobGroup, StringComparison.Ordinal)))
                {
                    builder.Add(row);
                }
            }

            read += rows.Items.Count;
            if (!rows.HasMore || rows.Items.Count == 0)
            {
                return builder.Build(truncated: false);
            }

            if (read >= ExecutionStatistics.DefaultRowLimit)
            {
                return builder.Build(truncated: true);
            }
        }
    }
}
