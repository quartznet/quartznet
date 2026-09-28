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
using System.Diagnostics.CodeAnalysis;

using Weasel.Core;
using Weasel.SqlServer;
using Weasel.SqlServer.Tables;

using CascadeAction = Weasel.Core.CascadeAction;
using DbCommandBuilder = Weasel.Core.DbCommandBuilder;

namespace Quartz.Weasel.SqlServer;

/// <summary>
/// The names the objects <c>create_sqlServer.sql</c> creates go by, for one table prefix.
/// </summary>
/// <remarks>
/// <para>
/// Every name is the script's, in the script's case: <c>PK_QRTZ_TRIGGERS</c>,
/// <c>FK_QRTZ_TRIGGERS_QRTZ_JOB_DETAILS</c>, <c>IDX_QRTZ_T_NFT_ST</c>, with the prefix after its last dot
/// where the script writes <c>{1}</c>. Weasel compares a foreign key by its name, case-sensitively, so a
/// model naming one anything else reads as one key missing and another extra.
/// </para>
/// <para>
/// Every index compares its column directions. <c>IDX_QRTZ_T_NFT_ST</c> is
/// <c>PRIORITY DESC</c>; Weasel reads that direction back, and an index that did not ask for it to be
/// compared renders without it and reads as changed — dropped and recreated on every apply.
/// </para>
/// <para>
/// Column drift detection stays off, as on the other dialects: no Quartz migration changes an existing
/// column's nullability or default, so it could only ever undo a change an application made on purpose,
/// and the tables are add-only for exactly that reason.
/// </para>
/// </remarks>
internal sealed class QuartzTableNaming
{
    /// <param name="schema">The schema, bracketed or not: <see cref="SqlServerObjectName" /> takes the brackets off.</param>
    /// <param name="tablePrefix">The table prefix after its last dot.</param>
    public QuartzTableNaming(string schema, string tablePrefix)
    {
        Schema = schema;
        Prefix = tablePrefix;
    }

    public string Schema { get; }

    public string Prefix { get; }

    public Table Table(string name) => new(Name(name))
    {
        // An application's own columns, indexes and foreign keys on these tables are never dropped, and
        // neither are a newer Quartz node's columns when an older node applies its model.
        AddOnlyMigrations = true,
    };

    public SqlServerObjectName Name(string table) => new(Schema, Prefix + table);

    /// <summary><c>PK_{1}TRIGGERS</c> in the script. Weasel compares no primary key name, but creates this one.</summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Called on the naming instance by the generated model, which is written the same way for every dialect.")]
    public void PrimaryKey(Table table) => table.PrimaryKeyName = $"PK_{table.Identifier.Name}";

    /// <summary><c>FK_{1}TRIGGERS_{1}JOB_DETAILS</c> in the script.</summary>
    public void ForeignKey(Table table, string referencedTable, string[] columns, string[] referencedColumns, bool cascade)
    {
        table.ForeignKeys.Add(new ForeignKey($"FK_{table.Identifier.Name}_{Prefix}{referencedTable}")
        {
            LinkedTable = Name(referencedTable),
            ColumnNames = columns,
            LinkedNames = referencedColumns,
            DeleteAction = cascade ? CascadeAction.Cascade : CascadeAction.NoAction,
        });
    }

    public void Index(Table table, string suffix, string[] columns, string[]? descending = null)
    {
        IndexDefinition index = new(IndexName(suffix))
        {
            Columns = columns,
            CompareColumnDirection = true,
        };

        foreach (string column in descending ?? [])
        {
            index.DescendingColumns.Add(column);
        }

        table.Indexes.Add(index);
    }

    public ISchemaObject RetiredIndex(string table, string suffix) =>
        new RetiredSqlServerIndex(new SqlServerObjectName(Schema, IndexName(suffix)), Prefix + table);

    /// <summary><c>IDX_{1}T_NFT_ST</c> in the script.</summary>
    private string IndexName(string suffix) => $"IDX_{Prefix}{suffix}";
}

/// <summary>
/// An index name Quartz no longer creates, dropped when it is still on the table it was created on.
/// </summary>
/// <remarks>
/// The tables are add-only, so Weasel keeps anything the model does not declare — right for an
/// application's own index and wrong for the ones 3.x created and 4.x retired. This names them, the same
/// set <c>database/migrations/4.0/schema_30_to_40_indexes_sqlServer.sql</c> drops, <c>IDX_QRTZ_T_G_J</c>
/// and the two <c>IDX_QRTZ_T_NFT_ST_MISFIRE*</c> among them.
/// </remarks>
internal sealed class RetiredSqlServerIndex : SchemaObjectBase
{
    private readonly string table;

    public RetiredSqlServerIndex(SqlServerObjectName identifier, string table) : base(identifier)
    {
        this.table = table;
    }

    public override void ConfigureQueryCommand(DbCommandBuilder builder)
    {
        string schema = builder.AddParameter(Identifier.Schema).ParameterName;
        string name = builder.AddParameter(Identifier.Name).ParameterName;
        string tableName = builder.AddParameter(table).ParameterName;

        builder.Append(
            "select count(*) from sys.indexes i"
            + " inner join sys.tables t on t.object_id = i.object_id"
            + " inner join sys.schemas s on s.schema_id = t.schema_id"
            + $" where s.name = @{schema} and i.name = @{name} and t.name = @{tableName};");
    }

    public override async Task<ISchemaObjectDelta> CreateDeltaAsync(DbDataReader reader, CancellationToken ct = default)
    {
        bool present = await reader.ReadAsync(ct).ConfigureAwait(false)
                       && await ReadExistsCountAsync(reader, ct).ConfigureAwait(false) > 0;

        return new SchemaObjectDelta(this, present ? SchemaPatchDifference.Update : SchemaPatchDifference.None);
    }

    /// <summary>Nothing: a retired index is never created.</summary>
    public override void WriteCreateStatement(Migrator migrator, TextWriter writer)
    {
    }

    public override void WriteDropStatement(Migrator rules, TextWriter writer) =>
        writer.WriteLine($"drop index if exists {SchemaUtils.QuoteName(Identifier.Name)} on {new SqlServerObjectName(Identifier.Schema, table)};");
}

/// <summary>
/// Refuses a schema whose Quartz tables are memory-optimized, before Weasel compares a single table.
/// </summary>
/// <remarks>
/// <para>
/// <c>tables_sqlServerMOT.sql</c> creates Quartz's tables memory-optimized: hash indexes, non-clustered
/// primary keys, no foreign keys. Weasel models disk-based tables only, so it would read every one of them
/// as changed and set about creating indexes and keys a memory-optimized table cannot take — failing
/// halfway, with some of it applied. Read first in every comparison, this stops an apply, an assert and a
/// patch alike, and says why.
/// </para>
/// <para>
/// Nothing is ever created or dropped for it; it is a check that happens to be read the way the tables
/// are.
/// </para>
/// </remarks>
internal sealed class MemoryOptimizedTableGuard : SchemaObjectBase
{
    private readonly string schedulerName;
    private readonly string[] tables;

    public MemoryOptimizedTableGuard(SqlServerObjectName identifier, string schedulerName, IEnumerable<string> tables) : base(identifier)
    {
        this.schedulerName = schedulerName;
        this.tables = tables.ToArray();
    }

    public override void ConfigureQueryCommand(DbCommandBuilder builder)
    {
        string schema = builder.AddParameter(Identifier.Schema).ParameterName;
        IEnumerable<string> names = tables.Select(x => "@" + builder.AddParameter(x).ParameterName);

        builder.Append(
            "select t.name from sys.tables t"
            + " inner join sys.schemas s on s.schema_id = t.schema_id"
            + $" where s.name = @{schema} and t.is_memory_optimized = 1 and t.name in ({string.Join(", ", names)})"
            + " order by t.name;");
    }

    public override async Task<ISchemaObjectDelta> CreateDeltaAsync(DbDataReader reader, CancellationToken ct = default)
    {
        List<string> memoryOptimized = [];
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            memoryOptimized.Add(await reader.GetFieldValueAsync<string>(0, ct).ConfigureAwait(false));
        }

        if (memoryOptimized.Count > 0)
        {
            throw new SchedulerException(
                $"Scheduler '{schedulerName}' keeps its schedule in memory-optimized tables ({string.Join(", ", memoryOptimized)}"
                + $" in schema {Identifier.Schema}), and Quartz.Weasel.SqlServer manages disk-based tables only. Nothing was changed."
                + " Leave UseWeaselForSqlServer() out for this scheduler, and keep its schema with"
                + " database/tables/tables_sqlServerMOT.sql.");
        }

        return new SchemaObjectDelta(this, SchemaPatchDifference.None);
    }

    /// <summary>Nothing: a check creates nothing.</summary>
    public override void WriteCreateStatement(Migrator migrator, TextWriter writer)
    {
    }

    /// <summary>Nothing: a check drops nothing.</summary>
    public override void WriteDropStatement(Migrator rules, TextWriter writer)
    {
    }
}
