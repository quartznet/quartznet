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

using JasperFx;

using Microsoft.Extensions.DependencyInjection;

using MySqlConnector;

using Quartz.Tests.Integration.Impl.AdoJobStore;

using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.MySql;
using Weasel.MySql.Tables;

namespace Quartz.Tests.Integration.Weasel;

/// <summary>
/// What Weasel makes of Quartz's MySQL schema, read back from a real catalog: every route to a schema
/// arrives at the same one, in the database the store's own SQL uses; an application's own objects and a
/// second Weasel model survive; and appliers racing on one server take turns.
/// </summary>
[Category("db-mysql")]
public sealed class MySqlWeaselSchemaTest
{
    private MySqlWeaselDatabase database = null!;

    [SetUp]
    public async Task CreateEmptyDatabase()
    {
        database = await MySqlWeaselDatabase.CreateAsync();
    }

    [TearDown]
    public async Task DropDatabase()
    {
        await database.DisposeAsync();
    }

    [Test]
    public async Task WeaselCreatesTheSchemaAFreshInstallCreates()
    {
        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-my-fresh");
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Create);
            (await weasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None,
                "what Weasel just created reads back as the model");
        }

        await using MySqlWeaselDatabase fresh = await MySqlWeaselDatabase.CreateAsync();
        await fresh.RunRepositoryScriptAsync("database", "tables", "tables_mysql_innodb.sql");

        await ShouldMatchAsync(database, fresh,
            "a schema is one schema, whichever route built it — column types, defaults, foreign key names and index directions included");
    }

    [Test]
    public async Task SchemasTheScriptsCreatedReadAsUnchanged()
    {
        await database.RunRepositoryScriptAsync("database", "tables", "tables_mysql_innodb.sql");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-my-fresh-install");
        await using (services)
        {
            SchemaMigration migration = await weasel.CreateMigrationAsync();
            migration.Difference.Should().Be(SchemaPatchDifference.None, "tables_mysql_innodb.sql is the model: " + Describe(migration));

            await weasel.Invoking(x => x.AssertDatabaseMatchesConfigurationAsync()).Should().NotThrowAsync("db-assert passes");
        }

        await using MySqlWeaselDatabase provisioned = await MySqlWeaselDatabase.CreateAsync();
        await provisioned.ProvisionAsync();

        (ServiceProvider provisionedServices, IDatabase provisionedWeasel) = await provisioned.WeaselAsync("weasel-my-provisioned");
        await using (provisionedServices)
        {
            SchemaMigration migration = await provisionedWeasel.CreateMigrationAsync();
            migration.Difference.Should().Be(SchemaPatchDifference.None, "create_mysql_innodb.sql is the model: " + Describe(migration));
        }
    }

    /// <summary>
    /// Weasel's own MySQL default schema is the literal <c>public</c>; the tables go where the store's SQL
    /// looks for them — the connection's database, or the one the table prefix names.
    /// </summary>
    [Test]
    public async Task TheTablesAreInTheConnectionsDatabaseOrThePrefixs()
    {
        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-my-connection-database");
        await using (services)
        {
            int tables = weasel.BuildFeatureSchemas().Single().Objects.OfType<Table>().Count();
            weasel.Describe().SchemaOrNamespace.Should().Be(database.Name);

            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Create);

            (await database.ScalarAsync("SELECT count(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME LIKE 'QRTZ!_%' ESCAPE '!'"))
                .Should().Be((long) tables, "every table the model has is in the database the connection opens in");
            (await database.AdminScalarAsync("SELECT count(*) FROM information_schema.SCHEMATA WHERE SCHEMA_NAME = 'public'"))
                .Should().Be(0L, "Weasel's default schema never becomes a database of its own");
        }

        string other = database.Name + "_other";
        await database.CreateDatabaseAsync(other);

        (ServiceProvider otherServices, IDatabase otherWeasel) = await database.WeaselAsync("weasel-my-prefix-database", tablePrefix: other + ".QRTZ_");
        await using (otherServices)
        {
            otherWeasel.Describe().SchemaOrNamespace.Should().Be(other);
            (await otherWeasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Create,
                "the prefix's database is another database, with no Quartz tables in it yet");
            (await otherWeasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);
        }

        (SchemaSnapshot inPrefix, List<string> inPrefixDetails) = await database.ReadAsync(other);
        (SchemaSnapshot inConnection, List<string> inConnectionDetails) = await database.ReadAsync();
        inPrefix.Tables.Should().BeEquivalentTo(inConnection.Tables);
        inPrefixDetails.Should().BeEquivalentTo(inConnectionDetails, "one model, in two databases");
    }

    [Test]
    public async Task A42SchemaIsBroughtToAFreshInstall()
    {
        await database.RunRepositoryScriptAsync("src", "Quartz.Tests.Integration", "SchemaBaselines", "4.2", "tables_mysql_innodb.sql");
        await SeedAsync(database);

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-my-from-42");
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Update);
            (await weasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);
        }

        await using MySqlWeaselDatabase fresh = await MySqlWeaselDatabase.CreateAsync();
        await fresh.RunRepositoryScriptAsync("database", "tables", "tables_mysql_innodb.sql");

        await ShouldMatchAsync(database, fresh,
            "what 4.3 and 4.4 added — columns, an index and QRTZ_JOB_STATUS — is all a 4.2 schema lacks");
        await ShouldHoldTheSeedAsync(database);
    }

    /// <summary>
    /// 4.3 → 4.4 only adds: five columns and an index on the history table, and the rollup table.
    /// </summary>
    [Test]
    public async Task A43SchemaIsBroughtToAFreshInstallByAddingOnly()
    {
        await database.RunRepositoryScriptAsync("src", "Quartz.Tests.Integration", "SchemaBaselines", "4.3", "tables_mysql_innodb.sql");
        await SeedAsync(database);
        await SeedHistoryAsync(database);

        (SchemaSnapshot before, List<string> beforeDetails) = await database.ReadAsync();

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-my-from-43");
        await using (services)
        {
            SchemaMigration planned = await weasel.CreateMigrationAsync();
            List<ISchemaObjectDelta> changed = [.. planned.Deltas.Where(x => x.Difference != SchemaPatchDifference.None)];

            changed.Select(x => (x.SchemaObject.Identifier.Name.ToUpperInvariant(), x.Difference)).Should().BeEquivalentTo(
                [("QRTZ_EXECUTION_HISTORY", SchemaPatchDifference.Update), ("QRTZ_JOB_STATUS", SchemaPatchDifference.Create)],
                "4.4 changes nothing a 4.3 schema has but the history table, and adds the rollup: " + Describe(planned));

            TableDelta history = changed.OfType<TableDelta>().Single(x => x.Difference == SchemaPatchDifference.Update);
            history.Columns!.Missing.Select(x => x.Name).Should().BeEquivalentTo(["RESULT", "SUMMARY", "METRICS", "MANUAL", "FIRE_INSTANCE_ID"]);
            history.Columns.Different.Should().BeEmpty("no column a 4.3 schema has is altered");
            history.Columns.Extras.Should().BeEmpty();
            history.Indexes!.Missing.Select(x => x.Name).Should().Equal(["IDX_QRTZ_EH_JOB_TIME"]);
            history.Indexes.Different.Should().BeEmpty();
            history.Indexes.Extras.Should().BeEmpty();

            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Update);
            (await weasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None,
                "the second apply finds nothing left to do");
        }

        (SchemaSnapshot after, List<string> afterDetails) = await database.ReadAsync();
        after.Tables.Except(before.Tables).Should().Equal(["JOB_STATUS"]);
        after.Columns.Should().Contain(before.Columns, "an add-only apply alters and drops no column a 4.3 schema has");
        after.Indexes.Should().Contain(before.Indexes, "nor an index");
        afterDetails.Should().Contain(beforeDetails, "nor a type, a default, a key or a direction");

        await using MySqlWeaselDatabase fresh = await MySqlWeaselDatabase.CreateAsync();
        await fresh.RunRepositoryScriptAsync("database", "tables", "tables_mysql_innodb.sql");

        await ShouldMatchAsync(database, fresh, "what 4.4 added is all a 4.3 schema lacks");
        await ShouldHoldTheSeedAsync(database);
        await ShouldHoldTheHistoryRowWithNoOutcomeAsync(database);
    }

    [Test]
    public async Task A320SchemaIsBroughtToAFreshInstallAndItsRetiredIndexesGo()
    {
        await database.RunRepositoryScriptAsync("src", "Quartz.Tests.Integration", "SchemaBaselines", "3.20", "tables_mysql_innodb.sql");
        await SeedAsync(database);

        await using MySqlWeaselDatabase fresh = await MySqlWeaselDatabase.CreateAsync();
        await fresh.RunRepositoryScriptAsync("database", "tables", "tables_mysql_innodb.sql");

        List<string> retired = [.. IndexNames((await database.ReadAsync()).Schema).Except(IndexNames((await fresh.ReadAsync()).Schema))];
        retired.Should().NotBeEmpty("the premise: 3.20 created indexes 4.x no longer has");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-my-from-320");
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().NotBe(SchemaPatchDifference.None);
            (await weasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);
        }

        IndexNames((await database.ReadAsync()).Schema).Should().NotIntersectWith(retired,
            "the tables are add-only, and the model names the retired indexes to drop them all the same");

        await ShouldMatchAsync(database, fresh,
            "the 4.x columns, tables and index shapes arrive — IDX_QRTZ_T_NFT_ST with PRIORITY DESC among them");
        await ShouldHoldTheSeedAsync(database);
    }

    [Test]
    public async Task ObjectsTheApplicationAddedAndASecondModelSurviveAnApply()
    {
        await database.RunRepositoryScriptAsync("database", "tables", "tables_mysql_innodb.sql");
        await SeedAsync(database);

        await database.ExecuteAsync("""
            ALTER TABLE QRTZ_TRIGGERS ADD USER_NOTE VARCHAR(100) NULL;
            UPDATE QRTZ_TRIGGERS SET USER_NOTE = 'keep me';
            CREATE INDEX IDX_USER_DESCRIPTION ON QRTZ_TRIGGERS (DESCRIPTION);
            ALTER TABLE QRTZ_JOB_DETAILS ADD CONSTRAINT USER_CLASS_NAME_NOT_BLANK CHECK (JOB_CLASS_NAME <> '');
            CREATE TABLE APP_AUDIT (
              ID INT AUTO_INCREMENT PRIMARY KEY,
              SCHED_NAME VARCHAR(120), JOB_NAME VARCHAR(200), JOB_GROUP VARCHAR(200),
              CONSTRAINT FK_APP_AUDIT_QRTZ_JOB_DETAILS FOREIGN KEY (SCHED_NAME, JOB_NAME, JOB_GROUP)
                REFERENCES QRTZ_JOB_DETAILS (SCHED_NAME, JOB_NAME, JOB_GROUP)) ENGINE=InnoDB;
            INSERT INTO APP_AUDIT (SCHED_NAME, JOB_NAME, JOB_GROUP) VALUES ('weasel', 'job', 'group');
            ALTER TABLE QRTZ_TRIGGERS ADD CONSTRAINT FK_USER_TRIGGERS_CALENDARS FOREIGN KEY (SCHED_NAME, CALENDAR_NAME)
              REFERENCES QRTZ_CALENDARS (SCHED_NAME, CALENDAR_NAME);
            """);

        Table settings = new(new MySqlObjectName(database.Name, "APP_SETTINGS"));
        settings.AddColumn("SETTING_KEY", "VARCHAR(100)").NotNull().AsPrimaryKey();
        settings.AddColumn("SETTING_VALUE", "LONGTEXT");

        await using (MySqlConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            MySqlMigrator migrator = new();
            await migrator.ApplyAllAsync(connection, await SchemaMigration.DetermineAsync(connection, migrator, default, settings), AutoCreate.CreateOrUpdate);
        }

        await database.ExecuteAsync("INSERT INTO APP_SETTINGS (SETTING_KEY, SETTING_VALUE) VALUES ('k', 'v')");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-my-coexisting");
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.None,
                "the add-only model withholds the drops of what it does not declare, and there is nothing else to do");
        }

        (await database.ScalarAsync("SELECT USER_NOTE FROM QRTZ_TRIGGERS")).Should().Be("keep me");
        (await database.ScalarAsync("SELECT count(*) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND INDEX_NAME = 'IDX_USER_DESCRIPTION'")).Should().Be(1L);
        (await database.ScalarAsync("SELECT count(*) FROM information_schema.CHECK_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'USER_CLASS_NAME_NOT_BLANK'")).Should().Be(1L);
        (await database.ScalarAsync("SELECT count(*) FROM information_schema.REFERENTIAL_CONSTRAINTS WHERE CONSTRAINT_SCHEMA = DATABASE() AND CONSTRAINT_NAME = 'FK_USER_TRIGGERS_CALENDARS'")).Should().Be(1L,
            "a foreign key the application added to a Quartz table is kept");
        (await database.ScalarAsync("SELECT count(*) FROM APP_AUDIT")).Should().Be(1L);
        (await database.ScalarAsync("SELECT SETTING_VALUE FROM APP_SETTINGS")).Should().Be("v");

        await using MySqlConnection check = new(database.ConnectionString);
        await check.OpenAsync();
        (await SchemaMigration.DetermineAsync(check, new MySqlMigrator(), default, settings)).Difference
            .Should().Be(SchemaPatchDifference.None, "Weasel only ever looks at the objects a model declares");
    }

    /// <summary>
    /// A column and an index the application put on the history table survive the 4.4 apply, which only adds.
    /// </summary>
    [Test]
    public async Task ObjectsTheApplicationAddedToTheHistorySurviveThe44Apply()
    {
        await database.RunRepositoryScriptAsync("src", "Quartz.Tests.Integration", "SchemaBaselines", "4.3", "tables_mysql_innodb.sql");
        await SeedHistoryAsync(database);

        await database.ExecuteAsync("""
            ALTER TABLE QRTZ_EXECUTION_HISTORY ADD APP_TENANT VARCHAR(100) NULL;
            UPDATE QRTZ_EXECUTION_HISTORY SET APP_TENANT = 'keep me';
            CREATE INDEX IDX_APP_EH_TENANT ON QRTZ_EXECUTION_HISTORY (APP_TENANT);
            """);

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-my-43-coexisting");
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Update);
            (await weasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);
        }

        (await database.ScalarAsync("SELECT APP_TENANT FROM QRTZ_EXECUTION_HISTORY")).Should().Be("keep me",
            "the tables are add-only, so the application's column is kept");
        (await database.ScalarAsync(
                "SELECT count(DISTINCT INDEX_NAME) FROM information_schema.STATISTICS WHERE TABLE_SCHEMA = DATABASE() AND INDEX_NAME IN ('IDX_APP_EH_TENANT', 'IDX_QRTZ_EH_JOB_TIME')"))
            .Should().Be(2L, "the application's index is kept beside the one 4.4 added");
        await ShouldHoldTheHistoryRowWithNoOutcomeAsync(database);
    }

    /// <summary>
    /// Nodes coming up at once against an empty database: the user lock lets one of them create the
    /// schema, and the others, once they hold it, read a schema with nothing left to do.
    /// </summary>
    [Test]
    public async Task ConcurrentAppliersTakeTurnsAndOneCreates()
    {
        List<(ServiceProvider Services, IDatabase Database)> appliers = [];
        for (int i = 0; i < 5; i++)
        {
            // A lock of this test's own: a user lock is server-wide, and the other tests apply at the same time.
            appliers.Add(await database.WeaselAsync($"weasel-my-race-{i}", configure: options => options.LockName = database.Name));
        }

        try
        {
            SchemaPatchDifference[] results = await Task.WhenAll(
                appliers.Select(x => Task.Run(() => x.Database.ApplyAllConfiguredChangesToDatabaseAsync())));

            results.Count(x => x != SchemaPatchDifference.None).Should().Be(1,
                "one applier creates the schema; every other one waits for the lock and then finds it done");
            (await appliers[0].Database.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);
        }
        finally
        {
            foreach ((ServiceProvider services, IDatabase _) in appliers)
            {
                await services.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Weasel releases the lock only after an apply that succeeded; this one fails, and the lock has to be
    /// free straight after all the same, or the next node waits out its whole timeout.
    /// </summary>
    [Test]
    public async Task AFailedApplyReleasesTheLock()
    {
        // A view where Quartz wants a table: Weasel reads its one column, and the ALTER TABLE that adds the
        // rest fails on something that is not a base table.
        await database.ExecuteAsync("CREATE VIEW QRTZ_JOB_DETAILS AS SELECT 'x' AS SCHED_NAME");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-my-failing", configure: options => options.LockName = database.Name);
        await using (services)
        {
            Func<Task> apply = () => weasel.ApplyAllConfiguredChangesToDatabaseAsync();
            await apply.Should().ThrowAsync<Exception>();
        }

        // A session of its own: a pooled one could be the very session a leaked lock is held on, and
        // GET_LOCK grants a session the lock it already holds.
        await using MySqlConnection connection = new(new MySqlConnectionStringBuilder(database.ConnectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        await using MySqlCommand command = new($"SELECT GET_LOCK('{database.Name}', 0)", connection);
        (await command.ExecuteScalarAsync()).Should().Be(1L, "the failed apply gave the lock back");
    }

    [Test]
    public async Task AnApplyWaitsForAnotherHolderOfTheLock()
    {
        string lockName = database.Name + ":held";

        await using MySqlConnection holder = new(database.ConnectionString);
        await holder.OpenAsync();
        await using (MySqlCommand take = new($"SELECT GET_LOCK('{lockName}', 0)", holder))
        {
            (await take.ExecuteScalarAsync()).Should().Be(1L);
        }

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-my-waiting", configure: options =>
        {
            options.LockName = lockName;
            options.LockTimeout = TimeSpan.FromMilliseconds(500);
        });

        await using (services)
        {
            Func<Task> apply = () => weasel.ApplyAllConfiguredChangesToDatabaseAsync();
            await apply.Should().ThrowAsync<InvalidOperationException>().WithMessage("*global lock*",
                "the lock is held elsewhere for longer than LockTimeout, and FailFast is the default");

            await using (MySqlCommand release = new($"SELECT RELEASE_LOCK('{lockName}')", holder))
            {
                await release.ExecuteScalarAsync();
            }

            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Create,
                "with the lock free the same apply goes ahead");
        }
    }

    /// <summary>
    /// <c>GET_LOCK</c> waits inside a command, and the connection's command timeout bounds that command: a
    /// wait longer than it has to end in the lock's refusal, not in a client-side timeout.
    /// </summary>
    [Test]
    public async Task AWaitLongerThanTheCommandTimeoutEndsInARefusal()
    {
        string lockName = database.Name + ":slow";

        await using MySqlConnection holder = new(database.ConnectionString);
        await holder.OpenAsync();
        await using (MySqlCommand take = new($"SELECT GET_LOCK('{lockName}', 0)", holder))
        {
            (await take.ExecuteScalarAsync()).Should().Be(1L);
        }

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync(
            "weasel-my-command-timeout",
            configure: options =>
            {
                options.LockName = lockName;
                options.LockTimeout = TimeSpan.FromSeconds(4);
            },
            adjustConnection: connection => connection.DefaultCommandTimeout = 2);

        await using (services)
        {
            Func<Task> apply = () => weasel.ApplyAllConfiguredChangesToDatabaseAsync();
            await apply.Should().ThrowAsync<InvalidOperationException>().WithMessage("*global lock*",
                "the lock asks in slices the command timeout leaves room for, and gives up once LockTimeout is spent");
        }
    }

    private static IEnumerable<string> IndexNames(SchemaSnapshot schema) => schema.Indexes.Select(x => x.Split('|')[1]).Distinct();

    private static async Task ShouldMatchAsync(MySqlWeaselDatabase actual, MySqlWeaselDatabase expected, string because)
    {
        (SchemaSnapshot actualSchema, List<string> actualDetails) = await actual.ReadAsync();
        (SchemaSnapshot expectedSchema, List<string> expectedDetails) = await expected.ReadAsync();

        actualSchema.Tables.Should().BeEquivalentTo(expectedSchema.Tables, because);
        actualSchema.Columns.Should().BeEquivalentTo(expectedSchema.Columns, because);
        actualSchema.Indexes.Should().BeEquivalentTo(expectedSchema.Indexes, because);
        actualDetails.Should().BeEquivalentTo(expectedDetails, because);
    }

    private static Task SeedAsync(MySqlWeaselDatabase database) => database.ExecuteAsync("""
        INSERT INTO QRTZ_JOB_DETAILS (SCHED_NAME, JOB_NAME, JOB_GROUP, JOB_CLASS_NAME, IS_DURABLE, IS_NONCONCURRENT, IS_UPDATE_DATA, REQUESTS_RECOVERY)
          VALUES ('weasel', 'job', 'group', 'Weasel.Job', 1, 0, 0, 0);
        INSERT INTO QRTZ_TRIGGERS (SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP, JOB_NAME, JOB_GROUP, TRIGGER_STATE, TRIGGER_TYPE, START_TIME)
          VALUES ('weasel', 'trigger', 'group', 'job', 'group', 'WAITING', 'SIMPLE', 1);
        INSERT INTO QRTZ_SIMPLE_TRIGGERS (SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP, REPEAT_COUNT, REPEAT_INTERVAL, TIMES_TRIGGERED)
          VALUES ('weasel', 'trigger', 'group', 3, 1000, 0);
        """);

    /// <summary>A row a 4.3 node wrote: a failed run, with no outcome columns to fill.</summary>
    private static Task SeedHistoryAsync(MySqlWeaselDatabase database) => database.ExecuteAsync("""
        INSERT INTO QRTZ_EXECUTION_HISTORY (SCHED_NAME, ENTRY_ID, INSTANCE_NAME, JOB_NAME, JOB_GROUP, TRIGGER_NAME, TRIGGER_GROUP, FIRED_TIME, RUN_TIME, SUCCEEDED, ERROR_MESSAGE)
          VALUES ('weasel', 'entry-43', 'node-43', 'job', 'group', 'trigger', 'group', 1, 10, 0, 'failed on 4.3');
        """);

    private static async Task ShouldHoldTheHistoryRowWithNoOutcomeAsync(MySqlWeaselDatabase database)
    {
        (await database.ScalarAsync(
                "SELECT count(*) FROM QRTZ_EXECUTION_HISTORY WHERE ENTRY_ID = 'entry-43' AND ERROR_MESSAGE = 'failed on 4.3'"
                + " AND RESULT IS NULL AND SUMMARY IS NULL AND METRICS IS NULL AND MANUAL IS NULL AND FIRE_INSTANCE_ID IS NULL"))
            .Should().Be(1L, "a row a 4.3 node wrote keeps its values and reads NULL in every column 4.4 added");
    }

    private static async Task ShouldHoldTheSeedAsync(MySqlWeaselDatabase database)
    {
        (await database.ScalarAsync("SELECT count(*) FROM QRTZ_JOB_DETAILS")).Should().Be(1L);
        (await database.ScalarAsync("SELECT count(*) FROM QRTZ_TRIGGERS")).Should().Be(1L);
        (await database.ScalarAsync("SELECT REPEAT_COUNT FROM QRTZ_SIMPLE_TRIGGERS")).Should().Be(3L,
            "migrating the schema moves no Quartz data");
    }

    private static string Describe(SchemaMigration migration) => string.Join("; ", migration.Deltas
        .Where(x => x.Difference != SchemaPatchDifference.None)
        .Select(x => $"{x.SchemaObject.Identifier} {x.Difference}"));
}
