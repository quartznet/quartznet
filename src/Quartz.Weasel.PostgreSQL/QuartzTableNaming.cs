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
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

using CascadeAction = Weasel.Core.CascadeAction;
using DbCommandBuilder = Weasel.Core.DbCommandBuilder;

namespace Quartz.Weasel.PostgreSQL;

/// <summary>
/// The names PostgreSQL gives the objects <c>create_postgres.sql</c> creates, for one table prefix.
/// </summary>
/// <remarks>
/// <para>
/// The generated <see cref="QuartzTables" /> says what the schema is; this says what PostgreSQL calls
/// it. Every identifier is lower case, because the script writes them unquoted and PostgreSQL folds
/// unquoted identifiers — an upper-case name in the model would be quoted and be a different table.
/// </para>
/// <para>
/// The script names no primary or foreign key, so PostgreSQL chooses the names, and the model has to
/// choose the same ones or Weasel reads every constraint as changed: <c>&lt;table&gt;_pkey</c>, and
/// <c>&lt;table&gt;_&lt;columns&gt;_fkey</c> cut to 63 bytes the way PostgreSQL's
/// <c>makeObjectName</c> cuts it, which is how <c>qrtz_simprop_triggers</c>' foreign key comes to end in
/// <c>trigger_grou_fkey</c>.
/// </para>
/// </remarks>
internal sealed class QuartzTableNaming
{
    /// <summary><c>NAMEDATALEN - 1</c>, the longest identifier PostgreSQL keeps.</summary>
    private const int MaxIdentifierLength = 63;

    public QuartzTableNaming(string schema, string tablePrefix)
    {
        Schema = schema.ToLowerInvariant();
        Prefix = tablePrefix.ToLowerInvariant();
    }

    public string Schema { get; }

    public string Prefix { get; }

    public Table Table(string name) => new(Name(name))
    {
        // An application's own columns, indexes and foreign keys on these tables are never dropped, and
        // neither are a newer Quartz node's columns when an older node applies its model.
        AddOnlyMigrations = true,
    };

    public PostgresqlObjectName Name(string table) => new(Schema, Prefix + table.ToLowerInvariant(), SchemaUtils.IdentifierUsage.General);

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Called on the naming instance by the generated model, which is written the same way for every dialect.")]
    public void PrimaryKey(Table table) => table.PrimaryKeyName = DefaultConstraintName(table.Identifier.Name, null, "pkey");

    public void ForeignKey(Table table, string referencedTable, string[] columns, string[] referencedColumns, bool cascade)
    {
        table.ForeignKeys.Add(new ForeignKey(DefaultConstraintName(table.Identifier.Name, string.Join('_', columns), "fkey"))
        {
            LinkedTable = Name(referencedTable),
            ColumnNames = columns,
            LinkedNames = referencedColumns,
            DeleteAction = cascade ? CascadeAction.Cascade : CascadeAction.NoAction,
        });
    }

    public void Index(Table table, string suffix, string[] columns) =>
        table.Indexes.Add(new IndexDefinition(IndexName(suffix)) { Columns = columns });

    public ISchemaObject RetiredIndex(string table, string suffix) =>
        new RetiredPostgresIndex(
            new PostgresqlObjectName(Schema, IndexName(suffix), SchemaUtils.IdentifierUsage.General),
            Prefix + table.ToLowerInvariant());

    /// <summary><c>IDX_{1}J_G_N</c> in the script: the unqualified prefix, folded.</summary>
    private string IndexName(string suffix) => $"idx_{Prefix}{suffix.ToLowerInvariant()}";

    /// <summary>
    /// The name PostgreSQL chooses for a constraint the statement left unnamed.
    /// </summary>
    /// <remarks>
    /// <c>makeObjectName</c> in <c>src/backend/commands/indexcmds.c</c>: the label and the underscores
    /// are kept whole, and the table and column parts are shortened one character at a time, the longer
    /// one first, until the whole fits in 63 bytes. The names here are ASCII, so a character is a byte.
    /// </remarks>
    internal static string DefaultConstraintName(string table, string? columns, string label)
    {
        int available = MaxIdentifierLength - label.Length - 1 - (columns is null ? 0 : 1);
        int tableLength = table.Length;
        int columnsLength = columns?.Length ?? 0;

        while (tableLength + columnsLength > available)
        {
            if (tableLength > columnsLength)
            {
                tableLength--;
            }
            else
            {
                columnsLength--;
            }
        }

        return columns is null
            ? $"{table[..tableLength]}_{label}"
            : $"{table[..tableLength]}_{columns[..columnsLength]}_{label}";
    }
}

/// <summary>
/// An index name Quartz no longer creates, dropped when it is still on the table it was created on.
/// </summary>
/// <remarks>
/// The tables are add-only, so Weasel keeps anything the model does not declare — which is right for an
/// application's own index and wrong for the ones 3.x created and 4.x retired. This names them, the same
/// set <c>database/migrations/4.0/schema_30_to_40_indexes_postgres.sql</c> drops, and only when the
/// index is on the Quartz table it belonged to.
/// </remarks>
internal sealed class RetiredPostgresIndex : SchemaObjectBase
{
    private readonly string table;

    public RetiredPostgresIndex(PostgresqlObjectName identifier, string table) : base(identifier)
    {
        this.table = table;
    }

    public override void ConfigureQueryCommand(DbCommandBuilder builder)
    {
        string schema = builder.AddParameter(Identifier.Schema).ParameterName;
        string name = builder.AddParameter(Identifier.Name).ParameterName;
        string tableName = builder.AddParameter(table).ParameterName;

        builder.Append(
            $"select count(*) from pg_indexes where schemaname = :{schema} and indexname = :{name} and tablename = :{tableName};");
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
        writer.WriteLine($"drop index if exists {Identifier.QualifiedName};");
}
