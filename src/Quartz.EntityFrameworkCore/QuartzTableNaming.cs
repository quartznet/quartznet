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

using System.Text;

namespace Quartz.EntityFrameworkCore;

/// <summary>
/// The names that depend on the table prefix and the schema: the one part of the model that is a rule rather
/// than data.
/// </summary>
/// <remarks>
/// <para>
/// A table is the prefix and its name, and an index is <c>IDX_</c>, the prefix and its suffix, as the job
/// store's scripts name them. Every name is folded as the database folds the store's unquoted identifiers: to
/// lower case on PostgreSQL, to upper case on Oracle and Firebird. EF Core quotes the names it is given, so it
/// would otherwise create tables the store cannot find.
/// </para>
/// <para>
/// On Oracle and Firebird the primary- and foreign-key names are the scripts', through <see cref="Constraint" />.
/// Elsewhere they are left to EF Core, which derives them from the table and column names;
/// <see cref="ForeignKeyName" /> says what EF Core derives, for the migration that has to name one.
/// </para>
/// <para>
/// A prefix that makes a name longer than the database takes is refused here, before anything runs, because
/// EF Core passes a name it is given through unchecked: PostgreSQL would cut it silently and Firebird refuse it
/// half-way through a migration.
/// </para>
/// </remarks>
internal sealed class QuartzTableNaming
{
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
    /// The prefix holds what an unquoted identifier cannot or makes a name too long for the database, or the
    /// schema is set for a database without schemas.
    /// </exception>
    internal static QuartzTableNaming Create(QuartzDialect dialect, Action<QuartzTablesOptions>? configure)
    {
        QuartzTablesOptions options = new();
        configure?.Invoke(options);

        // What an unquoted identifier can hold, because the store writes the prefix into its SQL unquoted, and so does
        // CompleteQuartzSchema into SQLite's trigger statements.
        string? prefix = options.TablePrefix;
        if (prefix is null || !prefix.All(c => char.IsLetterOrDigit(c) || c is '_' or '$' or '#'))
        {
            throw new ArgumentException(
                $"QuartzTablesOptions.TablePrefix '{prefix}' is not a table prefix: it may hold letters, digits, '_', '$' "
                + "and '#', as an unquoted identifier does. Give the prefix alone and put the schema in "
                + "QuartzTablesOptions.Schema: a job store whose TablePrefix is 'quartz.QRTZ_' has Schema = \"quartz\" and "
                + "TablePrefix = \"QRTZ_\" here.",
                nameof(configure));
        }

        string? schema = string.IsNullOrWhiteSpace(options.Schema) ? null : options.Schema;
        if (schema is not null && !dialect.SupportsSchemas)
        {
            throw new ArgumentException(
                $"{dialect.Label} has no schema for Quartz's tables to go in here: they go in the connection's database, "
                + "or on Oracle the session's schema. Leave QuartzTablesOptions.Schema unset.",
                nameof(configure));
        }

        QuartzTableNaming naming = new(dialect, prefix, schema);
        naming.ThrowIfANameIsTooLong(prefix, nameof(configure));
        return naming;
    }

    /// <summary>A constraint name from the script's template, the prefix filled in where it says <c>{1}</c>.</summary>
    internal string Constraint(string template) => Fold(template).Replace("{1}", TablePrefix, StringComparison.Ordinal);

    /// <summary>Every name the model gives that depends on the prefix: tables, indexes and the scripts' constraints.</summary>
    internal IEnumerable<string> PrefixedNames() => dialect.Tables.SelectMany(t =>
        new[] { Table(t.Name), t.PrimaryKeyName is { } pk ? Constraint(pk) : null, t.ForeignKey?.Name is { } fk ? Constraint(fk) : null }
            .Concat(t.Indexes.Select(i => Index(i.Suffix))))
        .OfType<string>();

    private void ThrowIfANameIsTooLong(string prefix, string parameterName)
    {
        string? longest = PrefixedNames().MaxBy(n => Encoding.UTF8.GetByteCount(n));
        int length = longest is null ? 0 : Encoding.UTF8.GetByteCount(longest);
        if (longest is null || length <= dialect.MaxIdentifierLength)
        {
            return;
        }

        int beyondThePrefix = length - Encoding.UTF8.GetByteCount(TablePrefix);
        string note = dialect == QuartzDialect.Firebird ? " (Firebird 3's limit, which every Firebird database is held to)" : "";

        throw new ArgumentException(
            $"QuartzTablesOptions.TablePrefix '{prefix}' makes the name {longest} {length} bytes long, and {dialect.Label} takes "
            + $"{dialect.MaxIdentifierLength}{note}. Use a prefix of at most {dialect.MaxIdentifierLength - beyondThePrefix} characters.",
            parameterName);
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

    private string Fold(string identifier) => dialect.IdentifierCase switch
    {
        QuartzIdentifierCase.Lower => identifier.ToLowerInvariant(),
        QuartzIdentifierCase.Upper => identifier.ToUpperInvariant(),
        _ => identifier,
    };
}
