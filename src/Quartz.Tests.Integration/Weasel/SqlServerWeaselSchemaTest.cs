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

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Tests.Integration.Impl.AdoJobStore;

using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.SqlServer;
using Weasel.SqlServer.Tables;

namespace Quartz.Tests.Integration.Weasel;

/// <summary>
/// What Weasel makes of Quartz's SQL Server schema, read back from a real catalog: every route to a
/// schema arrives at the same one, an application's own objects and a second Weasel model survive,
/// appliers racing on one database take turns, and a memory-optimized schema is left alone.
/// </summary>
[Category("db-sqlserver")]
public sealed class SqlServerWeaselSchemaTest
{
    /// <summary>The index names 3.20 created on SQL Server that 4.x retired.</summary>
    private const string Retired320Indexes =
        "'IDX_QRTZ_T_G_J', 'IDX_QRTZ_T_N_STATE', 'IDX_QRTZ_T_N_G_STATE', 'IDX_QRTZ_T_NEXT_FIRE_TIME',"
        + " 'IDX_QRTZ_T_NFT_ST_MISFIRE', 'IDX_QRTZ_T_NFT_ST_MISFIRE_GRP', 'IDX_QRTZ_FT_G_J', 'IDX_QRTZ_FT_G_T'";

    private SqlServerWeaselDatabase database = null!;

    [SetUp]
    public async Task CreateEmptyDatabase()
    {
        database = await SqlServerWeaselDatabase.CreateAsync();
    }

    [TearDown]
    public async Task DropDatabase()
    {
        await database.DisposeAsync();
    }

    [Test]
    public async Task WeaselCreatesTheSchemaAFreshInstallCreates()
    {
        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-ss-fresh");
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Create);
            (await weasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None,
                "what Weasel just created reads back as the model");
        }

        await using SqlServerWeaselDatabase fresh = await SqlServerWeaselDatabase.CreateAsync();
        await fresh.RunRepositoryScriptAsync("database", "tables", "tables_sqlServer.sql");

        await ShouldMatchAsync(database, fresh,
            "a schema is one schema, whichever route built it — constraint names, index directions and defaults included");
    }

    [Test]
    public async Task SchemasTheScriptsCreatedReadAsUnchanged()
    {
        await database.RunRepositoryScriptAsync("database", "tables", "tables_sqlServer.sql");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-ss-fresh-install");
        await using (services)
        {
            SchemaMigration migration = await weasel.CreateMigrationAsync();
            migration.Difference.Should().Be(SchemaPatchDifference.None, "tables_sqlServer.sql is the model: " + Describe(migration));

            await weasel.Invoking(x => x.AssertDatabaseMatchesConfigurationAsync()).Should().NotThrowAsync("db-assert passes");
        }

        await using SqlServerWeaselDatabase provisioned = await SqlServerWeaselDatabase.CreateAsync();
        await provisioned.ProvisionAsync();

        (await provisioned.ScalarAsync("SELECT count(*) FROM sys.foreign_keys WHERE name = 'FK_QRTZ_BLOB_TRIGGERS_QRTZ_TRIGGERS'"))
            .Should().Be(1, "the premise: ProvisionSchema() creates the one foreign key the fresh-install script does not");

        (ServiceProvider provisionedServices, IDatabase provisionedWeasel) = await provisioned.WeaselAsync("weasel-ss-provisioned");
        await using (provisionedServices)
        {
            SchemaMigration migration = await provisionedWeasel.CreateMigrationAsync();
            migration.Difference.Should().Be(SchemaPatchDifference.None,
                "create_sqlServer.sql is the model, and the add-only table keeps the key it has beyond it: " + Describe(migration));
        }
    }

    /// <summary>
    /// A prefix in a schema of its own, bracketed the way a SQL Server table prefix may be, moves every
    /// name the script derives from the prefix.
    /// </summary>
    [Test]
    public async Task ASchemaUnderAPrefixAndASchemaOfItsOwnReadsAsUnchanged()
    {
        const string prefix = "[scheduling].QRTZ_REPORTING_";

        await database.ExecuteAsync("CREATE SCHEMA scheduling");
        await database.ProvisionAsync(prefix);

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-ss-prefixed", tablePrefix: prefix);
        await using (services)
        {
            SchemaMigration migration = await weasel.CreateMigrationAsync();
            migration.Difference.Should().Be(SchemaPatchDifference.None,
                "the constraint and index names follow the prefix the way the script derives them: " + Describe(migration));
        }

        (await database.ScalarAsync(
                "SELECT count(*) FROM sys.foreign_keys WHERE SCHEMA_NAME(schema_id) = 'scheduling' AND name = 'FK_QRTZ_REPORTING_TRIGGERS_QRTZ_REPORTING_JOB_DETAILS'"))
            .Should().Be(1, "the premise: the script put the prefix without its schema into both halves of the name");
    }

    [Test]
    public async Task A42SchemaIsBroughtToAFreshInstall()
    {
        await database.RunRepositoryScriptAsync("src", "Quartz.Tests.Integration", "SchemaBaselines", "4.2", "tables_sqlServer.sql");
        await SeedAsync(database);

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-ss-from-42");
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Update);
            (await weasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);
        }

        await using SqlServerWeaselDatabase fresh = await SqlServerWeaselDatabase.CreateAsync();
        await fresh.RunRepositoryScriptAsync("database", "tables", "tables_sqlServer.sql");

        await ShouldMatchAsync(database, fresh, "4.3's columns are all a 4.2 schema lacks");
        await ShouldHoldTheSeedAsync(database);
    }

    [Test]
    public async Task A320SchemaIsBroughtToAFreshInstallAndItsRetiredIndexesGo()
    {
        await database.RunRepositoryScriptAsync("src", "Quartz.Tests.Integration", "SchemaBaselines", "3.20", "tables_sqlServer.sql");
        await SeedAsync(database);

        (await database.ScalarAsync($"SELECT count(*) FROM sys.indexes WHERE name IN ({Retired320Indexes})"))
            .Should().Be(8, "the premise: 3.20 created the eight indexes 4.0 retired on SQL Server");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-ss-from-320");
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().NotBe(SchemaPatchDifference.None);
            (await weasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);
        }

        (await database.ScalarAsync($"SELECT count(*) FROM sys.indexes WHERE name IN ({Retired320Indexes})"))
            .Should().Be(0, "the tables are add-only, and the model names the retired indexes to drop them all the same");

        await using SqlServerWeaselDatabase fresh = await SqlServerWeaselDatabase.CreateAsync();
        await fresh.RunRepositoryScriptAsync("database", "tables", "tables_sqlServer.sql");

        await ShouldMatchAsync(database, fresh,
            "the 4.0 columns, tables and index shapes arrive — IDX_QRTZ_T_NFT_ST with PRIORITY DESC among them");
        await ShouldHoldTheSeedAsync(database);
    }

    [Test]
    public async Task ObjectsTheApplicationAddedAndASecondModelSurviveAnApply()
    {
        await database.RunRepositoryScriptAsync("database", "tables", "tables_sqlServer.sql");
        await SeedAsync(database);

        await database.ExecuteAsync("""
            ALTER TABLE QRTZ_TRIGGERS ADD USER_NOTE nvarchar(100) NULL;
            GO
            UPDATE QRTZ_TRIGGERS SET USER_NOTE = 'keep me';
            CREATE INDEX IDX_USER_DESCRIPTION ON QRTZ_TRIGGERS (DESCRIPTION);
            ALTER TABLE QRTZ_JOB_DETAILS ADD CONSTRAINT USER_CLASS_NAME_NOT_BLANK CHECK (JOB_CLASS_NAME <> '');
            CREATE TABLE APP_AUDIT (
              ID int IDENTITY PRIMARY KEY,
              SCHED_NAME nvarchar(120), JOB_NAME nvarchar(150), JOB_GROUP nvarchar(150),
              CONSTRAINT FK_APP_AUDIT_QRTZ_JOB_DETAILS FOREIGN KEY (SCHED_NAME, JOB_NAME, JOB_GROUP)
                REFERENCES QRTZ_JOB_DETAILS (SCHED_NAME, JOB_NAME, JOB_GROUP));
            INSERT INTO APP_AUDIT (SCHED_NAME, JOB_NAME, JOB_GROUP) VALUES ('weasel', 'job', 'group');
            ALTER TABLE QRTZ_TRIGGERS ADD CONSTRAINT FK_USER_TRIGGERS_CALENDARS FOREIGN KEY (SCHED_NAME, CALENDAR_NAME)
              REFERENCES QRTZ_CALENDARS (SCHED_NAME, CALENDAR_NAME);
            """);

        Table settings = new("dbo.APP_SETTINGS");
        settings.AddColumn("SETTING_KEY", "nvarchar(100)").NotNull().AsPrimaryKey();
        settings.AddColumn("SETTING_VALUE", "nvarchar(max)");

        await using (SqlConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            SqlServerMigrator migrator = new();
            await migrator.ApplyAllAsync(connection, await SchemaMigration.DetermineAsync(connection, migrator, default, settings), AutoCreate.CreateOrUpdate);
        }

        await database.ExecuteAsync("INSERT INTO APP_SETTINGS (SETTING_KEY, SETTING_VALUE) VALUES ('k', 'v')");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-ss-coexisting");
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.None,
                "the add-only model withholds the drops of what it does not declare, and there is nothing else to do");
        }

        (await database.ScalarAsync("SELECT USER_NOTE FROM QRTZ_TRIGGERS")).Should().Be("keep me");
        (await database.ScalarAsync("SELECT count(*) FROM sys.indexes WHERE name = 'IDX_USER_DESCRIPTION'")).Should().Be(1);
        (await database.ScalarAsync("SELECT count(*) FROM sys.check_constraints WHERE name = 'USER_CLASS_NAME_NOT_BLANK'")).Should().Be(1);
        (await database.ScalarAsync("SELECT count(*) FROM sys.foreign_keys WHERE name = 'FK_USER_TRIGGERS_CALENDARS'")).Should().Be(1,
            "a foreign key the application added to a Quartz table is kept");
        (await database.ScalarAsync("SELECT count(*) FROM APP_AUDIT")).Should().Be(1);
        (await database.ScalarAsync("SELECT SETTING_VALUE FROM APP_SETTINGS")).Should().Be("v");

        await using SqlConnection check = new(database.ConnectionString);
        await check.OpenAsync();
        (await SchemaMigration.DetermineAsync(check, new SqlServerMigrator(), default, settings)).Difference
            .Should().Be(SchemaPatchDifference.None, "Weasel only ever looks at the objects a model declares");
    }

    /// <summary>
    /// Nodes coming up at once against an empty database: the application lock lets one of them create
    /// the schema, and the others, once they hold it, read a schema with nothing left to do.
    /// </summary>
    [Test]
    public async Task ConcurrentAppliersTakeTurnsAndOneCreates()
    {
        List<(ServiceProvider Services, IDatabase Database)> appliers = [];
        for (int i = 0; i < 5; i++)
        {
            appliers.Add(await database.WeaselAsync($"weasel-ss-race-{i}"));
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
        // A view where Quartz wants a table: the guarded CREATE TABLE passes over the name, and creating an
        // index on a view that is not schema-bound then fails.
        await database.ExecuteAsync("CREATE VIEW QRTZ_JOB_DETAILS AS SELECT N'x' AS SCHED_NAME, N'y' AS JOB_NAME, N'z' AS JOB_GROUP");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-ss-failing");
        await using (services)
        {
            Func<Task> apply = () => weasel.ApplyAllConfiguredChangesToDatabaseAsync();
            await apply.Should().ThrowAsync<Exception>();
        }

        // A session of its own: a pooled one could be the very session a leaked lock is held on, and
        // sp_getapplock grants a session the lock it already holds.
        await using SqlConnection connection = new(new SqlConnectionStringBuilder(database.ConnectionString) { Pooling = false }.ConnectionString);
        await connection.OpenAsync();
        (await connection.TryGetGlobalLock(SqlServerWeaselOptions.DefaultLockResource, default, 0)).Should().BeTrue(
            "the failed apply gave the lock back");
        await connection.ReleaseGlobalLock(SqlServerWeaselOptions.DefaultLockResource);
    }

    [Test]
    public async Task AnApplyWaitsForAnotherHolderOfTheLock()
    {
        await using SqlConnection holder = new(database.ConnectionString);
        await holder.OpenAsync();
        (await holder.TryGetGlobalLock("held-elsewhere", default, 0)).Should().BeTrue();

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-ss-waiting", configure: options =>
        {
            options.LockResource = "held-elsewhere";
            options.LockTimeout = TimeSpan.FromMilliseconds(500);
        });

        await using (services)
        {
            Func<Task> apply = () => weasel.ApplyAllConfiguredChangesToDatabaseAsync();
            await apply.Should().ThrowAsync<InvalidOperationException>().WithMessage("*global lock*",
                "the lock is held elsewhere for longer than LockTimeout, and FailFast is the default");

            await holder.ReleaseGlobalLock("held-elsewhere");

            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Create,
                "with the lock free the same apply goes ahead");
        }
    }

    /// <summary>
    /// <c>sp_getapplock</c> waits inside a command, and the connection's command timeout bounds that
    /// command: a wait longer than it has to end in the lock's refusal, not in a client-side timeout.
    /// </summary>
    [Test]
    public async Task AWaitLongerThanTheCommandTimeoutEndsInARefusal()
    {
        await using SqlConnection holder = new(database.ConnectionString);
        await holder.OpenAsync();
        (await holder.TryGetGlobalLock("held-past-the-command-timeout", default, 0)).Should().BeTrue();

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync(
            "weasel-ss-command-timeout",
            configure: options =>
            {
                options.LockResource = "held-past-the-command-timeout";
                options.LockTimeout = TimeSpan.FromSeconds(3);
            },
            adjustConnection: connection => connection.CommandTimeout = 2);

        await using (services)
        {
            Func<Task> apply = () => weasel.ApplyAllConfiguredChangesToDatabaseAsync();
            await apply.Should().ThrowAsync<InvalidOperationException>().WithMessage("*global lock*",
                "the lock asks in slices the command timeout leaves room for, and gives up once LockTimeout is spent");
        }

        await holder.ReleaseGlobalLock("held-past-the-command-timeout");
    }

    /// <summary>
    /// <c>tables_sqlServerMOT.sql</c>'s schema, in a database of the test's own with its own
    /// memory-optimized filegroup: every Weasel route stops before it changes anything.
    /// </summary>
    [Test]
    public async Task AMemoryOptimizedSchemaIsRefusedAndLeftAsItWas()
    {
        await database.RunRepositoryScriptAsync(asAdministrator: true, "database", "tables", "tables_sqlServerMOT.sql");

        (await database.ScalarAsync("SELECT count(*) FROM sys.tables WHERE is_memory_optimized = 1 AND name LIKE 'QRTZ!_%' ESCAPE '!'"))
            .Should().NotBe(0, "the premise: the script created memory-optimized Quartz tables");

        (SchemaSnapshot before, List<string> beforeDetails) = await database.ReadAsync();

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-ss-mot");
        await using (services)
        {
            Func<Task> apply = () => weasel.ApplyAllConfiguredChangesToDatabaseAsync();
            await apply.Should().ThrowAsync<SchedulerException>().WithMessage("*memory-optimized*tables_sqlServerMOT.sql*");

            Func<Task> assert = () => weasel.AssertDatabaseMatchesConfigurationAsync();
            await assert.Should().ThrowAsync<SchedulerException>().WithMessage("*memory-optimized*", "db-assert says why too");
        }

        (SchemaSnapshot after, List<string> afterDetails) = await database.ReadAsync();
        after.Indexes.Should().BeEquivalentTo(before.Indexes, "nothing was changed");
        after.Columns.Should().BeEquivalentTo(before.Columns);
        afterDetails.Should().BeEquivalentTo(beforeDetails);
    }

    private static async Task ShouldMatchAsync(SqlServerWeaselDatabase actual, SqlServerWeaselDatabase expected, string because)
    {
        (SchemaSnapshot actualSchema, List<string> actualDetails) = await actual.ReadAsync();
        (SchemaSnapshot expectedSchema, List<string> expectedDetails) = await expected.ReadAsync();

        actualSchema.Tables.Should().BeEquivalentTo(expectedSchema.Tables, because);
        actualSchema.Columns.Should().BeEquivalentTo(expectedSchema.Columns, because);
        actualSchema.Indexes.Should().BeEquivalentTo(expectedSchema.Indexes, because);
        actualDetails.Should().BeEquivalentTo(expectedDetails, because);
    }

    private static Task SeedAsync(SqlServerWeaselDatabase database) => database.ExecuteAsync("""
        INSERT INTO QRTZ_JOB_DETAILS (SCHED_NAME, JOB_NAME, JOB_GROUP, JOB_CLASS_NAME, IS_DURABLE, IS_NONCONCURRENT, IS_UPDATE_DATA, REQUESTS_RECOVERY)
          VALUES ('weasel', 'job', 'group', 'Weasel.Job', 1, 0, 0, 0);
        INSERT INTO QRTZ_TRIGGERS (SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP, JOB_NAME, JOB_GROUP, TRIGGER_STATE, TRIGGER_TYPE, START_TIME)
          VALUES ('weasel', 'trigger', 'group', 'job', 'group', 'WAITING', 'SIMPLE', 1);
        INSERT INTO QRTZ_SIMPLE_TRIGGERS (SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP, REPEAT_COUNT, REPEAT_INTERVAL, TIMES_TRIGGERED)
          VALUES ('weasel', 'trigger', 'group', 3, 1000, 0);
        """);

    private static async Task ShouldHoldTheSeedAsync(SqlServerWeaselDatabase database)
    {
        (await database.ScalarAsync("SELECT count(*) FROM QRTZ_JOB_DETAILS")).Should().Be(1);
        (await database.ScalarAsync("SELECT count(*) FROM QRTZ_TRIGGERS")).Should().Be(1);
        (await database.ScalarAsync("SELECT REPEAT_COUNT FROM QRTZ_SIMPLE_TRIGGERS")).Should().Be(3,
            "migrating the schema moves no Quartz data");
    }

    private static string Describe(SchemaMigration migration) => string.Join("; ", migration.Deltas
        .Where(x => x.Difference != SchemaPatchDifference.None)
        .Select(x => $"{x.SchemaObject.Identifier} {x.Difference}"));
}
