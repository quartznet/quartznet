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
using System.Globalization;

using Weasel.Core;
using Weasel.Sqlite;
using Weasel.Sqlite.Tables;

using DbCommandBuilder = Weasel.Core.DbCommandBuilder;

namespace Quartz.Weasel.SQLite;

/// <summary>
/// The names the objects <c>create_sqlite.sql</c> creates go by, for one table prefix.
/// </summary>
/// <remarks>
/// <para>
/// Table and index names are the script's, prefix and all. Column names are written in lower case,
/// which is how Weasel reads them back from <c>pragma_table_info</c>; SQLite compares identifiers without
/// regard to case, so the script's upper-case columns are these.
/// </para>
/// <para>
/// The script names no foreign key, and SQLite keeps no name for one it was not given, so Weasel reads an
/// unnamed key back as <c>fk_&lt;table&gt;_&lt;referenced table&gt;_&lt;id&gt;</c>. The model says the
/// same name, or Weasel sees one key missing and another extra — which on SQLite it repairs by rebuilding
/// the table. Each Quartz table has at most one foreign key, so the id is always 0.
/// </para>
/// </remarks>
internal sealed class QuartzTableNaming
{
    public QuartzTableNaming(string schema, string tablePrefix)
    {
        Schema = schema;
        Prefix = tablePrefix;
    }

    public string Schema { get; }

    public string Prefix { get; }

    public Table Table(string name) => new(Name(name))
    {
        // An application's own columns and indexes on these tables are never dropped, and neither are a
        // newer Quartz node's columns when an older node applies its model.
        AddOnlyMigrations = true,
    };

    public SqliteObjectName Name(string table) => new(Schema, Prefix + table);

    /// <summary>Nothing to say: SQLite reports no primary key name, so Weasel compares none.</summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Called on the naming instance by the generated model, which is written the same way for every dialect.")]
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "The generated model passes every table, whether the dialect has anything to say about its key or not.")]
    public void PrimaryKey(Table table)
    {
    }

    public void ForeignKey(Table table, string referencedTable, string[] columns, string[] referencedColumns, bool cascade)
    {
        table.ForeignKeys.Add(new ForeignKey($"fk_{table.Identifier.Name}_{Prefix}{referencedTable}_0")
        {
            LinkedTable = Name(referencedTable),
            ColumnNames = columns,
            LinkedNames = referencedColumns,
            DeleteAction = cascade ? CascadeAction.Cascade : CascadeAction.NoAction,
        });
    }

    public void Index(Table table, string suffix, string[] columns) =>
        table.Indexes.Add(new IndexDefinition(IndexName(suffix)) { Columns = columns });

    /// <summary>
    /// An index whose columns carry directions, which SQLite's <c>IndexDefinition</c> takes only as an
    /// expression — the column list exactly as the script writes it.
    /// </summary>
    public void Index(Table table, string suffix, string expression) =>
        table.Indexes.Add(new IndexDefinition(IndexName(suffix)) { Expression = expression });

    public ISchemaObject Trigger(string name, string createStatement) =>
        new QuartzDeleteTrigger(
            Name(name),
            string.Format(CultureInfo.InvariantCulture, createStatement, Prefix));

    public ISchemaObject RetiredIndex(string table, string suffix) =>
        new RetiredSqliteIndex(new SqliteObjectName(Schema, IndexName(suffix)), Prefix + table);

    /// <summary><c>IDX_{1}J_G_N</c> in the script.</summary>
    private string IndexName(string suffix) => $"IDX_{Prefix}{suffix}";
}

/// <summary>
/// One of the triggers that delete a trigger's detail row, created when it is missing and otherwise left
/// as it is.
/// </summary>
/// <remarks>
/// <para>
/// The script creates them because SQLite enforces the cascading foreign keys only on a connection that
/// turns them on. Modelled, so that a schema Weasel created is the schema the script creates; modelled as
/// add-only, because Weasel's own SQLite trigger compares the whole statement text, and the text differs
/// between the script (no timing keyword) and Weasel's rendering (<c>BEFORE</c>) — every database the script
/// created would read as changed.
/// </para>
/// <para>
/// A database created by 3.x names these triggers without the table prefix. Those keep working and are not
/// touched; the prefixed ones are added beside them, and a delete then runs twice, which deletes nothing
/// twice.
/// </para>
/// </remarks>
internal sealed class QuartzDeleteTrigger : SchemaObjectBase
{
    private readonly string createStatement;

    public QuartzDeleteTrigger(SqliteObjectName identifier, string createStatement) : base(identifier)
    {
        this.createStatement = createStatement;
    }

    public override void ConfigureQueryCommand(DbCommandBuilder builder)
    {
        string name = builder.AddParameter(Identifier.Name).ParameterName;
        builder.Append(
            $"SELECT count(*) FROM {SchemaUtils.QuoteName(Identifier.Schema)}.sqlite_master WHERE type = 'trigger' AND name = @{name} COLLATE NOCASE;");
    }

    public override void WriteCreateStatement(Migrator migrator, TextWriter writer) => writer.WriteLine(createStatement + ";");

    public override void WriteDropStatement(Migrator rules, TextWriter writer) =>
        writer.WriteLine($"DROP TRIGGER IF EXISTS {Identifier.QualifiedName};");
}

/// <summary>
/// An index name Quartz no longer creates, dropped when it is still on the table it was created on.
/// </summary>
/// <remarks>
/// The tables are add-only, so Weasel keeps anything the model does not declare — right for an
/// application's own index and wrong for the ones 3.x created and 4.x retired. This names them, the same
/// set <c>database/migrations/4.0/schema_30_to_40_indexes_sqlite.sql</c> drops.
/// </remarks>
internal sealed class RetiredSqliteIndex : SchemaObjectBase
{
    private readonly string table;

    public RetiredSqliteIndex(SqliteObjectName identifier, string table) : base(identifier)
    {
        this.table = table;
    }

    public override void ConfigureQueryCommand(DbCommandBuilder builder)
    {
        string name = builder.AddParameter(Identifier.Name).ParameterName;
        string tableName = builder.AddParameter(table).ParameterName;

        builder.Append(
            $"SELECT count(*) FROM {SchemaUtils.QuoteName(Identifier.Schema)}.sqlite_master WHERE type = 'index'"
            + $" AND name = @{name} COLLATE NOCASE AND tbl_name = @{tableName} COLLATE NOCASE;");
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
        writer.WriteLine($"DROP INDEX IF EXISTS {Identifier.QualifiedName};");
}
