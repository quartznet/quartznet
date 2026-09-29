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

using System.Globalization;
using System.Text.RegularExpressions;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The migration scripts under <c>database/migrations/</c>, read as the folder holds them, so a test that
/// asks what the migrations do asks the files rather than a list kept beside them.
/// </summary>
internal static class MigrationFiles
{
    /// <summary>Every dialect a migration ships a file for.</summary>
    public static readonly string[] Dialects =
        ["sqlServer", "postgres", "mysql_innodb", "oracle", "sqlite", "firebird"];

    /// <summary>
    /// One dialect's scripts in the folders at or after <paramref name="from" />, oldest folder first and
    /// ordinal within one, each with its comments stripped.
    /// </summary>
    /// <param name="dialect">The dialect whose files to read.</param>
    /// <param name="from">The oldest folder read.</param>
    /// <param name="inclusive">Whether <paramref name="from" /> itself is read.</param>
    /// <returns>
    /// Each script's path under <c>database/migrations/</c> — the form an <c>AdoConstants.Migration*</c>
    /// template formats to — and its text.
    /// </returns>
    public static List<(string Path, string Text)> For(string dialect, string from, bool inclusive)
    {
        Version floor = Version.Parse(from);
        string root = Path.Combine(RepositoryRoot.Find().FullName, "database", "migrations");
        string suffix = $"_{dialect}.sql";

        return Directory.GetDirectories(root)
            .Select(folder => (Folder: Path.GetFileName(folder), Version: Version.Parse(Path.GetFileName(folder))))
            .Where(x => inclusive ? x.Version >= floor : x.Version > floor)
            .OrderBy(x => x.Version)
            .SelectMany(x => Directory.GetFiles(Path.Combine(root, x.Folder), "*" + suffix)
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal)
                .Select(file => (Path: $"{x.Folder}/{file}", Text: WithoutComments(File.ReadAllText(Path.Combine(root, x.Folder, file))))))
            .ToList();
    }

    /// <summary>The path a migration template names for one dialect.</summary>
    public static string PathOf(string migration, string dialect) =>
        string.Format(CultureInfo.InvariantCulture, migration, dialect);

    /// <summary>
    /// The tables a script creates, without the prefix, in any dialect's spelling: bracketed, guarded,
    /// or inside an <c>EXECUTE IMMEDIATE</c> string.
    /// </summary>
    public static IEnumerable<string> CreatedTables(string script) => CreateTable.Matches(script)
        .Select(m => Unprefixed(m.Groups["table"].Value))
        .Distinct(StringComparer.Ordinal);

    /// <summary>
    /// The columns a script adds to a table that was already there, as table without the prefix and
    /// column.
    /// </summary>
    /// <remarks>
    /// Each dialect wraps the statement in whatever conditional it has — a <c>DO $$</c> block, a
    /// prepared statement, an <c>EXECUTE IMMEDIATE</c>, an <c>EXECUTE BLOCK</c> — and SQL Server
    /// brackets its identifiers while PostgreSQL lower-cases everything. What none of them varies is
    /// the statement inside, which is what this reads.
    /// </remarks>
    public static IEnumerable<(string Table, string Column)> AddedColumns(string script) => AddColumn.Matches(script)
        .Select(m => (Unprefixed(m.Groups["table"].Value), m.Groups["column"].Value.ToUpperInvariant()))
        .Distinct();

    private static readonly Regex CreateTable = new(
        @"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?:\[?dbo\]?\.)?\[?(?<table>\w+)\]?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AddColumn = new(
        @"ALTER\s+TABLE\s+(?:\[dbo\]\.)?\[?(?<table>\w+)\]?\s+ADD\s+(?:COLUMN\s+)?\(?\[?(?<column>\w+)\]?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// A table as the constants name it: upper case, without the prefix, which is configuration rather
    /// than part of the name.
    /// </summary>
    private static string Unprefixed(string table)
    {
        string upper = table.ToUpperInvariant();
        return upper.StartsWith("QRTZ_", StringComparison.Ordinal) ? upper[5..] : upper;
    }

    /// <summary>
    /// Drops every <c>--</c> comment, so a header that describes a statement is not read as one.
    /// </summary>
    private static string WithoutComments(string script) => string.Join('\n', script
        .Replace("\r\n", "\n")
        .Split('\n')
        .Select(line => line.IndexOf("--", StringComparison.Ordinal) is int comment and >= 0 ? line[..comment] : line));
}
