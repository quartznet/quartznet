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

using Weasel.Core;
using Weasel.Oracle;
using Weasel.Oracle.Tables;

using DbCommandBuilder = Weasel.Core.DbCommandBuilder;

namespace Quartz.Weasel.Oracle;

/// <summary>
/// The names the objects <c>create_oracle.sql</c> creates go by, for one table prefix.
/// </summary>
/// <remarks>
/// <para>
/// Every name is upper case, because the script writes them unquoted and Oracle folds an unquoted
/// identifier to upper case — the prefix too, whatever case the store's options spell it in. The schema
/// is the table prefix's or the session's current schema, never Weasel's own default, the literal
/// <c>WEASEL</c>.
/// </para>
/// <para>
/// The constraint names are the script's: <c>{1}JOB_DETAILS_PK</c>, <c>{1}TRIGGER_TO_JOBS_FK</c>, built from
/// the stems <c>tables_oracle.sql</c> has always used, which the generated model passes in. Weasel compares a
/// foreign key by name; it compares no primary key name, but creates this one.
/// </para>
/// <para>
/// <c>IDX_QRTZ_T_NFT_ST</c> is <c>PRIORITY DESC</c>. Oracle keeps a descending key column as a hidden
/// function-based column, and Weasel reads it back to the column and its direction, into
/// <see cref="IndexDefinition.DescendingColumns" />. An index's tablespace is read back too, and compared
/// only when the model names one, which it does not.
/// </para>
/// </remarks>
internal sealed class QuartzTableNaming
{
    /// <param name="schema">The schema as Oracle stores its name: folded, unless it was quoted.</param>
    /// <param name="tablePrefix">The table prefix after its last dot, which the store's SQL writes unquoted.</param>
    public QuartzTableNaming(string schema, string tablePrefix)
    {
        Schema = schema;
        Prefix = tablePrefix.ToUpperInvariant();
    }

    public string Schema { get; }

    public string Prefix { get; }

    public Table Table(string name) => new(Name(name))
    {
        // An application's own columns, indexes and foreign keys on these tables are never dropped, and
        // neither are a newer Quartz node's columns when an older node applies its model.
        AddOnlyMigrations = true,
    };

    public OracleObjectName Name(string table) => new(Schema, Prefix + table);

    /// <summary><c>{1}SIMPLE_TRIG_PK</c> in the script, from the table's stem.</summary>
    public void PrimaryKey(Table table, string stem) => table.PrimaryKeyName = $"{Prefix}{stem}_PK";

    /// <summary><c>{1}SIMPLE_TRIG_TO_TRIG_FK</c> in the script.</summary>
    public void ForeignKey(Table table, string referencedTable, string[] columns, string[] referencedColumns, bool cascade, string name) =>
        table.ForeignKeys.Add(new ForeignKey(Prefix + name).Links(Name(referencedTable), columns, referencedColumns, cascade));

    public void Index(Table table, string suffix, string[] columns, string[]? descending = null)
    {
        IndexDefinition index = new(QuartzNaming.IndexName(Prefix, suffix));
        table.Indexes.AddIndex(index, columns, index.DescendingColumns, descending);
    }

    public ISchemaObject RetiredIndex(string table, string suffix) =>
        new RetiredOracleIndex(new OracleObjectName(Schema, QuartzNaming.IndexName(Prefix, suffix)), Prefix + table);
}

/// <summary>
/// A retired index on Oracle, asked after in <c>ALL_INDEXES</c>.
/// </summary>
internal sealed class RetiredOracleIndex : RetiredQuartzIndex
{
    public RetiredOracleIndex(OracleObjectName identifier, string table) : base(identifier, table)
    {
    }

    public override void ConfigureQueryCommand(DbCommandBuilder builder)
    {
        string schema = builder.AddParameter(Identifier.Schema).ParameterName;
        string name = builder.AddParameter(Identifier.Name).ParameterName;
        string tableName = builder.AddParameter(Table).ParameterName;

        builder.Append(
            "SELECT count(*) FROM all_indexes"
            + $" WHERE owner = :{schema} AND index_name = :{name} AND table_owner = :{schema} AND table_name = :{tableName}");
    }

    /// <summary>One statement, ended the way Weasel's Oracle scripts end theirs, with a lone <c>/</c>.</summary>
    public override void WriteDropStatement(Migrator rules, TextWriter writer)
    {
        writer.WriteLine($"DROP INDEX {Identifier.QualifiedName}");
        writer.WriteLine("/");
    }
}
