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

using Oracle.ManagedDataAccess.Client;

using Quartz.Tests.Integration.Impl.AdoJobStore;
using Quartz.Weasel.Oracle;

using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Oracle;
using Weasel.Oracle.Tables;

namespace Quartz.Tests.Integration.Weasel;

/// <summary>
/// What Weasel makes of Quartz's Oracle schema, read back from a real catalog: every route to a schema
/// arrives at the same one, in the schema the store's own SQL uses; an application's own objects and a
/// second Weasel model survive; and appliers racing on one schema, with no lock, converge.
/// </summary>
/// <remarks>
/// Every test has a schema of its own, so they run side by side: Oracle's DDL is what an Oracle leg spends
/// its time on, one statement at a time.
/// </remarks>
[Category("db-oracle")]
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[Parallelizable(ParallelScope.All)]
public sealed class OracleWeaselSchemaTest
{
    private OracleWeaselDatabase database = null!;

    [SetUp]
    public async Task CreateEmptySchema()
    {
        database = await OracleWeaselDatabase.CreateAsync();
    }

    [TearDown]
    public async Task DropSchema()
    {
        await database.DisposeAsync();
    }

    [Test]
    public async Task WeaselCreatesTheSchemaAFreshInstallCreates()
    {
        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-ora-fresh");
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Create);
            (await weasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None,
                "what Weasel just created reads back as the model");

            const string AcquisitionIndexSql = "SELECT object_id FROM user_objects WHERE object_type = 'INDEX' AND object_name = 'IDX_QRTZ_T_NFT_ST'";
            long acquisitionIndex = await database.CountAsync(AcquisitionIndexSql);

            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.None,
                "a second apply finds nothing to do");
            (await database.CountAsync(AcquisitionIndexSql)).Should().Be(acquisitionIndex,
                "IDX_QRTZ_T_NFT_ST is neither dropped nor recreated: its descending PRIORITY reads back whole on the apply's own comparison");
        }

        await ShouldMatchFreshInstallAsync(database,
            "a schema is one schema, whichever route built it — column types, defaults, constraint names and index directions included");
    }

    [Test]
    public async Task SchemasTheScriptsCreatedReadAsUnchanged()
    {
        await database.RunRepositoryScriptAsync("database", "tables", "tables_oracle.sql");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-ora-fresh-install");
        await using (services)
        {
            SchemaMigration migration = await weasel.CreateMigrationAsync();
            migration.Difference.Should().Be(SchemaPatchDifference.None, "tables_oracle.sql is the model: " + Describe(migration));

            await weasel.Invoking(x => x.AssertDatabaseMatchesConfigurationAsync()).Should().NotThrowAsync("db-assert passes");
        }

        await using OracleWeaselDatabase provisioned = await OracleWeaselDatabase.CreateAsync();
        await provisioned.ProvisionAsync();

        (ServiceProvider provisionedServices, IDatabase provisionedWeasel) = await provisioned.WeaselAsync("weasel-ora-provisioned");
        await using (provisionedServices)
        {
            SchemaMigration migration = await provisionedWeasel.CreateMigrationAsync();
            migration.Difference.Should().Be(SchemaPatchDifference.None, "create_oracle.sql is the model: " + Describe(migration));
        }
    }

    /// <summary>
    /// A login whose session is moved to another schema, the way a logon trigger moves an application
    /// user to the schema that owns its tables: the tables go where the store's unqualified SQL looks for
    /// them — the current schema, not the user and not Weasel's default <c>WEASEL</c>.
    /// </summary>
    [Test]
    public async Task TheTablesAreInTheSessionsCurrentSchema()
    {
        string login = OracleWeaselDatabase.NewUserName();
        await database.CreateUserAsync(login);
        await database.AdminExecuteAsync(
            $"GRANT CREATE ANY TABLE, ALTER ANY TABLE, CREATE ANY INDEX, SELECT ANY TABLE, SELECT ANY DICTIONARY TO {login}",
            $"CREATE OR REPLACE TRIGGER {login}.MOVE_TO_OWNER AFTER LOGON ON {login}.SCHEMA BEGIN EXECUTE IMMEDIATE 'ALTER SESSION SET CURRENT_SCHEMA = {database.Name}'; END;");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync(
            "weasel-ora-current-schema",
            connectionString: OracleWeaselDatabase.ConnectionStringFor(login));

        await using (services)
        {
            weasel.Describe().SchemaOrNamespace.Should().Be(login, "until the model is needed, nothing has asked the server");

            List<Table> tables = weasel.BuildFeatureSchemas().Single().Objects.OfType<Table>().ToList();
            tables.Should().OnlyContain(x => x.Identifier.Schema == database.Name, "the session's current schema, read from the server");
            weasel.Describe().SchemaOrNamespace.Should().Be(database.Name);

            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Create);
            (await weasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);

            (await database.AdminCountAsync($"SELECT count(*) FROM all_tables WHERE owner = '{database.Name}' AND table_name LIKE 'QRTZ!_%' ESCAPE '!'"))
                .Should().Be(tables.Count, "every table the model has is in the schema the session was moved to");
            (await database.AdminCountAsync($"SELECT count(*) FROM all_tables WHERE owner = '{login}'"))
                .Should().Be(0, "nothing is created in the login's own schema");
        }

        (SchemaSnapshot created, List<string> createdDetails) = await database.ReadAsync();
        (SchemaSnapshot expected, List<string> expectedDetails) = await OracleWeaselDatabase.FreshInstallAsync();

        created.Tables.Should().BeEquivalentTo(expected.Tables);
        createdDetails.Should().BeEquivalentTo(expectedDetails, "the same schema as a fresh install, only elsewhere");
    }

    [Test]
    public async Task A42SchemaIsBroughtToAFreshInstall()
    {
        await database.RunRepositoryScriptAsync("src", "Quartz.Tests.Integration", "SchemaBaselines", "4.2", "tables_oracle.sql");
        await SeedAsync(database);

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-ora-from-42");
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Update);
            (await weasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);
        }

        await ShouldMatchFreshInstallAsync(database,
            "what 4.3 and 4.4 added — columns, an index and QRTZ_JOB_STATUS — is all a 4.2 schema lacks");
        await ShouldHoldTheSeedAsync(database);
    }

    /// <summary>
    /// 4.3 → 4.4 only adds: five columns and an index on the history table, and the rollup table.
    /// </summary>
    [Test]
    public async Task A43SchemaIsBroughtToAFreshInstallByAddingOnly()
    {
        await database.RunRepositoryScriptAsync("src", "Quartz.Tests.Integration", "SchemaBaselines", "4.3", "tables_oracle.sql");
        await SeedAsync(database);
        await SeedHistoryAsync(database);

        (SchemaSnapshot before, List<string> beforeDetails) = await database.ReadAsync();

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-ora-from-43");
        await using (services)
        {
            SchemaMigration planned = await weasel.CreateMigrationAsync();
            List<ISchemaObjectDelta> changed = [.. planned.Deltas.Where(x => x.Difference != SchemaPatchDifference.None)];

            changed.Select(x => (x.SchemaObject.Identifier.Name.ToUpperInvariant(), x.Difference)).Should().BeEquivalentTo(
                [("QRTZ_EXECUTION_HISTORY", SchemaPatchDifference.Update), ("QRTZ_JOB_STATUS", SchemaPatchDifference.Create)],
                "4.4 changes nothing a 4.3 schema has but the history table, and adds the rollup: " + Describe(planned));

            TableDelta history = changed.OfType<TableDelta>().Single(x => x.Difference == SchemaPatchDifference.Update);
            history.Columns.Missing.Select(x => x.Name.ToUpperInvariant()).Should().BeEquivalentTo(["RESULT", "SUMMARY", "METRICS", "MANUAL", "FIRE_INSTANCE_ID", "JOB_INPUT", "JOB_INPUT_TOO_LARGE"]);
            history.Columns.Different.Should().BeEmpty("no column a 4.3 schema has is altered");
            history.Columns.Extras.Should().BeEmpty();
            history.Indexes.Missing.Select(x => x.Name).Should().Equal(["IDX_QRTZ_EH_JOB_TIME"]);
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
        afterDetails.Should().Contain(beforeDetails, "nor a type, a default or a key");

        await ShouldMatchFreshInstallAsync(database, "what 4.4 added is all a 4.3 schema lacks");
        await ShouldHoldTheSeedAsync(database);
        await ShouldHoldTheHistoryRowWithNoOutcomeAsync(database);
    }

    [Test]
    public async Task A320SchemaIsBroughtToAFreshInstallAndItsRetiredIndexesGo()
    {
        await database.RunRepositoryScriptAsync("src", "Quartz.Tests.Integration", "SchemaBaselines", "3.20", "tables_oracle.sql");
        await SeedAsync(database);

        List<string> retired = [.. IndexNames((await database.ReadAsync()).Schema).Except(IndexNames((await OracleWeaselDatabase.FreshInstallAsync()).Schema))];
        retired.Should().NotBeEmpty("the premise: 3.20 created indexes 4.x no longer has");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-ora-from-320");
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().NotBe(SchemaPatchDifference.None);
            (await weasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);
        }

        IndexNames((await database.ReadAsync()).Schema).Should().NotIntersectWith(retired,
            "the tables are add-only, and the model names the retired indexes to drop them all the same");

        await ShouldMatchFreshInstallAsync(database,
            "the 4.x columns, tables and index shapes arrive — IDX_QRTZ_T_NFT_ST with PRIORITY DESC among them");
        await ShouldHoldTheSeedAsync(database);
    }

    [Test]
    public async Task ObjectsTheApplicationAddedAndASecondModelSurviveAnApply()
    {
        await database.RunRepositoryScriptAsync("database", "tables", "tables_oracle.sql");
        await SeedAsync(database);

        await database.ExecuteAsync(
            "ALTER TABLE QRTZ_TRIGGERS ADD USER_NOTE VARCHAR2(100) NULL",
            "UPDATE QRTZ_TRIGGERS SET USER_NOTE = 'keep me'",
            "CREATE INDEX IDX_USER_DESCRIPTION ON QRTZ_TRIGGERS (DESCRIPTION)",
            "ALTER TABLE QRTZ_JOB_DETAILS ADD CONSTRAINT USER_CLASS_NAME_NOT_BLANK CHECK (LENGTH(TRIM(JOB_CLASS_NAME)) > 0)",
            """
            CREATE TABLE APP_AUDIT (
              ID NUMBER(10) PRIMARY KEY,
              SCHED_NAME VARCHAR2(120), JOB_NAME VARCHAR2(200), JOB_GROUP VARCHAR2(200),
              CONSTRAINT FK_APP_AUDIT_QRTZ_JOB_DETAILS FOREIGN KEY (SCHED_NAME, JOB_NAME, JOB_GROUP)
                REFERENCES QRTZ_JOB_DETAILS (SCHED_NAME, JOB_NAME, JOB_GROUP))
            """,
            "INSERT INTO APP_AUDIT (ID, SCHED_NAME, JOB_NAME, JOB_GROUP) VALUES (1, 'weasel', 'job', 'group')",
            "ALTER TABLE QRTZ_TRIGGERS ADD CONSTRAINT FK_USER_TRIGGERS_CALENDARS FOREIGN KEY (SCHED_NAME, CALENDAR_NAME) REFERENCES QRTZ_CALENDARS (SCHED_NAME, CALENDAR_NAME)");

        Table settings = new(new OracleObjectName(database.Name, "APP_SETTINGS"));
        settings.AddColumn("SETTING_KEY", "VARCHAR2(100)").NotNull().AsPrimaryKey();
        settings.AddColumn("SETTING_VALUE", "CLOB");

        await using (OracleConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            OracleMigrator migrator = new();
            await migrator.ApplyAllAsync(connection, await SchemaMigration.DetermineAsync(connection, migrator, default, settings), AutoCreate.CreateOrUpdate);
        }

        await database.ExecuteAsync("INSERT INTO APP_SETTINGS (SETTING_KEY, SETTING_VALUE) VALUES ('k', 'v')");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-ora-coexisting");
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.None,
                "the add-only model withholds the drops of what it does not declare, and there is nothing else to do");
        }

        (await database.ScalarAsync("SELECT USER_NOTE FROM QRTZ_TRIGGERS")).Should().Be("keep me");
        (await database.CountAsync("SELECT count(*) FROM user_indexes WHERE index_name = 'IDX_USER_DESCRIPTION'")).Should().Be(1);
        (await database.CountAsync("SELECT count(*) FROM user_constraints WHERE constraint_name = 'USER_CLASS_NAME_NOT_BLANK'")).Should().Be(1);
        (await database.CountAsync("SELECT count(*) FROM user_constraints WHERE constraint_name = 'FK_USER_TRIGGERS_CALENDARS'")).Should().Be(1,
            "a foreign key the application added to a Quartz table is kept");
        (await database.CountAsync("SELECT count(*) FROM APP_AUDIT")).Should().Be(1);
        (await database.ScalarAsync("SELECT TO_CHAR(SETTING_VALUE) FROM APP_SETTINGS")).Should().Be("v");

        await using OracleConnection check = new(database.ConnectionString);
        await check.OpenAsync();
        (await SchemaMigration.DetermineAsync(check, new OracleMigrator(), default, settings)).Difference
            .Should().Be(SchemaPatchDifference.None, "Weasel only ever looks at the objects a model declares");
    }

    /// <summary>
    /// A column and an index the application put on the history table survive the 4.4 apply, which only adds.
    /// </summary>
    [Test]
    public async Task ObjectsTheApplicationAddedToTheHistorySurviveThe44Apply()
    {
        await database.RunRepositoryScriptAsync("src", "Quartz.Tests.Integration", "SchemaBaselines", "4.3", "tables_oracle.sql");
        await SeedHistoryAsync(database);

        await database.ExecuteAsync(
            "ALTER TABLE QRTZ_EXECUTION_HISTORY ADD APP_TENANT VARCHAR2(100) NULL",
            "UPDATE QRTZ_EXECUTION_HISTORY SET APP_TENANT = 'keep me'",
            "CREATE INDEX IDX_APP_EH_TENANT ON QRTZ_EXECUTION_HISTORY (APP_TENANT)");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-ora-43-coexisting");
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Update);
            (await weasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);
        }

        (await database.ScalarAsync("SELECT APP_TENANT FROM QRTZ_EXECUTION_HISTORY")).Should().Be("keep me",
            "the tables are add-only, so the application's column is kept");
        (await database.CountAsync("SELECT count(*) FROM user_indexes WHERE index_name IN ('IDX_APP_EH_TENANT', 'IDX_QRTZ_EH_JOB_TIME')"))
            .Should().Be(2, "the application's index is kept beside the one 4.4 added");
        await ShouldHoldTheHistoryRowWithNoOutcomeAsync(database);
    }

    /// <summary>
    /// Nodes coming up at once against an empty schema, with no lock between them: each loser of a
    /// statement reads the schema again, and every one of them ends with the schema complete.
    /// </summary>
    [Test]
    public async Task ConcurrentAppliersConvergeWithoutALock()
    {
        List<(ServiceProvider Services, IDatabase Database)> appliers = [];
        for (int i = 0; i < 5; i++)
        {
            appliers.Add(await database.WeaselAsync($"weasel-ora-race-{i}"));
        }

        try
        {
            SchemaPatchDifference[] results = await Task.WhenAll(
                appliers.Select(x => Task.Run(() => x.Database.ApplyAllConfiguredChangesToDatabaseAsync())));

            results.Should().Contain(x => x != SchemaPatchDifference.None, "somebody created the schema");
            foreach ((ServiceProvider _, IDatabase applier) in appliers)
            {
                (await applier.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None,
                    "every applier returned with the schema complete, whoever created which part of it");
            }
        }
        finally
        {
            foreach ((ServiceProvider services, IDatabase _) in appliers)
            {
                await services.DisposeAsync();
            }
        }

        await ShouldMatchFreshInstallAsync(database, "five appliers racing build the schema one applier builds");
    }

    /// <summary>
    /// Each statement an apply issues, failed the way it fails for the applier that loses it — the change
    /// is already there — and, for contrast, failures no race causes. The error numbers are the server's own,
    /// which is what <see cref="OracleQuartzDatabase.LostRaceErrors" /> is held to.
    /// </summary>
    [TestCaseSource(nameof(FailedStatements))]
    public async Task OnlyAnErrorALostRaceRaisesIsReadAgain(string[] setup, string statement, int number, bool lostRace)
    {
        await database.ExecuteAsync(setup);

        Func<Task> execute = () => database.ExecuteAsync(statement);
        OracleException error = (await execute.Should().ThrowAsync<OracleException>()).Which;

        error.Number.Should().Be(number, error.Message);
        OracleQuartzDatabase.IsLostRace(error).Should().Be(lostRace, error.Message);
    }

    /// <summary>
    /// An index created on a table another session is writing to — a node still running, during a rolling
    /// upgrade — fails at once rather than waiting for the writer, which is gone a moment later.
    /// </summary>
    [Test]
    public async Task AnIndexOnATableAnotherSessionIsWritingIsALostRace()
    {
        await database.ExecuteAsync("CREATE TABLE RACE_T (ID NUMBER(10) NOT NULL)");

        // Unpooled, so both sessions end with the test and the schema's user can be dropped.
        string unpooled = new OracleConnectionStringBuilder(database.ConnectionString) { Pooling = false }.ConnectionString;

        await using OracleConnection writer = new(unpooled);
        await writer.OpenAsync();
        await using OracleTransaction transaction = (OracleTransaction) await writer.BeginTransactionAsync();
        await using (OracleCommand insert = new("INSERT INTO RACE_T (ID) VALUES (1)", writer) { Transaction = transaction })
        {
            await insert.ExecuteNonQueryAsync();
        }

        await using OracleConnection applier = new(unpooled);
        await applier.OpenAsync();

        // Bounded, so that a server whose DDL waits for the writer fails the test instead of hanging it.
        await using OracleCommand index = new("CREATE INDEX IDX_RACE ON RACE_T (ID)", applier) { CommandTimeout = 30 };

        Func<Task> execute = () => index.ExecuteNonQueryAsync();
        OracleException error = (await execute.Should().ThrowAsync<OracleException>()).Which;

        error.Number.Should().Be(54, error.Message);
        OracleQuartzDatabase.IsLostRace(error).Should().BeTrue("the writer's lock goes when its transaction does");

        await transaction.RollbackAsync();
    }

    /// <summary>
    /// A schema the login may not create in fails the apply at once: no other process applying the same
    /// schema would change that, so it is neither read again nor applied again.
    /// </summary>
    [Test]
    public async Task AnApplyNoRaceFailsFailsAtOnceAsASchedulerException()
    {
        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-ora-refused", "SYSTEM.QRTZ_");
        await using (services)
        {
            Func<Task> apply = () => weasel.ApplyAllConfiguredChangesToDatabaseAsync();

            SchedulerException failure = (await apply.Should().ThrowAsync<SchedulerException>()).Which;
            failure.Message.Should().Contain("'weasel-ora-refused'").And.Contain(weasel.Describe().DatabaseUri().ToString())
                .And.Contain("not retried");

            OracleException? error = null;
            for (Exception? current = failure; current is not null; current = current.InnerException)
            {
                error ??= current as OracleException;
            }

            error.Should().NotBeNull("the server's own refusal is kept, under Weasel's");
            OracleQuartzDatabase.IsLostRace(error!).Should().BeFalse(error!.Message);
        }
    }

    private static IEnumerable<TestCaseData> FailedStatements()
    {
        const string Table = "CREATE TABLE RACE_T (ID NUMBER(10) NOT NULL, P_ID NUMBER(10) NULL)";
        const string Parent = "CREATE TABLE RACE_P (ID NUMBER(10) NOT NULL, CONSTRAINT RACE_P_PK PRIMARY KEY (ID))";
        const string Index = "CREATE INDEX IDX_RACE ON RACE_T (ID)";
        const string Column = "ALTER TABLE RACE_T ADD NOTE VARCHAR2(10)";
        const string PrimaryKey = "ALTER TABLE RACE_T ADD CONSTRAINT RACE_T_PK PRIMARY KEY (ID)";
        const string ForeignKey = "ALTER TABLE RACE_T ADD CONSTRAINT RACE_T_FK FOREIGN KEY (P_ID) REFERENCES RACE_P (ID)";

        yield return Case("A CREATE TABLE another applier made first", [Table], Table, 955, true);
        yield return Case(
            "A guarded CREATE TABLE whose check another applier beat",
            [Table],
            $"BEGIN EXECUTE IMMEDIATE '{Table}'; END;",
            955,
            true);
        yield return Case("A CREATE INDEX another applier made first", [Table, Index], Index, 955, true);
        yield return Case("An ADD another applier made first", [Table, Column], Column, 1430, true);
        yield return Case("A primary key another applier added first", [Table, PrimaryKey], PrimaryKey, 2260, true);
        yield return Case("A foreign key another applier added first", [Parent, Table, ForeignKey], ForeignKey, 2275, true);
        yield return Case("A DROP INDEX another applier made first", [Table, Index, "DROP INDEX IDX_RACE"], "DROP INDEX IDX_RACE", 1418, true);

        yield return Case("A schema the login may not create in", [], "CREATE TABLE SYSTEM.RACE_T (ID NUMBER(10))", 1031, false);
        yield return Case("A table that is not there", [], Column, 942, false);
        yield return Case(
            "A NOT NULL column on a table with rows",
            [Table, "INSERT INTO RACE_T (ID) VALUES (1)"],
            "ALTER TABLE RACE_T ADD NOTE VARCHAR2(10) NOT NULL",
            1758,
            false);

        static TestCaseData Case(string name, string[] setup, string statement, int number, bool lostRace) =>
            new TestCaseData(setup, statement, number, lostRace).SetArgDisplayNames(name);
    }

    private static IEnumerable<string> IndexNames(SchemaSnapshot schema) => schema.Indexes.Select(x => x.Split('|')[1]).Distinct();

    private static async Task ShouldMatchFreshInstallAsync(OracleWeaselDatabase actual, string because)
    {
        (SchemaSnapshot actualSchema, List<string> actualDetails) = await actual.ReadAsync();
        (SchemaSnapshot expectedSchema, List<string> expectedDetails) = await OracleWeaselDatabase.FreshInstallAsync();

        actualSchema.Tables.Should().BeEquivalentTo(expectedSchema.Tables, because);
        actualSchema.Columns.Should().BeEquivalentTo(expectedSchema.Columns, because);
        actualSchema.Indexes.Should().BeEquivalentTo(expectedSchema.Indexes, because);
        actualDetails.Should().BeEquivalentTo(expectedDetails, because);
    }

    private static Task SeedAsync(OracleWeaselDatabase database) => database.ExecuteAsync(
        """
        INSERT INTO QRTZ_JOB_DETAILS (SCHED_NAME, JOB_NAME, JOB_GROUP, JOB_CLASS_NAME, IS_DURABLE, IS_NONCONCURRENT, IS_UPDATE_DATA, REQUESTS_RECOVERY)
          VALUES ('weasel', 'job', 'group', 'Weasel.Job', '1', '0', '0', '0')
        """,
        """
        INSERT INTO QRTZ_TRIGGERS (SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP, JOB_NAME, JOB_GROUP, TRIGGER_STATE, TRIGGER_TYPE, START_TIME)
          VALUES ('weasel', 'trigger', 'group', 'job', 'group', 'WAITING', 'SIMPLE', 1)
        """,
        """
        INSERT INTO QRTZ_SIMPLE_TRIGGERS (SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP, REPEAT_COUNT, REPEAT_INTERVAL, TIMES_TRIGGERED)
          VALUES ('weasel', 'trigger', 'group', 3, 1000, 0)
        """);

    /// <summary>A row a 4.3 node wrote: a failed run, with no outcome columns to fill.</summary>
    private static Task SeedHistoryAsync(OracleWeaselDatabase database) => database.ExecuteAsync("""
        INSERT INTO QRTZ_EXECUTION_HISTORY (SCHED_NAME, ENTRY_ID, INSTANCE_NAME, JOB_NAME, JOB_GROUP, TRIGGER_NAME, TRIGGER_GROUP, FIRED_TIME, RUN_TIME, SUCCEEDED, ERROR_MESSAGE)
          VALUES ('weasel', 'entry-43', 'node-43', 'job', 'group', 'trigger', 'group', 1, 10, '0', 'failed on 4.3')
        """);

    private static async Task ShouldHoldTheHistoryRowWithNoOutcomeAsync(OracleWeaselDatabase database)
    {
        (await database.CountAsync(
                "SELECT count(*) FROM QRTZ_EXECUTION_HISTORY WHERE ENTRY_ID = 'entry-43' AND ERROR_MESSAGE = 'failed on 4.3'"
                + " AND RESULT IS NULL AND SUMMARY IS NULL AND METRICS IS NULL AND MANUAL IS NULL AND FIRE_INSTANCE_ID IS NULL AND JOB_INPUT IS NULL AND JOB_INPUT_TOO_LARGE IS NULL"))
            .Should().Be(1, "a row a 4.3 node wrote keeps its values and reads NULL in every column 4.4 added");
    }

    private static async Task ShouldHoldTheSeedAsync(OracleWeaselDatabase database)
    {
        (await database.CountAsync("SELECT count(*) FROM QRTZ_JOB_DETAILS")).Should().Be(1);
        (await database.CountAsync("SELECT count(*) FROM QRTZ_TRIGGERS")).Should().Be(1);
        (await database.CountAsync("SELECT REPEAT_COUNT FROM QRTZ_SIMPLE_TRIGGERS")).Should().Be(3,
            "migrating the schema moves no Quartz data");
    }

    private static string Describe(SchemaMigration migration) => string.Join("; ", migration.Deltas
        .Where(x => x.Difference != SchemaPatchDifference.None)
        .Select(x => $"{x.SchemaObject.Identifier} {x.Difference}"));
}
