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
using System.Text;

using Quartz.Extensibility;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// The claims and fire writes of an acquisition round that fires what is due in the same transaction
/// (#3864), each sent as one <see cref="DbBatch" />.
/// </summary>
/// <remarks>
/// <para>
/// A round cost two round trips per trigger it fired — the claim, and the fire's writes — beside the
/// handful it pays once. Batched, it pays a fixed number whatever its size.
/// </para>
/// <para>
/// <b>Only where nothing is bypassed.</b> A batch writes the statements
/// <see cref="UpdateTriggerStateFromOtherStateWithNextFireTime" /> and <see cref="ApplyTriggerFired" /> would
/// issue, without calling either. A subclass may have overridden them — to reshape a claim for its database,
/// or to write something more on a fire — and a batch would skip the override without a word. So the
/// delegates Quartz ships batch, where the connection can, and a subclass gets the single-trigger members
/// for each trigger, exactly as before. Telling an override apart from the base's own member would take
/// reflection over the subclass's methods, which the trimming analysis this assembly is built under
/// refuses; the type is what can be asked without it.
/// </para>
/// <para>
/// Of the drivers Quartz is tested with, Npgsql, Microsoft.Data.SqlClient and MySqlConnector report
/// <see cref="DbConnection.CanCreateBatch" />. The Oracle, Firebird and SQLite drivers do not, and issue the
/// statements one at a time as they always have.
/// </para>
/// </remarks>
public partial class StdAdoDelegate
{
    /// <summary>
    /// Whether this is one of the delegates Quartz ships, whose single-trigger claim and fire writes are
    /// this class's own. Asked once.
    /// </summary>
    private bool? shipped;

    /// <summary>
    /// Whether a round's statements go to the database as one batch: the connection can batch, and no
    /// subclass override would be bypassed.
    /// </summary>
    private bool BatchesRounds(ConnectionAndTransactionHolder conn)
    {
        return conn.CanCreateBatch && (shipped ??= IsShippedDelegate());
    }

    /// <summary>
    /// Roughly how many bytes of statements and values one of a round's batches carries at most.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured, not derived (#3864). On PostgreSQL in a container on Windows, S4 at twenty firings a second
    /// sends rounds of nine due fires, each about 2.4 KB by the count below. The median round took:
    /// </para>
    /// <list type="table">
    /// <listheader><term>Fires a batch</term><description>Round</description></listheader>
    /// <item><term>all nine, 22 KB</term><description>52.5 ms</description></item>
    /// <item><term>five, 12.2 KB</term><description>52.4 ms</description></item>
    /// <item><term>four, 9.8 KB</term><description>11.6 ms</description></item>
    /// <item><term>two, 4.9 KB (this size)</term><description>11.2 ms</description></item>
    /// </list>
    /// <para>
    /// The forty milliseconds a round loses is the length of a delayed acknowledgement, and the edge lies
    /// between four fires and five, near the 8 KiB Npgsql writes to its socket at a time. It takes the round
    /// around the batch: the same statements sent alone, once a second, did not stall at any size up to
    /// 39 KB. So the limit is the one the round was measured at, with room below the edge; fewer, larger
    /// batches bought nothing.
    /// </para>
    /// <para>
    /// The stall is the Windows host's: between two containers on Linux, the same round took 7.4 ms as one
    /// batch and 8.0 ms split at this size. Linux pays 0.6 ms a round for the split; a development box on
    /// Windows would pay forty without it.
    /// </para>
    /// <para>
    /// Counted in UTF-8 bytes, statement text and values, with a fixed allowance for the protocol's framing
    /// of each.
    /// </para>
    /// </remarks>
    private const int MaxRoundBatchBytes = 7 * 1024;

    /// <summary>
    /// The statement count of each batch the round's <paramref name="statements" /> go in: as many as fit
    /// <see cref="MaxRoundBatchBytes" />, and never fewer than one.
    /// </summary>
    private static List<int> RoundBatchLengths(List<SqlStatement> statements)
    {
        List<int> lengths = [];
        int length = 0;
        int bytes = 0;
        foreach (SqlStatement statement in statements)
        {
            int size = EstimateSize(statement);
            if (length > 0 && (bytes + size > MaxRoundBatchBytes || length == MaxStatementsPerBatch))
            {
                lengths.Add(length);
                length = 0;
                bytes = 0;
            }

            length++;
            bytes += size;
        }

        if (length > 0)
        {
            lengths.Add(length);
        }

        return lengths;

        static int EstimateSize(SqlStatement statement)
        {
            // In the bytes the driver writes, which for text is UTF-8 on the wire: a name in a script other
            // than Latin is two or three bytes a character, and counted in characters a batch of them
            // would pass the size it is meant to stay under. The framing of a statement in a batch —
            // parse, bind, describe and execute messages, or a batch's RPC headers — is a few tens of
            // bytes; a parameter's type and length a few more.
            int size = Encoding.UTF8.GetByteCount(statement.Sql) + 32;
            foreach (SqlStatementParameter parameter in statement.Parameters)
            {
                size += 6 + parameter.Value switch
                {
                    string text => Encoding.UTF8.GetByteCount(text),
                    byte[] data => data.Length,
                    _ => 8,
                };
            }

            return size;
        }
    }

    private bool IsShippedDelegate()
    {
        Type type = GetType();
        return type == typeof(StdAdoDelegate)
               || type == typeof(PostgreSQLDelegate)
               || type == typeof(SqlServerDelegate)
               || type == typeof(MySQLDelegate)
               || type == typeof(OracleDelegate)
               || type == typeof(SQLiteDelegate)
               || type == typeof(FirebirdDelegate);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// One batch of <see cref="UpdateTriggerStateFromOtherStateWithNextFireTime" />'s statement, one per claim,
    /// where the round batches; otherwise that member for each claim.
    /// </para>
    /// <para>
    /// The batch's total is what says every claim took, which is what a round under the lock nearly always
    /// finds. Short of it — a node acquiring without the lock took a row first — each command's own count
    /// says which, provided the provider reports them; one that reports counts that do not add up throws
    /// rather than guess, and the store claims the round again one trigger at a time.
    /// </para>
    /// </remarks>
    public virtual async ValueTask<List<TriggerKey>> UpdateTriggerStatesFromOtherStateWithNextFireTime(
        ConnectionAndTransactionHolder conn,
        IReadOnlyList<TriggerClaim> claims,
        StoredTriggerState newState,
        StoredTriggerState oldState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claims);

        List<TriggerKey> moved = new(claims.Count);
        if (claims.Count < 2 || !BatchesRounds(conn))
        {
            foreach (TriggerClaim claim in claims)
            {
                int updated = await UpdateTriggerStateFromOtherStateWithNextFireTime(
                    conn,
                    claim.TriggerKey,
                    newState,
                    oldState,
                    claim.NextFireTimeUtc,
                    cancellationToken).ConfigureAwait(false);

                if (updated > 0)
                {
                    moved.Add(claim.TriggerKey);
                }
            }

            return moved;
        }

        string sql = ReplaceTablePrefix(StdAdoConstants.SqlUpdateTriggerStateFromStateWithNextFireTime);
        object newStateValue = StoredTriggerStates.ToStoredValue(newState);
        object oldStateValue = StoredTriggerStates.ToStoredValue(oldState);

        List<SqlStatement> statements = new(claims.Count);
        foreach (TriggerClaim claim in claims)
        {
            // In the order the statement mentions them, for providers that bind positionally.
            statements.Add(new SqlStatement(sql,
            [
                new SqlStatementParameter(SqlParameters.NewState, newStateValue),
                new SqlStatementParameter(SqlParameters.SchedulerName, schedulerName),
                new SqlStatementParameter(SqlParameters.TriggerName, claim.TriggerKey.Name),
                new SqlStatementParameter(SqlParameters.TriggerGroup, claim.TriggerKey.Group),
                new SqlStatementParameter(SqlParameters.OldState, oldStateValue),
                new SqlStatementParameter(SqlParameters.NextFireTime, GetDbDateTimeValue(claim.NextFireTimeUtc))
            ]));
        }

        int offset = 0;
        foreach (int length in RoundBatchLengths(statements))
        {
            using DbCommand parameterFactory = conn.Connection.CreateCommand();
            using DbBatch batch = CreateStatementBatch(conn, parameterFactory, statements, offset, length);
            int total = await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (total == length)
            {
                for (int i = offset; i < offset + length; i++)
                {
                    moved.Add(claims[i].TriggerKey);
                }
            }
            else
            {
                AddClaimsThatTook(batch, claims, offset, total, moved);
            }

            offset += length;
        }

        return moved;
    }

    /// <summary>
    /// Adds the claims of a batch that moved a row, by each command's own count, for a batch whose total
    /// says some did not.
    /// </summary>
    /// <exception cref="ClaimOutcomeUnknownException">
    /// The counts are not one or nothing each, or do not add up to the total: the provider does not report
    /// them, and which claims took cannot be told.
    /// </exception>
    private static void AddClaimsThatTook(DbBatch batch, IReadOnlyList<TriggerClaim> claims, int offset, int total, List<TriggerKey> moved)
    {
        int length = batch.BatchCommands.Count;
        List<TriggerKey> took = new(total);
        int counted = 0;
        for (int i = 0; i < length; i++)
        {
            int affected = batch.BatchCommands[i].RecordsAffected;
            if (affected is not (0 or 1))
            {
                throw new ClaimOutcomeUnknownException(total, length);
            }

            counted += affected;
            if (affected == 1)
            {
                took.Add(claims[offset + i].TriggerKey);
            }
        }

        if (counted != total)
        {
            throw new ClaimOutcomeUnknownException(total, length);
        }

        moved.AddRange(took);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// One batch of every statement <see cref="ApplyTriggerFired" /> would issue for each update, in the order
    /// given, where the round batches; otherwise that member for each update. A type-table write that cannot
    /// be described as a statement follows the batch, as it follows a single fire's.
    /// </para>
    /// <para>
    /// A batch that fails is not run again a statement at a time: it fails as a unit, so which fire failed is
    /// not known, and the store answers by rolling the round back and firing its triggers one at a time,
    /// which finds it. A transient failure comes out as itself, for the store to retry.
    /// </para>
    /// </remarks>
    public virtual async ValueTask ApplyTriggersFired(
        ConnectionAndTransactionHolder conn,
        IReadOnlyList<TriggerFiredUpdate> updates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updates);

        if (updates.Count < 2 || !BatchesRounds(conn))
        {
            await IDriverDelegate.ApplyEachTriggerFired(this, conn, updates, cancellationToken).ConfigureAwait(false);
            return;
        }

        List<SqlStatement> statements = [];
        List<(TriggerFiredUpdate Update, TriggerTypeTableWrite Write)>? typeTableWrites = null;
        foreach (TriggerFiredUpdate update in updates)
        {
            if (DescribeTriggerFired(update, statements) is { } write)
            {
                (typeTableWrites ??= []).Add((update, write));
            }
        }

        try
        {
            int offset = 0;
            foreach (int length in RoundBatchLengths(statements))
            {
                using DbCommand parameterFactory = conn.Connection.CreateCommand();
                using DbBatch batch = CreateStatementBatch(conn, parameterFactory, statements, offset, length);
                await batch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                offset += length;
            }

            if (typeTableWrites is not null)
            {
                foreach ((TriggerFiredUpdate update, TriggerTypeTableWrite write) in typeTableWrites)
                {
                    await WriteTriggerTypeTable(conn, update.Trigger, update.NewState, update.JobDetail, update.StoredTriggerType, write.Type, write.PersistenceDelegate, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException && !TransientErrorDetector.IsTransient(e))
        {
            throw new TriggerWriteFailedException(-1, e);
        }
    }
}
