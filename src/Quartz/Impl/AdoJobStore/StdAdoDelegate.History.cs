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
using System.Globalization;
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
    /// bytes: 1,000 characters is at most 4,000 bytes of UTF-8, so this length fits every dialect
    /// whatever the message is written in.
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
        AddCommandParameter(cmd, SqlParameters.ErrorMessage, Truncate(entry.ExceptionMessage));

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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

        predicateBuilder.Append(StdAdoConstants.SqlExecutionHistoryNotBefore);
        parameters.Add(new KeyValuePair<string, object?>(SqlParameters.HistoryCutoff, GetDbDateTimeValue(notBefore)));

        AppendNodePredicate(predicateBuilder, parameters, query.SchedulerInstanceId);
        AppendContainsPredicate(
            predicateBuilder,
            parameters,
            SqlParameters.HistoryJobContains,
            HistoryKeyExpression(AdoConstants.ColumnJobGroup, AdoConstants.ColumnJobName),
            query.JobContains);
        AppendContainsPredicate(
            predicateBuilder,
            parameters,
            SqlParameters.HistoryTriggerContains,
            HistoryKeyExpression(AdoConstants.ColumnTriggerGroup, AdoConstants.ColumnTriggerName),
            query.TriggerContains);

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

            (items, hasMore) = await ReadPage(
                cmd, query, reader => ReadExecutionHistoryEntry(reader, schedulerName), cancellationToken).ConfigureAwait(false);
        }

        int? totalCount = null;
        if (query.IncludeTotalCount)
        {
            totalCount = await CountHistory(
                conn, StdAdoConstants.SqlCountExecutionHistory + predicate, schedulerName, parameters, cancellationToken).ConfigureAwait(false);
        }

        return new PagedResult<ExecutionHistoryEntry>(items, hasMore, totalCount);
    }

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

    // ---------------------------------------------------------------------------------------------
    // Building the two reads
    // ---------------------------------------------------------------------------------------------

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

    private ExecutionHistoryEntry ReadExecutionHistoryEntry(DbDataReader rs, string schedulerName)
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
            ExceptionMessage: rs.IsDBNull(8) ? null : rs.GetString(8));
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
            ScheduledFireTimeUtc: rs.IsDBNull(6) ? null : GetDateTimeFromDbValue(rs.GetValue(6)));
    }

    /// <summary>
    /// Cuts an exception message to what the column holds, so that a long one is recorded short rather
    /// than not at all.
    /// </summary>
    private static string? Truncate(string? message)
    {
        if (message is null || message.Length <= MaxErrorMessageLength)
        {
            return message;
        }

        return message[..MaxErrorMessageLength];
    }
}
