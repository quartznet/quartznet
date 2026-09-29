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
using Weasel.MySql;
using Weasel.MySql.Tables;

using CascadeAction = Weasel.Core.CascadeAction;
using DbCommandBuilder = Weasel.Core.DbCommandBuilder;

namespace Quartz.Weasel.MySQL;

/// <summary>
/// The names MySQL gives the objects <c>create_mysql_innodb.sql</c> creates, for one table prefix.
/// </summary>
/// <remarks>
/// <para>
/// Table, column and index names are the script's, in the script's case, with the prefix after its last
/// dot. Every table is qualified with a database, because Weasel's MySQL default is the literal
/// <c>public</c>: the table prefix's database, or the connection's.
/// </para>
/// <para>
/// The script names no foreign key, so InnoDB names it <c>&lt;table&gt;_ibfk_1</c>. Weasel compares a foreign
/// key by name, so the model says the same one, or a database the script created reads as one key missing
/// and another extra. Each Quartz table has at most one foreign key, so the number is always 1. Every
/// primary key is named <c>PRIMARY</c>, which Weasel does not compare.
/// </para>
/// <para>
/// <c>IDX_QRTZ_T_NFT_ST</c> is <c>PRIORITY DESC</c>. MySQL 8.0 honours a key column's direction and Weasel
/// reads it back per column into <see cref="IndexDefinition.DescendingColumns" />; 5.7 parses the
/// <c>DESC</c> and ignores it, so there the index reads as changed on every apply.
/// </para>
/// </remarks>
internal sealed class QuartzTableNaming
{
    public QuartzTableNaming(string schema, string tablePrefix)
    {
        Schema = schema;
        Prefix = tablePrefix;
    }

    /// <summary>The database the tables are in.</summary>
    public string Schema { get; }

    public string Prefix { get; }

    public Table Table(string name) => new(Name(name))
    {
        // An application's own columns, indexes and foreign keys on these tables are never dropped, and
        // neither are a newer Quartz node's columns when an older node applies its model.
        AddOnlyMigrations = true,
    };

    public MySqlObjectName Name(string table) => new(Schema, Prefix + table);

    /// <summary>Nothing to say: MySQL names every primary key <c>PRIMARY</c>.</summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Called on the naming instance by the generated model, which is written the same way for every dialect.")]
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "The generated model passes every table, whether the dialect has anything to say about its key or not.")]
    public void PrimaryKey(Table table)
    {
    }

    /// <summary><c>&lt;table&gt;_ibfk_1</c>, which InnoDB gives the script's unnamed foreign key.</summary>
    public void ForeignKey(Table table, string referencedTable, string[] columns, string[] referencedColumns, bool cascade)
    {
        table.ForeignKeys.Add(new ForeignKey($"{table.Identifier.Name}_ibfk_1")
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
        };

        foreach (string column in descending ?? [])
        {
            index.DescendingColumns.Add(column);
        }

        table.Indexes.Add(index);
    }

    public ISchemaObject RetiredIndex(string table, string suffix) =>
        new RetiredMySqlIndex(new MySqlObjectName(Schema, IndexName(suffix)), Prefix + table);

    /// <summary><c>IDX_{1}T_NFT_ST</c> in the script.</summary>
    private string IndexName(string suffix) => $"IDX_{Prefix}{suffix}";
}

/// <summary>
/// An index name Quartz no longer creates, dropped when it is still on the table it was created on.
/// </summary>
/// <remarks>
/// The tables are add-only, so Weasel keeps anything the model does not declare — right for an
/// application's own index and wrong for the ones 3.x created and 4.x retired. This names them, the same
/// set <c>database/migrations/4.0/schema_30_to_40_indexes_mysql_innodb.sql</c> drops. MySQL has no
/// <c>DROP INDEX IF EXISTS</c>; the drop is only written when the catalog has just reported the index,
/// and the apply holds the migration lock.
/// </remarks>
internal sealed class RetiredMySqlIndex : SchemaObjectBase
{
    private readonly string table;

    public RetiredMySqlIndex(MySqlObjectName identifier, string table) : base(identifier)
    {
        this.table = table;
    }

    public override void ConfigureQueryCommand(DbCommandBuilder builder)
    {
        string schema = builder.AddParameter(Identifier.Schema).ParameterName;
        string name = builder.AddParameter(Identifier.Name).ParameterName;
        string tableName = builder.AddParameter(table).ParameterName;

        builder.Append(
            "SELECT count(*) FROM information_schema.STATISTICS"
            + $" WHERE TABLE_SCHEMA = @{schema} AND INDEX_NAME = @{name} AND TABLE_NAME = @{tableName};");
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
        writer.WriteLine($"DROP INDEX {SchemaUtils.QuoteName(Identifier.Name)} ON {new MySqlObjectName(Identifier.Schema, table).QualifiedName};");
}
