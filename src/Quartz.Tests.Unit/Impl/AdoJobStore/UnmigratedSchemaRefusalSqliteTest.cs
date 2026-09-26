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
/// What a 4.x store does when it meets a schema Quartz 3.x created and nobody migrated.
/// </summary>
/// <remarks>
/// <para>
/// The schema is the real one: <c>SchemaBaselines/3.20/tables_sqlite.sql</c>, vendored verbatim from
/// the <c>v3.20.0</c> tag, so this is a database a 3.x deployment actually has rather than one written
/// to fail. SQLite is a file, which is what lets the whole thing be a unit test;
/// <c>SchemaProvisioningTest</c> asks the same question of a container.
/// </para>
/// <para>
/// Both halves of the rc.1 rehearsal's finding are here. A 3.20 database is missing one table and two
/// columns, so a table-level check refused it for the table and said nothing about the columns — and
/// the remedy it recommended, <c>ProvisionSchema()</c>, made the table, reported the schema validated,
/// started the scheduler, and then failed every acquisition and every misfire pass for ever on
/// <c>RETRY_POLICY</c>. Nothing fired, the log said "Successfully validated", and the process exited
/// zero. So: the columns are probed too, provisioning refuses a schema 4.x did not create instead of
/// building around it, and both messages name the migration script rather than the two things that
/// make the database worse.
/// </para>
/// </remarks>
public sealed class UnmigratedSchemaRefusalSqliteTest
{
    private SqliteTestDatabase database = null!;
    private ServiceProvider? container;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("unmigrated");
    }

    [TearDown]
    public async Task DeleteDatabase()
    {
        if (container is not null)
        {
            await container.DisposeAsync();
            container = null;
        }

        database.Dispose();
    }

    /// <summary>
    /// The table-level half, which has always worked — with the message the rehearsal asked for.
    /// </summary>
    [Test]
    public async Task A3xSchemaIsRefusedAndTheMessageNamesTheMigrationRatherThanTheTwoThingsThatMakeItWorse()
    {
        Install320Schema();

        SchedulerException failure = await StartAndCatch(nameof(A3xSchemaIsRefusedAndTheMessageNamesTheMigrationRatherThanTheTwoThingsThatMakeItWorse));

        failure.Message.Should().Contain("schema validation failed")
            .And.Contain("database/migrations/4.0/schema_30_to_40_upgrade_sqlite.sql",
                "the reader of this message is upgrading from 3.x far more often than not, and nothing "
                + "else Quartz says at run time points at database/migrations/ at all")
            .And.Contain("never adds a column to a table that exists",
                "ProvisionSchema() is the other thing the message offers, and on a 3.x schema it makes "
                + "the missing table and leaves the missing columns")
            .And.Contain("fresh installs only",
                "the scripts in database/tables/ drop the schema they find, which is the whole database "
                + "for someone who ran them to fix this");

        MessagesOf(failure).Should().ContainMatch("*QRTZ_PAUSED_JOB_GRPS*",
            "the table 4.x added is the first thing missing, and naming it is how the reader knows "
            + "which migration they skipped");
    }

    /// <summary>
    /// The column half: the schema <c>ProvisionSchema()</c> used to leave behind, which validated and
    /// then fired nothing.
    /// </summary>
    /// <remarks>
    /// Built here the way provisioning built it — the 3.20 schema plus the one table 4.x added — so
    /// what is under test is exactly the database the rehearsal was left with. A table-level check
    /// passes it; every trigger statement 4.x issues names <c>RETRY_POLICY</c>.
    /// </remarks>
    [Test]
    public async Task ASchemaWithEveryTableButNotEveryColumnIsRefusedForTheColumn()
    {
        Install320Schema();
        Execute(
            """
            CREATE TABLE QRTZ_PAUSED_JOB_GRPS (
              SCHED_NAME NVARCHAR(120) NOT NULL,
              JOB_GROUP NVARCHAR(150) NOT NULL,
              PRIMARY KEY (SCHED_NAME,JOB_GROUP)
            );
            """);

        SchedulerException failure = await StartAndCatch(nameof(ASchemaWithEveryTableButNotEveryColumnIsRefusedForTheColumn));

        MessagesOf(failure).Should().ContainMatch($"*{AdoConstants.ColumnRetryPolicy}*",
            "a startup that reports twelve validated tables and then fails every acquisition for ever "
            + "on a column is the worst outcome available, so the column is probed at startup and the "
            + "failure names it");

        failure.Message.Should().Contain("database/migrations/4.0/schema_30_to_40_upgrade_sqlite.sql");
    }

    /// <summary>
    /// Provisioning does not build on top of a schema 4.x did not create.
    /// </summary>
    /// <remarks>
    /// The tell is a column, not a table count: a table 4.x created has every column 4.x needs,
    /// because a table arrives whole, so <c>QRTZ_TRIGGERS</c> without <c>RETRY_POLICY</c> is a table
    /// something else made. Counting tables cannot say it — a cluster whose winner died half-way
    /// through the create script leaves the same count, and that schema is one provisioning may and
    /// must finish.
    /// </remarks>
    [Test]
    public async Task ProvisioningA3xSchemaCreatesNothingAndSaysWhy()
    {
        Install320Schema();

        SchedulerException failure = await StartAndCatch(nameof(ProvisioningA3xSchemaCreatesNothingAndSaysWhy), provision: true);

        failure.Message.Should().Contain("was not created by Quartz 4.x")
            .And.Contain($"QRTZ_TRIGGERS.{AdoConstants.ColumnRetryPolicy}",
                "the column that says whose schema this is gets named, since it is the evidence")
            .And.Contain("Nothing was created")
            .And.Contain("database/migrations/4.0/schema_30_to_40_upgrade_sqlite.sql");

        TableExists("QRTZ_PAUSED_JOB_GRPS").Should().BeFalse(
            "creating the one table a 3.x schema is missing is what produced a scheduler that started, "
            + "logged itself validated and then fired nothing — so a 3.x schema is one CreateIfMissing "
            + "leaves exactly as it found it");
    }

    /// <summary>
    /// The other half of that rule: a schema whose tables 4.x did create is one provisioning finishes.
    /// </summary>
    /// <remarks>
    /// This is a cluster cold-start, in miniature: a node whose create died part-way leaves tables
    /// that are 4.x-shaped and tables that are not there at all, and the next node has to fill the
    /// gaps rather than refuse — which is what <c>SchemaProvisioningTest</c>'s race case asserts
    /// against a real database of each dialect, Firebird included, where it is the ordinary outcome.
    /// </remarks>
    [Test]
    public async Task ProvisioningFinishesASchemaItsOwnCreateLeftHalfMade()
    {
        await using (ServiceProvider first = BuildProvisioningContainer(nameof(ProvisioningFinishesASchemaItsOwnCreateLeftHalfMade)))
        {
            IScheduler scheduler = await first.GetRequiredService<ISchedulerFactory>().GetScheduler();
            await scheduler.Shutdown();
        }

        // What a create that died at the last statement leaves behind.
        Execute("DROP TABLE QRTZ_LOCKS");

        Func<Task> act = async () => await (await GetScheduler(
            nameof(ProvisioningFinishesASchemaItsOwnCreateLeftHalfMade) + "_second", provision: true)).Shutdown();

        await act.Should().NotThrowAsync(
            "the tables that are there are 4.x's own, so this schema is one provisioning may finish — "
            + "refusing it would strand every cluster whose first node died mid-script");

        TableExists("QRTZ_LOCKS").Should().BeTrue();
    }

    /// <summary>
    /// The control: the same schema, migrated, starts — with provisioning on, which is the
    /// configuration the two refusals above were made under.
    /// </summary>
    /// <remarks>
    /// Without it every case here would pass against a store that refused everything, and the migration
    /// scripts are what the messages tell the reader to run — so this is also the claim that following
    /// them works. All of them: 4.2 added three columns and 4.3 two, and the message names those
    /// scripts too.
    /// </remarks>
    [Test]
    public async Task TheMigrationsTheMessageNamesAreTheOnesThatMakeTheSchemaStart()
    {
        Install320Schema();
        ApplyMigration("4.0", "schema_30_to_40_upgrade_sqlite.sql");
        ApplyMigration("4.2", "add_continuations_sqlite.sql");
        ApplyMigration("4.3", "add_fire_progress_sqlite.sql");

        Func<Task> act = async () => await (await GetScheduler(
            nameof(TheMigrationsTheMessageNamesAreTheOnesThatMakeTheSchemaStart), provision: true)).Shutdown();

        await act.Should().NotThrowAsync(
            "the migrations are what turn a 3.20 database into one 4.x validates, which is the whole "
            + "of what the two messages above ask the reader to do");
    }

    /// <summary>
    /// A database created by 4.0 or 4.1 is missing only what 4.2 added, and the refusal says so
    /// without sending the reader through the 3.x upgrade they do not need.
    /// </summary>
    [Test]
    public async Task A41SchemaIsRefusedForTheColumnsFourTwoAdded()
    {
        Install320Schema();
        ApplyMigration("4.0", "schema_30_to_40_upgrade_sqlite.sql");

        SchedulerException failure = await StartAndCatch(nameof(A41SchemaIsRefusedForTheColumnsFourTwoAdded));

        MessagesOf(failure).Should().ContainMatch($"*{AdoConstants.ColumnContinuesTriggerName}*",
            "every trigger 4.2 stores names all three columns, so a database without them fails on the "
            + "first write rather than at startup unless startup probes for them");

        failure.Message.Should().Contain("database/migrations/4.2/add_continuations_sqlite.sql",
            "the reader of this message has a 4.0 or 4.1 database and needs one script, which the "
            + "message names beside the one they have already run");
    }

    /// <summary>
    /// A database created by 4.2 is missing only the two progress columns 4.3 added, and the refusal
    /// names them and the one script that adds them.
    /// </summary>
    [Test]
    public async Task A42SchemaIsRefusedForTheColumnsFourThreeAdded()
    {
        Install320Schema();
        ApplyMigration("4.0", "schema_30_to_40_upgrade_sqlite.sql");
        ApplyMigration("4.2", "add_continuations_sqlite.sql");

        SchedulerException failure = await StartAndCatch(nameof(A42SchemaIsRefusedForTheColumnsFourThreeAdded));

        MessagesOf(failure).Should().ContainMatch($"*{AdoConstants.ColumnProgress}*",
            "every 4.3 node reads the progress columns whenever it lists what is running, so a database "
            + "without them fails the dashboard rather than startup unless startup probes for them");

        failure.Message.Should().Contain("database/migrations/4.3/add_fire_progress_sqlite.sql",
            "a reader with a 4.2 database needs exactly one script, and the message has to name it");
    }

    private void Install320Schema()
    {
        Execute(File.ReadAllText(RepositoryFile(
            "src", "Quartz.Tests.Integration", "SchemaBaselines", "3.20", "tables_sqlite.sql")));
    }

    /// <summary>
    /// Runs one of the SQLite migration scripts the way its own header tells a reader on a
    /// partly-migrated database to run it.
    /// </summary>
    /// <remarks>
    /// SQLite has no conditional DDL for <c>ADD COLUMN</c>, so its scripts are the ones that say NOT
    /// IDEMPOTENT and tell a reader whose database took some of the optional 3.x migrations to "check
    /// PRAGMA table_info(&lt;table&gt;) and apply only the sections whose columns are absent". A 3.20
    /// database took all of them, so this does exactly that check and nothing else — the same shim
    /// <c>MigrationScriptTest</c> carries, for the same reason.
    /// </remarks>
    private void ApplyMigration(string version, string fileName)
    {
        string script = File.ReadAllText(
            RepositoryFile("database", "migrations", version, fileName));

        // Comments first: they carry semicolons of their own, and splitting on those would cut a
        // sentence in half and hand the fragment to SQLite as a statement.
        string statements = string.Join('\n', script
            .Replace("\r\n", "\n")
            .Split('\n')
            .Where(line => !line.TrimStart().StartsWith("--", StringComparison.Ordinal)));

        using SqliteConnection connection = new(database.ConnectionString);
        connection.Open();

        foreach (string statement in statements.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string sql = statement.Trim();

            if (sql.Length == 0 || AddsAColumnThatIsAlreadyThere(connection, sql))
            {
                continue;
            }

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }

    private static bool AddsAColumnThatIsAlreadyThere(SqliteConnection connection, string sql)
    {
        string[] words = sql.Split([' ', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries);

        if (words.Length < 6
            || !words[0].Equals("ALTER", StringComparison.OrdinalIgnoreCase)
            || !words[3].Equals("ADD", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"SELECT COUNT(*) FROM pragma_table_info('{words[2]}') WHERE UPPER(name) = '{words[5].ToUpperInvariant()}'";

        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    /// <summary>
    /// A 4.2 database is missing the execution history's two tables until its own migration has run,
    /// and only a store that was told to keep a history notices.
    /// </summary>
    /// <remarks>
    /// That is what makes <c>4.2/add_execution_history_&lt;dialect&gt;.sql</c> optional: nothing else
    /// reads those tables, so a deployment that never calls <c>UseExecutionHistory()</c> never has to
    /// run it, and one that does is refused at startup rather than at the first firing.
    /// </remarks>
    [Test]
    public async Task ASchemaWithoutTheHistoryTablesIsRefusedOnlyWhenTheHistoryIsOn()
    {
        Install320Schema();
        ApplyMigration("4.0", "schema_30_to_40_upgrade_sqlite.sql");
        ApplyMigration("4.2", "add_continuations_sqlite.sql");
        ApplyMigration("4.3", "add_fire_progress_sqlite.sql");

        TableExists("QRTZ_EXECUTION_HISTORY").Should().BeFalse(
            "the migrations run so far are the ones every 4.2 database needs, and the history's are not "
            + "among them");

        Func<Task> withoutHistory = async () => await (await GetScheduler(
            nameof(ASchemaWithoutTheHistoryTablesIsRefusedOnlyWhenTheHistoryIsOn) + "-off", provision: false)).Shutdown();

        await withoutHistory.Should().NotThrowAsync(
            "a scheduler that keeps no history never reads those tables, so their absence is not a "
            + "reason to refuse it — which is the whole of what makes the migration optional");

        await container!.DisposeAsync();
        container = null;

        SchedulerException failure = await StartAndCatch(
            nameof(ASchemaWithoutTheHistoryTablesIsRefusedOnlyWhenTheHistoryIsOn) + "-on",
            configure: store => store.UseExecutionHistory());

        failure.Message.Should().Contain("database/migrations/4.2/add_execution_history_sqlite.sql",
            "the reader turned the history on against a database that has never had its tables, and the "
            + "script that creates them is the whole remedy");

        MessagesOf(failure).Should().ContainMatch("*database/migrations/4.3/add_execution_log_sqlite.sql*",
            "a table that is missing is missing the column 4.3 added to it as well, so both scripts are "
            + "named at once rather than one per restart");

        MessagesOf(failure).Should().ContainMatch("*QRTZ_EXECUTION_HISTORY*",
            "and the table that is missing is what says which feature they asked for");
    }

    /// <summary>
    /// A history table created by 4.2 is missing the column 4.3 added to it, and only a store that
    /// keeps its history there notices.
    /// </summary>
    [Test]
    public async Task A42HistoryTableIsRefusedForTheExecutionLogOnlyWhenTheHistoryIsOn()
    {
        Install320Schema();
        ApplyMigration("4.0", "schema_30_to_40_upgrade_sqlite.sql");
        ApplyMigration("4.2", "add_continuations_sqlite.sql");
        ApplyMigration("4.2", "add_execution_history_sqlite.sql");
        ApplyMigration("4.3", "add_fire_progress_sqlite.sql");

        Func<Task> withoutHistory = async () => await (await GetScheduler(
            nameof(A42HistoryTableIsRefusedForTheExecutionLogOnlyWhenTheHistoryIsOn) + "-off", provision: false)).Shutdown();

        await withoutHistory.Should().NotThrowAsync(
            "a scheduler that keeps no history never reads the table the column is on, so 4.3's history "
            + "migration is as optional as 4.2's");

        await container!.DisposeAsync();
        container = null;

        SchedulerException failure = await StartAndCatch(
            nameof(A42HistoryTableIsRefusedForTheExecutionLogOnlyWhenTheHistoryIsOn) + "-on",
            configure: store => store.UseExecutionHistory());

        MessagesOf(failure).Should().ContainMatch($"*{AdoConstants.ColumnExecutionLog}*",
            "every history row a 4.3 store writes names the column, so its absence is refused at startup "
            + "rather than dropping every row at the first firing");

        MessagesOf(failure).Should().ContainMatch("*database/migrations/4.3/add_execution_log_sqlite.sql*",
            "and the one script that adds it is the whole remedy");
    }

    /// <summary>
    /// The control for the cases above: the scripts they name are what make that schema start.
    /// </summary>
    [Test]
    public async Task TheHistoryMigrationIsWhatMakesAHistoryStoreStart()
    {
        Install320Schema();
        ApplyMigration("4.0", "schema_30_to_40_upgrade_sqlite.sql");
        ApplyMigration("4.2", "add_continuations_sqlite.sql");
        ApplyMigration("4.2", "add_execution_history_sqlite.sql");
        ApplyMigration("4.3", "add_fire_progress_sqlite.sql");
        ApplyMigration("4.3", "add_execution_log_sqlite.sql");

        Func<Task> act = async () => await (await GetScheduler(
            nameof(TheHistoryMigrationIsWhatMakesAHistoryStoreStart),
            provision: false,
            configure: store => store.UseExecutionHistory())).Shutdown();

        await act.Should().NotThrowAsync(
            "running the script the refusal names is what the reader is being asked to do, so it has to "
            + "be what turns the refusal into a scheduler that starts");
    }

    private async Task<SchedulerException> StartAndCatch(
        string schedulerName,
        bool provision = false,
        Action<IPersistentStoreBuilder>? configure = null)
    {
        Func<Task> act = () => GetScheduler(schedulerName, provision, configure);

        return (await act.Should().ThrowAsync<SchedulerException>(
                "a 4.x node against a schema it cannot use has to refuse to start, which is the one "
                + "outcome an operator can act on"))
            .Which;
    }

    private static IEnumerable<string> MessagesOf(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current.Message;
        }
    }

    private static string RepositoryFile(params string[] segments)
    {
        return Path.Combine([RepositoryRoot.Find().FullName, .. segments]);
    }

    /// <summary>
    /// Runs a whole script through SQLite's own parser, which is the only splitter that gets the
    /// <c>CREATE TRIGGER … BEGIN … END;</c> blocks in these files right.
    /// </summary>
    private void Execute(string sql)
    {
        using SqliteConnection connection = new(database.ConnectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private bool TableExists(string table)
    {
        using SqliteConnection connection = new(database.ConnectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}'";

        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private async Task<IScheduler> GetScheduler(
        string schedulerName,
        bool provision,
        Action<IPersistentStoreBuilder>? configure = null)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = schedulerName;
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                configure?.Invoke(store);

                if (provision)
                {
                    store.ProvisionSchema();
                }
            });
        });

        container = services.BuildServiceProvider();
        return await container.GetRequiredService<ISchedulerFactory>().GetScheduler();
    }

    /// <summary>
    /// A container of its own, so a case can build one scheduler, dispose everything it owns, and then
    /// build a second against the same file — which is what two nodes of a cluster are here.
    /// </summary>
    private ServiceProvider BuildProvisioningContainer(string schedulerName)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                options.InstanceName = schedulerName;
                options.InstanceId = "one";
            });

            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.ProvisionSchema();
            });
        });

        return services.BuildServiceProvider();
    }
}
