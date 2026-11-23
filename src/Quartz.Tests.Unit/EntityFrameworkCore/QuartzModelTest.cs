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

#nullable enable

using System.Globalization;
using System.Text.RegularExpressions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

using Quartz.EntityFrameworkCore;
using Quartz.Impl.AdoJobStore;
using Quartz.Tests.Unit.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.EntityFrameworkCore;

/// <summary>
/// The model <c>AddQuartzTables</c> adds, under each provider it has one for: the DDL the provider writes for it,
/// that DDL against the job store's own script, and what each release added.
/// </summary>
public sealed class QuartzModelTest
{
    private static IEnumerable<string> Dialects() => EntityFrameworkCoreContext.Dialects;

    /// <summary>
    /// What <c>Database.GenerateCreateScript()</c> writes, which is the DDL of an initial migration: the reviewed
    /// record of what an application's database gets.
    /// </summary>
    [TestCaseSource(nameof(Dialects))]
    public async Task TheProviderWritesThisCreateScript(string dialect)
    {
        await using EntityFrameworkCoreContext context = EntityFrameworkCoreContext.Quartz(dialect);

        await Verify(context.Database.GenerateCreateScript().Replace("\r\n", "\n", StringComparison.Ordinal), extension: "sql")
            .UseDirectory("../Verify")
            .UseFileName($"QuartzModelTest_CreateScript_{dialect}");
    }

    /// <summary>
    /// The provider's DDL declares what <c>create_&lt;dialect&gt;.sql</c> declares: the same tables, columns, types,
    /// nullability, defaults, keys and indexes, read by the parser the scripts are compared with each other by.
    /// Constraint names are not compared: EF Core names the keys.
    /// </summary>
    /// <remarks>
    /// The differences listed are the ones the provider makes and the database does not see, except MySQL's
    /// acquisition index, which <c>CompleteQuartzSchema</c> creates again with its direction.
    /// </remarks>
    [TestCaseSource(nameof(Dialects))]
    public async Task TheCreateScriptDeclaresTheJobStoresSchema(string dialect)
    {
        await using EntityFrameworkCoreContext context = EntityFrameworkCoreContext.Quartz(dialect);

        SqlObjects ours = SqlObjects.Parse(ScriptSpelling(context.Database.GenerateCreateScript()), dialect);
        SqlObjects script = SqlObjects.Parse(SchemaScriptTest.GeneratedScript(dialect), dialect);

        Dictionary<string, Dictionary<string, SqlColumn>> expectedTables = script.Tables.ToDictionary(
            t => t.Key,
            t => t.Value.ToDictionary(c => c.Key, c => Expected(dialect, c.Value), StringComparer.Ordinal),
            StringComparer.Ordinal);

        ours.Tables.Should().BeEquivalentTo(expectedTables);
        ours.PrimaryKeys.Should().BeEquivalentTo(script.PrimaryKeys);
        ours.ForeignKeys.Should().Equal(script.ForeignKeys);
        ours.Indexes.Should().BeEquivalentTo(script.Indexes.ToDictionary(
            i => i.Key,
            i => dialect == "mysql_innodb"
                ? i.Value.Replace(" ASC", "", StringComparison.Ordinal).Replace(" DESC", "", StringComparison.Ordinal)
                : i.Value.Replace(" ASC", "", StringComparison.Ordinal),
            StringComparer.Ordinal),
            "the provider writes every index the script declares, and Oracle's MySQL provider leaves out a column's direction, "
            + "which CompleteQuartzSchema puts back");
        ours.Triggers.Should().BeEmpty("a model cannot declare a trigger; CompleteQuartzSchema creates SQLite's");
    }

    private static readonly Regex CastLiteral = new(@"CAST\((?<value>\d+) AS \w+\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>
    /// The provider's script in the spelling the parser reads: EF Core cuts a constraint name it made up with a
    /// <c>~</c>, which is no identifier character, and writes a default on SQL Server as its literal cast to the
    /// column's type, <c>CAST(0 AS bit)</c> for the script's <c>0</c>. Neither is part of what is compared.
    /// </summary>
    private static string ScriptSpelling(string script) =>
        CastLiteral.Replace(script.Replace('~', '_'), "${value}");

    /// <summary>
    /// How the provider spells what the script spells otherwise, where the database stores the two the same way.
    /// An index column's <c>ASC</c> is the default, and no provider writes it.
    /// </summary>
    private static SqlColumn Expected(string dialect, SqlColumn column) => dialect switch
    {
        // MySQL stores BOOLEAN as TINYINT(1), and Oracle's provider writes a bool any other way with a (1) after it.
        "mysql_innodb" when column.Type == "BOOLEAN" => column with { Type = "TINYINT(1)" },
        _ => column,
    };

    /// <summary>The model has every table the job store reads, in each dialect.</summary>
    [TestCaseSource(nameof(Dialects))]
    public async Task TheModelHasEveryTableTheStoreReads(string dialect)
    {
        await using EntityFrameworkCoreContext context = EntityFrameworkCoreContext.Quartz(dialect);

        IEnumerable<string> tables = context.DesignModel.GetRelationalModel().Tables
            .Select(t => t.Name.ToUpperInvariant()["QRTZ_".Length..]);

        tables.Should().BeEquivalentTo([.. AdoConstants.AllTableNames, .. AdoConstants.OptionalTableNames.Select(t => t.Table)]);
    }

    /// <summary>
    /// The releases since 4.0 with a <c>database/migrations</c> folder, oldest first: the ones that changed the schema.
    /// </summary>
    private static string[] ReleasesWithAMigration()
    {
        DirectoryInfo migrations = new(Path.Combine(RepositoryRoot.Find().FullName, "database", "migrations"));
        migrations.Exists.Should().BeTrue("the premise: the migrations are under database/migrations");

        return [.. migrations.GetDirectories()
            .Select(d => Version.TryParse(d.Name, out Version? version) ? version : null)
            .OfType<Version>()
            .Where(v => v > new Version(4, 0))
            .Order()
            .Select(v => v.ToString())];
    }

    /// <summary>
    /// Each release with a migration since 4.0, and the one before it: the releases the model's <c>AddedBy</c> marks
    /// have to be exactly these, so a release that ships a migration without marking what it added, or marks a
    /// release no migration ships, fails here.
    /// </summary>
    private static IEnumerable<TestCaseData> ReleaseBoundaries()
    {
        string[] releases = ReleasesWithAMigration();

        string[] marked = [.. QuartzDialect.PostgreSql.Tables
            .SelectMany(t => new[] { t.AddedBy }
                .Concat(t.Columns.Select(c => c.AddedBy))
                .Concat(t.Indexes.Select(i => i.AddedBy)))
            .OfType<string>()
            .Distinct()
            .OrderBy(Version.Parse)];

        marked.Should().Equal(releases,
            "every release since 4.0 with a database/migrations folder marks what it added with AddedBy in "
            + "build/Build.DatabaseSchema.cs, and every AddedBy names such a release");

        string previous = "4.0";
        foreach (string release in releases)
        {
            foreach (string dialect in EntityFrameworkCoreContext.Dialects)
            {
                yield return new TestCaseData(dialect, previous, release).SetArgDisplayNames(dialect, release);
            }

            previous = release;
        }
    }

    /// <summary>
    /// What a release added, as the differ finds it between the model as of the release before and as of that
    /// release: tables, columns and indexes only, and the ones <c>database/migrations/&lt;release&gt;</c> adds. An
    /// upgrade of Quartz is therefore an add-only migration, which a previous minor's nodes run beside.
    /// </summary>
    [TestCaseSource(nameof(ReleaseBoundaries))]
    public async Task EachReleaseOnlyAddsWhatItsMigrationAdds(string dialect, string previous, string release)
    {
        await using EntityFrameworkCoreContext before = EntityFrameworkCoreContext.QuartzAsOf(dialect, previous);
        await using EntityFrameworkCoreContext after = EntityFrameworkCoreContext.QuartzAsOf(dialect, release);

        IReadOnlyList<MigrationOperation> operations = after.DifferencesFrom(before);

        operations.Should().NotBeEmpty();
        operations.Select(o => o.GetType().Name).Distinct().Should().BeSubsetOf(
            [nameof(AddColumnOperation), nameof(CreateTableOperation), nameof(CreateIndexOperation)],
            "a 4.x release adds tables, nullable columns and indexes, and nothing else");

        operations.OfType<AddColumnOperation>().Should().AllSatisfy(c => c.IsNullable.Should().BeTrue(
            $"{c.Table}.{c.Name} is added to a table a previous minor's nodes are still writing"));

        IEnumerable<string> added = operations.Select(o => o switch
        {
            AddColumnOperation c => $"{c.Table}.{c.Name}",
            CreateTableOperation t => t.Name,
            CreateIndexOperation i => i.Name,
            _ => o.GetType().Name,
        }).Select(n => n.ToUpperInvariant());

        added.Should().BeEquivalentTo(WhatTheMigrationAdds(dialect, release),
            $"database/migrations/{release} is what a database the scripts created runs, and the model's difference has to be that");
    }

    /// <summary>
    /// The model as of the newest release with a migration is the model: nothing is marked as added by a release
    /// after it.
    /// </summary>
    [TestCaseSource(nameof(Dialects))]
    public async Task NothingIsAddedAfterTheNewestMigration(string dialect)
    {
        string newest = ReleasesWithAMigration()[^1];

        await using EntityFrameworkCoreContext asOfNewest = EntityFrameworkCoreContext.QuartzAsOf(dialect, newest);
        await using EntityFrameworkCoreContext current = EntityFrameworkCoreContext.Quartz(dialect);

        current.DifferencesFrom(asOfNewest).Should().BeEmpty(
            $"what the model marks as added after {newest} has no database/migrations folder, so a database the scripts "
            + "created would never get it");
    }

    private static readonly Regex AddedColumn = new(
        @"ALTER TABLE\s+(?:\[?DBO\]?\.)?[\[`""]?(?<table>QRTZ_\w+)[\]`""]?\s+ADD\s+(?:COLUMN\s+)?(?:IF NOT EXISTS\s+)?\(?[\[`""]?(?<column>\w+)",
        RegexOptions.CultureInvariant);

    private static readonly Regex CreatedTable = new(
        @"CREATE TABLE\s+(?:IF NOT EXISTS\s+)?(?:\[?DBO\]?\.)?[\[`""]?(?<table>QRTZ_\w+)",
        RegexOptions.CultureInvariant);

    private static readonly Regex CreatedIndex = new(
        @"(?:CREATE INDEX\s+(?:IF NOT EXISTS\s+)?|\bKEY\s+)[\[`""]?(?<index>IDX_QRTZ_\w+)",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// The tables, columns and indexes a release's migration scripts for one dialect add, read off the scripts with
    /// their comments removed.
    /// </summary>
    private static HashSet<string> WhatTheMigrationAdds(string dialect, string release)
    {
        DirectoryInfo folder = new(Path.Combine(RepositoryRoot.Find().FullName, "database", "migrations", release));
        folder.Exists.Should().BeTrue($"the premise: {release} shipped a migration");

        HashSet<string> added = new(StringComparer.Ordinal);
        foreach (FileInfo file in folder.GetFiles($"*_{dialect}.sql"))
        {
            string sql = string.Join("\n", File.ReadAllLines(file.FullName)
                .Select(l => l.Contains("--", StringComparison.Ordinal) ? l[..l.IndexOf("--", StringComparison.Ordinal)] : l))
                .ToUpperInvariant();

            added.UnionWith(AddedColumn.Matches(sql).Select(m => $"{m.Groups["table"].Value}.{m.Groups["column"].Value}"));
            added.UnionWith(CreatedTable.Matches(sql).Select(m => m.Groups["table"].Value));
            added.UnionWith(CreatedIndex.Matches(sql).Select(m => m.Groups["index"].Value));
        }

        added.Should().NotBeEmpty($"the premise: database/migrations/{release} has a {dialect} script that adds something");
        return added;
    }
}
