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

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The columns startup probes for are the columns the migrations since 4.0 add, each named beside the
/// script that adds it.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AdoConstants.MigratedColumnNames" /> is what a 4.x store checks is there before it
/// starts, and a column missing from that list is a column an older database can be missing while the
/// scheduler starts, reports itself validated and then fails every acquisition for ever.
/// <see cref="AdoConstants.OptionalColumnNames" /> is the same for a store keeping its history in the
/// database. The list that makes both checks complete already exists, in every
/// <c>ALTER TABLE … ADD</c> under <c>database/migrations/</c> from 4.0 on — so it is read out of the
/// folders here rather than written down twice, and a new migration needs no edit to this test.
/// </para>
/// <para>
/// The scripts are generated from <c>build/Build.DatabaseMigrations.Scripts.cs</c> and
/// <c>VerifyMigrations</c> fails a pull request whose copies are stale, so reading them is reading the
/// generator's own answer: a column added to a migration and not to the constants fails here, and so
/// does an entry that names a script other than the one that adds its column.
/// </para>
/// </remarks>
public sealed class MigratedColumnTest
{
    private static readonly string[] Dialects = MigrationFiles.Dialects;

    [TestCaseSource(nameof(Dialects))]
    public void TheProbedColumnsAreTheOnesTheMigrationsAdd(string dialect)
    {
        Dictionary<(string Table, string Column), List<string>> added = ColumnsAddedBy(dialect);

        added.Should().HaveCountGreaterThan(4,
            $"the {dialect} migrations add several columns between them, so a parse that found almost "
            + "none is a parse that stopped matching rather than a migration that shrank");

        Probed(AdoConstants.MigratedColumnNames.Select(c => (c.Table, c.Column, c.Migration)), dialect).Should().BeEquivalentTo(
            Added(added, optional: false),
            $"AdoConstants.MigratedColumnNames is what every scheduler probes for, and the {dialect} migrations "
            + "are what an upgraded database has — a column in one and not the other is either a check with a "
            + "hole in it or a probe for a column nothing creates, and an entry naming another script sends "
            + "the reader of the refusal to the wrong file");
    }

    /// <summary>
    /// The optional migrations' column additions, which startup probes only when the feature that
    /// reads them is on — and which therefore must not be in the list every scheduler probes.
    /// </summary>
    [TestCaseSource(nameof(Dialects))]
    public void TheOptionalColumnsAreTheOnesTheOptionalTablesGain(string dialect)
    {
        Probed(AdoConstants.OptionalColumnNames.Select(c => (c.Table, c.Column, c.Migration)), dialect).Should().BeEquivalentTo(
            Added(ColumnsAddedBy(dialect), optional: true),
            "AdoConstants.OptionalColumnNames is what a store keeping its history in the database probes "
            + $"for, and the {dialect} migrations that alter an optional table are what add it");
    }

    [Test]
    public void NoColumnIsBothProbedAlwaysAndOnlyWithTheFeature()
    {
        AdoConstants.OptionalColumnNames.Select(c => (c.Table, c.Column)).Should().NotIntersectWith(
            AdoConstants.MigratedColumnNames.Select(c => (c.Table, c.Column)),
            "a column every scheduler probes for is a migration every database needs, and an optional "
            + "table is one a database may never have had");
    }

    /// <summary>
    /// The six dialects' migrations add the same columns as each other, which is the claim the checks
    /// above rest on having only one list to compare against.
    /// </summary>
    [Test]
    public void EveryDialectsMigrationAddsTheSameColumns()
    {
        HashSet<(string Table, string Column)> first = [.. ColumnsAddedBy(Dialects[0]).Keys];

        foreach (string dialect in Dialects.Skip(1))
        {
            ColumnsAddedBy(dialect).Keys.Should().BeEquivalentTo(first,
                $"every migration ships a file for every dialect and they describe one change, so the "
                + $"{dialect} scripts and the {Dialects[0]} ones have to add the same columns");
        }
    }

    /// <summary>
    /// What a store keeping its history in the database tells a reader to run: every script after 4.0
    /// that creates or alters an optional table, once, in the order the folders run in.
    /// </summary>
    /// <remarks>
    /// The migrations are cumulative, and 4.4's alters the table 4.2's creates, so an order that put a
    /// later folder first would send the reader to a script that fails.
    /// </remarks>
    [TestCaseSource(nameof(Dialects))]
    public void TheOptionalMigrationsAreNamedOldestFirst(string dialect)
    {
        HashSet<string> optionalTables = [.. AdoConstants.OptionalTableNames.Select(t => t.Table)];

        List<string> touchingAnOptionalTable = MigrationFiles.For(dialect, "4.0", inclusive: false)
            .Where(x => MigrationFiles.CreatedTables(x.Text).Any(optionalTables.Contains)
                        || MigrationFiles.AddedColumns(x.Text).Any(c => optionalTables.Contains(c.Table)))
            .Select(x => x.Path)
            .ToList();

        touchingAnOptionalTable.Should().NotBeEmpty("the execution history's own migration creates optional tables");

        AdoConstants.OptionalMigrations.Select(m => MigrationFiles.PathOf(m, dialect)).Should().Equal(touchingAnOptionalTable,
            "the table migration comes before the migrations that alter its tables, whatever order the "
            + "table and column lists happen to name them in, and a script that touches an optional table "
            + "is one a store keeping its history there needs");

        AdoConstants.MigrationVersion("4.10/later_{0}.sql").Should().BeGreaterThan(
            AdoConstants.MigrationVersion("4.9/earlier_{0}.sql"),
            "folders order as releases do, which ordinal string comparison would get wrong");
    }

    /// <summary>
    /// Each column the migrations from 4.0 on add, and every script that adds it.
    /// </summary>
    /// <remarks>
    /// The constants name a table without the prefix the scripts spell it with, since the prefix is
    /// configuration and the table name is not.
    /// </remarks>
    private static Dictionary<(string Table, string Column), List<string>> ColumnsAddedBy(string dialect)
    {
        Dictionary<(string, string), List<string>> added = [];

        foreach ((string path, string text) in MigrationFiles.For(dialect, "4.0", inclusive: true))
        {
            foreach ((string, string) key in MigrationFiles.AddedColumns(text))
            {
                if (!added.TryGetValue(key, out List<string> paths))
                {
                    added[key] = paths = [];
                }

                if (!paths.Contains(path, StringComparer.Ordinal))
                {
                    paths.Add(path);
                }
            }
        }

        return added;
    }

    /// <summary>
    /// The additions to a required table, or to an optional one, as table, column and every script
    /// that adds the column — one script, when the migrations are what they should be.
    /// </summary>
    private static List<string> Added(Dictionary<(string Table, string Column), List<string>> added, bool optional) => added
        .Where(x => AdoConstants.OptionalTableNames.Any(t => t.Table == x.Key.Table) == optional)
        .Select(x => $"{x.Key.Table}.{x.Key.Column} from {string.Join(" and ", x.Value)}")
        .ToList();

    /// <summary>The constants' entries in the same shape, with each migration formatted for the dialect.</summary>
    private static List<string> Probed(IEnumerable<(string Table, string Column, string Migration)> entries, string dialect) => entries
        .Select(c => $"{c.Table.ToUpperInvariant()}.{c.Column.ToUpperInvariant()} from {MigrationFiles.PathOf(c.Migration, dialect)}")
        .ToList();
}
