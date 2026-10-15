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

using Quartz.Extensibility;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// The reads a round makes once for all of its triggers, and the deletion a completion makes as one
/// batch, in place of the per-trigger members they answer for (#4066).
/// </summary>
/// <remarks>
/// <para>
/// A round of five triggers was about eleven round trips on PostgreSQL, and a trigger whose job disallows
/// concurrent execution added two to four of its own: the executing-jobs check, asked once by acquisition
/// and again by the fire, and the one to three paused-group reads the fire made. The headers of the round
/// were read back although the round had just claimed them and had read their rows to build them. The
/// members here answer each of those questions once for the round, from the same rows, in the same
/// transaction and under the same lock, so what they answer is what the per-trigger members would have.
/// </para>
/// <para>
/// <b>Only for the delegates Quartz ships.</b> Each member here stands in for a public member a subclass
/// may have overridden — <c>SelectTriggers</c>, <c>IsJobCurrentlyExecuting</c>, <c>IsTriggerGroupPaused</c>,
/// <c>SelectTriggerHeader</c>, <c>DeleteTrigger</c> — and would bypass the override without a word. The
/// store asks them only of a delegate whose exact type is one of these, as <see cref="SupportsFireOnAcquire" />
/// answers for the round members, and asks a subclass the single-trigger members, as before. These are
/// internal for the same reason: they are not a contract, and a delegate of somebody's own keeps the
/// contract it has.
/// </para>
/// </remarks>
public partial class StdAdoDelegate
{
    /// <summary>
    /// The triggers of a round's candidates, each with the discriminator its row was stored under: what
    /// <see cref="SelectTriggers" /> reads, keeping the one column it read and dropped. A round's fires
    /// compare the trigger's type with it when they write, which is what <see cref="SelectStoredTriggerHeaders" />
    /// went back to the database for.
    /// </summary>
    /// <remarks>
    /// The discriminator is the row's at the time of this read. It stands for the row at the time the
    /// round claims and fires it only because the round holds <see cref="SchedulerLock.TriggerAccess" />
    /// from this read to its commit, and every reschedule takes that lock: nothing can replace the row
    /// in between. A round that reads without the lock has to read the header back after its claim, as
    /// the store did before #4066.
    /// </remarks>
    /// <param name="conn">The round's transaction.</param>
    /// <param name="triggerKeys">The candidates' keys. May be empty.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// The triggers whose rows are there, in the order asked; a key with no row is absent, as it is from
    /// <see cref="SelectTriggers" />.
    /// </returns>
    internal async ValueTask<List<StoredTriggerRead>> SelectStoredTriggers(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        if (triggerKeys.Count == 0)
        {
            return [];
        }

        if (triggerKeys.Count == 1)
        {
            // One key is one statement either way, as SelectTriggers says.
            (IOperableTrigger? only, string? storedType) = await SelectTriggerWithStoredType(conn, triggerKeys.First(), cancellationToken).ConfigureAwait(false);
            return only is null ? [] : [new StoredTriggerRead(only, storedType!)];
        }

        return await SelectStoredTriggersByKeys(conn, Deduplicate(triggerKeys), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Which of the given jobs has a firing <c>EXECUTING</c> anywhere: the set form of
    /// <see cref="IsJobCurrentlyExecuting" />, asked once per acquisition pass for every job that
    /// disallows concurrent execution among its candidates.
    /// </summary>
    /// <remarks>
    /// One read answers for every candidate only because the round holds
    /// <see cref="SchedulerLock.TriggerAccess" />: every fire, recovery and hand-back that writes an
    /// <c>EXECUTING</c> row takes that lock, so nothing can start a firing while the pass runs, and a job
    /// not in the answer is one the pass may take a trigger of. A job in it may have finished in the
    /// meantime, exactly as it may have between the per-candidate ask and the claim before. A round that
    /// does not hold the lock has no such answer, and asks per candidate.
    /// </remarks>
    /// <param name="conn">The round's transaction.</param>
    /// <param name="jobKeys">The jobs to ask about. May be empty, and may repeat a key.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal async ValueTask<HashSet<JobKey>> SelectExecutingJobs(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<JobKey> jobKeys,
        CancellationToken cancellationToken = default)
    {
        HashSet<JobKey> executing = [];
        if (jobKeys.Count == 0)
        {
            return executing;
        }

        List<JobKey> keys = Deduplicate(jobKeys);
        object executingState = StoredTriggerStates.ToStoredValue(StoredTriggerState.Executing);

        for (int offset = 0; offset < keys.Count; offset += AdoUtil.MaxJobKeysPerPredicate)
        {
            int length = Math.Min(AdoUtil.MaxJobKeysPerPredicate, keys.Count - offset);
            int paddedCount = AdoUtil.RoundUpJobKeyCount(length);

            using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectExecutingJobsPrefix + AdoUtil.BuildJobKeyPredicate(paddedCount)));

            // Statement order, for providers that bind positionally.
            AddCommandParameter(cmd, SqlParameters.SchedulerName, schedulerName);
            AddCommandParameter(cmd, SqlParameters.ExecutingState, executingState);
            for (int i = 0; i < paddedCount; i++)
            {
                // Padded up to the bucket size by repeating the chunk's last key; the predicate is a
                // disjunction, so a repeated term cannot change which rows match.
                JobKey key = keys[offset + Math.Min(i, length - 1)];
                AddCommandParameter(cmd, AdoUtil.JobKeyNameParameter(i), key.Name);
                AddCommandParameter(cmd, AdoUtil.JobKeyGroupParameter(i), key.Group);
            }

            using DbDataReader rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                executing.Add(new JobKey(rs.GetString(0), rs.GetString(1)));
            }
        }

        return executing;
    }

    /// <summary>
    /// Every paused trigger group — the all-groups marker among them — and every paused job group, in one
    /// read: what <see cref="IsTriggerGroupPaused" /> and <see cref="IsJobGroupPaused" /> answer, for every
    /// group at once.
    /// </summary>
    /// <remarks>
    /// One read serves every fire of the round only because the round holds
    /// <see cref="SchedulerLock.TriggerAccess" />: every pause and resume takes that lock, so the two
    /// tables cannot change between the read and the round's last fire. A round without the lock has to
    /// ask per fire, as the store did before #4066.
    /// </remarks>
    /// <param name="conn">The round's transaction.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal async ValueTask<PausedGroups> SelectPausedGroups(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectPausedGroups));

        // Statement order.
        AddCommandParameter(cmd, SqlParameters.SchedulerName, schedulerName);
        AddCommandParameter(cmd, SqlParameters.SchedulerNameAgain, schedulerName);

        PausedGroups paused = new();
        using DbDataReader rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            string group = rs.GetString(0);
            if (rs.GetString(1) == "T")
            {
                paused.TriggerGroups.Add(group);
            }
            else
            {
                paused.JobGroups.Add(group);
            }
        }

        return paused;
    }

    /// <summary>
    /// A trigger's header and whether anything awaits its firing, in one read: what
    /// <see cref="SelectTriggerHeader" /> and <see cref="SelectAwaitingContinuations" /> answer for a
    /// completion that has taken no lock, which escalates on the second and deletes on the first.
    /// </summary>
    /// <param name="conn">The completion's transaction.</param>
    /// <param name="triggerKey">The trigger completing.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal async ValueTask<TriggerCompletionProbe> SelectTriggerHeaderAndAwaiting(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectTriggerHeaderAndAwaiting));

        // Statement order: the header's half, then the awaiting half.
        AddCommandParameter(cmd, SqlParameters.SchedulerName, schedulerName);
        AddCommandParameter(cmd, SqlParameters.TriggerName, triggerKey.Name);
        AddCommandParameter(cmd, SqlParameters.TriggerGroup, triggerKey.Group);
        AddCommandParameter(cmd, SqlParameters.SchedulerNameAgain, schedulerName);
        AddCommandParameter(cmd, SqlParameters.State, StoredTriggerStates.ToStoredValue(StoredTriggerState.Awaiting));
        AddCommandParameter(cmd, SqlParameters.TriggerContinuesName, triggerKey.Name);
        AddCommandParameter(cmd, SqlParameters.TriggerContinuesGroup, triggerKey.Group);

        StoredTriggerHeader? header = null;
        bool anythingAwaiting = false;

        using DbDataReader rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            // Not GetInt32: Oracle hands back a decimal for a numeric literal.
            if (Convert.ToInt32(rs.GetValue(7), CultureInfo.InvariantCulture) == 0)
            {
                header = new StoredTriggerHeader(
                    triggerKey,
                    new JobKey(rs.GetString(4), rs.GetString(5)),
                    StoredTriggerStates.FromStoredValue(rs.GetString(2)),
                    GetDateTimeFromDbValue(rs.GetValue(3)),
                    rs.GetString(6));
            }
            else
            {
                anythingAwaiting = true;
            }
        }

        return new TriggerCompletionProbe(header, anythingAwaiting);
    }

    /// <summary>
    /// Deletes the trigger row a completion has just read the header of, the row of the type table its
    /// discriminator names, and the fired-trigger rows of its firings: what <see cref="DeleteTrigger" />
    /// and then <see cref="DeleteFiredTriggers(ConnectionAndTransactionHolder, FiredTriggerQuery, CancellationToken)" />
    /// delete, as one <see cref="DbBatch" /> where the round batches and as the same three statements
    /// where it does not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The caller knows the discriminator from the header it has just read, so the type table is named
    /// rather than tried in turn as <see cref="DeleteTriggerExtension" /> tries them. A discriminator
    /// whose table this delegate cannot name — a persistence delegate of somebody's own — takes the two
    /// members as before.
    /// </para>
    /// <para>
    /// <b>The row deleted is the row that was read, and no other.</b> A lock-free completion holds no
    /// <see cref="SchedulerLock.TriggerAccess" /> and has written nothing to the row before its header
    /// read, so a reschedule under the lock can replace the row between that read and this write. The
    /// trigger-row delete names the discriminator the header read (<see cref="StdAdoConstants.SqlDeleteTriggerOfType" />),
    /// so a replacement of another type is left alone — where a delete by key alone, with the type
    /// table's foreign key cascading, would have taken the replacement and its schedule with it — and the
    /// fired rows are swept only while no row of the key is there at all
    /// (<see cref="StdAdoConstants.SqlDeleteFiredTriggersOfDeletedTrigger" />), so a replacement keeps its
    /// fired rows, which may be another node's firing of it in flight. A row that was really deleted loses
    /// them, as before. A replacement of the same type is deleted as it always was, by this path and by
    /// the two members: the row's key and type are all a completion knows it by.
    /// </para>
    /// </remarks>
    /// <param name="conn">The transaction.</param>
    /// <param name="triggerKey">The trigger to delete.</param>
    /// <param name="storedTriggerType">The discriminator the trigger's row holds, as its header read it.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>Whether the trigger's row was still the one read, and was deleted.</returns>
    internal async ValueTask<bool> DeleteTriggerAndFiredRows(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        string storedTriggerType,
        CancellationToken cancellationToken = default)
    {
        if (TypeTableDeleteSql(storedTriggerType) is not { } typeTableDelete)
        {
            bool removed = await DeleteTrigger(conn, triggerKey, cancellationToken).ConfigureAwait(false) > 0;
            if (removed)
            {
                await DeleteFiredTriggers(conn, new FiredTriggerQuery { Trigger = triggerKey }, cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }

        List<SqlStatement> statements =
        [
            KeyedStatement(typeTableDelete, triggerKey),
            KeyedStatement(StdAdoConstants.SqlDeleteTriggerOfType, triggerKey, new SqlStatementParameter(SqlParameters.TriggerType, storedTriggerType)),
            KeyedStatement(StdAdoConstants.SqlDeleteFiredTriggersOfDeletedTrigger, triggerKey),
        ];

        if (!BatchesRounds(conn))
        {
            int triggerRowsDeleted = 0;
            for (int i = 0; i < statements.Count; i++)
            {
                SqlStatement statement = statements[i];
                using DbCommand cmd = PrepareCommand(conn, statement.Sql);
                foreach (SqlStatementParameter parameter in statement.Parameters)
                {
                    AddCommandParameter(cmd, parameter.Name, parameter.Value, parameter.DataType);
                }

                int rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                if (i == 1)
                {
                    triggerRowsDeleted = rows;
                }
            }

            return triggerRowsDeleted > 0;
        }

        using DbCommand parameterFactory = conn.Connection.CreateCommand();
        using DbBatch batch = CreateStatementBatch(conn, parameterFactory, statements, 0, statements.Count);
        int total = await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        // The trigger row's own count where the provider reports one per command. A provider that does
        // not reports -1, and then the total says: the type row goes only with the row it belongs to, and
        // the fired rows only once no row is there, so anything deleted at all means the row read was
        // deleted, or was already gone with its fired rows left behind by a reschedule to another key —
        // which this sweep takes, as the deletion of a row that is there would have.
        int triggerRows = batch.BatchCommands[1].RecordsAffected;
        return triggerRows >= 0 ? triggerRows > 0 : total > 0;
    }

    /// <summary>
    /// The statement that deletes a trigger's row from the type table its discriminator names, or
    /// <see langword="null" /> when the table is not one this delegate can name: a persistence delegate
    /// of somebody's own, or a discriminator nothing registered handles.
    /// </summary>
    /// <remarks>
    /// Exact types, not <c>is</c>: a subclass of a shipped persistence delegate may delete something more,
    /// and gets its own member called.
    /// </remarks>
    private string? TypeTableDeleteSql(string storedTriggerType)
    {
        ITriggerPersistenceDelegate? persistenceDelegate = FindTriggerPersistenceDelegate(storedTriggerType);
        if (persistenceDelegate is null)
        {
            return storedTriggerType == AdoConstants.TriggerTypeBlob ? StdAdoConstants.SqlDeleteBlobTrigger : null;
        }

        Type type = persistenceDelegate.GetType();
        if (type == typeof(SimpleTriggerPersistenceDelegate))
        {
            return StdAdoConstants.SqlDeleteSimpleTrigger;
        }

        if (type == typeof(CronTriggerPersistenceDelegate))
        {
            return StdAdoConstants.SqlDeleteCronTrigger;
        }

        if (type == typeof(CalendarIntervalTriggerPersistenceDelegate) || type == typeof(DailyTimeIntervalTriggerPersistenceDelegate))
        {
            return SimplePropertiesTriggerPersistenceDelegateBase.DeleteSimplePropsTrigger;
        }

        return null;
    }

    /// <summary>
    /// A statement over one trigger's key: the scheduler name, then the name and group, then whatever
    /// else the statement binds after them, in that order.
    /// </summary>
    private SqlStatement KeyedStatement(string sql, TriggerKey triggerKey, params SqlStatementParameter[] afterKey)
    {
        List<SqlStatementParameter> parameters = new(3 + afterKey.Length)
        {
            new SqlStatementParameter(SqlParameters.SchedulerName, schedulerName),
            new SqlStatementParameter(SqlParameters.TriggerName, triggerKey.Name),
            new SqlStatementParameter(SqlParameters.TriggerGroup, triggerKey.Group)
        };
        parameters.AddRange(afterKey);

        return new SqlStatement(ReplaceTablePrefix(sql), parameters);
    }
}

/// <summary>
/// A trigger read back with the discriminator its row was stored under.
/// </summary>
/// <param name="Trigger">The trigger, built from its row.</param>
/// <param name="TriggerType">The row's <c>TRIGGER_TYPE</c>, which a fire's write compares the trigger's current type with.</param>
internal sealed record StoredTriggerRead(IOperableTrigger Trigger, string TriggerType);

/// <summary>
/// The paused groups of one scheduler as one read found them, for a round to decide every fire by.
/// </summary>
/// <remarks>
/// Mutable on purpose: a fire that materializes a trigger group's row because everything is paused adds
/// the group here as well, so the next fire of the round reads what the next per-trigger read would have.
/// </remarks>
internal sealed class PausedGroups
{
    /// <summary>The paused trigger groups, the all-groups marker among them when everything is paused.</summary>
    public HashSet<string> TriggerGroups { get; } = new(StringComparer.Ordinal);

    /// <summary>The paused job groups.</summary>
    public HashSet<string> JobGroups { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// What a lock-free completion asks in one read before it writes: the trigger's header, if its row is
/// still there, and whether any trigger awaits its firing.
/// </summary>
/// <param name="Header">The trigger's header, or <see langword="null" /> when its row is gone.</param>
/// <param name="AnythingAwaiting">Whether a trigger is <c>AWAITING</c> this one's firing.</param>
internal readonly record struct TriggerCompletionProbe(StoredTriggerHeader? Header, bool AnythingAwaiting);
