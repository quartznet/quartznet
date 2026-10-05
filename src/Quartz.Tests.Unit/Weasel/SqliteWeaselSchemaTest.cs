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

using Microsoft.Data.Sqlite;

using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Sqlite;
using Weasel.Sqlite.Tables;

namespace Quartz.Tests.Unit.Weasel;

/// <summary>
/// What Weasel makes of Quartz's SQLite schema: the routes to a schema — Weasel, the provisioning script,
/// the fresh-install script, a migrated release — arrive at the same one, and an application's own objects
/// survive every apply.
/// </summary>
public sealed class SqliteWeaselSchemaTest
{
    private SqliteTestDatabase database = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("weasel-schema");
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database.Dispose();
    }

    [Test]
    public async Task WeaselCreatesTheSchemaAFreshInstallCreates()
    {
        await using (WeaselSqliteContainer weasel = await WeaselSqliteContainer.CreateAsync(database.ConnectionString, "weasel-fresh"))
        {
            SchemaPatchDifference difference = await weasel.Database.ApplyAllConfiguredChangesToDatabaseAsync();
            difference.Should().Be(SchemaPatchDifference.Create, "an empty database is missing every object");

            (await weasel.Database.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None,
                "what Weasel just created has to read back as the model, or every start would migrate again");
        }

        using SqliteTestDatabase fresh = new("weasel-fresh-install");
        await SqliteSchema.CreateWithFreshInstallScriptAsync(fresh.ConnectionString);

        await ShouldMatchAsync(database.ConnectionString, fresh.ConnectionString, "a schema is one schema, whichever route built it");
    }

    [Test]
    public async Task ASchemaTheProvisioningScriptCreatedReadsAsUnchanged()
    {
        await SqliteSchema.CreateWithProvisioningScriptAsync(database.ConnectionString);

        await using WeaselSqliteContainer weasel = await WeaselSqliteContainer.CreateAsync(database.ConnectionString, "weasel-provisioned");

        SchemaMigration migration = await weasel.Database.CreateMigrationAsync();
        migration.Difference.Should().Be(SchemaPatchDifference.None,
            "the model is spelled the way SQLite reads the script back: " + Describe(migration));

        await weasel.Database.Invoking(x => x.AssertDatabaseMatchesConfigurationAsync())
            .Should().NotThrowAsync("db-assert passes on a database the store provisioned");
    }

    [Test]
    public async Task ASchemaTheFreshInstallScriptCreatedReadsAsUnchanged()
    {
        await SqliteSchema.CreateWithFreshInstallScriptAsync(database.ConnectionString);

        await using WeaselSqliteContainer weasel = await WeaselSqliteContainer.CreateAsync(database.ConnectionString, "weasel-fresh-install");

        SchemaMigration migration = await weasel.Database.CreateMigrationAsync();
        migration.Difference.Should().Be(SchemaPatchDifference.None,
            "database/tables/tables_sqlite.sql and the model are the same schema: " + Describe(migration));
    }

    [Test]
    public async Task ASchemaUnderAPrefixOfItsOwnReadsAsUnchanged()
    {
        await SqliteSchema.CreateWithProvisioningScriptAsync(database.ConnectionString, "QRTZM_");

        await using WeaselSqliteContainer weasel = await WeaselSqliteContainer.CreateAsync(
            database.ConnectionString, "weasel-prefixed", tablePrefix: "QRTZM_");

        SchemaMigration migration = await weasel.Database.CreateMigrationAsync();
        migration.Difference.Should().Be(SchemaPatchDifference.None,
            "the names the prefix goes into — tables, indexes, triggers and the foreign keys SQLite names from them — follow it: "
            + Describe(migration));
    }

    [Test]
    public async Task A42SchemaIsBroughtToAFreshInstallAndKeepsItsRows()
    {
        await SqliteSchema.CreateWithBaselineAsync(database.ConnectionString, "4.2");
        await SeedAsync(database.ConnectionString);

        await using (WeaselSqliteContainer weasel = await WeaselSqliteContainer.CreateAsync(database.ConnectionString, "weasel-from-42"))
        {
            (await weasel.Database.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Update);
            (await weasel.Database.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);
        }

        using SqliteTestDatabase fresh = new("weasel-from-42-fresh");
        await SqliteSchema.CreateWithFreshInstallScriptAsync(fresh.ConnectionString);

        await ShouldMatchAsync(database.ConnectionString, fresh.ConnectionString,
            "what 4.3 and 4.4 added — columns, an index and QRTZ_JOB_STATUS — is all a 4.2 schema lacks");
        await ShouldHoldTheSeedAsync(database.ConnectionString);
    }

    /// <summary>
    /// 4.3 → 4.4 only adds: five columns and an index on the history table, and the rollup table.
    /// </summary>
    [Test]
    public async Task A43SchemaIsBroughtToAFreshInstallByAddingOnlyAndKeepsItsRows()
    {
        await SqliteSchema.CreateWithBaselineAsync(database.ConnectionString, "4.3");
        await SeedAsync(database.ConnectionString);
        await SeedHistoryAsync(database.ConnectionString);

        await using (WeaselSqliteContainer weasel = await WeaselSqliteContainer.CreateAsync(database.ConnectionString, "weasel-from-43"))
        {
            SchemaMigration planned = await weasel.Database.CreateMigrationAsync();
            List<ISchemaObjectDelta> changed = [.. planned.Deltas.Where(x => x.Difference != SchemaPatchDifference.None)];

            changed.Select(x => (x.SchemaObject.Identifier.Name.ToUpperInvariant(), x.Difference)).Should().BeEquivalentTo(
                [("QRTZ_EXECUTION_HISTORY", SchemaPatchDifference.Update), ("QRTZ_JOB_STATUS", SchemaPatchDifference.Create)],
                "4.4 changes nothing a 4.3 schema has but the history table, and adds the rollup: " + Describe(planned));

            TableDelta history = changed.OfType<TableDelta>().Single(x => x.Difference == SchemaPatchDifference.Update);
            history.Columns.Missing.Select(x => x.Name.ToUpperInvariant()).Should().BeEquivalentTo(
                ["RESULT", "SUMMARY", "METRICS", "MANUAL", "FIRE_INSTANCE_ID", "JOB_INPUT", "JOB_INPUT_TOO_LARGE"]);
            history.Indexes.Missing.Select(x => x.Name.ToUpperInvariant()).Should().Equal(["IDX_QRTZ_EH_JOB_TIME"]);
            history.Columns.Extras.Should().BeEmpty();
            history.Columns.Different.Should().BeEmpty("no column 4.3 created changes shape");
            history.Indexes.Different.Should().BeEmpty("no index 4.3 created changes shape");
            history.ForeignKeys.Missing.Should().BeEmpty();

            (await weasel.Database.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Update);
            (await weasel.Database.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None,
                "the second apply finds nothing left to do");
        }

        using SqliteTestDatabase fresh = new("weasel-from-43-fresh");
        await SqliteSchema.CreateWithFreshInstallScriptAsync(fresh.ConnectionString);

        await ShouldMatchAsync(database.ConnectionString, fresh.ConnectionString, "what 4.4 added is all a 4.3 schema lacks");
        await ShouldHoldTheSeedAsync(database.ConnectionString);
        await ShouldHoldTheHistoryRowWithNoOutcomeAsync(database.ConnectionString);
    }

    /// <summary>
    /// A column and an index the application put on the history table survive the 4.4 apply, whose columns
    /// arrive by <c>ADD COLUMN</c>.
    /// </summary>
    [Test]
    public async Task ObjectsTheApplicationAddedToTheHistorySurviveThe44Apply()
    {
        await SqliteSchema.CreateWithBaselineAsync(database.ConnectionString, "4.3");
        await SeedHistoryAsync(database.ConnectionString);

        await SqliteSchema.ExecuteAsync(database.ConnectionString, """
            ALTER TABLE QRTZ_EXECUTION_HISTORY ADD COLUMN APP_TENANT TEXT NULL;
            UPDATE QRTZ_EXECUTION_HISTORY SET APP_TENANT = 'keep me';
            CREATE INDEX IDX_APP_EH_TENANT ON QRTZ_EXECUTION_HISTORY(APP_TENANT);
            """);

        await using (WeaselSqliteContainer weasel = await WeaselSqliteContainer.CreateAsync(database.ConnectionString, "weasel-43-coexisting"))
        {
            (await weasel.Database.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Update,
                "4.4 only adds to the history table, so nothing is rebuilt");
            (await weasel.Database.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);
        }

        (await SqliteSchema.ScalarAsync(database.ConnectionString, "SELECT APP_TENANT FROM QRTZ_EXECUTION_HISTORY")).Should().Be("keep me");
        (await IndexNamesAsync(database.ConnectionString)).Should().Contain(["IDX_APP_EH_TENANT", "IDX_QRTZ_EH_JOB_TIME"]);
        await ShouldHoldTheHistoryRowWithNoOutcomeAsync(database.ConnectionString);
    }

    [Test]
    public async Task A320SchemaIsBroughtToAFreshInstallAndItsRetiredIndexesGo()
    {
        await SqliteSchema.CreateWithBaselineAsync(database.ConnectionString, "3.20");
        await SeedAsync(database.ConnectionString);

        (await IndexNamesAsync(database.ConnectionString)).Should().Contain(["IDX_QRTZ_J_REQ_RECOVERY", "IDX_QRTZ_T_NEXT_FIRE_TIME"],
            "the premise: 3.20 created the two indexes 4.0 retired on SQLite");

        await using (WeaselSqliteContainer weasel = await WeaselSqliteContainer.CreateAsync(database.ConnectionString, "weasel-from-320"))
        {
            (await weasel.Database.ApplyAllConfiguredChangesToDatabaseAsync()).Should().NotBe(SchemaPatchDifference.None);
            (await weasel.Database.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);
        }

        using SqliteTestDatabase fresh = new("weasel-from-320-fresh");
        await SqliteSchema.CreateWithFreshInstallScriptAsync(fresh.ConnectionString);

        SqliteSchema migrated = await SqliteSchema.ReadAsync(database.ConnectionString);
        SqliteSchema expected = await SqliteSchema.ReadAsync(fresh.ConnectionString);

        migrated.Tables.Should().Equal(expected.Tables);
        migrated.Columns.Should().BeEquivalentTo(expected.Columns);
        migrated.Indexes.Should().Equal(expected.Indexes,
            "the retired indexes are Quartz's own, so the add-only tables still let them go, and IDX_QRTZ_T_NFT_ST takes its 4.x shape");
        migrated.ForeignKeys.Should().Equal(expected.ForeignKeys);
        migrated.Triggers.Should().Contain(expected.Triggers,
            "3.x named its delete triggers without the prefix; they stay, and the prefixed ones are added beside them");

        await ShouldHoldTheSeedAsync(database.ConnectionString);
    }

    [Test]
    public async Task ObjectsTheApplicationAddedSurviveAnApply()
    {
        await SqliteSchema.CreateWithProvisioningScriptAsync(database.ConnectionString);
        await SeedAsync(database.ConnectionString);

        await SqliteSchema.ExecuteAsync(database.ConnectionString, """
            ALTER TABLE QRTZ_TRIGGERS ADD COLUMN USER_NOTE TEXT NULL;
            UPDATE QRTZ_TRIGGERS SET USER_NOTE = 'keep me';
            CREATE INDEX IDX_USER_DESCRIPTION ON QRTZ_TRIGGERS(DESCRIPTION);
            CREATE TABLE APP_AUDIT (
              ID INTEGER PRIMARY KEY,
              SCHED_NAME NVARCHAR(120), JOB_NAME NVARCHAR(150), JOB_GROUP NVARCHAR(150),
              FOREIGN KEY (SCHED_NAME, JOB_NAME, JOB_GROUP) REFERENCES QRTZ_JOB_DETAILS (SCHED_NAME, JOB_NAME, JOB_GROUP));
            INSERT INTO APP_AUDIT (SCHED_NAME, JOB_NAME, JOB_GROUP) VALUES ('weasel', 'job', 'group');
            """);

        // A second model on the same database, applied by Weasel on its own first.
        Table settings = new(new SqliteObjectName("APP_SETTINGS"));
        settings.AddColumn("key", "TEXT").NotNull().AsPrimaryKey();
        settings.AddColumn("value", "TEXT");

        await using (SqliteConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            SqliteMigrator migrator = new();
            SchemaMigration settingsMigration = await SchemaMigration.DetermineAsync(connection, migrator, default, settings);
            await migrator.ApplyAllAsync(connection, settingsMigration, AutoCreate.CreateOrUpdate);
        }

        await SqliteSchema.ExecuteAsync(database.ConnectionString, "INSERT INTO APP_SETTINGS (key, value) VALUES ('k', 'v')");

        await using (WeaselSqliteContainer weasel = await WeaselSqliteContainer.CreateAsync(database.ConnectionString, "weasel-coexisting"))
        {
            (await weasel.Database.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.None,
                "the add-only model withholds the drops of what it does not declare, and there is nothing else to do");
        }

        (await SqliteSchema.ScalarAsync(database.ConnectionString, "SELECT USER_NOTE FROM QRTZ_TRIGGERS")).Should().Be("keep me");
        (await IndexNamesAsync(database.ConnectionString)).Should().Contain("IDX_USER_DESCRIPTION");
        (await SqliteSchema.ScalarAsync(database.ConnectionString, "SELECT count(*) FROM APP_AUDIT")).Should().Be(1L);
        (await SqliteSchema.ScalarAsync(database.ConnectionString, "SELECT value FROM APP_SETTINGS")).Should().Be("v");

        await using SqliteConnection check = new(database.ConnectionString);
        await check.OpenAsync();
        (await SchemaMigration.DetermineAsync(check, new SqliteMigrator(), default, settings)).Difference
            .Should().Be(SchemaPatchDifference.None, "Weasel only ever looks at the objects a model declares");
    }

    /// <summary>
    /// A table the application rebuilt without Quartz's foreign key can only get it back by being rebuilt
    /// again. The tables are add-only, so the rebuild keeps what the application added, rows and all.
    /// </summary>
    [Test]
    public async Task ARebuildKeepsTheApplicationsColumnsIndexesAndForeignKeysWithTheirRows()
    {
        await SqliteSchema.CreateWithTriggersTableMissingItsForeignKeyAsync(database.ConnectionString,
            extraDefinitions: "USER_NOTE TEXT NULL,\n  APP_OWNER INTEGER NULL,\n  FOREIGN KEY (APP_OWNER) REFERENCES APP_OWNERS (ID)");
        await SqliteSchema.ExecuteAsync(database.ConnectionString, """
            CREATE TABLE APP_OWNERS (ID INTEGER PRIMARY KEY);
            INSERT INTO APP_OWNERS (ID) VALUES (7);
            """);
        await SeedAsync(database.ConnectionString);
        await SqliteSchema.ExecuteAsync(database.ConnectionString, """
            UPDATE QRTZ_TRIGGERS SET USER_NOTE = 'keep me', APP_OWNER = 7;
            CREATE INDEX IDX_APP_T_NOTE ON QRTZ_TRIGGERS(USER_NOTE);
            CREATE INDEX IDX_QRTZ_T_NEXT_FIRE_TIME ON QRTZ_TRIGGERS(SCHED_NAME, NEXT_FIRE_TIME);
            """);

        await using (WeaselSqliteContainer weasel = await WeaselSqliteContainer.CreateAsync(database.ConnectionString, "weasel-rebuilt-keeping"))
        {
            SchemaMigration planned = await weasel.Database.CreateMigrationAsync();
            planned.Deltas.OfType<TableDelta>().Single(x => x.SchemaObject.Identifier.Name == "QRTZ_TRIGGERS").CanRebuildInPlace
                .Should().BeTrue("the premise: the missing foreign key makes the apply a rebuild of QRTZ_TRIGGERS");

            (await weasel.Database.ApplyAllConfiguredChangesToDatabaseAsync()).Should().NotBe(SchemaPatchDifference.None);
            (await weasel.Database.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None,
                "the rebuilt table reads back as the model, the application's objects beside it");
        }

        (await SqliteSchema.ScalarAsync(database.ConnectionString, "SELECT USER_NOTE || '/' || APP_OWNER FROM QRTZ_TRIGGERS"))
            .Should().Be("keep me/7", "the rebuild copies the columns the model does not declare, with their values");
        (await ForeignKeysAsync(database.ConnectionString)).Should().BeEquivalentTo(
            ["APP_OWNERS|APP_OWNER|ID", "QRTZ_JOB_DETAILS|JOB_GROUP|JOB_GROUP", "QRTZ_JOB_DETAILS|JOB_NAME|JOB_NAME", "QRTZ_JOB_DETAILS|SCHED_NAME|SCHED_NAME"],
            "Quartz's foreign key is back, and the application's is kept beside it");
        (await IndexNamesAsync(database.ConnectionString)).Should().Contain("IDX_APP_T_NOTE")
            .And.NotContain("IDX_QRTZ_T_NEXT_FIRE_TIME", "an index name Quartz retired still goes, rebuild or not");
        await ShouldHoldTheSeedAsync(database.ConnectionString);
    }

    /// <summary>
    /// A rebuild that fails part-way changes nothing. Here the table lost its foreign key long enough to
    /// take a trigger whose job does not exist, so the rebuilt table's key would not hold.
    /// </summary>
    [Test]
    public async Task ARebuildThatFailsRollsBackAndKeepsTheRowsAndTheApplicationsObjects()
    {
        await SqliteSchema.CreateWithTriggersTableMissingItsForeignKeyAsync(database.ConnectionString, extraDefinitions: "USER_NOTE TEXT NULL");
        await SeedAsync(database.ConnectionString);
        await SqliteSchema.ExecuteAsync(database.ConnectionString, """
            UPDATE QRTZ_TRIGGERS SET USER_NOTE = 'keep me';
            CREATE INDEX IDX_APP_T_NOTE ON QRTZ_TRIGGERS(USER_NOTE);
            INSERT INTO QRTZ_TRIGGERS (SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP, JOB_NAME, JOB_GROUP, TRIGGER_STATE, TRIGGER_TYPE, START_TIME, USER_NOTE)
              VALUES ('weasel', 'orphan', 'group', 'no-such-job', 'group', 'WAITING', 'SIMPLE', 1, 'orphan');
            """);

        await using WeaselSqliteContainer weasel = await WeaselSqliteContainer.CreateAsync(database.ConnectionString, "weasel-rebuild-failed");

        Func<Task> apply = () => weasel.Database.ApplyAllConfiguredChangesToDatabaseAsync();
        (await apply.Should().ThrowAsync<SchedulerException>().WithMessage("*'weasel-rebuild-failed'*not retried*"))
            .WithInnerException<InvalidOperationException>().WithMessage("*QRTZ_TRIGGERS*rolled back*");

        (await ForeignKeysAsync(database.ConnectionString)).Should().BeEmpty("the rebuild rolled back, so the table is the one it replaced");
        (await NamesAsync(database.ConnectionString, "SELECT TRIGGER_NAME || '=' || USER_NOTE FROM QRTZ_TRIGGERS"))
            .Should().Equal(["orphan=orphan", "trigger=keep me"], "both rows are there, with the application's column");
        (await IndexNamesAsync(database.ConnectionString)).Should().Contain("IDX_APP_T_NOTE");
    }

    /// <summary>
    /// Rolling a rebuild back, as <c>db-patch</c>'s <c>.drop.sql</c> does, rebuilds the table again. The
    /// application's column keeps its values that way too, which needed Weasel's rebuild (#639) and its
    /// rollback (#648) to agree on what an add-only table carries.
    /// </summary>
    [Test]
    public async Task RollingBackARebuildKeepsTheApplicationsColumnWithItsValues()
    {
        await SqliteSchema.CreateWithTriggersTableMissingItsForeignKeyAsync(database.ConnectionString, extraDefinitions: "USER_NOTE TEXT NULL");
        await SeedAsync(database.ConnectionString);
        await SqliteSchema.ExecuteAsync(database.ConnectionString, """
            UPDATE QRTZ_TRIGGERS SET USER_NOTE = 'keep me';
            CREATE INDEX IDX_APP_T_NOTE ON QRTZ_TRIGGERS(USER_NOTE);
            """);

        await using WeaselSqliteContainer weasel = await WeaselSqliteContainer.CreateAsync(database.ConnectionString, "weasel-rolled-back");
        SchemaMigration migration = await weasel.Database.CreateMigrationAsync();

        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await weasel.Database.Migrator.ApplyAllAsync(connection, migration, AutoCreate.CreateOrUpdate);
        (await ForeignKeysAsync(database.ConnectionString)).Should().HaveCount(3, "the premise: the forward rebuild put Quartz's key back");

        await migration.RollbackAllAsync(connection, weasel.Database.Migrator);

        (await ForeignKeysAsync(database.ConnectionString)).Should().BeEmpty("the rollback restores the table as it was");
        (await SqliteSchema.ScalarAsync(database.ConnectionString, "SELECT USER_NOTE FROM QRTZ_TRIGGERS")).Should().Be("keep me",
            "the rollback copies the undeclared column back too, rather than leaving it NULL");
        (await IndexNamesAsync(database.ConnectionString)).Should().Contain("IDX_APP_T_NOTE");
        await ShouldHoldTheSeedAsync(database.ConnectionString);
    }

    [Test]
    public async Task ARebuildWithNothingUndeclaredGoesAheadAndKeepsTheRows()
    {
        await SqliteSchema.CreateWithTriggersTableMissingItsForeignKeyAsync(database.ConnectionString, extraDefinitions: null);
        await SeedAsync(database.ConnectionString);

        await using WeaselSqliteContainer weasel = await WeaselSqliteContainer.CreateAsync(database.ConnectionString, "weasel-rebuilt");

        (await weasel.Database.ApplyAllConfiguredChangesToDatabaseAsync()).Should().NotBe(SchemaPatchDifference.None);
        (await weasel.Database.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);

        (await SqliteSchema.ScalarAsync(database.ConnectionString, "SELECT count(*) FROM pragma_foreign_key_list('QRTZ_TRIGGERS')"))
            .Should().Be(3L, "the rebuilt table has the three-column foreign key back");
        await ShouldHoldTheSeedAsync(database.ConnectionString);
    }

    /// <summary>
    /// Several appliers upgrading one file at once: every <c>ADD COLUMN</c> but the first loses its race,
    /// and the loser reads the schema again and finds nothing left to do.
    /// </summary>
    [Test]
    public async Task AppliersRacingOnOneFileAllSucceed()
    {
        await SqliteSchema.CreateWithBaselineAsync(database.ConnectionString, "4.2");

        List<WeaselSqliteContainer> appliers = [];
        for (int i = 0; i < 4; i++)
        {
            appliers.Add(await WeaselSqliteContainer.CreateAsync(database.ConnectionString, $"weasel-race-{i}"));
        }

        try
        {
            SchemaPatchDifference[] results = await Task.WhenAll(
                appliers.Select(x => Task.Run(() => x.Database.ApplyAllConfiguredChangesToDatabaseAsync())));

            results.Should().Contain(x => x != SchemaPatchDifference.None, "somebody applied the 4.3 columns");
            (await appliers[0].Database.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);
        }
        finally
        {
            foreach (WeaselSqliteContainer applier in appliers)
            {
                await applier.DisposeAsync();
            }
        }
    }

    private static async Task ShouldMatchAsync(string actualConnectionString, string expectedConnectionString, string because)
    {
        SqliteSchema actual = await SqliteSchema.ReadAsync(actualConnectionString);
        SqliteSchema expected = await SqliteSchema.ReadAsync(expectedConnectionString);

        actual.Tables.Should().Equal(expected.Tables, because);
        actual.Columns.Should().BeEquivalentTo(expected.Columns, because);
        actual.Indexes.Should().Equal(expected.Indexes, because);
        actual.ForeignKeys.Should().Equal(expected.ForeignKeys, because);
        actual.Triggers.Should().Equal(expected.Triggers, because);
    }

    private static async Task SeedAsync(string connectionString)
    {
        await SqliteSchema.ExecuteAsync(connectionString, """
            INSERT INTO QRTZ_JOB_DETAILS (SCHED_NAME, JOB_NAME, JOB_GROUP, JOB_CLASS_NAME, IS_DURABLE, IS_NONCONCURRENT, IS_UPDATE_DATA, REQUESTS_RECOVERY)
              VALUES ('weasel', 'job', 'group', 'Weasel.Job', 1, 0, 0, 0);
            INSERT INTO QRTZ_TRIGGERS (SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP, JOB_NAME, JOB_GROUP, TRIGGER_STATE, TRIGGER_TYPE, START_TIME)
              VALUES ('weasel', 'trigger', 'group', 'job', 'group', 'WAITING', 'SIMPLE', 1);
            INSERT INTO QRTZ_SIMPLE_TRIGGERS (SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP, REPEAT_COUNT, REPEAT_INTERVAL, TIMES_TRIGGERED)
              VALUES ('weasel', 'trigger', 'group', 3, 1000, 0);
            """);
    }

    /// <summary>A row a 4.3 node wrote: a failed run, with no outcome columns to fill.</summary>
    private static Task SeedHistoryAsync(string connectionString) => SqliteSchema.ExecuteAsync(connectionString, """
        INSERT INTO QRTZ_EXECUTION_HISTORY (SCHED_NAME, ENTRY_ID, INSTANCE_NAME, JOB_NAME, JOB_GROUP, TRIGGER_NAME, TRIGGER_GROUP, FIRED_TIME, RUN_TIME, SUCCEEDED, ERROR_MESSAGE)
          VALUES ('weasel', 'entry-43', 'node-43', 'job', 'group', 'trigger', 'group', 1, 10, 0, 'failed on 4.3');
        """);

    private static async Task ShouldHoldTheHistoryRowWithNoOutcomeAsync(string connectionString)
    {
        (await SqliteSchema.ScalarAsync(connectionString,
                "SELECT count(*) FROM QRTZ_EXECUTION_HISTORY WHERE ENTRY_ID = 'entry-43' AND ERROR_MESSAGE = 'failed on 4.3'"
                + " AND RESULT IS NULL AND SUMMARY IS NULL AND METRICS IS NULL AND MANUAL IS NULL AND FIRE_INSTANCE_ID IS NULL AND JOB_INPUT IS NULL AND JOB_INPUT_TOO_LARGE IS NULL"))
            .Should().Be(1L, "a row a 4.3 node wrote keeps its values and reads NULL in every column 4.4 added");
    }

    private static async Task ShouldHoldTheSeedAsync(string connectionString)
    {
        (await SqliteSchema.ScalarAsync(connectionString, "SELECT count(*) FROM QRTZ_JOB_DETAILS")).Should().Be(1L);
        (await SqliteSchema.ScalarAsync(connectionString, "SELECT count(*) FROM QRTZ_TRIGGERS")).Should().Be(1L);
        (await SqliteSchema.ScalarAsync(connectionString, "SELECT REPEAT_COUNT FROM QRTZ_SIMPLE_TRIGGERS")).Should().Be(3L,
            "migrating the schema moves no Quartz data");
    }

    private static Task<List<string>> IndexNamesAsync(string connectionString) =>
        NamesAsync(connectionString, "SELECT upper(name) FROM sqlite_master WHERE type = 'index' AND sql IS NOT NULL");

    private static Task<List<string>> ForeignKeysAsync(string connectionString) =>
        NamesAsync(connectionString, "SELECT upper(\"table\") || '|' || upper(\"from\") || '|' || upper(\"to\") FROM pragma_foreign_key_list('QRTZ_TRIGGERS')");

    private static async Task<List<string>> NamesAsync(string connectionString, string sql)
    {
        await using SqliteConnection connection = new(connectionString);
        await connection.OpenAsync();
        return await SqliteSchema.RowsAsync(connection, sql);
    }

    internal static string Describe(SchemaMigration migration) => string.Join("; ", migration.Deltas
        .Where(x => x.Difference != SchemaPatchDifference.None)
        .Select(x => x is TableDelta { Difference: SchemaPatchDifference.Update } table
            ? $"{x.SchemaObject.Identifier} {x.Difference} ({table.InvalidReason}):"
              + $" columns {Describe(table.Columns)}, indexes {Describe(table.Indexes)}, foreign keys {Describe(table.ForeignKeys)}"
            : $"{x.SchemaObject.Identifier} {x.Difference}"));

    private static string Describe<T>(ItemDelta<T> delta) where T : INamed =>
        $"missing [{string.Join(", ", delta.Missing.Select(x => x.Name))}]"
        + $" extra [{string.Join(", ", delta.Extras.Select(x => x.Name))}]"
        + $" different [{string.Join(", ", delta.Different.Select(x => $"{x.Expected} / {x.Actual}"))}]";
}
