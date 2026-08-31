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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// The list of tables the store checks is there before it starts, held against the scripts that create
/// them.
/// </summary>
/// <remarks>
/// <para>
/// <c>AdoConstants.AllTableNames</c> is the whole of what <c>StdAdoDelegate.ValidateSchema</c> probes
/// with a <c>SELECT 1</c>, and <c>JobStoreSupport.PerformSchemaValidation</c> is on by default. A table
/// the scripts create and that list does not name is therefore one a database can be missing while
/// startup reports the schema good — which is what #3564 was, about <c>QRTZ_SIMPROP_TRIGGERS</c>.
/// </para>
/// <para>
/// The other half of the subject — installing a schema, dropping one table, and watching the store
/// refuse to start — is <c>Quartz.Tests.Integration.Impl.AdoJobStore.SchemaValidationTest</c>. It needs
/// a database, even if only a SQLite file, so it cannot live here. These cases need none.
/// </para>
/// </remarks>
public class SchemaValidationTest
{
    /// <summary>
    /// The fresh-install scripts, named rather than enumerated so a reader can see which ones took part.
    /// <see cref="TheFreshInstallScriptsAreTheOnesThisTestKnowsAbout" /> keeps the list honest.
    /// </summary>
    private static readonly string[] FreshInstallScripts =
    {
        "tables_firebird.sql",
        "tables_mysql_innodb.sql",
        "tables_oracle.sql",
        "tables_postgres.sql",
        "tables_sqlServer.sql",
        "tables_sqlServerMOT.sql",
        "tables_sqlServer_Below2016.sql",
        "tables_sqlite.sql"
    };

    private static readonly Regex CreateTableStatement = new Regex(
        @"CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?(?<name>[^\s(]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// The eleven tables, spelled out rather than counted.
    /// </summary>
    /// <remarks>
    /// A count would have passed all along: <c>QRTZ_SIMPROP_TRIGGERS</c> was missing from the list
    /// because its name lived on the delegate that writes it rather than on <c>AdoConstants</c>, and ten
    /// was what everything downstream agreed the answer was (#3564). Naming them is what makes a twelfth
    /// table, or an eleventh that goes missing again, a failing test here rather than an insert against a
    /// table nobody checked for.
    /// </remarks>
    [Test]
    public void TheStoreValidatesEveryTableOfTheSchema()
    {
        AdoConstants.AllTableNames.Should().Equal(
            new[]
            {
                "JOB_DETAILS",
                "TRIGGERS",
                "SIMPLE_TRIGGERS",
                "SIMPROP_TRIGGERS",
                "CRON_TRIGGERS",
                "BLOB_TRIGGERS",
                "FIRED_TRIGGERS",
                "CALENDARS",
                "PAUSED_TRIGGER_GRPS",
                "LOCKS",
                "SCHEDULER_STATE"
            },
            "these are the tables the schema has, and the list is the whole of what schema validation "
            + "probes at startup");
    }

    /// <summary>
    /// Every table a fresh install creates is one the store asks about.
    /// </summary>
    /// <remarks>
    /// This is the guard that catches the class of bug #3564 was. A hand-written list of eleven would
    /// not have: whoever wrote it would have written ten, for the same reason the original list did.
    /// Reading the answer out of each dialect's script instead means the two can only agree by being
    /// right.
    /// </remarks>
    [TestCaseSource(nameof(FreshInstallScripts))]
    public void EveryTableTheFreshInstallScriptCreatesIsOneTheStoreValidates(string script)
    {
        List<string> created = TablesCreatedBy(script);

        created.Should().BeEquivalentTo(AdoConstants.AllTableNames,
            $"database/tables/{script} and AdoConstants.AllTableNames describe the same schema — one "
            + "creates it, the other is the whole of what startup checks is there");
    }

    /// <summary>
    /// The list above names every script in <c>database/tables/</c>, so a dialect added later is not
    /// quietly left unchecked.
    /// </summary>
    [Test]
    public void TheFreshInstallScriptsAreTheOnesThisTestKnowsAbout()
    {
        IEnumerable<string> onDisk = Directory
            .GetFiles(Path.Combine(RepositoryRoot(), "database", "tables"), "tables_*.sql")
            .Select(path => Path.GetFileName(path));

        onDisk.Should().BeEquivalentTo(FreshInstallScripts,
            "a fresh-install script this fixture does not name is a schema nothing holds "
            + "AdoConstants.AllTableNames to");
    }

    /// <summary>The tables a script creates, without the configurable <c>QRTZ_</c> prefix.</summary>
    private static List<string> TablesCreatedBy(string script)
    {
        string sql = File.ReadAllText(Path.Combine(RepositoryRoot(), "database", "tables", script));

        return CreateTableStatement.Matches(sql)
            .Cast<Match>()
            .Select(match => UnprefixedTableName(match.Groups["name"].Value))
            .ToList();
    }

    /// <summary>
    /// The bare name the store would append its table prefix to: no schema qualifier, no quoting, no
    /// case, and no <c>QRTZ_</c>, which is configuration rather than part of the name.
    /// </summary>
    private static string UnprefixedTableName(string identifier)
    {
        string name = identifier
            .Split('.')
            .Last()
            .Trim('[', ']', '"', '`', ';')
            .ToUpperInvariant();

        return name.StartsWith("QRTZ_", StringComparison.Ordinal) ? name.Substring("QRTZ_".Length) : name;
    }

    /// <summary>
    /// The working tree the test assembly was built in, found by walking up to the
    /// <c>database/tables</c> the scripts live in.
    /// </summary>
    /// <remarks>
    /// Counting directories up from the output path would be one more thing to fix when a target
    /// framework is added or the output layout moves.
    /// </remarks>
    private static string RepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "database", "tables")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull(
            $"the fresh-install scripts are read from the working tree, and none was found above {AppContext.BaseDirectory}");

        return directory.FullName;
    }
}
