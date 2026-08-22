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

using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore;

public partial class StdAdoDelegate
{
    /// <summary>
    /// Appends the dialect's paging clause to a statement that already carries its ORDER BY.
    /// </summary>
    /// <remarks>
    /// The default is the ANSI form, understood by SQL Server 2012+, Oracle 12c+, PostgreSQL and
    /// Firebird 3+. Both bounds are bound as parameters, so one statement text serves every page.
    /// A delegate overriding this must override <see cref="AddPagingParameters" /> as well when its
    /// clause names the two parameters in the other order — providers that bind positionally take
    /// parameters in the order the statement mentions them.
    /// </remarks>
    /// <param name="sql">The statement to page.</param>
    /// <param name="takeLimited">
    /// Whether the page has an upper bound. When false the caller only wants to skip, and the clause
    /// must not limit the row count.
    /// </param>
    protected virtual string ApplyPaging(string sql, bool takeLimited)
    {
        return takeLimited
            ? sql + " OFFSET @pageSkip ROWS FETCH NEXT @pageTake ROWS ONLY"
            : sql + " OFFSET @pageSkip ROWS";
    }

    /// <summary>
    /// Binds the parameters of the clause <see cref="ApplyPaging" /> appended.
    /// </summary>
    /// <param name="cmd">The command to bind.</param>
    /// <param name="skip">Rows to skip.</param>
    /// <param name="take">
    /// Rows to read: one more than the page size, so that the extra row tells the caller whether
    /// anything follows the page.
    /// </param>
    /// <param name="takeLimited">Whether the page has an upper bound.</param>
    protected virtual void AddPagingParameters(DbCommand cmd, int skip, int take, bool takeLimited)
    {
        AddCommandParameter(cmd, "pageSkip", skip);
        if (takeLimited)
        {
            AddCommandParameter(cmd, "pageTake", take);
        }
    }

    /// <summary>
    /// Whether the query asks for anything but the whole result set.
    /// </summary>
    private static bool IsPaged(PagedQuery query) => query.Skip > 0 || query.Take != int.MaxValue;

    private string BuildPagedSql(string sql, PagedQuery query)
    {
        return IsPaged(query) ? ApplyPaging(sql, query.Take != int.MaxValue) : sql;
    }

    private void BindPaging(DbCommand cmd, PagedQuery query)
    {
        if (IsPaged(query))
        {
            bool takeLimited = query.Take != int.MaxValue;

            // Read one row past the page: its presence is what HasMore reports, and it costs one row
            // rather than a second query. When the take is unbounded the value is never bound, so any
            // value serves; int.MaxValue avoids overflowing the increment.
            AddPagingParameters(cmd, query.Skip, takeLimited ? query.Take + 1 : int.MaxValue, takeLimited);
        }
    }

    private static async ValueTask<(List<T> Items, bool HasMore)> ReadPage<T>(
        DbCommand cmd,
        PagedQuery query,
        Func<DbDataReader, T> readItem,
        CancellationToken cancellationToken)
    {
        List<T> items = [];
        bool hasMore = false;

        using DbDataReader rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (items.Count == query.Take)
            {
                hasMore = true;
                break;
            }

            items.Add(readItem(rs));
        }

        return (items, hasMore);
    }

    private static async ValueTask<int> SelectCount(DbCommand cmd, CancellationToken cancellationToken)
    {
        object? result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Whether the query is the count idiom — Take of zero with the total count asked for — in
    /// which case the page select is skipped entirely and only the count statement runs.
    /// </summary>
    private static bool IsCountOnly(PagedQuery query) => query.Take == 0 && query.IncludeTotalCount;

    /// <summary>
    /// Builds the count idiom's result: no items, and HasMore derived from the count the way the
    /// page select would have reported it.
    /// </summary>
    private static PagedResult<T> CountOnlyResult<T>(PagedQuery query, int totalCount)
    {
        return new PagedResult<T>([], HasMore: totalCount > query.Skip, totalCount);
    }

    /// <summary>
    /// Folds duplicate keys away, keeping the order they were asked for. A key-set predicate is a
    /// disjunction and cannot tell a repeated key from a single one, so a duplicate would otherwise
    /// silently disappear anyway — this makes that explicit and keeps the requested order recoverable.
    /// </summary>
    private static List<T> Deduplicate<T>(IReadOnlyCollection<T> keys) where T : notnull
    {
        HashSet<T> seen = new(keys.Count);
        List<T> distinct = new(keys.Count);
        foreach (T key in keys)
        {
            if (seen.Add(key))
            {
                distinct.Add(key);
            }
        }

        return distinct;
    }

    /// <summary>
    /// Puts a batch read's results back into the order the keys were asked in, which is the order the
    /// in-memory job store returns them in and is not something a database result set guarantees.
    /// </summary>
    private static void SortByRequestedOrder<TItem, TKey>(List<TItem> items, List<TKey> requested, Func<TItem, TKey> keySelector)
        where TKey : notnull
    {
        if (items.Count < 2)
        {
            return;
        }

        Dictionary<TKey, int> orderByKey = new(requested.Count);
        for (int i = 0; i < requested.Count; i++)
        {
            orderByKey[requested[i]] = i;
        }

        items.Sort((left, right) =>
        {
            int leftIndex = orderByKey.TryGetValue(keySelector(left), out int index) ? index : int.MaxValue;
            int rightIndex = orderByKey.TryGetValue(keySelector(right), out index) ? index : int.MaxValue;
            return leftIndex.CompareTo(rightIndex);
        });
    }

    /// <summary>
    /// Translates a group or name matcher into a predicate and the value to bind for it: equality
    /// matchers compare with '=', everything else with LIKE over the pattern the matcher translates to.
    /// </summary>
    private (string Predicate, string? Parameter) BuildMatcherPredicate<T>(
        StringMatcher<T>? matcher,
        string equalsPredicate,
        string likePredicate) where T : Key<T>
    {
        if (matcher is null)
        {
            return ("", null);
        }

        return IsMatcherEquals(matcher)
            ? (equalsPredicate, ToSqlEqualsClause(matcher))
            : (likePredicate, ToSqlLikeClause(matcher));
    }

    /// <summary>
    /// The <see cref="CalendarNameMatcher" /> form of <see cref="BuildMatcherPredicate{T}" />: a
    /// calendar has no key type, so its matcher is not a <see cref="StringMatcher{TKey}" />, but the
    /// equality-versus-LIKE decision and the wildcard escaping are the same.
    /// </summary>
    private (string Predicate, string? Parameter) BuildMatcherPredicate(
        CalendarNameMatcher? matcher,
        string equalsPredicate,
        string likePredicate)
    {
        if (matcher is null)
        {
            return ("", null);
        }

        return matcher.CompareWithOperator.Equals(StringOperator.Equality)
            ? (equalsPredicate, matcher.CompareToValue)
            : (likePredicate, ToSqlLikeClause(matcher.CompareWithOperator, matcher.CompareToValue));
    }

    /// <inheritdoc />
    public virtual async ValueTask<PagedResult<JobHeader>> SelectJobHeaders(
        ConnectionAndTransactionHolder conn,
        JobQuery query,
        CancellationToken cancellationToken = default)
    {
        (string groupPredicate, string? groupParameter) = BuildMatcherPredicate(query.Group, StdAdoConstants.SqlJobGroupEqualsPredicate, StdAdoConstants.SqlJobGroupLikePredicate);
        (string namePredicate, string? nameParameter) = BuildMatcherPredicate(query.Name, StdAdoConstants.SqlJobNameEqualsPredicate, StdAdoConstants.SqlJobNameLikePredicate);
        string predicate = groupPredicate + namePredicate;

        if (IsCountOnly(query))
        {
            using DbCommand countCmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlCountJobHeaders + predicate));
            AddCommandParameter(countCmd, "schedulerName", schedulerName);
            BindJobHeaderFilters(countCmd, groupParameter, nameParameter);

            return CountOnlyResult<JobHeader>(query, await SelectCount(countCmd, cancellationToken).ConfigureAwait(false));
        }

        List<JobHeader> items;
        bool hasMore;

        using (DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(BuildPagedSql(StdAdoConstants.SqlSelectJobHeaders + predicate + StdAdoConstants.SqlOrderByJobGroupAndName, query))))
        {
            AddCommandParameter(cmd, "schedulerName", schedulerName);
            BindJobHeaderFilters(cmd, groupParameter, nameParameter);
            BindPaging(cmd, query);

            (items, hasMore) = await ReadPage(cmd, query, ReadJobHeader, cancellationToken).ConfigureAwait(false);
        }

        int? totalCount = null;
        if (query.IncludeTotalCount)
        {
            using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlCountJobHeaders + predicate));
            AddCommandParameter(cmd, "schedulerName", schedulerName);
            BindJobHeaderFilters(cmd, groupParameter, nameParameter);

            totalCount = await SelectCount(cmd, cancellationToken).ConfigureAwait(false);
        }

        return new PagedResult<JobHeader>(items, hasMore, totalCount);
    }

    /// <summary>
    /// Binds whichever of the job listing's optional filters are in play, in the order the statement
    /// names them: providers that adapt named parameters positionally depend on that order.
    /// </summary>
    private void BindJobHeaderFilters(DbCommand cmd, string? groupParameter, string? nameParameter)
    {
        if (groupParameter is not null)
        {
            AddCommandParameter(cmd, "jobGroup", groupParameter);
        }

        if (nameParameter is not null)
        {
            AddCommandParameter(cmd, "jobName", nameParameter);
        }
    }

    private JobHeader ReadJobHeader(DbDataReader rs)
    {
        return new JobHeader(
            new JobKey(rs.GetString(0), rs.GetString(1)),
            rs.IsDBNull(2) ? null : rs.GetString(2),
            rs.GetString(3),
            GetBooleanFromDbValue(rs.GetValue(4)),
            GetBooleanFromDbValue(rs.GetValue(5)),
            GetBooleanFromDbValue(rs.GetValue(6)),
            GetBooleanFromDbValue(rs.GetValue(7)));
    }

    /// <inheritdoc />
    public virtual async ValueTask<PagedResult<TriggerHeader>> SelectTriggerHeaders(
        ConnectionAndTransactionHolder conn,
        TriggerQuery query,
        CancellationToken cancellationToken = default)
    {
        StringBuilder predicateBuilder = new();
        List<KeyValuePair<string, object?>> parameters = [];

        (string groupPredicate, string? groupParameter) = BuildMatcherPredicate(query.Group, StdAdoConstants.SqlTriggerGroupEqualsPredicate, StdAdoConstants.SqlTriggerGroupLikePredicate);
        predicateBuilder.Append(groupPredicate);
        if (groupParameter is not null)
        {
            parameters.Add(new KeyValuePair<string, object?>("triggerGroup", groupParameter));
        }

        (string namePredicate, string? nameParameter) = BuildMatcherPredicate(query.Name, StdAdoConstants.SqlTriggerNameEqualsPredicate, StdAdoConstants.SqlTriggerNameLikePredicate);
        predicateBuilder.Append(namePredicate);
        if (nameParameter is not null)
        {
            parameters.Add(new KeyValuePair<string, object?>("triggerName", nameParameter));
        }

        if (query.Job is not null)
        {
            predicateBuilder.Append(StdAdoConstants.SqlTriggerJobPredicate);
            parameters.Add(new KeyValuePair<string, object?>("jobName", query.Job.Name));
            parameters.Add(new KeyValuePair<string, object?>("jobGroup", query.Job.Group));
        }

        if (query.CalendarName is not null)
        {
            predicateBuilder.Append(StdAdoConstants.SqlTriggerCalendarPredicate);
            parameters.Add(new KeyValuePair<string, object?>("calendarName", query.CalendarName));
        }

        if (query.State is not null)
        {
            TriggerStateFilter filter = TriggerStateMapping.ToFilter(query.State.Value);
            predicateBuilder.Append(filter.Negated ? StdAdoConstants.SqlTriggerStateNotInPredicateStart : StdAdoConstants.SqlTriggerStateInPredicateStart);
            for (int i = 0; i < filter.States.Length; i++)
            {
                if (i > 0)
                {
                    predicateBuilder.Append(", ");
                }

                // Single digit, so no state parameter name is a prefix of another one.
                string parameterName = "state" + i.ToString(CultureInfo.InvariantCulture);
                predicateBuilder.Append('@').Append(parameterName);
                parameters.Add(new KeyValuePair<string, object?>(parameterName, filter.States[i]));
            }

            predicateBuilder.Append(')');

            // Executing is not a stored state, so it cannot be part of the list above; it has to be
            // established against FIRED_TRIGGERS. Requiring its absence for the states executing outranks
            // is what keeps this filter in step with what ReadTriggerHeader will report.
            if (filter.Executing is not null)
            {
                predicateBuilder.Append(filter.Executing.Value ? StdAdoConstants.SqlTriggerExecutingPredicate : StdAdoConstants.SqlTriggerNotExecutingPredicate);
            }
        }

        string predicate = predicateBuilder.ToString();

        if (IsCountOnly(query))
        {
            using DbCommand countCmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlCountTriggerHeaders + predicate));
            AddCommandParameter(countCmd, "schedulerName", schedulerName);
            foreach (KeyValuePair<string, object?> parameter in parameters)
            {
                AddCommandParameter(countCmd, parameter.Key, parameter.Value);
            }

            return CountOnlyResult<TriggerHeader>(query, await SelectCount(countCmd, cancellationToken).ConfigureAwait(false));
        }

        List<TriggerHeader> items;
        bool hasMore;

        using (DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(BuildPagedSql(StdAdoConstants.SqlSelectTriggerHeaders + predicate + StdAdoConstants.SqlOrderByTriggerGroupAndName, query))))
        {
            AddCommandParameter(cmd, "schedulerName", schedulerName);
            foreach (KeyValuePair<string, object?> parameter in parameters)
            {
                AddCommandParameter(cmd, parameter.Key, parameter.Value);
            }

            BindPaging(cmd, query);

            (items, hasMore) = await ReadPage(cmd, query, ReadTriggerHeader, cancellationToken).ConfigureAwait(false);
        }

        int? totalCount = null;
        if (query.IncludeTotalCount)
        {
            using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlCountTriggerHeaders + predicate));
            AddCommandParameter(cmd, "schedulerName", schedulerName);
            foreach (KeyValuePair<string, object?> parameter in parameters)
            {
                AddCommandParameter(cmd, parameter.Key, parameter.Value);
            }

            totalCount = await SelectCount(cmd, cancellationToken).ConfigureAwait(false);
        }

        return new PagedResult<TriggerHeader>(items, hasMore, totalCount);
    }

    /// <summary>
    /// Reads one trigger listing row.
    /// </summary>
    /// <remarks>
    /// The last column is the executing flag the statement computes per row, so a listing reports the
    /// same state <c>AdoJobStoreBase.GetTriggerState</c> would for the same trigger. It costs one
    /// correlated subquery per row within the single listing statement rather than a query per trigger.
    /// </remarks>
    private TriggerHeader ReadTriggerHeader(DbDataReader rs)
    {
        return new TriggerHeader(
            new TriggerKey(rs.GetString(0), rs.GetString(1)),
            new JobKey(rs.GetString(2), rs.GetString(3)),
            rs.IsDBNull(4) ? null : rs.GetString(4),
            rs.GetString(5),
            TriggerStateMapping.ToTriggerState(rs.GetString(6), Convert.ToInt32(rs.GetValue(14), CultureInfo.InvariantCulture) != 0),
            GetDateTimeFromDbValue(rs.GetValue(7)) ?? DateTimeOffset.MinValue,
            GetDateTimeFromDbValue(rs.GetValue(8)),
            GetDateTimeFromDbValue(rs.GetValue(9)),
            GetDateTimeFromDbValue(rs.GetValue(10)),
            rs.IsDBNull(11) ? null : rs.GetString(11),
            Convert.ToInt32(rs.GetValue(12), CultureInfo.InvariantCulture),
            rs.IsDBNull(13) ? null : rs.GetString(13));
    }

    /// <inheritdoc />
    public virtual async ValueTask<PagedResult<JobGroup>> SelectJobGroups(
        ConnectionAndTransactionHolder conn,
        JobGroupQuery query,
        CancellationToken cancellationToken = default)
    {
        // The ADO store does not persist job group pause state, so every group reads as not paused and
        // a listing restricted to paused groups is necessarily empty.
        if (query.Paused == true)
        {
            return new PagedResult<JobGroup>([], false, query.IncludeTotalCount ? 0 : null);
        }

        string predicate = query.Name is null ? "" : StdAdoConstants.SqlJobGroupNamePredicate;

        if (IsCountOnly(query))
        {
            using DbCommand countCmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlCountJobGroups + predicate));
            AddCommandParameter(countCmd, "schedulerName", schedulerName);
            BindGroupName(countCmd, query.Name);

            return CountOnlyResult<JobGroup>(query, await SelectCount(countCmd, cancellationToken).ConfigureAwait(false));
        }

        List<JobGroup> items;
        bool hasMore;

        using (DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(BuildPagedSql(StdAdoConstants.SqlSelectJobGroups + predicate + StdAdoConstants.SqlOrderByJobGroup, query))))
        {
            AddCommandParameter(cmd, "schedulerName", schedulerName);
            BindGroupName(cmd, query.Name);
            BindPaging(cmd, query);

            (items, hasMore) = await ReadPage(cmd, query, static rs => new JobGroup(rs.GetString(0), Paused: false), cancellationToken).ConfigureAwait(false);
        }

        int? totalCount = null;
        if (query.IncludeTotalCount)
        {
            using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlCountJobGroups + predicate));
            AddCommandParameter(cmd, "schedulerName", schedulerName);
            BindGroupName(cmd, query.Name);
            totalCount = await SelectCount(cmd, cancellationToken).ConfigureAwait(false);
        }

        return new PagedResult<JobGroup>(items, hasMore, totalCount);
    }

    /// <inheritdoc />
    public virtual async ValueTask<PagedResult<TriggerGroup>> SelectTriggerGroups(
        ConnectionAndTransactionHolder conn,
        TriggerGroupQuery query,
        CancellationToken cancellationToken = default)
    {
        string sql;
        string countSql;
        string orderBy;
        string predicate;
        if (query.Paused == true)
        {
            // Read from PAUSED_TRIGGER_GRPS rather than TRIGGERS, so a group that is paused but has no
            // triggers is still reported — same set the paused trigger group listing has always had.
            sql = StdAdoConstants.SqlSelectPausedTriggerGroups;
            countSql = StdAdoConstants.SqlCountPausedTriggerGroups;
            orderBy = StdAdoConstants.SqlOrderByTriggerGroup;
            predicate = query.Name is null ? "" : StdAdoConstants.SqlTriggerGroupNamePredicate;
        }
        else if (query.Paused == false)
        {
            sql = StdAdoConstants.SqlSelectUnpausedTriggerGroups;
            countSql = StdAdoConstants.SqlCountUnpausedTriggerGroups;
            orderBy = StdAdoConstants.SqlOrderByAliasedTriggerGroup;
            predicate = query.Name is null ? "" : StdAdoConstants.SqlAliasedTriggerGroupNamePredicate;
        }
        else
        {
            sql = StdAdoConstants.SqlSelectTriggerGroupsWithPausedFlag;
            countSql = StdAdoConstants.SqlCountTriggerGroups;
            orderBy = StdAdoConstants.SqlOrderByAliasedTriggerGroup;
            predicate = query.Name is null ? "" : StdAdoConstants.SqlAliasedTriggerGroupNamePredicate;
        }

        bool? paused = query.Paused;

        if (IsCountOnly(query))
        {
            using DbCommand countCmd = PrepareCommand(conn, ReplaceTablePrefix(countSql + predicate));
            AddCommandParameter(countCmd, "schedulerName", schedulerName);
            BindGroupName(countCmd, query.Name);

            return CountOnlyResult<TriggerGroup>(query, await SelectCount(countCmd, cancellationToken).ConfigureAwait(false));
        }

        List<TriggerGroup> items;
        bool hasMore;

        using (DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(BuildPagedSql(sql + predicate + orderBy, query))))
        {
            AddCommandParameter(cmd, "schedulerName", schedulerName);
            BindGroupName(cmd, query.Name);
            BindPaging(cmd, query);

            (items, hasMore) = await ReadPage(
                cmd,
                query,
                rs => new TriggerGroup(rs.GetString(0), paused ?? (Convert.ToInt32(rs.GetValue(1), CultureInfo.InvariantCulture) != 0)),
                cancellationToken).ConfigureAwait(false);
        }

        int? totalCount = null;
        if (query.IncludeTotalCount)
        {
            using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(countSql + predicate));
            AddCommandParameter(cmd, "schedulerName", schedulerName);
            BindGroupName(cmd, query.Name);
            totalCount = await SelectCount(cmd, cancellationToken).ConfigureAwait(false);
        }

        return new PagedResult<TriggerGroup>(items, hasMore, totalCount);
    }

    /// <summary>
    /// Binds a group listing's exact-name filter when it has one.
    /// </summary>
    private void BindGroupName(DbCommand cmd, string? groupName)
    {
        if (groupName is not null)
        {
            AddCommandParameter(cmd, "groupName", groupName);
        }
    }

    /// <inheritdoc />
    public virtual async ValueTask<PagedResult<string>> SelectCalendarNames(
        ConnectionAndTransactionHolder conn,
        CalendarQuery query,
        CancellationToken cancellationToken = default)
    {
        (string namePredicate, string? nameParameter) = BuildMatcherPredicate(query.Name, StdAdoConstants.SqlCalendarNameEqualsPredicate, StdAdoConstants.SqlCalendarNameLikePredicate);

        if (IsCountOnly(query))
        {
            using DbCommand countCmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlCountCalendarNames + namePredicate));
            AddCommandParameter(countCmd, "schedulerName", schedulerName);
            BindCalendarNameFilter(countCmd, nameParameter);

            return CountOnlyResult<string>(query, await SelectCount(countCmd, cancellationToken).ConfigureAwait(false));
        }

        List<string> items;
        bool hasMore;

        using (DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(BuildPagedSql(StdAdoConstants.SqlSelectCalendarNames + namePredicate + StdAdoConstants.SqlOrderByCalendarName, query))))
        {
            AddCommandParameter(cmd, "schedulerName", schedulerName);
            BindCalendarNameFilter(cmd, nameParameter);
            BindPaging(cmd, query);

            (items, hasMore) = await ReadPage(cmd, query, static rs => rs.GetString(0), cancellationToken).ConfigureAwait(false);
        }

        int? totalCount = null;
        if (query.IncludeTotalCount)
        {
            using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlCountCalendarNames + namePredicate));
            AddCommandParameter(cmd, "schedulerName", schedulerName);
            BindCalendarNameFilter(cmd, nameParameter);
            totalCount = await SelectCount(cmd, cancellationToken).ConfigureAwait(false);
        }

        return new PagedResult<string>(items, hasMore, totalCount);
    }

    /// <summary>
    /// Binds the calendar listing's optional name filter, after the scheduler name the statement
    /// already carries: providers that adapt named parameters positionally depend on that order.
    /// </summary>
    private void BindCalendarNameFilter(DbCommand cmd, string? nameParameter)
    {
        if (nameParameter is not null)
        {
            AddCommandParameter(cmd, "calendarName", nameParameter);
        }
    }

    /// <inheritdoc />
    public virtual async ValueTask<PagedResult<FireInstance>> SelectFireInstances(
        ConnectionAndTransactionHolder conn,
        FireInstanceQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        StringBuilder predicateBuilder = new();
        List<KeyValuePair<string, object?>> parameters = [];

        (string groupPredicate, string? groupParameter) = BuildMatcherPredicate(
            query.TriggerGroup,
            StdAdoConstants.SqlFireInstanceTriggerGroupEqualsPredicate,
            StdAdoConstants.SqlFireInstanceTriggerGroupLikePredicate);
        predicateBuilder.Append(groupPredicate);
        if (groupParameter is not null)
        {
            parameters.Add(new KeyValuePair<string, object?>("triggerGroup", groupParameter));
        }

        (string namePredicate, string? nameParameter) = BuildMatcherPredicate(
            query.TriggerName,
            StdAdoConstants.SqlFireInstanceTriggerNameEqualsPredicate,
            StdAdoConstants.SqlFireInstanceTriggerNameLikePredicate);
        predicateBuilder.Append(namePredicate);
        if (nameParameter is not null)
        {
            parameters.Add(new KeyValuePair<string, object?>("triggerName", nameParameter));
        }

        if (query.Job is not null)
        {
            predicateBuilder.Append(StdAdoConstants.SqlFireInstanceJobPredicate);
            parameters.Add(new KeyValuePair<string, object?>("jobName", query.Job.Name));
            parameters.Add(new KeyValuePair<string, object?>("jobGroup", query.Job.Group));
        }

        if (query.SchedulerInstanceId is not null)
        {
            predicateBuilder.Append(StdAdoConstants.SqlFireInstanceInstancePredicate);
            parameters.Add(new KeyValuePair<string, object?>("instanceName", query.SchedulerInstanceId));
        }

        if (query.State is not null)
        {
            // The stored state ACQUIRED is the whole of the distinction — a row in any other state
            // belongs to an execution the store has started — so both directions of the filter compare
            // against the same value, and the filter cannot drift from what ReadFireInstance reports.
            predicateBuilder.Append(query.State == FireInstanceState.Acquired
                ? StdAdoConstants.SqlFireInstanceStateEqualsPredicate
                : StdAdoConstants.SqlFireInstanceStateNotEqualsPredicate);
            parameters.Add(new KeyValuePair<string, object?>("entryState", StoredTriggerState.Acquired.ToStoredValue()));
        }

        string predicate = predicateBuilder.ToString();

        if (IsCountOnly(query))
        {
            using DbCommand countCmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlCountFireInstances + predicate));
            BindFireInstanceFilters(countCmd, parameters);

            return CountOnlyResult<FireInstance>(query, await SelectCount(countCmd, cancellationToken).ConfigureAwait(false));
        }

        List<FireInstance> items;
        bool hasMore;

        using (DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(BuildPagedSql(StdAdoConstants.SqlSelectFireInstances + predicate + StdAdoConstants.SqlOrderByFireInstance, query))))
        {
            BindFireInstanceFilters(cmd, parameters);
            BindPaging(cmd, query);

            (items, hasMore) = await ReadPage(cmd, query, ReadFireInstance, cancellationToken).ConfigureAwait(false);
        }

        int? totalCount = null;
        if (query.IncludeTotalCount)
        {
            using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlCountFireInstances + predicate));
            BindFireInstanceFilters(cmd, parameters);
            totalCount = await SelectCount(cmd, cancellationToken).ConfigureAwait(false);
        }

        return new PagedResult<FireInstance>(items, hasMore, totalCount);
    }

    /// <summary>
    /// Binds the scheduler name every fire-instance statement carries, then whichever filters are in
    /// play, in the order the statement names them: providers that adapt named parameters positionally
    /// depend on that order.
    /// </summary>
    private void BindFireInstanceFilters(DbCommand cmd, List<KeyValuePair<string, object?>> parameters)
    {
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        foreach (KeyValuePair<string, object?> parameter in parameters)
        {
            AddCommandParameter(cmd, parameter.Key, parameter.Value);
        }
    }

    private FireInstance ReadFireInstance(DbDataReader rs)
    {
        StoredTriggerState state = StoredTriggerStates.FromStoredValue(rs.GetString(6));

        // A reservation is written before the job is loaded, so its job columns hold nothing yet — the
        // same rule ReadFiredTriggerRecord applies, and the reason FireInstance.JobKey is nullable.
        bool acquired = state == StoredTriggerState.Acquired;

        return new FireInstance(
            rs.GetString(0),
            new TriggerKey(rs.GetString(1), rs.GetString(2)),
            acquired ? null : new JobKey(rs.GetString(3), rs.GetString(4)),
            rs.GetString(5),
            acquired ? FireInstanceState.Acquired : FireInstanceState.Executing,
            GetDateTimeFromDbValue(rs.GetValue(7)) ?? DateTimeOffset.MinValue,
            GetDateTimeFromDbValue(rs.GetValue(8)),
            rs.IsDBNull(9) ? null : rs.GetString(9));
    }
}
