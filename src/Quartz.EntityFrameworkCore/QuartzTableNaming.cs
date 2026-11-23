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

using System.Buffers;
using System.Text;

namespace Quartz.EntityFrameworkCore;

/// <summary>
/// The names that depend on the table prefix and the schema: the one part of the model that is a rule rather
/// than data.
/// </summary>
/// <remarks>
/// <para>
/// A table is the prefix and its name, and an index is <c>IDX_</c>, the prefix and its suffix, as the job
/// store's scripts name them. On PostgreSQL every name is folded to lower case, which is what the database does
/// to the store's unquoted identifiers; EF Core quotes the names it is given, so it would otherwise create
/// tables the store cannot find.
/// </para>
/// <para>
/// Primary- and foreign-key constraint names are left to EF Core, which derives them from the table and column
/// names. <see cref="ForeignKeyName" /> says what EF Core derives, for the migration that has to name one.
/// </para>
/// </remarks>
internal sealed class QuartzTableNaming
{
    private static readonly SearchValues<char> notInAPrefix = SearchValues.Create(".[]\"`' \t\r\n");

    private readonly QuartzDialect dialect;

    private QuartzTableNaming(QuartzDialect dialect, string tablePrefix, string? schema)
    {
        this.dialect = dialect;
        TablePrefix = Fold(tablePrefix);
        Schema = schema is null ? null : Fold(schema);
    }

    /// <summary>The prefix, as the database names it.</summary>
    internal string TablePrefix { get; }

    /// <summary>The schema, as the database names it, or <see langword="null" /> for the model's default.</summary>
    internal string? Schema { get; }

    /// <summary>The naming the options describe, for the dialect a provider speaks.</summary>
    /// <exception cref="ArgumentException">
    /// The prefix carries a schema or quotes, or the schema is set for a database without schemas.
    /// </exception>
    internal static QuartzTableNaming Create(QuartzDialect dialect, Action<QuartzTablesOptions>? configure)
    {
        QuartzTablesOptions options = new();
        configure?.Invoke(options);

        string? prefix = options.TablePrefix;
        if (prefix is null || prefix.AsSpan().ContainsAny(notInAPrefix))
        {
            throw new ArgumentException(
                $"QuartzTablesOptions.TablePrefix '{prefix}' is not a table prefix. Give the prefix alone, without a schema "
                + "or quotes, and put the schema in QuartzTablesOptions.Schema: a job store whose TablePrefix is "
                + "'quartz.QRTZ_' has Schema = \"quartz\" and TablePrefix = \"QRTZ_\" here.",
                nameof(configure));
        }

        string? schema = string.IsNullOrWhiteSpace(options.Schema) ? null : options.Schema;
        if (schema is not null && !dialect.SupportsSchemas)
        {
            throw new ArgumentException(
                $"{dialect.Label} has no schema for Quartz's tables to go in: they go in the connection's database. "
                + "Leave QuartzTablesOptions.Schema unset.",
                nameof(configure));
        }

        return new QuartzTableNaming(dialect, prefix, schema);
    }

    internal string Table(string name) => TablePrefix + Fold(name);

    internal string Column(string name) => Fold(name);

    internal string Index(string suffix) => Fold("IDX_") + TablePrefix + Fold(suffix);

    /// <summary>
    /// The name of the table's entity type, which only the model snapshot shows. It carries the schema and the
    /// prefix so that two schedulers' tables can share one model.
    /// </summary>
    internal string EntityName(string name) => "Quartz." + (Schema is null ? "" : Schema + ".") + Table(name);

    /// <summary>
    /// The name EF Core gives a foreign key it is not told the name of: <c>FK_</c>, the table, the referenced
    /// table and the columns, joined by underscores and cut to the provider's identifier length with a
    /// trailing <c>~</c>.
    /// </summary>
    internal string ForeignKeyName(string table, QuartzForeignKeyDefinition foreignKey)
    {
        StringBuilder name = new StringBuilder("FK_")
            .Append(Table(table)).Append('_')
            .Append(Table(foreignKey.ReferencedTable)).Append('_')
            .AppendJoin('_', foreignKey.Columns.Select(Column));

        return name.Length <= dialect.MaxIdentifierLength
            ? name.ToString()
            : name.ToString(0, dialect.MaxIdentifierLength - 1) + "~";
    }

    private string Fold(string identifier) => dialect.FoldsToLowerCase ? identifier.ToLowerInvariant() : identifier;
}
