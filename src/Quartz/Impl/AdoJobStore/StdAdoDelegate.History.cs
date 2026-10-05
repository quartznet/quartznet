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

using System.Collections.Frozen;
using System.Data.Common;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace Quartz.Impl.AdoJobStore;

// The statements AdoExecutionHistoryStore issues against QRTZ_EXECUTION_HISTORY and
// QRTZ_MISFIRE_HISTORY.
//
// Internal rather than public, unlike the rest of this class. Nothing here is a seam: the statements
// are dialect-neutral, and the two things that are not - how a page is limited and how two strings are
// joined - are ApplyPaging and HistoryKeyExpression, which a dialect delegate already overrides or
// inherits. A delegate written outside Quartz gets the history working without implementing anything.
//
// None of it runs inside a job's transaction or under the trigger lock. The store opens its own
// connection for each statement, which is what makes a history write something a firing cannot wait on
// and cannot be failed by.
public partial class StdAdoDelegate
{
    /// <summary>Every result this version writes, in the order a result predicate names them.</summary>
    private static readonly JobRunResult[] knownResults =
        [JobRunResult.Succeeded, JobRunResult.Failed, JobRunResult.Cancelled, JobRunResult.Skipped];

    /// <summary>Every misfire reason this version writes, in the order a reason predicate names them.</summary>
    private static readonly MisfireReason[] knownReasons =
        [MisfireReason.Missed, MisfireReason.Overlap, MisfireReason.Vetoed];

    /// <summary>
    /// A delegate of the same dialect for the execution history to initialize and use on its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The history store can be read before anything has built its scheduler — a dashboard or the HTTP
    /// API resolves it at their first request — and the job store is what initializes the scheduler's
    /// delegate, once, when the scheduler is built. Until then this delegate has no connection helper and
    /// the default table prefix. Initializing <em>this</em> instance from the history store as well would
    /// be a second initialization of an object the job store owns, racing the job store's own with an
    /// instance id only the scheduler's build settles. A copy initialized separately touches none of it.
    /// </para>
    /// <para>
    /// A copy rather than a fresh instance, because the runtime type is the dialect — the paging, the key
    /// expression, the parameter binding, and whatever a delegate derived outside Quartz overrides — and
    /// constructing that type again would take reflection over it or a second registration of every way
    /// a delegate is chosen. The copy is shallow. What it shares with this one are the statement caches,
    /// which are keyed by statement and filled with the one table prefix the scheduler's options name,
    /// and whatever a derived delegate keeps — both of which the history store shared whole until now.
    /// The trigger persistence delegates are not shared: initializing the copy would otherwise point the
    /// ones the job store registered at the copy.
    /// </para>
    /// </remarks>
    internal StdAdoDelegate CopyForHistory()
    {
        StdAdoDelegate copy = (StdAdoDelegate) MemberwiseClone();
        copy.triggerPersistenceDelegates = [];
        copy.triggerPersistenceDelegatesByDiscriminator = FrozenDictionary<string, ITriggerPersistenceDelegate>.Empty;
        return copy;
    }

    /// <summary>
    /// How a dialect writes "this key, as one string": the group, a dot, and the name, lowered so that
    /// a <c>Contains</c> filter reads the way the in-memory history reads it — case-insensitively.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One predicate rather than three, because the three the in-memory store applies collapse into
    /// it: a filter found in the group or in the name is found in <c>group.name</c> too, and only the
    /// joined form matches a filter that spans the dot.
    /// </para>
    /// <para>
    /// <c>||</c> is ANSI and is what PostgreSQL, Oracle, SQLite and Firebird take. SQL Server and
    /// MySQL say it differently and override this.
    /// </para>
    /// </remarks>
    internal virtual string HistoryKeyExpression(string groupColumn, string nameColumn)
    {
        return "LOWER(" + groupColumn + " || '.' || " + nameColumn + ")";
    }

    /// <summary>
    /// The most characters an exception message is stored with. Longer messages are cut, because a
    /// history row is a summary an operator reads rather than the log.
    /// </summary>
    /// <remarks>
    /// The column is declared 1,000 wide everywhere and 4,000 on Oracle, whose <c>VARCHAR2</c> counts
    /// bytes: 1,000 characters is at most 3,000 bytes of UTF-8. Firebird's is 1,000 bytes in a database
    /// created without a character set, so there a message is also cut to that many bytes; see
    /// <see cref="TextColumnByteWidth" />.
    /// </remarks>
    internal const int MaxErrorMessageLength = 1000;

    /// <summary>Records one execution.</summary>
    /// <param name="conn">The unit of work, which is the history store's own connection.</param>
    /// <param name="entryId">The row's key, which the store mints.</param>
    /// <param name="entry">The execution that finished.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual async ValueTask<int> InsertExecutionHistory(
        ConnectionAndTransactionHolder conn,
        string entryId,
        ExecutionHistoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlInsertExecutionHistory));

        AddCommandParameter(cmd, SqlParameters.SchedulerName, entry.SchedulerName);
        AddCommandParameter(cmd, SqlParameters.EntryId, entryId);
        AddCommandParameter(cmd, SqlParameters.InstanceName, entry.SchedulerInstanceId);
        AddCommandParameter(cmd, SqlParameters.JobName, entry.JobName);
        AddCommandParameter(cmd, SqlParameters.JobGroup, entry.JobGroup);
        AddCommandParameter(cmd, SqlParameters.TriggerName, entry.TriggerName);
        AddCommandParameter(cmd, SqlParameters.TriggerGroup, entry.TriggerGroup);
        AddCommandParameter(cmd, SqlParameters.FiredTime, GetDbDateTimeValue(entry.FiredAtUtc));
        AddCommandParameter(cmd, SqlParameters.RunTime, entry.Duration.Ticks);
        AddCommandParameter(cmd, SqlParameters.Succeeded, GetDbBooleanValue(entry.Succeeded));
        AddCommandParameter(cmd, SqlParameters.ErrorMessage, CutToColumn(entry.ExceptionMessage, AdoConstants.ColumnErrorMessage, MaxErrorMessageLength));
        AddCommandParameter(cmd, SqlParameters.HistoryRetryAttempt, entry.RetryAttempt);
        AddCommandParameter(cmd, SqlParameters.HistoryRetryScheduled, GetDbBooleanValue(entry.RetryScheduled));

        // Written whole: the capture already bounded it, and the column is a large object on every
        // dialect, so there is no width here to cut it to. Bound as the driver's large-text type where
        // its description names one, which is the managed Oracle driver's Clob: a string it would bind
        // as Varchar2, and more than 4,000 bytes of that into a CLOB fails the whole row.
        AddCommandParameter(cmd, SqlParameters.ExecutionLog, entry.Log, DbProvider.Metadata.LargeTextParameterType);

        // The 4.4 outcome. RESULT is written as reported, null included, so a reader tells a row that
        // said nothing from one that said Succeeded. The summary is cut as ERROR_MESSAGE is; the metrics
        // are a large object like the log, already bounded by the recorder.
        AddCommandParameter(cmd, SqlParameters.HistoryResult, entry.Result is { } result ? (int) result : null);
        AddCommandParameter(cmd, SqlParameters.HistorySummary, CutToColumn(entry.Summary, AdoConstants.ColumnSummary, JobRunReport.MaxSummaryLength));
        AddCommandParameter(cmd, SqlParameters.HistoryMetrics, entry.MetricsJson, DbProvider.Metadata.LargeTextParameterType);
        AddCommandParameter(cmd, SqlParameters.HistoryManual, GetDbBooleanValue(entry.Manual));
        AddCommandParameter(cmd, SqlParameters.HistoryFireInstanceId, entry.FireInstanceId);

        // The run's input, a large object like the log: the recorder already held it to
        // ExecutionHistoryOptions.MaxInputBytes, and a cut one would be a different input.
        AddCommandParameter(cmd, SqlParameters.HistoryJobInput, entry.Input, DbProvider.Metadata.LargeTextParameterType);
        AddCommandParameter(cmd, SqlParameters.HistoryJobInputTooLarge, GetDbBooleanValue(entry.InputTooLarge));

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Folds one recorded execution into its job's <c>QRTZ_JOB_STATUS</c> row, if the job has one.
    /// </summary>
    /// <remarks>
    /// The update is chosen by what the execution achieved; see <see cref="JobStatusStatement" />. Run in
    /// the transaction that inserted the execution, so the row and the status commit together.
    /// </remarks>
    /// <returns>The rows updated: <c>0</c> on the job's first recorded run.</returns>
    /// <param name="conn">The unit of work, which is the history store's own connection and transaction.</param>
    /// <param name="entry">The execution just recorded, its <see cref="ExecutionHistoryEntry.EntryId" /> set.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual async ValueTask<int> UpdateJobRunStatus(
        ConnectionAndTransactionHolder conn,
        ExecutionHistoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        JobStatusStatement statement = JobStatusStatement.For(entry) switch
        {
            JobStatusUpdate.Succeeded => StdAdoConstants.SqlUpdateJobStatusSucceeded,
            JobStatusUpdate.FailedFinally => StdAdoConstants.SqlUpdateJobStatusFailedFinally,
            JobStatusUpdate.FailedRetried => StdAdoConstants.SqlUpdateJobStatusFailedRetried,
            _ => StdAdoConstants.SqlUpdateJobStatusOther
        };

        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(statement.Sql));

        // In the order the statement names them: each placeholder is a parameter of its own.
        foreach ((string name, JobStatusValue value) in statement.Parameters)
        {
            AddCommandParameter(cmd, name, JobStatusParameter(entry, value));
        }

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a job's <c>QRTZ_JOB_STATUS</c> row from its first recorded run.
    /// </summary>
    /// <remarks>
    /// The values are <see cref="JobRunStatusFold" /> of that one run, so the first row is what the fold
    /// says it is. Two nodes recording a job's first run at once both find no row, and the second insert
    /// fails on the key; the store rolls that transaction back and records the run again.
    /// </remarks>
    /// <param name="conn">The unit of work, which is the history store's own connection and transaction.</param>
    /// <param name="entry">The execution just recorded, its <see cref="ExecutionHistoryEntry.EntryId" /> set.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual async ValueTask<int> InsertJobRunStatus(
        ConnectionAndTransactionHolder conn,
        ExecutionHistoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        JobRunStatus first = JobRunStatusFold.Apply(current: null, entry);

        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlInsertJobStatus));

        AddCommandParameter(cmd, SqlParameters.SchedulerName, entry.SchedulerName);
        AddCommandParameter(cmd, SqlParameters.JobGroup, entry.JobGroup);
        AddCommandParameter(cmd, SqlParameters.JobName, entry.JobName);
        AddCommandParameter(cmd, SqlParameters.StatusFirstFiredTime, GetDbDateTimeValue(first.FirstFiredAtUtc));
        AddCommandParameter(cmd, SqlParameters.StatusLastFiredTime, GetDbDateTimeValue(first.LastFiredAtUtc));
        AddCommandParameter(cmd, SqlParameters.StatusLastResult, (int) first.LastResult);
        AddCommandParameter(cmd, SqlParameters.StatusLastRunTime, first.LastDuration.Ticks);
        AddCommandParameter(cmd, SqlParameters.StatusLastInstanceName, first.LastSchedulerInstanceId);
        AddCommandParameter(cmd, SqlParameters.StatusLastEntryId, first.LastEntryId);
        AddCommandParameter(cmd, SqlParameters.StatusLastSummary, CutToColumn(first.LastSummary, AdoConstants.ColumnLastSummary, JobRunReport.MaxSummaryLength));
        AddCommandParameter(cmd, SqlParameters.StatusLastSuccessTime, GetDbDateTimeValue(first.LastSucceededAtUtc));
        AddCommandParameter(cmd, SqlParameters.StatusLastFailureTime, GetDbDateTimeValue(first.LastFailedAtUtc));
        AddCommandParameter(cmd, SqlParameters.StatusLastFailureMessage, CutToColumn(first.LastFailureMessage, AdoConstants.ColumnLastFailureMessage, MaxErrorMessageLength));
        AddCommandParameter(cmd, SqlParameters.StatusConsecutiveFailures, first.ConsecutiveFailures);
        AddCommandParameter(cmd, SqlParameters.StatusRunCount, first.RunCount);
        AddCommandParameter(cmd, SqlParameters.StatusFailureCount, first.FailureCount);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>What one placeholder of a <c>QRTZ_JOB_STATUS</c> update is bound to.</summary>
    private object? JobStatusParameter(ExecutionHistoryEntry entry, JobStatusValue value)
    {
        return value switch
        {
            JobStatusValue.SchedulerName => entry.SchedulerName,
            JobStatusValue.JobGroup => entry.JobGroup,
            JobStatusValue.JobName => entry.JobName,
            JobStatusValue.FiredTime => GetDbDateTimeValue(entry.FiredAtUtc),
            JobStatusValue.Result => (int) entry.EffectiveResult,
            JobStatusValue.RunTime => entry.Duration.Ticks,
            JobStatusValue.InstanceName => entry.SchedulerInstanceId,
            JobStatusValue.EntryId => entry.EntryId,
            JobStatusValue.Summary => CutToColumn(entry.Summary, AdoConstants.ColumnLastSummary, JobRunReport.MaxSummaryLength),
            JobStatusValue.FailureMessage => CutToColumn(
                entry.ExceptionMessage ?? entry.Summary, AdoConstants.ColumnLastFailureMessage, MaxErrorMessageLength),
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Not a value a job status update binds.")
        };
    }

    /// <summary>Reads one job's run status, or <see langword="null" /> when no run of it is recorded.</summary>
    /// <param name="conn">The unit of work, which is the history store's own connection.</param>
    /// <param name="schedulerName">The scheduler the job belongs to.</param>
    /// <param name="jobKey">The job.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual async ValueTask<JobRunStatus?> SelectJobRunStatus(
        ConnectionAndTransactionHolder conn,
        string schedulerName,
        JobKey jobKey,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectJobStatus));

        AddCommandParameter(cmd, SqlParameters.SchedulerName, schedulerName);
        AddCommandParameter(cmd, SqlParameters.JobGroup, jobKey.Group);
        AddCommandParameter(cmd, SqlParameters.JobName, jobKey.Name);

        using DbDataReader rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await rs.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? ReadJobRunStatus(rs, schedulerName)
            : null;
    }

    /// <summary>Reads one page of a scheduler's run statuses, by job group and then name.</summary>
    /// <param name="conn">The unit of work, which is the history store's own connection.</param>
    /// <param name="query">Which statuses to return, and how many.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual async ValueTask<PagedResult<JobRunStatus>> SelectJobRunStatuses(
        ConnectionAndTransactionHolder conn,
        JobRunStatusQuery query,
        CancellationToken cancellationToken = default)
    {
        string failing = query.Failing switch
        {
            true => StdAdoConstants.SqlJobStatusFailing,
            false => StdAdoConstants.SqlJobStatusNotFailing,
            null => ""
        };

        if (query.Jobs is { } jobs)
        {
            return await SelectJobRunStatusesOf(conn, query, jobs, failing, cancellationToken).ConfigureAwait(false);
        }

        string schedulerName = query.SchedulerName;

        if (IsCountOnly(query))
        {
            return CountOnlyResult<JobRunStatus>(query, await CountHistory(
                conn, StdAdoConstants.SqlCountJobStatuses + failing, schedulerName, [], cancellationToken).ConfigureAwait(false));
        }

        List<JobRunStatus> items;
        bool hasMore;

        using (DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(BuildPagedSql(
                   StdAdoConstants.SqlSelectJobStatuses + failing + StdAdoConstants.SqlOrderByJobStatus, query))))
        {
            AddCommandParameter(cmd, SqlParameters.SchedulerName, schedulerName);
            BindPaging(cmd, query);

            (items, hasMore) = await ReadPage(
                cmd, query, reader => ReadJobRunStatus(reader, schedulerName), cancellationToken).ConfigureAwait(false);
        }

        int? totalCount = null;
        if (query.IncludeTotalCount)
        {
            totalCount = await CountHistory(
                conn, StdAdoConstants.SqlCountJobStatuses + failing, schedulerName, [], cancellationToken).ConfigureAwait(false);
        }

        return new PagedResult<JobRunStatus>(items, hasMore, totalCount);
    }

    /// <summary>
    /// The statuses of the jobs a query names, read by key and paged here.
    /// </summary>
    /// <remarks>
    /// A key-set predicate, chunked as every batch read by key is, so the page cannot be the database's:
    /// the rows are at most one per key asked for, and they are ordered and paged once all are read.
    /// </remarks>
    private async ValueTask<PagedResult<JobRunStatus>> SelectJobRunStatusesOf(
        ConnectionAndTransactionHolder conn,
        JobRunStatusQuery query,
        IReadOnlyCollection<JobKey> jobs,
        string failing,
        CancellationToken cancellationToken)
    {
        List<JobKey> requested = Deduplicate(jobs);
        List<JobRunStatus> found = [];

        for (int offset = 0; offset < requested.Count; offset += AdoUtil.MaxJobKeysPerPredicate)
        {
            int length = Math.Min(AdoUtil.MaxJobKeysPerPredicate, requested.Count - offset);
            int paddedCount = AdoUtil.RoundUpJobKeyCount(length);

            using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(
                StdAdoConstants.SqlSelectJobStatuses + failing + " AND " + AdoUtil.BuildJobKeyPredicate(paddedCount)));
            AddCommandParameter(cmd, SqlParameters.SchedulerName, query.SchedulerName);

            for (int i = 0; i < paddedCount; i++)
            {
                // Padded by repeating the chunk's last key: the predicate is a disjunction.
                JobKey key = requested[offset + Math.Min(i, length - 1)];
                AddCommandParameter(cmd, AdoUtil.JobKeyNameParameter(i), key.Name);
                AddCommandParameter(cmd, AdoUtil.JobKeyGroupParameter(i), key.Group);
            }

            using DbDataReader rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                found.Add(ReadJobRunStatus(rs, query.SchedulerName));
            }
        }

        found.Sort(static (left, right) =>
        {
            int byGroup = string.CompareOrdinal(left.Job.Group, right.Job.Group);
            return byGroup != 0 ? byGroup : string.CompareOrdinal(left.Job.Name, right.Job.Name);
        });

        int skip = Math.Min(query.Skip, found.Count);
        int take = Math.Min(query.Take, found.Count - skip);
        List<JobRunStatus> page = found.GetRange(skip, take);

        return new PagedResult<JobRunStatus>(page, skip + page.Count < found.Count, query.IncludeTotalCount ? found.Count : null);
    }

    /// <summary>Reads one recorded execution by its key, its captured log and recorded input included.</summary>
    /// <param name="conn">The unit of work, which is the history store's own connection.</param>
    /// <param name="schedulerName">The scheduler the execution belongs to.</param>
    /// <param name="entryId">The row's key.</param>
    /// <param name="notBefore">The age bound: a row older than this is not part of the history.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual async ValueTask<ExecutionHistoryEntry?> SelectExecutionHistoryEntry(
        ConnectionAndTransactionHolder conn,
        string schedulerName,
        string entryId,
        DateTimeOffset notBefore,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectExecutionHistoryEntry));

        // In the order the statement names them, for the providers that bind positionally.
        AddCommandParameter(cmd, SqlParameters.SchedulerName, schedulerName);
        AddCommandParameter(cmd, SqlParameters.EntryId, entryId);
        AddCommandParameter(cmd, SqlParameters.HistoryCutoff, GetDbDateTimeValue(notBefore));

        using DbDataReader rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        // Found by name, as the outcome columns are: its position moved when they were added, and a
        // statement written before 4.4 does not select the input.
        return ReadExecutionHistoryEntry(rs, schedulerName, ExecutionHistoryOrdinals.Of(rs)) with
        {
            Log = ReadOptionalString(rs, OptionalOrdinal(rs, AdoConstants.ColumnExecutionLog)),
            Input = ReadOptionalString(rs, OptionalOrdinal(rs, AdoConstants.ColumnJobInput))
        };
    }

    /// <summary>Records one misfire.</summary>
    /// <param name="conn">The unit of work, which is the history store's own connection.</param>
    /// <param name="entryId">The row's key, which the store mints.</param>
    /// <param name="entry">The firing that was missed.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual async ValueTask<int> InsertMisfireHistory(
        ConnectionAndTransactionHolder conn,
        string entryId,
        MisfireHistoryEntry entry,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlInsertMisfireHistory));

        AddCommandParameter(cmd, SqlParameters.SchedulerName, entry.SchedulerName);
        AddCommandParameter(cmd, SqlParameters.EntryId, entryId);
        AddCommandParameter(cmd, SqlParameters.InstanceName, entry.SchedulerInstanceId);
        AddCommandParameter(cmd, SqlParameters.TriggerName, entry.TriggerName);
        AddCommandParameter(cmd, SqlParameters.TriggerGroup, entry.TriggerGroup);
        AddCommandParameter(cmd, SqlParameters.JobName, entry.JobKey?.Name);
        AddCommandParameter(cmd, SqlParameters.JobGroup, entry.JobKey?.Group);
        AddCommandParameter(cmd, SqlParameters.MisfireTime, GetDbDateTimeValue(entry.MisfiredAtUtc));
        AddCommandParameter(cmd, SqlParameters.ScheduledTime, GetDbDateTimeValue(entry.ScheduledFireTimeUtc));
        AddCommandParameter(cmd, SqlParameters.MisfireReason, (int) entry.Reason);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads one page of the recorded executions, newest first.</summary>
    /// <param name="conn">The unit of work, which is the history store's own connection.</param>
    /// <param name="query">Which executions to return, and how many.</param>
    /// <param name="notBefore">The age bound: rows older than this are not part of the history.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual async ValueTask<PagedResult<ExecutionHistoryEntry>> SelectExecutionHistory(
        ConnectionAndTransactionHolder conn,
        ExecutionHistoryQuery query,
        DateTimeOffset notBefore,
        CancellationToken cancellationToken = default)
    {
        StringBuilder predicateBuilder = new();
        List<KeyValuePair<string, object?>> parameters = [];

        AppendExecutionHistoryPredicates(predicateBuilder, parameters, query, notBefore);

        string predicate = predicateBuilder.ToString();
        string schedulerName = query.SchedulerName;

        if (IsCountOnly(query))
        {
            return CountOnlyResult<ExecutionHistoryEntry>(query, await CountHistory(
                conn, StdAdoConstants.SqlCountExecutionHistory + predicate, schedulerName, parameters, cancellationToken).ConfigureAwait(false));
        }

        List<ExecutionHistoryEntry> items;
        bool hasMore;

        using (DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(BuildPagedSql(
                   StdAdoConstants.SqlSelectExecutionHistory + predicate + StdAdoConstants.SqlOrderByExecutionHistory, query))))
        {
            BindHistoryParameters(cmd, schedulerName, parameters);
            BindPaging(cmd, query);

            // The outcome columns are found once per page, by name, rather than once per row.
            ExecutionHistoryOrdinals? ordinals = null;
            (items, hasMore) = await ReadPage(
                cmd,
                query,
                reader => ReadExecutionHistoryEntry(reader, schedulerName, ordinals ??= ExecutionHistoryOrdinals.Of(reader)),
                cancellationToken).ConfigureAwait(false);
        }

        int? totalCount = null;
        if (query.IncludeTotalCount)
        {
            totalCount = await CountHistory(
                conn, StdAdoConstants.SqlCountExecutionHistory + predicate, schedulerName, parameters, cancellationToken).ConfigureAwait(false);
        }

        return new PagedResult<ExecutionHistoryEntry>(items, hasMore, totalCount);
    }

    /// <summary>
    /// How a dialect writes the bucket a row falls in: its fire time's ticks divided by the bucket size's,
    /// as an integer.
    /// </summary>
    /// <remarks>
    /// <c>/</c> divides two integers as integers in PostgreSQL, SQL Server, SQLite and Firebird. MySQL and
    /// Oracle divide exactly, and override this; Firebird overrides it to cast the bucket size.
    /// </remarks>
    internal virtual string HistoryStatisticsBucketExpression => StdAdoConstants.SqlStatisticsBucketByDivision;

    /// <summary>
    /// Whether the dialect has <c>PERCENTILE_CONT</c> as an aggregate that a <c>GROUP BY</c> can carry, as
    /// PostgreSQL and Oracle do.
    /// </summary>
    /// <remarks>
    /// Without it the run statistics read the runs either side of each percentile's rank, ranked by
    /// <c>ROW_NUMBER</c>, and interpolate between them here — the same arithmetic, so the same answer. SQL
    /// Server has <c>PERCENTILE_CONT</c> only as a window function, which sorts every row of the window
    /// once more for each percentile; the ranked read sorts them once.
    /// </remarks>
    internal virtual bool HistoryHasPercentileAggregate => false;

    /// <summary>Counts and times the recorded executions in buckets of fire time.</summary>
    /// <param name="conn">The unit of work, which is the history store's own connection.</param>
    /// <param name="query">Which executions to count, and the bucket size.</param>
    /// <param name="notBefore">The age bound: rows older than this are not part of the history.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual async ValueTask<ExecutionStatistics> SelectExecutionStatistics(
        ConnectionAndTransactionHolder conn,
        ExecutionStatisticsQuery query,
        DateTimeOffset notBefore,
        CancellationToken cancellationToken = default)
    {
        StringBuilder predicate = new();
        List<KeyValuePair<string, object?>> parameters = [];

        AppendExecutionHistoryPredicates(predicate, parameters, query.AsHistoryQuery(skip: 0, take: 0), notBefore);
        if (query.JobGroup is { } jobGroup)
        {
            predicate.Append(StdAdoConstants.SqlHistoryJobGroupPredicate);
            parameters.Add(new KeyValuePair<string, object?>(SqlParameters.HistoryJobGroup, jobGroup));
        }

        bool aggregate = HistoryHasPercentileAggregate;
        string sql = (aggregate ? StdAdoConstants.SqlSelectExecutionStatisticsAggregate : StdAdoConstants.SqlSelectExecutionStatisticsRanked)
                     + HistoryStatisticsBucketExpression
                     + StdAdoConstants.SqlSelectExecutionStatisticsRows
                     + predicate
                     + (aggregate ? StdAdoConstants.SqlSelectExecutionStatisticsAggregateTail : StdAdoConstants.SqlSelectExecutionStatisticsRankedTail);

        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(sql));

        // In the order the statement names them: the two in the derived table's SELECT list come before
        // its WHERE.
        AddCommandParameter(cmd, SqlParameters.HistoryBucketSize, query.BucketSize.Ticks);
        AddCommandParameter(cmd, SqlParameters.HistoryStatisticsSucceeded, GetDbBooleanValue(true));
        BindHistoryParameters(cmd, query.SchedulerName, parameters);

        using DbDataReader rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<ExecutionStatisticsBucket> buckets = aggregate
            ? await ReadAggregatedBuckets(rs, query.BucketSize, cancellationToken).ConfigureAwait(false)
            : await ReadRankedBuckets(rs, query.BucketSize, cancellationToken).ConfigureAwait(false);

        return new ExecutionStatistics { BucketSize = query.BucketSize, Buckets = buckets };
    }

    /// <summary>One bucket a row, its percentiles already the database's.</summary>
    internal static async ValueTask<List<ExecutionStatisticsBucket>> ReadAggregatedBuckets(
        DbDataReader rs,
        TimeSpan bucketSize,
        CancellationToken cancellationToken)
    {
        List<ExecutionStatisticsBucket> buckets = [];
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            buckets.Add(new ExecutionStatisticsBucket(ExecutionStatisticsBuilder.StartOf(ReadLong(rs, 0), bucketSize))
            {
                SucceededCount = ReadLong(rs, 1),
                FailedCount = ReadLong(rs, 2),
                CancelledCount = ReadLong(rs, 3),
                SkippedCount = ReadLong(rs, 4),
                MaxDuration = TimeSpan.FromTicks(ReadLong(rs, 5)),
                P50Duration = TimeSpan.FromTicks(ReadLong(rs, 6)),
                P95Duration = TimeSpan.FromTicks(ReadLong(rs, 7))
            });
        }

        return buckets;
    }

    /// <summary>
    /// Up to five rows a bucket, by bucket and then rank: the runs either side of each percentile's rank and
    /// the longest, each carrying the bucket's counts.
    /// </summary>
    internal static async ValueTask<List<ExecutionStatisticsBucket>> ReadRankedBuckets(
        DbDataReader rs,
        TimeSpan bucketSize,
        CancellationToken cancellationToken)
    {
        List<ExecutionStatisticsBucket> buckets = [];
        Dictionary<long, long> durationByRank = [];
        long? current = null;
        ExecutionStatisticsBucket? counts = null;
        long runs = 0;

        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            long bucket = ReadLong(rs, 0);
            if (bucket != current)
            {
                if (counts is not null)
                {
                    buckets.Add(Timed(counts, durationByRank, runs));
                }

                current = bucket;
                durationByRank.Clear();
                runs = ReadLong(rs, 3);
                counts = new ExecutionStatisticsBucket(ExecutionStatisticsBuilder.StartOf(bucket, bucketSize))
                {
                    SucceededCount = ReadLong(rs, 4),
                    FailedCount = ReadLong(rs, 5),
                    CancelledCount = ReadLong(rs, 6),
                    SkippedCount = ReadLong(rs, 7)
                };
            }

            durationByRank[ReadLong(rs, 2)] = ReadLong(rs, 1);
        }

        if (counts is not null)
        {
            buckets.Add(Timed(counts, durationByRank, runs));
        }

        return buckets;

        static ExecutionStatisticsBucket Timed(ExecutionStatisticsBucket counts, Dictionary<long, long> durationByRank, long runs) => counts with
        {
            P50Duration = PercentileOfRanks(durationByRank, runs, ExecutionStatisticsBuilder.Median),
            P95Duration = PercentileOfRanks(durationByRank, runs, ExecutionStatisticsBuilder.NinetyFifth),
            MaxDuration = TimeSpan.FromTicks(durationByRank[runs])
        };
    }

    /// <summary>
    /// A percentile from the durations at the ranks either side of it, ranks counted from one as
    /// <c>ROW_NUMBER</c> counts them.
    /// </summary>
    private static TimeSpan PercentileOfRanks(Dictionary<long, long> durationByRank, long runs, int hundredths)
    {
        (int lower, int remainder) = ExecutionStatisticsBuilder.Rank(runs, hundredths);
        long below = durationByRank[lower + 1];
        long above = remainder == 0 ? below : durationByRank[lower + 2];
        return ExecutionStatisticsBuilder.Interpolate(below, above, remainder);
    }

    /// <summary>
    /// An integer column: not <c>GetInt64</c>, because Oracle hands back a decimal for a <c>NUMBER</c> and a
    /// floored quotient, and Firebird 4 an <c>INT128</c>, as a <see cref="BigInteger" />, for a <c>SUM</c> of
    /// <c>BIGINT</c>s.
    /// </summary>
    private static long ReadLong(DbDataReader rs, int ordinal) => rs.GetValue(ordinal) switch
    {
        BigInteger wide => (long) wide,
        object value => Convert.ToInt64(value, CultureInfo.InvariantCulture)
    };

    /// <summary>Reads one page of the recorded misfires, newest first.</summary>
    /// <param name="conn">The unit of work, which is the history store's own connection.</param>
    /// <param name="query">Which misfires to return, and how many.</param>
    /// <param name="notBefore">The age bound: rows older than this are not part of the history.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual async ValueTask<PagedResult<MisfireHistoryEntry>> SelectMisfireHistory(
        ConnectionAndTransactionHolder conn,
        MisfireHistoryQuery query,
        DateTimeOffset notBefore,
        CancellationToken cancellationToken = default)
    {
        StringBuilder predicateBuilder = new();
        List<KeyValuePair<string, object?>> parameters = [];

        predicateBuilder.Append(StdAdoConstants.SqlMisfireHistoryNotBefore);
        parameters.Add(new KeyValuePair<string, object?>(SqlParameters.HistoryCutoff, GetDbDateTimeValue(notBefore)));

        AppendNodePredicate(predicateBuilder, parameters, query.SchedulerInstanceId);
        AppendContainsPredicate(
            predicateBuilder,
            parameters,
            SqlParameters.HistoryTriggerContains,
            HistoryKeyExpression(AdoConstants.ColumnTriggerGroup, AdoConstants.ColumnTriggerName),
            query.TriggerContains);
        AppendJobPredicate(predicateBuilder, parameters, query.Job);

        if (query.Reasons is { } reasons)
        {
            AppendReasonsPredicate(predicateBuilder, parameters, reasons);
        }

        string predicate = predicateBuilder.ToString();
        string schedulerName = query.SchedulerName;

        if (IsCountOnly(query))
        {
            return CountOnlyResult<MisfireHistoryEntry>(query, await CountHistory(
                conn, StdAdoConstants.SqlCountMisfireHistory + predicate, schedulerName, parameters, cancellationToken).ConfigureAwait(false));
        }

        List<MisfireHistoryEntry> items;
        bool hasMore;

        using (DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(BuildPagedSql(
                   StdAdoConstants.SqlSelectMisfireHistory + predicate + StdAdoConstants.SqlOrderByMisfireHistory, query))))
        {
            BindHistoryParameters(cmd, schedulerName, parameters);
            BindPaging(cmd, query);

            (items, hasMore) = await ReadPage(
                cmd, query, reader => ReadMisfireHistoryEntry(reader, schedulerName), cancellationToken).ConfigureAwait(false);
        }

        int? totalCount = null;
        if (query.IncludeTotalCount)
        {
            totalCount = await CountHistory(
                conn, StdAdoConstants.SqlCountMisfireHistory + predicate, schedulerName, parameters, cancellationToken).ConfigureAwait(false);
        }

        return new PagedResult<MisfireHistoryEntry>(items, hasMore, totalCount);
    }

    /// <summary>Counts the misfires recorded since an instant.</summary>
    /// <param name="conn">The unit of work, which is the history store's own connection.</param>
    /// <param name="schedulerName">The scheduler whose misfires to count.</param>
    /// <param name="since">The instant to count from.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual async ValueTask<int> CountMisfireHistorySince(
        ConnectionAndTransactionHolder conn,
        string schedulerName,
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlCountMisfiresSince));

        AddCommandParameter(cmd, SqlParameters.SchedulerName, schedulerName);
        AddCommandParameter(cmd, SqlParameters.HistorySince, GetDbDateTimeValue(since));

        return await SelectCount(cmd, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The instant the count bound falls on, or <see langword="null" /> when the feed holds no more
    /// rows than the bound allows.
    /// </summary>
    /// <param name="conn">The unit of work, which is the history store's own connection.</param>
    /// <param name="misfires">Whether the misfire feed is being trimmed rather than the execution one.</param>
    /// <param name="schedulerName">The scheduler whose rows to bound.</param>
    /// <param name="keep">How many rows the bound keeps.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual ValueTask<DateTimeOffset?> SelectHistoryCountBoundary(
        ConnectionAndTransactionHolder conn,
        bool misfires,
        string schedulerName,
        int keep,
        CancellationToken cancellationToken = default)
    {
        string sql = misfires
            ? StdAdoConstants.SqlSelectMisfireHistoryCountBoundary
            : StdAdoConstants.SqlSelectExecutionHistoryCountBoundary;

        return SelectHistoryBoundary(conn, sql, schedulerName, cutoff: null, skip: keep, cancellationToken);
    }

    /// <summary>
    /// The instant one sweep batch of expired rows ends at, or <see langword="null" /> when fewer than
    /// a batch have expired — which is what tells the sweep it can finish in one statement.
    /// </summary>
    /// <param name="conn">The unit of work, which is the history store's own connection.</param>
    /// <param name="misfires">Whether the misfire feed is being swept rather than the execution one.</param>
    /// <param name="schedulerName">The scheduler whose rows to bound.</param>
    /// <param name="cutoff">The instant rows expire below.</param>
    /// <param name="batchSize">How many rows one statement should delete.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual ValueTask<DateTimeOffset?> SelectHistoryBatchBoundary(
        ConnectionAndTransactionHolder conn,
        bool misfires,
        string schedulerName,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        string sql = misfires
            ? StdAdoConstants.SqlSelectMisfireHistoryBatchBoundary
            : StdAdoConstants.SqlSelectExecutionHistoryBatchBoundary;

        return SelectHistoryBoundary(conn, sql, schedulerName, cutoff, skip: batchSize, cancellationToken);
    }

    /// <summary>Deletes the rows of one feed that fall below an instant.</summary>
    /// <param name="conn">The unit of work, which is the history store's own connection.</param>
    /// <param name="misfires">Whether the misfire feed is being swept rather than the execution one.</param>
    /// <param name="schedulerName">The scheduler whose rows to delete.</param>
    /// <param name="cutoff">The instant to delete below, which the caller has bounded.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual async ValueTask<int> DeleteHistoryBefore(
        ConnectionAndTransactionHolder conn,
        bool misfires,
        string schedulerName,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        string sql = misfires
            ? StdAdoConstants.SqlDeleteMisfireHistoryBefore
            : StdAdoConstants.SqlDeleteExecutionHistoryBefore;

        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(sql));

        AddCommandParameter(cmd, SqlParameters.SchedulerName, schedulerName);
        AddCommandParameter(cmd, SqlParameters.HistoryCutoff, GetDbDateTimeValue(cutoff));

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The instant one sweep batch of a slice's expired executions ends at, or <see langword="null" />
    /// when fewer than a batch have expired.
    /// </summary>
    /// <param name="conn">The unit of work, which is the history store's own connection.</param>
    /// <param name="schedulerName">The scheduler whose rows to bound.</param>
    /// <param name="slice">A retention tier's results, or the capped job.</param>
    /// <param name="cutoff">The instant the slice's rows expire below.</param>
    /// <param name="batchSize">How many rows one statement should delete.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual ValueTask<DateTimeOffset?> SelectExecutionHistorySliceBoundary(
        ConnectionAndTransactionHolder conn,
        string schedulerName,
        ExecutionHistorySlice slice,
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        StringBuilder sql = new(StdAdoConstants.SqlSelectExecutionHistoryFiredTimeBefore);
        List<KeyValuePair<string, object?>> parameters = [new(SqlParameters.HistoryCutoff, GetDbDateTimeValue(cutoff))];

        AppendSlicePredicate(sql, parameters, slice);
        sql.Append(StdAdoConstants.SqlOrderByFiredTime);

        return SelectHistoryBoundary(conn, sql.ToString(), schedulerName, parameters, skip: batchSize, cancellationToken);
    }

    /// <summary>Deletes a slice's executions that fall below an instant.</summary>
    /// <param name="conn">The unit of work, which is the history store's own connection.</param>
    /// <param name="schedulerName">The scheduler whose rows to delete.</param>
    /// <param name="slice">A retention tier's results, or the capped job.</param>
    /// <param name="cutoff">The instant to delete below, which the caller has bounded.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual async ValueTask<int> DeleteExecutionHistorySlice(
        ConnectionAndTransactionHolder conn,
        string schedulerName,
        ExecutionHistorySlice slice,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        StringBuilder sql = new(StdAdoConstants.SqlDeleteExecutionHistoryBefore);
        List<KeyValuePair<string, object?>> parameters = [new(SqlParameters.HistoryCutoff, GetDbDateTimeValue(cutoff))];

        AppendSlicePredicate(sql, parameters, slice);

        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(sql.ToString()));
        BindHistoryParameters(cmd, schedulerName, parameters);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The jobs with more executions that did not fail than <paramref name="cap" />, by group and then
    /// name, at most <paramref name="take" /> of them.
    /// </summary>
    /// <param name="conn">The unit of work, which is the history store's own connection.</param>
    /// <param name="schedulerName">The scheduler whose jobs to count.</param>
    /// <param name="cap">How many rows of a job the cap keeps.</param>
    /// <param name="take">How many jobs to return.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual async ValueTask<List<JobKey>> SelectJobsOverHistoryCap(
        ConnectionAndTransactionHolder conn,
        string schedulerName,
        int cap,
        int take,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(ApplyPaging(StdAdoConstants.SqlSelectJobsOverHistoryCap, takeLimited: true)));

        AddCommandParameter(cmd, SqlParameters.SchedulerName, schedulerName);
        AddCommandParameter(cmd, SqlParameters.HistoryNotFailedSucceeded, GetDbBooleanValue(true));
        AddCommandParameter(cmd, SqlParameters.HistoryJobCap, cap);
        AddPagingParameters(cmd, skip: 0, take, takeLimited: true);

        List<JobKey> jobs = [];
        using DbDataReader rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            jobs.Add(new JobKey(rs.GetString(1), rs.GetString(0)));
        }

        return jobs;
    }

    /// <summary>
    /// The fire time of the newest execution of a job the per-job cap removes, or <see langword="null" />
    /// when the job is within the cap. Its failures are neither counted nor removed.
    /// </summary>
    /// <param name="conn">The unit of work, which is the history store's own connection.</param>
    /// <param name="schedulerName">The scheduler the job belongs to.</param>
    /// <param name="job">The job.</param>
    /// <param name="keep">How many rows the cap keeps.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual ValueTask<DateTimeOffset?> SelectJobHistoryCapBoundary(
        ConnectionAndTransactionHolder conn,
        string schedulerName,
        JobKey job,
        int keep,
        CancellationToken cancellationToken = default)
    {
        StringBuilder sql = new(StdAdoConstants.SqlSelectExecutionHistoryFiredTime);
        List<KeyValuePair<string, object?>> parameters = [];

        AppendSlicePredicate(sql, parameters, new ExecutionHistorySlice { CappedJob = job });
        sql.Append(StdAdoConstants.SqlOrderByFiredTimeDescending);

        return SelectHistoryBoundary(conn, sql.ToString(), schedulerName, parameters, skip: keep, cancellationToken);
    }

    /// <summary>
    /// Deletes the run statuses of jobs that no longer exist and last fired before an instant.
    /// </summary>
    /// <param name="conn">The unit of work, which is the history store's own connection.</param>
    /// <param name="schedulerName">The scheduler whose statuses to delete.</param>
    /// <param name="cutoff">The longest retention window's cutoff.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual async ValueTask<int> DeleteOrphanedJobRunStatuses(
        ConnectionAndTransactionHolder conn,
        string schedulerName,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlDeleteOrphanedJobStatuses));

        AddCommandParameter(cmd, SqlParameters.SchedulerName, schedulerName);
        AddCommandParameter(cmd, SqlParameters.HistoryCutoff, GetDbDateTimeValue(cutoff));

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------------------------------------
    // Building the reads
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The age bound and every filter of a history listing, which the run statistics apply too, so a chart
    /// counts the rows its page lists.
    /// </summary>
    private void AppendExecutionHistoryPredicates(
        StringBuilder predicate,
        List<KeyValuePair<string, object?>> parameters,
        ExecutionHistoryQuery query,
        DateTimeOffset notBefore)
    {
        predicate.Append(StdAdoConstants.SqlExecutionHistoryNotBefore);
        parameters.Add(new KeyValuePair<string, object?>(SqlParameters.HistoryCutoff, GetDbDateTimeValue(notBefore)));

        AppendNodePredicate(predicate, parameters, query.SchedulerInstanceId);
        AppendContainsPredicate(
            predicate,
            parameters,
            SqlParameters.HistoryJobContains,
            HistoryKeyExpression(AdoConstants.ColumnJobGroup, AdoConstants.ColumnJobName),
            query.JobContains);
        AppendContainsPredicate(
            predicate,
            parameters,
            SqlParameters.HistoryTriggerContains,
            HistoryKeyExpression(AdoConstants.ColumnTriggerGroup, AdoConstants.ColumnTriggerName),
            query.TriggerContains);
        AppendFailedFinallyPredicate(predicate, parameters, query.FailedFinally);
        AppendJobPredicate(predicate, parameters, query.Job);
        AppendInstantPredicate(predicate, parameters, StdAdoConstants.SqlExecutionHistoryFiredFrom, SqlParameters.HistoryFiredFrom, query.FiredFrom);
        AppendInstantPredicate(predicate, parameters, StdAdoConstants.SqlExecutionHistoryFiredBefore, SqlParameters.HistoryFiredBefore, query.FiredBefore);

        if (query.Results is { } results)
        {
            AppendResultsPredicate(predicate, parameters, results);
        }
    }

    private async ValueTask<DateTimeOffset?> SelectHistoryBoundary(
        ConnectionAndTransactionHolder conn,
        string sql,
        string schedulerName,
        DateTimeOffset? cutoff,
        int skip,
        CancellationToken cancellationToken)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(ApplyPaging(sql, takeLimited: true)));

        AddCommandParameter(cmd, SqlParameters.SchedulerName, schedulerName);
        if (cutoff is { } expiry)
        {
            AddCommandParameter(cmd, SqlParameters.HistoryCutoff, GetDbDateTimeValue(expiry));
        }

        AddPagingParameters(cmd, skip, take: 1, takeLimited: true);

        object? boundary = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return boundary is null ? null : GetDateTimeFromDbValue(boundary);
    }

    /// <summary>
    /// <see cref="SelectHistoryBoundary(ConnectionAndTransactionHolder, string, string, DateTimeOffset?, int, CancellationToken)" />
    /// for a statement whose predicates the caller composed: the scheduler, then
    /// <paramref name="parameters" /> in the order the statement names them, then the page.
    /// </summary>
    private async ValueTask<DateTimeOffset?> SelectHistoryBoundary(
        ConnectionAndTransactionHolder conn,
        string sql,
        string schedulerName,
        List<KeyValuePair<string, object?>> parameters,
        int skip,
        CancellationToken cancellationToken)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(ApplyPaging(sql, takeLimited: true)));

        BindHistoryParameters(cmd, schedulerName, parameters);
        AddPagingParameters(cmd, skip, take: 1, takeLimited: true);

        object? boundary = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return boundary is null ? null : GetDateTimeFromDbValue(boundary);
    }

    /// <summary>
    /// Narrows a sweep statement to a slice: a capped job's rows that did not fail, then a tier's results.
    /// </summary>
    private void AppendSlicePredicate(
        StringBuilder predicate,
        List<KeyValuePair<string, object?>> parameters,
        ExecutionHistorySlice slice)
    {
        if (slice.CappedJob is { } job)
        {
            AppendJobPredicate(predicate, parameters, job);
            predicate.Append(StdAdoConstants.SqlExecutionHistoryNotFailed);
            parameters.Add(new KeyValuePair<string, object?>(SqlParameters.HistoryNotFailedSucceeded, GetDbBooleanValue(true)));
        }

        if (slice.Results is { } results)
        {
            AppendResultsPredicate(predicate, parameters, results);
        }
    }

    /// <summary>Narrows a history read to one job, matched exactly.</summary>
    private static void AppendJobPredicate(
        StringBuilder predicate,
        List<KeyValuePair<string, object?>> parameters,
        JobKey? job)
    {
        if (job is null)
        {
            return;
        }

        predicate.Append(StdAdoConstants.SqlHistoryJobPredicate);
        parameters.Add(new KeyValuePair<string, object?>(SqlParameters.JobGroup, job.Group));
        parameters.Add(new KeyValuePair<string, object?>(SqlParameters.JobName, job.Name));
    }

    /// <summary>Narrows a history read to the rows on one side of an instant.</summary>
    private void AppendInstantPredicate(
        StringBuilder predicate,
        List<KeyValuePair<string, object?>> parameters,
        string fragment,
        string parameterName,
        DateTimeOffset? instant)
    {
        if (instant is not { } value)
        {
            return;
        }

        predicate.Append(fragment);
        parameters.Add(new KeyValuePair<string, object?>(parameterName, GetDbDateTimeValue(value)));
    }

    /// <summary>
    /// Narrows a history statement to the rows whose effective result is one of <paramref name="results" />.
    /// </summary>
    /// <remarks>
    /// One fragment per result, in the enumeration's order and each once, whatever order and repeats the
    /// caller used, so the statement texts stay few. A set that names no result this version knows —
    /// the empty one included — matches nothing, rather than being read as no filter.
    /// </remarks>
    private void AppendResultsPredicate(
        StringBuilder predicate,
        List<KeyValuePair<string, object?>> parameters,
        IReadOnlyCollection<JobRunResult> results)
    {
        bool any = false;
        foreach (JobRunResult result in knownResults)
        {
            if (!results.Contains(result))
            {
                continue;
            }

            predicate.Append(any ? " OR " : " AND (");
            any = true;

            switch (result)
            {
                case JobRunResult.Succeeded:
                    predicate.Append(StdAdoConstants.SqlExecutionHistoryResultSucceeded);
                    parameters.Add(new KeyValuePair<string, object?>(SqlParameters.HistoryLegacySucceeded, GetDbBooleanValue(true)));
                    break;
                case JobRunResult.Failed:
                    predicate.Append(StdAdoConstants.SqlExecutionHistoryResultFailed);
                    parameters.Add(new KeyValuePair<string, object?>(SqlParameters.HistoryLegacyFailed, GetDbBooleanValue(false)));
                    break;
                case JobRunResult.Cancelled:
                    predicate.Append(StdAdoConstants.SqlExecutionHistoryResultCancelled);
                    break;
                default:
                    predicate.Append(StdAdoConstants.SqlExecutionHistoryResultSkipped);
                    break;
            }
        }

        predicate.Append(any ? ")" : StdAdoConstants.SqlMatchesNothing);
    }

    /// <summary>
    /// Narrows a misfire read to the rows whose reason is one of <paramref name="reasons" />.
    /// </summary>
    /// <remarks>
    /// As the result filter is built: a fragment per reason in the enumeration's order, each bound once, and
    /// a set naming no reason this version knows matches nothing. A row a 4.2 node wrote has no reason and
    /// is matched as <see cref="MisfireReason.Missed" />, as it reads.
    /// </remarks>
    private static void AppendReasonsPredicate(
        StringBuilder predicate,
        List<KeyValuePair<string, object?>> parameters,
        IReadOnlyCollection<MisfireReason> reasons)
    {
        bool any = false;
        foreach (MisfireReason reason in knownReasons)
        {
            if (!reasons.Contains(reason))
            {
                continue;
            }

            predicate.Append(any ? " OR " : " AND (");
            any = true;

            (string fragment, string parameterName) = reason switch
            {
                MisfireReason.Missed => (StdAdoConstants.SqlMisfireHistoryReasonMissed, SqlParameters.HistoryReasonMissed),
                MisfireReason.Overlap => (StdAdoConstants.SqlMisfireHistoryReasonOverlap, SqlParameters.HistoryReasonOverlap),
                _ => (StdAdoConstants.SqlMisfireHistoryReasonVetoed, SqlParameters.HistoryReasonVetoed)
            };

            predicate.Append(fragment);
            parameters.Add(new KeyValuePair<string, object?>(parameterName, (int) reason));
        }

        predicate.Append(any ? ")" : StdAdoConstants.SqlMatchesNothing);
    }

    private static void AppendNodePredicate(
        StringBuilder predicate,
        List<KeyValuePair<string, object?>> parameters,
        string? schedulerInstanceId)
    {
        if (string.IsNullOrWhiteSpace(schedulerInstanceId))
        {
            return;
        }

        // Compared as it was written, which is what lets IDX_*_EH_INST answer the dashboard's node
        // filter with a seek. The database's collation decides case here, as it does for every other
        // key this store compares - an instance id is generated rather than typed.
        predicate.Append(StdAdoConstants.SqlHistoryNodePredicate);
        parameters.Add(new KeyValuePair<string, object?>(SqlParameters.HistoryNode, schedulerInstanceId.Trim()));
    }

    /// <summary>
    /// Narrows a history read to the occurrences that gave up, or to everything else.
    /// </summary>
    /// <remarks>
    /// The <see langword="true" /> and <see langword="false" /> are bound once each and named by both
    /// forms of the predicate, so a dialect that stores a boolean as something other than a bit gets
    /// its own spelling of them from <c>GetDbBooleanValue</c> rather than from the statement text.
    /// </remarks>
    private void AppendFailedFinallyPredicate(
        StringBuilder predicate,
        List<KeyValuePair<string, object?>> parameters,
        bool? failedFinally)
    {
        if (failedFinally is not { } wanted)
        {
            return;
        }

        predicate.Append(wanted
            ? StdAdoConstants.SqlExecutionHistoryFailedFinally
            : StdAdoConstants.SqlExecutionHistoryNotFailedFinally);

        // "Gave up" is two falses ANDed; "everything else" is the De Morgan of it, two trues ORed.
        object? comparand = GetDbBooleanValue(!wanted);
        parameters.Add(new KeyValuePair<string, object?>(SqlParameters.Succeeded, comparand));
        parameters.Add(new KeyValuePair<string, object?>(SqlParameters.HistoryRetryScheduled, comparand));
    }

    private void AppendContainsPredicate(
        StringBuilder predicate,
        List<KeyValuePair<string, object?>> parameters,
        string parameterName,
        string keyExpression,
        string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return;
        }

        predicate.Append(" AND ").Append(keyExpression).Append(" LIKE @").Append(parameterName)
            .Append(StdAdoConstants.SqlLikeEscapeClause);

        string pattern = "%" + EscapeSqlLikeWildcards(filter.Trim(), AdditionalLikeWildcards).ToLowerInvariant() + "%";
        parameters.Add(new KeyValuePair<string, object?>(parameterName, pattern));
    }

    private async ValueTask<int> CountHistory(
        ConnectionAndTransactionHolder conn,
        string sql,
        string schedulerName,
        List<KeyValuePair<string, object?>> parameters,
        CancellationToken cancellationToken)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(sql));
        BindHistoryParameters(cmd, schedulerName, parameters);
        return await SelectCount(cmd, cancellationToken).ConfigureAwait(false);
    }

    private void BindHistoryParameters(
        DbCommand cmd,
        string schedulerName,
        List<KeyValuePair<string, object?>> parameters)
    {
        AddCommandParameter(cmd, SqlParameters.SchedulerName, schedulerName);
        foreach (KeyValuePair<string, object?> parameter in parameters)
        {
            AddCommandParameter(cmd, parameter.Key, parameter.Value);
        }
    }

    private ExecutionHistoryEntry ReadExecutionHistoryEntry(DbDataReader rs, string schedulerName, ExecutionHistoryOrdinals outcome)
    {
        return new ExecutionHistoryEntry(
            SchedulerName: schedulerName,
            SchedulerInstanceId: rs.GetString(0),
            JobGroup: rs.GetString(2),
            JobName: rs.GetString(1),
            TriggerGroup: rs.GetString(4),
            TriggerName: rs.GetString(3),
            FiredAtUtc: GetDateTimeFromDbValue(rs.GetValue(5)) ?? DateTimeOffset.MinValue,
            Duration: TimeSpan.FromTicks(Convert.ToInt64(rs.GetValue(6), CultureInfo.InvariantCulture)),
            Succeeded: GetBooleanFromDbValue(rs.GetValue(7)),
            ExceptionMessage: rs.IsDBNull(8) ? null : rs.GetString(8))
        {
            RetryAttempt = Convert.ToInt32(rs.GetValue(9), CultureInfo.InvariantCulture),
            RetryScheduled = GetBooleanFromDbValue(rs.GetValue(10)),
            EntryId = rs.GetString(11),
            Result = ReadResult(rs, outcome.Result),
            Summary = ReadOptionalString(rs, outcome.Summary),
            MetricsJson = ReadOptionalString(rs, outcome.Metrics),
            Manual = ReadOptionalFlag(rs, outcome.Manual),
            FireInstanceId = ReadOptionalString(rs, outcome.FireInstanceId),
            InputTooLarge = ReadOptionalFlag(rs, outcome.InputTooLarge)
        };
    }

    /// <summary>
    /// A flag column's value, or <see langword="false" /> when it is null or not in the result set.
    /// </summary>
    private bool ReadOptionalFlag(DbDataReader rs, int ordinal)
    {
        return ordinal >= 0 && !rs.IsDBNull(ordinal) && GetBooleanFromDbValue(rs.GetValue(ordinal));
    }

    private JobRunStatus ReadJobRunStatus(DbDataReader rs, string schedulerName)
    {
        return new JobRunStatus(
            SchedulerName: schedulerName,
            Job: new JobKey(rs.GetString(1), rs.GetString(0)),
            LastFiredAtUtc: GetDateTimeFromDbValue(rs.GetValue(3)) ?? DateTimeOffset.MinValue,
            // Not GetInt32: Oracle hands back a decimal for a NUMBER column.
            LastResult: (JobRunResult) Convert.ToInt32(rs.GetValue(4), CultureInfo.InvariantCulture))
        {
            FirstFiredAtUtc = GetDateTimeFromDbValue(rs.GetValue(2)) ?? DateTimeOffset.MinValue,
            LastDuration = TimeSpan.FromTicks(Convert.ToInt64(rs.GetValue(5), CultureInfo.InvariantCulture)),
            LastSchedulerInstanceId = rs.GetString(6),
            LastEntryId = ReadOptionalString(rs, 7),
            LastSummary = ReadOptionalString(rs, 8),
            LastSucceededAtUtc = GetDateTimeFromDbValue(rs.GetValue(9)),
            LastFailedAtUtc = GetDateTimeFromDbValue(rs.GetValue(10)),
            LastFailureMessage = ReadOptionalString(rs, 11),
            ConsecutiveFailures = Convert.ToInt32(rs.GetValue(12), CultureInfo.InvariantCulture),
            RunCount = Convert.ToInt64(rs.GetValue(13), CultureInfo.InvariantCulture),
            FailureCount = Convert.ToInt64(rs.GetValue(14), CultureInfo.InvariantCulture)
        };
    }

    /// <summary>A text column's value, or <see langword="null" /> when it is null or not in the result set.</summary>
    private static string? ReadOptionalString(DbDataReader rs, int ordinal)
    {
        return ordinal < 0 || rs.IsDBNull(ordinal) ? null : rs.GetString(ordinal);
    }

    /// <summary>
    /// A row's <c>RESULT</c>: <see langword="null" /> on a row a 4.3 node wrote, and for a value a newer
    /// node wrote that this one does not know, so that <see cref="ExecutionHistoryEntry.EffectiveResult" />
    /// reads it from <c>SUCCEEDED</c>.
    /// </summary>
    private static JobRunResult? ReadResult(DbDataReader rs, int ordinal)
    {
        if (ordinal < 0 || rs.IsDBNull(ordinal))
        {
            return null;
        }

        JobRunResult result = (JobRunResult) Convert.ToInt32(rs.GetValue(ordinal), CultureInfo.InvariantCulture);
        return Enum.IsDefined(result) ? result : null;
    }

    /// <summary>
    /// Where the 4.4 outcome columns stand in a history read, or <c>-1</c> for one it does not carry.
    /// </summary>
    /// <remarks>
    /// Found by name, once per read, as the acquisition's non-concurrency flag is: a statement written
    /// before 4.4 does not project them, and its rows still read, with no outcome.
    /// </remarks>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ExecutionHistoryOrdinals(int Result, int Summary, int Metrics, int Manual, int FireInstanceId, int InputTooLarge)
    {
        public static ExecutionHistoryOrdinals Of(DbDataReader rs) => new(
            OptionalOrdinal(rs, AdoConstants.ColumnResult),
            OptionalOrdinal(rs, AdoConstants.ColumnSummary),
            OptionalOrdinal(rs, AdoConstants.ColumnMetrics),
            OptionalOrdinal(rs, AdoConstants.ColumnManual),
            OptionalOrdinal(rs, AdoConstants.ColumnFireInstanceId),
            OptionalOrdinal(rs, AdoConstants.ColumnJobInputTooLarge));
    }

    private MisfireHistoryEntry ReadMisfireHistoryEntry(DbDataReader rs, string schedulerName)
    {
        // A trigger need not name a job, so both key halves are read before either is used.
        string? jobName = rs.IsDBNull(3) ? null : rs.GetString(3);
        string? jobGroup = rs.IsDBNull(4) ? null : rs.GetString(4);

        return new MisfireHistoryEntry(
            SchedulerName: schedulerName,
            SchedulerInstanceId: rs.GetString(0),
            TriggerGroup: rs.GetString(2),
            TriggerName: rs.GetString(1),
            JobKey: jobName is null || jobGroup is null ? null : new JobKey(jobName, jobGroup),
            MisfiredAtUtc: GetDateTimeFromDbValue(rs.GetValue(5)) ?? DateTimeOffset.MinValue,
            ScheduledFireTimeUtc: rs.IsDBNull(6) ? null : GetDateTimeFromDbValue(rs.GetValue(6)))
        {
            Reason = ReadMisfireReason(rs, 7)
        };
    }

    /// <summary>
    /// A row's reason: <see cref="MisfireReason.Missed" /> for one a 4.2 node wrote, which has none, and
    /// for a value a newer node wrote that this one does not know.
    /// </summary>
    private static MisfireReason ReadMisfireReason(DbDataReader rs, int ordinal)
    {
        if (rs.IsDBNull(ordinal))
        {
            return MisfireReason.Missed;
        }

        // Not GetInt32: Oracle hands back a decimal for a NUMBER column.
        MisfireReason reason = (MisfireReason) Convert.ToInt32(rs.GetValue(ordinal), CultureInfo.InvariantCulture);
        return Enum.IsDefined(reason) ? reason : MisfireReason.Missed;
    }
}
