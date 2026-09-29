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
using System.Text.RegularExpressions;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What one schema script declares — its tables with their columns and keys, its indexes and its
/// triggers — with the table prefix and each script's spelling normalized away, so a generated script
/// and a fresh-install one compare directly.
/// </summary>
/// <remarks>
/// <para>
/// A small parser rather than a SQL grammar: it reads the eight fresh-install scripts under
/// <c>database/tables/</c> and the six generated <c>create_&lt;dialect&gt;.sql</c>, and nothing else. What it
/// normalizes is spelling only — case, brackets and quotes, whitespace, Oracle's and Firebird's doubled
/// quotes, the prefix placeholder — and two equivalences SQL Server itself makes. Anything that could be
/// a real difference in the schema is left for the comparison to report.
/// </para>
/// <para>
/// A foreign key is recorded by its shape — table, columns, referenced table and columns, and whether
/// a delete cascades — never by its name: the dialects that leave a key unnamed let the database name
/// it, and a key that differs only in name is the same key.
/// </para>
/// </remarks>
/// <param name="Tables">Each table's columns, by name.</param>
/// <param name="PrimaryKeys">Each table's primary key columns, in key order.</param>
/// <param name="ForeignKeys">Every foreign key, as its shape.</param>
/// <param name="Indexes">Each index's name, and the columns it is declared over.</param>
/// <param name="Triggers">The triggers the script creates.</param>
internal sealed record SqlObjects(
    Dictionary<string, Dictionary<string, SqlColumn>> Tables,
    Dictionary<string, string> PrimaryKeys,
    List<string> ForeignKeys,
    Dictionary<string, string> Indexes,
    List<string> Triggers)
{
    private static readonly Regex CreateTable = new(
        @"CREATE TABLE (?:IF NOT EXISTS )?(?:DBO\.)?(?<name>\w+)\(",
        RegexOptions.CultureInvariant);

    // MySQL declares its indexes inside CREATE TABLE, which is the one shape difference between the
    // two kinds of script; matching both spellings is what lets one comparison serve both. The column
    // list comes along with the name, because a name says nothing about what an index can serve —
    // IDX_QRTZ_T_NFT_ST carries a per-column direction, and a script that lost it would still name the
    // index the other one names.
    private static readonly Regex CreateIndex = new(
        @"(?:CREATE INDEX (?:IF NOT EXISTS )?(?<name>\w+) ON [^(]+|(?<=[,(])KEY (?<name>\w+))\((?<columns>[^)]*)\)",
        RegexOptions.CultureInvariant);

    private static readonly Regex CreateTrigger = new(
        @"CREATE TRIGGER (?:IF NOT EXISTS )?(?<name>\w+)",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// A key added after the table, which is how the SQL Server fresh-install scripts declare every
    /// key: <c>ALTER TABLE … [WITH NOCHECK] ADD CONSTRAINT … PRIMARY KEY CLUSTERED (…)</c>.
    /// </summary>
    private static readonly Regex AlterTableAddKey = new(
        @"ALTER TABLE (?:DBO\.)?(?<table>\w+)(?: WITH NOCHECK)? ADD (?<clause>(?:CONSTRAINT \w+ )?(?:PRIMARY|FOREIGN) KEY[^;]*)",
        RegexOptions.CultureInvariant);

    private static readonly Regex NamedConstraint = new(@"^CONSTRAINT \w+ ", RegexOptions.CultureInvariant);

    private static readonly Regex PrimaryKeyClause = new(
        @"^PRIMARY KEY(?: CLUSTERED| NONCLUSTERED)?\((?<columns>[^)]*)\)",
        RegexOptions.CultureInvariant);

    private static readonly Regex ForeignKeyClause = new(
        @"^FOREIGN KEY\((?<columns>[^)]*)\) REFERENCES (?:DBO\.)?(?<table>\w+)\((?<referenced>[^)]*)\)(?<cascade> ON DELETE CASCADE)?",
        RegexOptions.CultureInvariant);

    /// <summary>A column item: an identifier, then its declaration.</summary>
    private static readonly Regex ColumnItem = new(@"^(?<name>[A-Z_]\w*) (?<declaration>.+)$", RegexOptions.CultureInvariant);

    /// <summary>
    /// A type is every token before the first of these, which is what lets Firebird's
    /// <c>BLOB SUB_TYPE TEXT</c> be one type.
    /// </summary>
    private static readonly Regex TypeEnd = new(
        @" (?:NOT|NULL|DEFAULT|COLLATE|PRIMARY|CONSTRAINT|IDENTITY)\b",
        RegexOptions.CultureInvariant);

    private static readonly Regex NotNull = new(@"\bNOT NULL\b", RegexOptions.CultureInvariant);

    private static readonly Regex DefaultValue = new(
        @"\bDEFAULT ?(?<value>\((?:[^()]|\([^()]*\))*\)|'[^']*'|[^ ]+)",
        RegexOptions.CultureInvariant);

    private static readonly Regex InlinePrimaryKey = new(@" PRIMARY KEY\b", RegexOptions.CultureInvariant);

    /// <summary>
    /// A drop in the teardown every fresh-install script opens with: a <c>DROP TABLE</c> of one table or
    /// of MySQL's list of them, or the table the Oracle script hands to its <c>DropQuartzTable</c>
    /// procedure, whose body is the <c>DROP TABLE</c>.
    /// </summary>
    private static readonly Regex DropTable = new(
        @"DROP TABLE (?:IF EXISTS )?(?<names>(?:DBO\.)?\w+(?:,(?:DBO\.)?\w+)*)|DROPQUARTZTABLE\('(?<names>\w+)'\)",
        RegexOptions.CultureInvariant);

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.CultureInvariant);

    private static readonly Regex SpaceBeforePunctuation = new(@" (?=[(),])", RegexOptions.CultureInvariant);

    private static readonly Regex SpaceAfterPunctuation = new(@"(?<=[(,]) ", RegexOptions.CultureInvariant);

    private static readonly string[] NotColumns = ["KEY", "INDEX", "UNIQUE"];

    /// <param name="script">The script's text.</param>
    /// <param name="dialect">
    /// The dialect the script is written in, as the generated scripts name it: the two SQL Server
    /// variants are <c>sqlServer</c>. It decides the equivalences SQL Server makes itself.
    /// </param>
    public static SqlObjects Parse(string script, string dialect)
    {
        string normalized = Normalize(script);

        Dictionary<string, Dictionary<string, SqlColumn>> tables = new(StringComparer.Ordinal);
        Dictionary<string, string> primaryKeys = new(StringComparer.Ordinal);
        List<string> foreignKeys = [];

        foreach (Match match in CreateTable.Matches(normalized))
        {
            string table = Unprefixed(match.Groups["name"].Value);
            Dictionary<string, SqlColumn> columns = new(StringComparer.Ordinal);
            tables[table] = columns;

            foreach (string item in Items(Body(normalized, match.Index + match.Length)))
            {
                string clause = NamedConstraint.Replace(item, "");
                if (Key(table, clause, primaryKeys, foreignKeys))
                {
                    continue;
                }

                Match column = ColumnItem.Match(item);
                if (!column.Success || NotColumns.Contains(column.Groups["name"].Value, StringComparer.Ordinal))
                {
                    continue;
                }

                string name = column.Groups["name"].Value;
                string declaration = column.Groups["declaration"].Value;
                columns[name] = Declaration(declaration, dialect);

                if (InlinePrimaryKey.IsMatch(" " + declaration))
                {
                    primaryKeys[table] = name;
                }
            }
        }

        foreach (Match match in AlterTableAddKey.Matches(normalized))
        {
            Key(Unprefixed(match.Groups["table"].Value), NamedConstraint.Replace(match.Groups["clause"].Value, ""), primaryKeys, foreignKeys);
        }

        foreignKeys.Sort(StringComparer.Ordinal);

        return new SqlObjects(tables, primaryKeys, foreignKeys, IndexesWithColumns(normalized), Names(CreateTrigger, normalized));
    }

    /// <summary>
    /// The tables a fresh-install script drops before its first <c>CREATE TABLE</c>: the teardown it
    /// opens with, whatever form the dialect gives it.
    /// </summary>
    public static HashSet<string> TornDown(string script)
    {
        string normalized = Normalize(script);
        int firstCreate = normalized.IndexOf("CREATE TABLE ", StringComparison.Ordinal);
        string teardown = firstCreate < 0 ? normalized : normalized[..firstCreate];

        HashSet<string> dropped = new(StringComparer.Ordinal);
        foreach (Match match in DropTable.Matches(teardown))
        {
            foreach (string name in match.Groups["names"].Value.Split(','))
            {
                dropped.Add(Unprefixed(name.StartsWith("DBO.", StringComparison.Ordinal) ? name[4..] : name));
            }
        }

        return dropped;
    }

    /// <summary>
    /// A primary or foreign key clause, recorded against <paramref name="table" />; false when the
    /// clause is neither.
    /// </summary>
    private static bool Key(string table, string clause, Dictionary<string, string> primaryKeys, List<string> foreignKeys)
    {
        if (PrimaryKeyClause.Match(clause) is { Success: true } primaryKey)
        {
            primaryKeys[table] = ColumnList(primaryKey.Groups["columns"].Value);
            return true;
        }

        if (ForeignKeyClause.Match(clause) is { Success: true } foreignKey)
        {
            string cascade = foreignKey.Groups["cascade"].Success ? " ON DELETE CASCADE" : "";
            foreignKeys.Add(
                $"{table}({ColumnList(foreignKey.Groups["columns"].Value)}) REFERENCES "
                + $"{Unprefixed(foreignKey.Groups["table"].Value)}({ColumnList(foreignKey.Groups["referenced"].Value)}){cascade}");
            return true;
        }

        return false;
    }

    /// <summary>
    /// A column's type, nullability and default, read off what follows its name.
    /// </summary>
    /// <remarks>
    /// <c>DEFAULT NULL</c> is no default at all, which is how Firebird spells a nullable column. On SQL
    /// Server, <c>INTEGER</c> is a synonym the server stores as <c>INT</c>, and a default in parentheses
    /// is the same default: the catalog reads both back the same way.
    /// </remarks>
    private static SqlColumn Declaration(string declaration, string dialect)
    {
        Match end = TypeEnd.Match(declaration);
        string type = end.Success ? declaration[..end.Index] : declaration;

        string defaultValue = DefaultValue.Match(declaration) is { Success: true } value ? value.Groups["value"].Value : null;
        if (defaultValue == "NULL")
        {
            defaultValue = null;
        }

        if (dialect == "sqlServer")
        {
            type = type == "INTEGER" ? "INT" : type;

            while (defaultValue is not null && defaultValue.StartsWith('(') && defaultValue.EndsWith(')'))
            {
                defaultValue = defaultValue[1..^1];
            }
        }

        return new SqlColumn(type, NotNull.IsMatch(declaration), defaultValue);
    }

    private static List<string> Names(Regex declaration, string script) => declaration.Matches(script)
        .Select(m => Unprefixed(m.Groups["name"].Value))
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToList();

    /// <summary>Each index's name, and the columns it is declared over.</summary>
    private static Dictionary<string, string> IndexesWithColumns(string script) => CreateIndex.Matches(script)
        .GroupBy(m => Unprefixed(m.Groups["name"].Value), StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => ColumnList(g.First().Groups["columns"].Value), StringComparer.Ordinal);

    /// <summary>
    /// One spelling for a column list. A direction keyword is part of the column and stays where it is.
    /// </summary>
    private static string ColumnList(string columns) => string.Join(", ",
        columns.Split(',').Select(c => Whitespace.Replace(c.Trim(), " ")));

    /// <summary>
    /// Puts every script into one vocabulary: comments go, since prose about a <c>CREATE INDEX</c> is not
    /// one; the placeholders become the default prefix; case stops mattering; SQL Server's brackets and
    /// the other dialects' quoted identifiers lose their delimiters; the doubled quotes of a statement
    /// Oracle and Firebird run from inside a string literal become single; and whitespace is one space,
    /// with none beside a parenthesis or a comma.
    /// </summary>
    private static string Normalize(string script)
    {
        IEnumerable<string> lines = script
            .Replace("\r\n", "\n")
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("#", StringComparison.Ordinal))
            .Select(StripTrailingComment);

        StringBuilder text = new StringBuilder(string.Join('\n', lines))
            .Replace("{0}", "QRTZ_")
            .Replace("{1}", "QRTZ_")
            .Replace("[", "")
            .Replace("]", "")
            .Replace("`", "")
            .Replace("\"", "")
            .Replace("''", "'");

        string collapsed = Whitespace.Replace(text.ToString().ToUpperInvariant(), " ");
        collapsed = SpaceBeforePunctuation.Replace(collapsed, "");
        return SpaceAfterPunctuation.Replace(collapsed, "");
    }

    /// <summary>
    /// Drops a <c>--</c> comment. No script has a string literal holding one, so the first occurrence
    /// on a line always starts a comment.
    /// </summary>
    private static string StripTrailingComment(string line)
    {
        int comment = line.IndexOf("--", StringComparison.Ordinal);
        return comment >= 0 ? line[..comment] : line;
    }

    /// <summary>
    /// Everything Quartz creates carries the prefix, and an index or constraint name carries it in the
    /// middle — <c>IDX_QRTZ_T_J</c> — so it comes off wherever it sits.
    /// </summary>
    private static string Unprefixed(string name) => name.Replace("QRTZ_", "", StringComparison.Ordinal);

    /// <summary>The text between a <c>CREATE TABLE … (</c> and its matching close paren.</summary>
    private static string Body(string script, int start)
    {
        int depth = 1;
        for (int i = start; i < script.Length; i++)
        {
            depth += script[i] switch { '(' => 1, ')' => -1, _ => 0 };
            if (depth == 0)
            {
                return script[start..i];
            }
        }

        throw new InvalidOperationException("Unbalanced parentheses in a CREATE TABLE body.");
    }

    /// <summary>
    /// The body's own items, split on the commas that separate them rather than on every comma: a
    /// type's precision and a key's column list each hold commas of their own.
    /// </summary>
    private static List<string> Items(string body)
    {
        List<string> items = [];
        int depth = 0;
        StringBuilder item = new();

        foreach (char c in body + ",")
        {
            depth += c switch { '(' => 1, ')' => -1, _ => 0 };

            if (c == ',' && depth == 0)
            {
                string trimmed = item.ToString().Trim();
                if (trimmed.Length > 0)
                {
                    items.Add(trimmed);
                }

                item.Clear();
                continue;
            }

            item.Append(c);
        }

        return items;
    }
}

/// <summary>One column as a script declares it.</summary>
/// <param name="Type">The type, as written once the script's spelling is normalized.</param>
/// <param name="NotNull">Whether the column is declared <c>NOT NULL</c>.</param>
/// <param name="Default">The default, or <see langword="null" /> when it has none.</param>
internal sealed record SqlColumn(string Type, bool NotNull, string Default)
{
    public override string ToString() =>
        Type + (NotNull ? " NOT NULL" : " NULL") + (Default is null ? "" : " DEFAULT " + Default);
}
