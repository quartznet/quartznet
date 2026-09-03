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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Impl.AdoJobStore;

/// <summary>
/// What a store checks is there before it starts, and what it does when one of them is not.
/// </summary>
/// <remarks>
/// <para>
/// The database is a real one: SQLite is a file, so a schema installed from
/// <c>database/tables/tables_sqlite.sql</c> — the script a reader is told to run — costs a temporary
/// path, and the store that validates it runs in this process. Nothing here needs a container, which
/// is why the case is a unit test rather than one more dialect in <c>SchemaProvisioningTest</c>.
/// </para>
/// <para>
/// Installing the script and then dropping one table is the shape of the real failure this guards
/// against: a database that is almost right. <c>SchemaScriptTest</c> is the other half, and compares
/// the list against every dialect's script without needing a database at all.
/// </para>
/// </remarks>
public sealed class SchemaValidationTest
{
    /// <summary>The tables the cases below drop one at a time.</summary>
    private static readonly string[] EveryValidatedTable = AdoConstants.AllTableNames;

    private string databaseFile = null!;
    private string connectionString = null!;
    private ServiceProvider? container;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        databaseFile = Path.Combine(Path.GetTempPath(), $"quartz-validation-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databaseFile}";
    }

    [TearDown]
    public async Task DeleteDatabase()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
            container = null;
        }

        SqliteConnection.ClearAllPools();

        if (File.Exists(databaseFile))
        {
            File.Delete(databaseFile);
        }
    }

    /// <summary>
    /// The twelve tables, spelled out rather than counted.
    /// </summary>
    /// <remarks>
    /// A count would have passed all along: <c>QRTZ_SIMPROP_TRIGGERS</c> was missing from the list
    /// because its name lived on the delegate that writes it rather than on <c>AdoConstants</c>, and
    /// eleven was what everything downstream agreed the answer was (#3564). Naming them is what makes
    /// a thirteenth table, or a twelfth that goes missing again, a failing test here rather than an
    /// insert against a table nobody checked for.
    /// </remarks>
    [Test]
    public void TheStoreValidatesEveryTableOfTheSchema()
    {
        AdoConstants.AllTableNames.Should().Equal(
            [
                "JOB_DETAILS",
                "TRIGGERS",
                "SIMPLE_TRIGGERS",
                "SIMPROP_TRIGGERS",
                "CRON_TRIGGERS",
                "BLOB_TRIGGERS",
                "FIRED_TRIGGERS",
                "CALENDARS",
                "PAUSED_TRIGGER_GRPS",
                "PAUSED_JOB_GRPS",
                "LOCKS",
                "SCHEDULER_STATE"
            ],
            "these are the tables the schema has, in the order the scripts create them, and the list is "
            + "the whole of what SchemaProvisioning.Validate probes at startup");
    }

    /// <summary>
    /// The control: an untouched schema starts.
    /// </summary>
    /// <remarks>
    /// Without it, a harness that installed nothing — a script that failed to run, a path that moved —
    /// would make every case below pass for the wrong reason, since a database with no tables at all
    /// also refuses to start.
    /// </remarks>
    [Test]
    public async Task AStoreStartsAgainstTheSchemaTheFreshInstallScriptCreates()
    {
        InstallSchemaFromFreshInstallScript();

        Func<Task> act = async () => await (await GetScheduler(
            nameof(AStoreStartsAgainstTheSchemaTheFreshInstallScriptCreates))).Shutdown();

        await act.Should().NotThrowAsync(
            "the twelve tables database/tables/tables_sqlite.sql creates are the twelve the store "
            + "validates, so a schema installed straight from it needs nothing else");
    }

    /// <summary>
    /// Every table on the list is one a database can be missing and be refused for.
    /// </summary>
    /// <remarks>
    /// Running the case for all twelve is what makes the list mean something: a name added to
    /// <c>AllTableNames</c> that no statement can query — a view, a misspelling, a table only some
    /// dialects have — would pass the two tests above and fail here.
    /// </remarks>
    [TestCaseSource(nameof(EveryValidatedTable))]
    public async Task AStoreRefusesToStartWhenATableIsMissing(string table)
    {
        InstallSchemaFromFreshInstallScript();
        Execute($"DROP TABLE QRTZ_{table}");

        Func<Task> act = () => GetScheduler(nameof(AStoreRefusesToStartWhenATableIsMissing));

        SchedulerException failure = (await act.Should().ThrowAsync<SchedulerException>(
                "a database missing a table Quartz writes to has to be a startup failure, not a "
                + "scheduler that runs until something schedules the trigger type that needs it"))
            .WithMessage("*schema validation failed*")
            .Which;

        MessagesOf(failure).Should().ContainMatch($"*QRTZ_{table}*",
            "the message names the table that is missing, since the reader's next move is to run the "
            + "migration or the fresh-install script that creates it");

        failure.Message.Should().Contain("database/migrations/4.0/schema_30_to_40_upgrade_sqlite.sql",
            "far more readers of this message are upgrading from 3.x than are installing fresh, and "
            + "nothing else Quartz says at run time points at database/migrations/ at all");
    }

    /// <summary>
    /// The scripts the message sends a reader to are named for the database in front of them, and the
    /// SQL Server one carries a warning of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>database/tables/tables_sqlServer.sql</c> begins <c>USE [enter_db_name_here];</c> and answers
    /// <c>Msg 911</c> as it ships. The reader who most needs to know that is the one this message sent
    /// there, and the placeholder appeared in no page and in no message until the rc.1 rehearsal ran
    /// the script it was told to run.
    /// </para>
    /// <para>
    /// The connection goes nowhere on purpose: what is under test is which file the message names,
    /// which the store decides from the driver delegate rather than from anything it reads. Port 1 is
    /// closed, and a one-second connect timeout keeps the case as quick as the rest of them.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TheMessageNamesTheScriptForTheDatabaseInFrontOfIt()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = nameof(TheMessageNamesTheScriptForTheDatabaseInFrontOfIt);
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
                store.UseSqlServer("Server=127.0.0.1,1;Database=quartz;Connect Timeout=1;Encrypt=False"));
        });

        container = services.BuildServiceProvider();

        Func<Task> act = async () => await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        SchedulerException failure = (await act.Should().ThrowAsync<SchedulerException>(
                "a store that cannot read its schema has to refuse to start whatever the reason"))
            .WithMessage("*schema validation failed*")
            .Which;

        failure.Message.Should().Contain("database/tables/tables_sqlServer.sql")
            .And.Contain("database/migrations/4.0/schema_30_to_40_upgrade_sqlServer.sql")
            .And.Contain("USE [enter_db_name_here];",
                "the first statement of that script fails as it ships, and this message is what sent "
                + "the reader to it");
    }

    /// <summary>
    /// Runs <c>database/tables/tables_sqlite.sql</c>, the script the documentation tells a reader to
    /// run, against the empty file.
    /// </summary>
    /// <remarks>
    /// The whole text goes to one command: SQLite's own parser splits it, which is the only splitter
    /// that gets the <c>CREATE TRIGGER … BEGIN … END;</c> blocks right.
    /// </remarks>
    private void InstallSchemaFromFreshInstallScript()
    {
        string path = Path.Combine(RepositoryRoot.Find().FullName, "database", "tables", "tables_sqlite.sql");

        Execute(File.ReadAllText(path));
    }

    private void Execute(string sql)
    {
        using SqliteConnection connection = new(connectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private async Task<IScheduler> GetScheduler(string schedulerName)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = schedulerName;
                options.InstanceId = "one";
            });

            // No ProvisionSchema(): SchemaProvisioning.Validate is the default, and validating what
            // somebody else installed is the whole subject here.
            q.UsePersistentStore(store => store.UseSqlite(SqliteFactory.Instance, connectionString));
        });

        container = services.BuildServiceProvider();

        return await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
    }

    /// <summary>Every message in an exception chain, outermost first.</summary>
    private static List<string> MessagesOf(Exception exception)
    {
        List<string> messages = [];

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return messages;
    }
}
