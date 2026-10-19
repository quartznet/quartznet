#region License

/*
 * Copyright 2009- Marko Lahma
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

/// <summary>
/// A SQL Server specific driver delegate.
/// </summary>
/// <author>Marko Lahma</author>
public class SqlServerDelegate : StdAdoDelegate
{
    /// <summary>
    /// The size every string parameter is bound with, so one statement does not become a plan per
    /// value length: the widest <c>nvarchar</c> short of <c>max</c>.
    /// </summary>
    private const int MaxSizedStringLength = 4000;

    /// <inheritdoc />
    /// <remarks>
    /// The standard schema. The memory-optimized and pre-2016 variants under
    /// <c>database/tables/</c> have no counterpart: each is a deliberate departure a person chose for
    /// a particular deployment, not something a scheduler should decide to create.
    /// </remarks>
    protected override string? SchemaResourceName => "Quartz.Impl.AdoJobStore.Schema.create_sqlServer.sql";

    /// <summary>
    /// SQL Server names its row limit in the projection: <c>SELECT TOP n …</c>.
    /// </summary>
    protected override SqlRowLimit GetRowLimit(int count) => SqlRowLimit.InProjection("TOP", count);

    /// <summary>
    /// The one statement <see cref="SelectReadsCommittedSnapshot" /> asks: whether the database the
    /// connection is in reads committed snapshots.
    /// </summary>
    private const string SqlSelectReadCommittedSnapshot = "SELECT is_read_committed_snapshot_on FROM sys.databases WHERE database_id = DB_ID()";

    /// <summary>
    /// True: SQL Server's read committed takes shared locks unless the database's
    /// <c>READ_COMMITTED_SNAPSHOT</c> is on (#4117).
    /// </summary>
    /// <remarks>
    /// A firing round that takes no lock reads its candidates while another node claims and fires rows. A
    /// locking read holds a shared lock on an index key while it looks the row up, and the other node's
    /// update holds the row and needs that key: SQL Server breaks the cycle as a deadlock, after up to five
    /// seconds. With the snapshot on, the read takes no shared lock and the cycle cannot form.
    /// </remarks>
    internal override bool LockFreeRoundsNeedReadCommittedSnapshot => true;

    /// <inheritdoc />
    /// <remarks>
    /// Every login sees its own database's row of <c>sys.databases</c>. A <c>NULL</c> or a missing row
    /// answers no.
    /// </remarks>
    internal override async ValueTask<bool> SelectReadsCommittedSnapshot(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = PrepareCommand(conn, SqlSelectReadCommittedSnapshot);
        object? value = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is true;
    }

    /// <summary>
    /// True: a round that takes no lock claims in one transaction and fires in another (#4117).
    /// </summary>
    /// <remarks>
    /// Folded into one transaction, a round holds its claimed rows through the read back, the job read and
    /// the fire's writes, and another node's claim of a row it read before this one's commit waits all that
    /// time to lose it. Two nodes acquiring one trigger at a time ran seconds behind their schedule that way,
    /// locked or not, against tenths of a second with 4.5's two transactions.
    /// </remarks>
    internal override bool FiresLockFreeRoundsApart => true;

    /// <summary>
    /// True: SQL Server's lock-free rounds help only beside another live 4.7 node (#4117).
    /// </summary>
    /// <remarks>
    /// What a split round saves is the queue on <c>TRIGGER_ACCESS</c>, which a single node does not have, and
    /// it costs statements and commits: on one node the one-off census fired 9–28% fewer triggers a second
    /// with it than with 4.5's round.
    /// </remarks>
    internal override bool LockFreeRoundsNeedLivePeer => true;

    /// <summary>
    /// 4.7: a 4.6 node keeps every SQL Server round locked, and so does every node of a cluster that has one
    /// (#4117).
    /// </summary>
    internal override Version LockFreeRoundsSince => SchedulerNodeVersion.SqlServerLockFreeRoundsSince;

    /// <summary>
    /// T-SQL reads <c>[</c> as the start of a character class in a <c>LIKE</c> pattern, so a filter
    /// asking for a name containing <c>[a-z]</c> would match by class here and literally everywhere else.
    /// </summary>
    protected override string AdditionalLikeWildcards => "[";

    /// <summary>
    /// T-SQL joins strings with <c>+</c>; <c>||</c> is a syntax error unless the connection asks for
    /// ANSI string concatenation, which nothing here does.
    /// </summary>
    internal override string HistoryKeyExpression(string groupColumn, string nameColumn)
    {
        return "LOWER(" + groupColumn + " + '.' + " + nameColumn + ")";
    }

    /// <inheritdoc />
    public override void AddCommandParameter(
        DbCommand cmd,
        string paramName,
        object? paramValue,
        Enum? dataType = null,
        int? size = null)
    {
        // deeded for SQL Server CE
        if (paramValue is bool && dataType is null)
        {
            paramValue = (bool) paramValue ? 1 : 0;
        }

        // varbinary support
        if (size is null && dataType is not null && dataType.Equals(DbProvider.Metadata.BinaryParameterType))
        {
            size = -1;
        }

        // avoid size inferred from value that cause multiple query plans - except for a string longer
        // than that, which SqlClient would cut to the size rather than refuse: it is bound as
        // nvarchar(max). Only the execution history's captured log is ever that long.
        if (size is null && paramValue is string text)
        {
            size = text.Length > MaxSizedStringLength ? -1 : MaxSizedStringLength;
        }

        base.AddCommandParameter(cmd, paramName, paramValue, dataType, size);
    }
}
