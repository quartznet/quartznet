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

using System.Diagnostics.CodeAnalysis;

using Weasel.Core;
using Weasel.Firebird;
using Weasel.Firebird.Tables;

using CascadeAction = Weasel.Core.CascadeAction;
using DbCommandBuilder = Weasel.Core.DbCommandBuilder;

namespace Quartz.Weasel.Firebird;

/// <summary>
/// The names the objects <c>create_firebird.sql</c> creates go by, for one table prefix.
/// </summary>
/// <remarks>
/// <para>
/// Every name is the script's: <c>PK_QRTZ_TRIGGERS</c>, <c>FK_QRTZ_TRIGGERS_1</c>,
/// <c>IDX_QRTZ_T_NFT_ST</c>. The script writes them undelimited, so Firebird stores them in upper case,
/// and Weasel folds the model's names the same way. Weasel compares a foreign key by its name, so a
/// model naming one anything else reads as one key missing and another extra.
/// </para>
/// <para>
/// There is no schema: Firebird 3, 4 and 5 have none, and every object is in Weasel's pseudo-schema
/// <see cref="FirebirdObjectName.DefaultSchema" />, which is never written into DDL. A table prefix that
/// names a schema is refused by <see cref="FirebirdQuartzDatabase" /> before the model is built.
/// </para>
/// <para>
/// No foreign key cascades. The script declares none, a key with no rule reads back as
/// <c>RESTRICT</c>, and Weasel reads that as <see cref="CascadeAction.NoAction" />: the store deletes a
/// trigger's detail row itself.
/// </para>
/// </remarks>
internal sealed class QuartzTableNaming
{
    /// <param name="schema">
    /// Ignored: Firebird 3, 4 and 5 have no schemas. Taken so that every dialect's naming is built the
    /// same way.
    /// </param>
    /// <param name="tablePrefix">The table prefix.</param>
    [SuppressMessage("Style", "IDE0060:Remove unused parameter", Justification = "Every dialect's naming takes a schema and a prefix, and Firebird has no schemas.")]
    public QuartzTableNaming(string schema, string tablePrefix)
    {
        Prefix = tablePrefix;
    }

    public string Prefix { get; }

    public Table Table(string name) => new(Name(name))
    {
        // An application's own columns, indexes and foreign keys on these tables are never dropped, and
        // neither are a newer Quartz node's columns when an older node applies its model.
        AddOnlyMigrations = true,
    };

    public FirebirdObjectName Name(string table) => new(Prefix + table);

    /// <summary><c>PK_{1}TRIGGERS</c> in the script.</summary>
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Called on the naming instance by the generated model, which is written the same way for every dialect.")]
    public void PrimaryKey(Table table) => table.PrimaryKeyName = $"PK_{table.Identifier.Name}";

    /// <summary>
    /// <c>FK_{1}TRIGGERS_1</c> in the script: numbered rather than naming both ends, which kept the
    /// names inside Firebird's old 31-character limit.
    /// </summary>
    public void ForeignKey(Table table, string referencedTable, string[] columns, string[] referencedColumns, bool cascade) =>
        table.ForeignKeys.Add(new ForeignKey($"FK_{table.Identifier.Name}_1").Links(Name(referencedTable), columns, referencedColumns, cascade));

    public void Index(Table table, string suffix, string[] columns) =>
        table.Indexes.Add(new IndexDefinition(QuartzNaming.IndexName(Prefix, suffix)) { Columns = columns });

    public ISchemaObject RetiredIndex(string table, string suffix) =>
        new RetiredFirebirdIndex(new FirebirdObjectName(QuartzNaming.IndexName(Prefix, suffix)), Prefix + table);
}

/// <summary>
/// A retired index on Firebird, asked after in <c>RDB$INDICES</c>.
/// </summary>
/// <remarks>
/// The drop is guarded like every other Weasel.Firebird statement, so two appliers that both read the
/// index as present both succeed.
/// </remarks>
internal sealed class RetiredFirebirdIndex : RetiredQuartzIndex
{
    public RetiredFirebirdIndex(FirebirdObjectName identifier, string table) : base(identifier, table)
    {
    }

    /// <summary>The index's name as the catalog stores it.</summary>
    private string CatalogName => SchemaUtils.CatalogName(Identifier.Name);

    /// <summary>The table's name as the catalog stores it.</summary>
    private string TableCatalogName => SchemaUtils.CatalogName(Table);

    public override void ConfigureQueryCommand(DbCommandBuilder builder)
    {
        string name = builder.AddParameter(CatalogName).ParameterName;
        string tableName = builder.AddParameter(TableCatalogName).ParameterName;

        builder.Append(
            $"SELECT COUNT(*) FROM RDB$INDICES WHERE RDB$INDEX_NAME = @{name} AND RDB$RELATION_NAME = @{tableName}");
    }

    public override void WriteDropStatement(Migrator rules, TextWriter writer) =>
        FirebirdScript.WriteGuardedWhenExists(
            writer,
            $"SELECT 1 FROM RDB$INDICES WHERE RDB$INDEX_NAME = {FirebirdScript.Literal(CatalogName)}"
            + $" AND RDB$RELATION_NAME = {FirebirdScript.Literal(TableCatalogName)}",
            $"DROP INDEX {SchemaUtils.QuoteName(Identifier.Name)}");
}
