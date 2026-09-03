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

using System.Text.RegularExpressions;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The columns startup probes for are the columns the 3.x-to-4.0 migration adds.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AdoConstants.MigratedColumnNames" /> is what a 4.x store checks is there before it
/// starts, and a column missing from that list is a column a 3.x database can be missing while the
/// scheduler starts, reports itself validated and then fails every acquisition for ever. The list that
/// makes the check complete already exists, in
/// <c>database/migrations/4.0/schema_30_to_40_upgrade_&lt;dialect&gt;.sql</c> — so it is read out of
/// those six scripts here rather than written down twice and kept in step by hand.
/// </para>
/// <para>
/// The scripts are generated from <c>build/Build.DatabaseMigrations.Scripts.cs</c> and
/// <c>VerifyMigrations</c> fails a pull request whose copies are stale, so reading them is reading the
/// generator's own answer: a column added to the migration and not to the constant fails here.
/// </para>
/// </remarks>
public sealed class MigratedColumnTest
{
    private static readonly string[] Dialects =
        ["sqlServer", "postgres", "mysql_innodb", "oracle", "sqlite", "firebird"];

    /// <summary>
    /// An <c>ALTER TABLE … ADD</c> in any of the six dialects' spellings, guard and all.
    /// </summary>
    /// <remarks>
    /// Each dialect wraps the statement in whatever conditional it has — a <c>DO $$</c> block, a
    /// prepared statement, an <c>EXECUTE IMMEDIATE</c>, an <c>EXECUTE BLOCK</c> — and SQL Server
    /// brackets its identifiers while PostgreSQL lower-cases everything. What none of them varies is
    /// the statement inside, which is what this reads.
    /// </remarks>
    private static readonly Regex AddColumn = new(
        @"ALTER\s+TABLE\s+(?:\[dbo\]\.)?\[?(?<table>\w+)\]?\s+ADD\s+(?:COLUMN\s+)?\(?\[?(?<column>\w+)\]?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Test]
    public void TheProbedColumnsAreTheOnesTheMigrationAdds()
    {
        HashSet<(string Table, string Column)> declared =
        [
            .. AdoConstants.MigratedColumnNames.Select(c => (c.Table.ToUpperInvariant(), c.Column.ToUpperInvariant()))
        ];

        foreach (string dialect in Dialects)
        {
            HashSet<(string Table, string Column)> added = ColumnsAddedBy(dialect);

            added.Should().HaveCountGreaterThan(4,
                $"the {dialect} 4.0 migration adds several columns, so a parse that found almost none "
                + "is a parse that stopped matching rather than a migration that shrank");

            added.Should().BeEquivalentTo(declared,
                $"AdoConstants.MigratedColumnNames is what startup probes for, and the {dialect} 4.0 "
                + "migration is what an upgraded database has — a column in one and not the other is "
                + "either a check with a hole in it or a probe for a column nothing creates");
        }
    }

    /// <summary>
    /// The six scripts add the same columns as each other, which is the claim the check above rests on
    /// having only one answer to compare against.
    /// </summary>
    [Test]
    public void EveryDialectsMigrationAddsTheSameColumns()
    {
        HashSet<(string Table, string Column)> first = ColumnsAddedBy(Dialects[0]);

        foreach (string dialect in Dialects.Skip(1))
        {
            ColumnsAddedBy(dialect).Should().BeEquivalentTo(first,
                $"every migration ships a file for every dialect and they describe one change, so the "
                + $"{dialect} script and the {Dialects[0]} one have to add the same columns");
        }
    }

    private static HashSet<(string Table, string Column)> ColumnsAddedBy(string dialect)
    {
        string script = File.ReadAllText(Path.Combine(
            RepositoryRoot.Find().FullName,
            "database", "migrations", "4.0", $"schema_30_to_40_upgrade_{dialect}.sql"));

        HashSet<(string, string)> added = [];

        foreach (Match match in AddColumn.Matches(script))
        {
            string table = match.Groups["table"].Value.ToUpperInvariant();
            string column = match.Groups["column"].Value.ToUpperInvariant();

            // The constants name a table without the prefix the scripts spell it with, since the
            // prefix is configuration and the table name is not.
            added.Add((table.StartsWith("QRTZ_", StringComparison.Ordinal) ? table[5..] : table, column));
        }

        return added;
    }
}
