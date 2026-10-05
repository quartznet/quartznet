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

using FirebirdSql.Data.FirebirdClient;

using JasperFx;

using MELT;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Tests.Integration.Impl.AdoJobStore;
using Quartz.Weasel.Firebird;

using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Firebird;
using Weasel.Firebird.Tables;

namespace Quartz.Tests.Integration.Weasel;

/// <summary>
/// What Weasel makes of Quartz's Firebird schema, read back from a real catalog: every route to a schema
/// arrives at the same one, older schemas are brought to it by adding, an application's own objects and a
/// second Weasel model survive, racing appliers all succeed, the store runs on what Weasel built, and a
/// table prefix Firebird 3 cannot take is refused before anything runs.
/// </summary>
/// <remarks>
/// Each test runs on a UTF8 database and on a NONE one. <c>QUARTZ_FIREBIRD_IMAGE</c> chooses the server:
/// <c>firebirdsql/firebird:3</c>, <c>:4</c> and <c>:5</c> run these on Firebird 3, 4 and 5.
/// </remarks>
[Category("db-firebird")]
[TestFixture("UTF8")]
[TestFixture("NONE")]
public sealed class FirebirdWeaselSchemaTest
{
    /// <summary>The index names 3.20 created on Firebird that 4.x retired.</summary>
    private const string Retired320Indexes =
        "'IDX_QRTZ_J_REQ_RECOVERY', 'IDX_QRTZ_J_GRP', 'IDX_QRTZ_T_JG', 'IDX_QRTZ_T_N_STATE', 'IDX_QRTZ_T_N_G_STATE',"
        + " 'IDX_QRTZ_T_NEXT_FIRE_TIME', 'IDX_QRTZ_T_NFT_ST_MISFIRE', 'IDX_QRTZ_T_NFT_ST_MISFIRE_GRP', 'IDX_QRTZ_FT_JG', 'IDX_QRTZ_FT_TG'";

    private readonly string charset;
    private readonly List<FirebirdWeaselDatabase> databases = [];
    private FirebirdWeaselDatabase database = null!;

    public FirebirdWeaselSchemaTest(string charset)
    {
        this.charset = charset;
    }

    [SetUp]
    public async Task CreateEmptyDatabase()
    {
        database = await CreateDatabaseAsync();
    }

    [TearDown]
    public async Task DropDatabases()
    {
        foreach (FirebirdWeaselDatabase created in databases)
        {
            await created.DisposeAsync();
        }

        databases.Clear();
    }

    [Test]
    public async Task WeaselCreatesTheSchemaAFreshInstallCreates()
    {
        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-fb-fresh");
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Create);
            SchemaMigration reread = await weasel.CreateMigrationAsync();
            reread.Difference.Should().Be(SchemaPatchDifference.None, "what Weasel just created reads back as the model: " + Describe(reread));
        }

        FirebirdWeaselDatabase fresh = await CreateDatabaseAsync();
        await fresh.RunRepositoryScriptAsync("database", "tables", "tables_firebird.sql");

        await ShouldMatchAsync(database, fresh,
            "a schema is one schema, whichever route built it — types, lengths, nullability, defaults, key and index names, rules and directions included");
    }

    [Test]
    public async Task SchemasTheScriptsCreatedReadAsUnchanged()
    {
        await database.RunRepositoryScriptAsync("database", "tables", "tables_firebird.sql");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-fb-fresh-install");
        await using (services)
        {
            SchemaMigration migration = await weasel.CreateMigrationAsync();
            migration.Difference.Should().Be(SchemaPatchDifference.None, "tables_firebird.sql is the model: " + Describe(migration));

            await weasel.Invoking(x => x.AssertDatabaseMatchesConfigurationAsync()).Should().NotThrowAsync("db-assert passes");
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.None);
        }

        FirebirdWeaselDatabase provisioned = await CreateDatabaseAsync();
        await provisioned.ProvisionAsync();

        (ServiceProvider provisionedServices, IDatabase provisionedWeasel) = await provisioned.WeaselAsync("weasel-fb-provisioned");
        await using (provisionedServices)
        {
            SchemaMigration migration = await provisionedWeasel.CreateMigrationAsync();
            migration.Difference.Should().Be(SchemaPatchDifference.None, "create_firebird.sql is the model: " + Describe(migration));
        }
    }

    /// <summary>
    /// A prefix of its own moves every name the script derives from it, and the schema Weasel creates
    /// under it is the one <c>ProvisionSchema()</c> creates. Six characters, the most Firebird 3 takes;
    /// and a lower-case one, which the store's undelimited SQL and the script fold to upper case.
    /// </summary>
    [TestCase("QRTZR_")]
    [TestCase("qrtz_")]
    public async Task ASchemaUnderAPrefixOfItsOwnReadsAsUnchanged(string prefix)
    {
        string catalogPrefix = prefix.ToUpperInvariant();

        await database.ProvisionAsync(prefix);

        (await database.CountAsync($"SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS WHERE RDB$CONSTRAINT_NAME = 'FK_{catalogPrefix}TRIGGERS_1'"))
            .Should().Be(1, "the premise: the script put the prefix into the foreign key's name");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-fb-prefixed", tablePrefix: prefix);
        await using (services)
        {
            SchemaMigration migration = await weasel.CreateMigrationAsync();
            migration.Difference.Should().Be(SchemaPatchDifference.None,
                "the constraint and index names follow the prefix the way the script derives them: " + Describe(migration));
        }

        FirebirdWeaselDatabase applied = await CreateDatabaseAsync();
        (ServiceProvider appliedServices, IDatabase appliedWeasel) = await applied.WeaselAsync("weasel-fb-prefixed-applied", tablePrefix: prefix);
        await using (appliedServices)
        {
            (await appliedWeasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Create);
        }

        await ShouldMatchAsync(applied, database, "Weasel under a prefix creates what ProvisionSchema() creates under it", catalogPrefix);
    }

    /// <summary>
    /// <c>db-patch</c> against an older schema writes an isql script that brings it to the model: the
    /// added columns and tables, and on a 3.20 schema the retired indexes' guarded drops.
    /// </summary>
    [TestCase("4.2")]
    [TestCase("4.3")]
    [TestCase("3.20")]
    public async Task APatchFromAnOlderSchemaRunsInIsql(string version)
    {
        await database.RunRepositoryScriptAsync("src", "Quartz.Tests.Integration", "SchemaBaselines", version, "tables_firebird.sql");
        await SeedAsync(database);

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-fb-patch-" + version);
        await using (services)
        {
            string patch = await WritePatchAsync(weasel);
            patch.Should().Contain("COMMIT;", "isql applies DDL at commit, and a guarded block beside plain DDL is lost without one");

            await database.RunIsqlAsync(patch);

            SchemaMigration after = await weasel.CreateMigrationAsync();
            after.Difference.Should().Be(SchemaPatchDifference.None, "the patch, run by isql, leaves the model: " + Describe(after));
        }

        FirebirdWeaselDatabase fresh = await CreateDatabaseAsync();
        await fresh.RunRepositoryScriptAsync("database", "tables", "tables_firebird.sql");
        await ShouldMatchAsync(database, fresh, $"a {version} schema patched by isql is a fresh install");
        await ShouldHoldTheSeedAsync(database);
    }

    /// <summary>
    /// The patch for an empty database, and the rollback <c>db-patch</c> writes beside it, both run
    /// through isql: one creates the schema, the other takes it away again.
    /// </summary>
    [Test]
    public async Task APatchAndItsRollbackRunInIsql()
    {
        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-fb-rollback");
        await using (services)
        {
            string file = Path.Combine(Path.GetTempPath(), $"quartz-weasel-fb-{Guid.NewGuid():N}.sql");
            string dropFile = file.Replace(".sql", ".drop.sql", StringComparison.Ordinal);

            try
            {
                await weasel.WriteMigrationFileAsync(file);

                await database.RunIsqlAsync(await File.ReadAllTextAsync(file));
                (await weasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);

                await database.RunIsqlAsync(await File.ReadAllTextAsync(dropFile));
                (await database.CountAsync("SELECT COUNT(*) FROM RDB$RELATIONS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0"))
                    .Should().Be(0, "the rollback of a creation drops what it created, foreign keys first");
            }
            finally
            {
                File.Delete(file);
                File.Delete(dropFile);
            }
        }
    }

    /// <summary>
    /// A rolling upgrade: a 4.2 node is in the middle of a transaction on <c>QRTZ_TRIGGERS</c> when a
    /// 4.4 node applies. Every column 4.3 and 4.4 add is nullable, which Firebird adds beside
    /// uncommitted rows, so the apply does not wait for the node, and the node's commit still succeeds.
    /// </summary>
    [Test]
    public async Task A42SchemaIsUpgradedBesideANodeInTheMiddleOfATransaction()
    {
        await database.RunRepositoryScriptAsync("src", "Quartz.Tests.Integration", "SchemaBaselines", "4.2", "tables_firebird.sql");
        await SeedAsync(database);

        await using FbConnection node = new(database.ConnectionString);
        await node.OpenAsync();
        await using FbTransaction transaction = await node.BeginTransactionAsync();
        await using (FbCommand update = new("UPDATE QRTZ_TRIGGERS SET TRIGGER_STATE = 'ACQUIRED'", node, transaction))
        {
            (await update.ExecuteNonQueryAsync()).Should().Be(1);
        }

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-fb-rolling");
        await using (services)
        {
            Task<SchemaPatchDifference> apply = weasel.ApplyAllConfiguredChangesToDatabaseAsync();
            (await Task.WhenAny(apply, Task.Delay(TimeSpan.FromSeconds(30)))).Should().Be(apply,
                "adding nullable columns does not wait for the node's open transaction");
            (await apply).Should().Be(SchemaPatchDifference.Update);

            await transaction.Invoking(x => x.CommitAsync()).Should().NotThrowAsync("the node's work survives the upgrade");
            (await weasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);
        }

        (await database.ScalarAsync("SELECT TRIGGER_STATE FROM QRTZ_TRIGGERS")).Should().Be("ACQUIRED");
    }

    [Test]
    public async Task A42SchemaIsBroughtToAFreshInstall()
    {
        await database.RunRepositoryScriptAsync("src", "Quartz.Tests.Integration", "SchemaBaselines", "4.2", "tables_firebird.sql");
        await SeedAsync(database);

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-fb-from-42");
        await using (services)
        {
            SchemaMigration planned = await weasel.CreateMigrationAsync();
            planned.Deltas.Where(x => x.Difference != SchemaPatchDifference.None)
                .Should().OnlyContain(x => x.Difference == SchemaPatchDifference.Update || x.Difference == SchemaPatchDifference.Create,
                    "4.3 and 4.4 only add: " + Describe(planned));

            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Update);
            SchemaMigration reread = await weasel.CreateMigrationAsync();
            reread.Difference.Should().Be(SchemaPatchDifference.None, Describe(reread));
        }

        FirebirdWeaselDatabase fresh = await CreateDatabaseAsync();
        await fresh.RunRepositoryScriptAsync("database", "tables", "tables_firebird.sql");

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
        await database.RunRepositoryScriptAsync("src", "Quartz.Tests.Integration", "SchemaBaselines", "4.3", "tables_firebird.sql");
        await SeedAsync(database);
        await SeedHistoryAsync(database);

        (SchemaSnapshot before, List<string> beforeDetails) = await database.ReadAsync();

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-fb-from-43");
        await using (services)
        {
            SchemaMigration planned = await weasel.CreateMigrationAsync();
            List<ISchemaObjectDelta> changed = [.. planned.Deltas.Where(x => x.Difference != SchemaPatchDifference.None)];

            changed.Select(x => (x.SchemaObject.Identifier.Name.ToUpperInvariant(), x.Difference)).Should().BeEquivalentTo(
                [("QRTZ_EXECUTION_HISTORY", SchemaPatchDifference.Update), ("QRTZ_JOB_STATUS", SchemaPatchDifference.Create)],
                "4.4 changes nothing a 4.3 schema has but the history table, and adds the rollup: " + Describe(planned));

            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Update);
            (await weasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None,
                "the second apply finds nothing left to do");
        }

        (SchemaSnapshot after, List<string> afterDetails) = await database.ReadAsync();
        after.Tables.Except(before.Tables).Should().Equal(["JOB_STATUS"]);
        after.Columns.Should().Contain(before.Columns, "an add-only apply alters and drops no column a 4.3 schema has");
        afterDetails.Should().Contain(beforeDetails, "nor a key, an index or a default");

        FirebirdWeaselDatabase fresh = await CreateDatabaseAsync();
        await fresh.RunRepositoryScriptAsync("database", "tables", "tables_firebird.sql");

        await ShouldMatchAsync(database, fresh, "what 4.4 added is all a 4.3 schema lacks");
        await ShouldHoldTheSeedAsync(database);

        (await database.CountAsync(
                "SELECT COUNT(*) FROM QRTZ_EXECUTION_HISTORY WHERE ENTRY_ID = 'entry-43' AND ERROR_MESSAGE = 'failed on 4.3'"
                + " AND RESULT IS NULL AND SUMMARY IS NULL AND METRICS IS NULL AND MANUAL IS NULL AND FIRE_INSTANCE_ID IS NULL AND JOB_INPUT IS NULL AND JOB_INPUT_TOO_LARGE IS NULL"))
            .Should().Be(1, "a row a 4.3 node wrote keeps its values and reads NULL in every column 4.4 added");
    }

    [Test]
    public async Task A320SchemaIsBroughtToAFreshInstallAndItsRetiredIndexesGo()
    {
        await database.RunRepositoryScriptAsync("src", "Quartz.Tests.Integration", "SchemaBaselines", "3.20", "tables_firebird.sql");
        await SeedAsync(database);

        (await database.CountAsync($"SELECT COUNT(*) FROM RDB$INDICES WHERE RDB$INDEX_NAME IN ({Retired320Indexes})"))
            .Should().Be(10, "the premise: 3.20 created the ten indexes 4.0 retired on Firebird");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-fb-from-320");
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().NotBe(SchemaPatchDifference.None);
            SchemaMigration reread = await weasel.CreateMigrationAsync();
            reread.Difference.Should().Be(SchemaPatchDifference.None, Describe(reread));
        }

        (await database.CountAsync($"SELECT COUNT(*) FROM RDB$INDICES WHERE RDB$INDEX_NAME IN ({Retired320Indexes})"))
            .Should().Be(0, "the tables are add-only, and the model names the retired indexes to drop them all the same");

        FirebirdWeaselDatabase fresh = await CreateDatabaseAsync();
        await fresh.RunRepositoryScriptAsync("database", "tables", "tables_firebird.sql");

        await ShouldMatchAsync(database, fresh, "the 4.x columns, tables and indexes arrive, and the retired ones go");
        await ShouldHoldTheSeedAsync(database);
    }

    [Test]
    public async Task ObjectsTheApplicationAddedAndASecondModelSurviveAnApply()
    {
        await database.RunRepositoryScriptAsync("database", "tables", "tables_firebird.sql");
        await SeedAsync(database);

        await database.ExecuteAsync(
            "ALTER TABLE QRTZ_TRIGGERS ADD USER_NOTE VARCHAR(100)",
            "UPDATE QRTZ_TRIGGERS SET USER_NOTE = 'keep me'",
            "CREATE INDEX IDX_USER_DESCRIPTION ON QRTZ_TRIGGERS (DESCRIPTION)",
            "ALTER TABLE QRTZ_JOB_DETAILS ADD CONSTRAINT USER_CLASS_NAME_NOT_BLANK CHECK (JOB_CLASS_NAME <> '')",
            """
            CREATE TABLE APP_AUDIT (
              ID INTEGER NOT NULL PRIMARY KEY,
              SCHED_NAME VARCHAR(120), JOB_NAME VARCHAR(150), JOB_GROUP VARCHAR(150),
              CONSTRAINT FK_APP_AUDIT_QRTZ_JOB_DETAILS FOREIGN KEY (SCHED_NAME, JOB_NAME, JOB_GROUP)
                REFERENCES QRTZ_JOB_DETAILS (SCHED_NAME, JOB_NAME, JOB_GROUP))
            """,
            "INSERT INTO APP_AUDIT (ID, SCHED_NAME, JOB_NAME, JOB_GROUP) VALUES (1, 'weasel', 'job', 'group')",
            "ALTER TABLE QRTZ_TRIGGERS ADD CONSTRAINT FK_USER_TRIGGERS_CALENDARS FOREIGN KEY (SCHED_NAME, CALENDAR_NAME) REFERENCES QRTZ_CALENDARS (SCHED_NAME, CALENDAR_NAME)");

        Table settings = new("APP_SETTINGS");
        settings.AddColumn("SETTING_KEY", "VARCHAR(100)").NotNull().AsPrimaryKey();
        settings.AddColumn("SETTING_VALUE", "BLOB SUB_TYPE TEXT");
        settings.PrimaryKeyName = "PK_APP_SETTINGS";

        await using (FbConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            FirebirdMigrator migrator = new();
            await migrator.ApplyAllAsync(connection, await SchemaMigration.DetermineAsync(connection, migrator, default, settings), AutoCreate.CreateOrUpdate);
        }

        await database.ExecuteAsync("INSERT INTO APP_SETTINGS (SETTING_KEY, SETTING_VALUE) VALUES ('k', 'v')");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-fb-coexisting");
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.None,
                "the add-only model withholds the drops of what it does not declare, and there is nothing else to do");
        }

        (await database.ScalarAsync("SELECT USER_NOTE FROM QRTZ_TRIGGERS")).Should().Be("keep me");
        (await database.CountAsync("SELECT COUNT(*) FROM RDB$INDICES WHERE RDB$INDEX_NAME = 'IDX_USER_DESCRIPTION'")).Should().Be(1);
        (await database.CountAsync("SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS WHERE RDB$CONSTRAINT_NAME = 'USER_CLASS_NAME_NOT_BLANK'")).Should().Be(1);
        (await database.CountAsync("SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS WHERE RDB$CONSTRAINT_NAME = 'FK_USER_TRIGGERS_CALENDARS'")).Should().Be(1,
            "a foreign key the application added to a Quartz table is kept");
        (await database.CountAsync("SELECT COUNT(*) FROM RDB$RELATION_CONSTRAINTS WHERE RDB$CONSTRAINT_NAME = 'FK_APP_AUDIT_QRTZ_JOB_DETAILS'")).Should().Be(1);
        (await database.CountAsync("SELECT COUNT(*) FROM APP_AUDIT")).Should().Be(1);
        (await database.ScalarAsync("SELECT CAST(SETTING_VALUE AS VARCHAR(10)) FROM APP_SETTINGS")).Should().Be("v");

        await using FbConnection check = new(database.ConnectionString);
        await check.OpenAsync();
        (await SchemaMigration.DetermineAsync(check, new FirebirdMigrator(), default, settings)).Difference
            .Should().Be(SchemaPatchDifference.None, "Weasel only ever looks at the objects a model declares");
    }

    /// <summary>
    /// An application widened a Quartz column. Firebird cannot narrow it back, so Weasel reads the table
    /// as a change it cannot make and refuses the apply before anything runs — once: a refusal is no race.
    /// </summary>
    [Test]
    public async Task AColumnTheApplicationWidenedIsRefusedBeforeAnythingRuns()
    {
        await database.RunRepositoryScriptAsync("database", "tables", "tables_firebird.sql");
        await database.ExecuteAsync("ALTER TABLE QRTZ_TRIGGERS ALTER DESCRIPTION TYPE VARCHAR(500)");

        (SchemaSnapshot before, List<string> beforeDetails) = await database.ReadAsync();

        ITestLoggerFactory loggerFactory = TestLoggerFactory.Create();
        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-fb-widened", loggerFactory: loggerFactory);
        await using (services)
        {
            (await weasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.Invalid);

            Func<Task> apply = () => weasel.ApplyAllConfiguredChangesToDatabaseAsync();
            (await apply.Should().ThrowAsync<SchedulerException>().WithMessage("*'weasel-fb-widened'*not retried*"))
                .WithInnerException<SchemaMigrationException>().WithMessage("*DESCRIPTION*Firebird only widens*");
        }

        loggerFactory.Sink.LogEntries.Select(x => x.EventId.Id).Should().NotContain([10008, 10009],
            "a refusal is not retried, nor is the schema read again for it");

        (SchemaSnapshot after, List<string> afterDetails) = await database.ReadAsync();
        after.Columns.Should().BeEquivalentTo(before.Columns, "nothing was changed");
        afterDetails.Should().BeEquivalentTo(beforeDetails);
    }

    /// <summary>
    /// Nodes coming up at once against an empty database, with no lock between them: every statement is
    /// guarded, so each applier either creates an object or finds it there, and every one of them ends
    /// with the schema a fresh install has.
    /// </summary>
    /// <remarks>
    /// Weasel.Firebird is what absorbs the race — a guarded statement that loses one runs again and
    /// finds the object there — so no applier should need Quartz's own re-read after a failed apply,
    /// which is a safety net rather than the mechanism. The Weasel log events say which it was. With no
    /// lock, several appliers read the database empty and all of them report <c>Create</c>.
    /// </remarks>
    [Test]
    public async Task ConcurrentAppliersAllSucceedAndLeaveAFreshInstall()
    {
        FirebirdWeaselDatabase fresh = await CreateDatabaseAsync();
        await fresh.RunRepositoryScriptAsync("database", "tables", "tables_firebird.sql");

        object version = (await database.ScalarAsync("SELECT rdb$get_context('SYSTEM', 'ENGINE_VERSION') FROM rdb$database"))!;

        for (int round = 0; round < 3; round++)
        {
            FirebirdWeaselDatabase target = round == 0 ? database : await CreateDatabaseAsync();
            ITestLoggerFactory loggerFactory = TestLoggerFactory.Create();

            List<(ServiceProvider Services, IDatabase Database)> appliers = [];
            for (int i = 0; i < 5; i++)
            {
                appliers.Add(await target.WeaselAsync($"weasel-fb-race-{i}", loggerFactory: loggerFactory));
            }

            try
            {
                using SemaphoreSlim start = new(0);
                Task<SchemaPatchDifference>[] racing = appliers.Select(x => Task.Run(async () =>
                {
                    await start.WaitAsync();
                    return await x.Database.ApplyAllConfiguredChangesToDatabaseAsync();
                })).ToArray();

                start.Release(racing.Length);
                SchemaPatchDifference[] results = await Task.WhenAll(racing);

                List<string> retried = loggerFactory.Sink.LogEntries
                    .Where(x => x.EventId.Id is 10007 or 10008 or 10009)
                    .Select(x => $"{x.EventId.Id}: {x.Message} {x.Exception?.GetBaseException().Message}")
                    .ToList();

                TestContext.Out.WriteLine(
                    $"Firebird {version} ({charset}) round {round}: racing appliers reported {string.Join(", ", results)};"
                    + $" {retried.Count} Quartz-level retries{string.Concat(retried.Select(x => Environment.NewLine + "    " + x))}");

                results.Should().Contain(SchemaPatchDifference.Create, "somebody created the schema");
                results.Should().OnlyContain(x => x == SchemaPatchDifference.Create || x == SchemaPatchDifference.None,
                    "an applier either read the database empty or read it finished");
                retried.Should().BeEmpty("Weasel.Firebird's guarded statements absorb a lost race themselves");
                (await appliers[0].Database.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);
            }
            finally
            {
                foreach ((ServiceProvider services, IDatabase _) in appliers)
                {
                    await services.DisposeAsync();
                }
            }

            await ShouldMatchAsync(target, fresh, "five appliers racing leave the one schema a fresh install has");
        }
    }

    /// <summary>
    /// The Firebird driver delegate schedules, fires and reads back a trigger of every persisted family
    /// on tables Weasel created, beside a fresh <c>QRTZ_</c> install in the same database.
    /// </summary>
    [Test]
    public async Task TheStoreRunsOnASchemaWeaselBuilt()
    {
        const string prefix = "QRTZW_";

        await database.RunRepositoryScriptAsync("database", "tables", "tables_firebird.sql");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-fb-smoke", tablePrefix: prefix);
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Create);
        }

        await using FbConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await MigratedSchemaWorkload.RunAsync(connection, "firebird", database.ConnectionString, prefix);
    }

    /// <summary>
    /// Firebird 3 names are at most 31 bytes, which a table prefix of seven characters overruns. The
    /// refusal comes before a connection is opened, so the database is left as it was.
    /// </summary>
    [Test]
    public async Task APrefixTooLongForFirebird3IsRefusedBeforeAnythingRuns()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "weasel-fb-long-prefix");
            q.UsePersistentStore(store =>
            {
                store.UseFirebird(FirebirdClientFactory.Instance, database.ConnectionString);
                store.ConfigureStore(options => options.TablePrefix = "QRTZ12_");
                store.UseWeaselForFirebird();
            });
        });

        await using (ServiceProvider provider = services.BuildServiceProvider())
        {
            Func<Task> build = async () => await provider.GetRequiredService<IDatabaseSource>().BuildDatabases();
            await build.Should().ThrowAsync<SchedulerConfigException>().WithMessage("*IDX_QRTZ12_FT_INST_JOB_REQ_RCVRY*at most 6 characters*");
        }

        (await database.CountAsync("SELECT COUNT(*) FROM RDB$RELATIONS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0"))
            .Should().Be(0, "nothing was created");

        if (await database.ServerMajorVersionAsync() == 3)
        {
            // Firebird 3 cannot take the longer prefix at all: see
            // TheFirebird4LimitOnFirebird3IsRefusedBeforeTheCatalogIsRead.
            return;
        }

        (ServiceProvider fourServices, IDatabase fourWeasel) = await database.WeaselAsync(
            "weasel-fb-long-prefix-63", "QRTZ_REPORTING_", options => options.MaxIdentifierLength = FirebirdWeaselOptions.Firebird4MaxIdentifierLength);
        await using (fourServices)
        {
            (await fourWeasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Create,
                "Firebird 4 and 5 take 63-character names");
            (await fourWeasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);
        }

        (await database.CountAsync("SELECT COUNT(*) FROM RDB$INDICES WHERE RDB$INDEX_NAME = 'IDX_QRTZ_REPORTING_FT_INST_JOB_REQ_RCVRY'"))
            .Should().Be(1);
    }

    /// <summary>
    /// <c>MaxIdentifierLength = 63</c> is for a database only Firebird 4 or later opens. Set against
    /// Firebird 3, Quartz's own check lets <c>QRTZ_REPORTING_</c> through, and Weasel refuses the first
    /// table name Firebird 3's catalog cannot hold before it binds it as a parameter — naming it, the
    /// server's limit, and that nothing by that name can exist — on every route in, and creates nothing.
    /// </summary>
    /// <remarks>
    /// The name used to reach the catalog query and fail it with "string truncation" (335544321), which
    /// said nothing about the name. Firebird 4 and 5 cannot be brought to the same place through Quartz:
    /// past their 63-character limit, Quartz refuses the prefix itself.
    /// </remarks>
    [Test]
    public async Task TheFirebird4LimitOnFirebird3IsRefusedBeforeTheCatalogIsRead()
    {
        if (await database.ServerMajorVersionAsync() != 3)
        {
            Assert.Ignore("Firebird 4 and 5 hold 63-character names, which is the most Quartz lets MaxIdentifierLength be.");
        }

        const string refusal = "*QRTZ_REPORTING_*the name of a table*over the 31-byte limit*Firebird 3*nothing by this name can exist*";

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync(
            "weasel-fb-63-on-3", "QRTZ_REPORTING_", options => options.MaxIdentifierLength = FirebirdWeaselOptions.Firebird4MaxIdentifierLength);
        await using (services)
        {
            Func<Task> read = () => weasel.CreateMigrationAsync();
            await read.Should().ThrowAsync<InvalidOperationException>().WithMessage(refusal, "db-assert and db-patch read the catalog first");

            Func<Task> apply = () => weasel.ApplyAllConfiguredChangesToDatabaseAsync();
            (await apply.Should().ThrowAsync<SchedulerException>().WithMessage("*'weasel-fb-63-on-3'*not retried*"))
                .WithInnerException<InvalidOperationException>().WithMessage(refusal);
        }

        (await database.CountAsync("SELECT COUNT(*) FROM RDB$RELATIONS WHERE COALESCE(RDB$SYSTEM_FLAG, 0) = 0"))
            .Should().Be(0, "nothing was created");
    }

    /// <summary>What <c>db-patch</c> writes for this database, read back.</summary>
    /// <summary>
    /// A statement run a second time, failed the way the applier that loses it fails — the object is
    /// already there — and, for contrast, failures no race causes. The numbers are the server's own, which
    /// is what <see cref="FirebirdQuartzDatabase.LostRaceErrors" /> is held to.
    /// </summary>
    [TestCaseSource(nameof(FailedStatements))]
    public async Task OnlyAnErrorALostRaceRaisesIsReadAgain(string[] setup, string statement, int number, bool lostRace)
    {
        await database.ExecuteAsync(setup);

        Func<Task> execute = () => database.ExecuteAsync(statement);
        FbException error = (await execute.Should().ThrowAsync<FbException>()).Which;

        Numbers(error).Should().Contain(number, error.Message);
        FirebirdQuartzDatabase.IsLostRace(error).Should().Be(lostRace, error.Message);
    }

    /// <summary>
    /// A column another applier is adding, committed or not, is the catalog's unique key to the one that
    /// adds it second, whether its transaction waits or not.
    /// </summary>
    [TestCase(FbTransactionBehavior.NoWait, false)]
    [TestCase(FbTransactionBehavior.Wait, false)]
    [TestCase(FbTransactionBehavior.Wait, true)]
    public async Task AColumnAnotherApplierIsAddingIsALostRace(FbTransactionBehavior behavior, bool otherCommits)
    {
        const string AddColumn = "ALTER TABLE RACE_T ADD NOTE VARCHAR(10)";

        await database.ExecuteAsync("CREATE TABLE RACE_T (ID INTEGER NOT NULL)");
        FbException error = await RaceAsync(AddColumn, AddColumn, behavior, otherCommits);

        Numbers(error).Should().Contain(335544665, error.Message);
        FirebirdQuartzDatabase.IsLostRace(error).Should().BeTrue(error.Message);
    }

    /// <summary>
    /// A catalog row another applier is changing — here a column's type — is a deadlock and an update
    /// conflict to the one that changes it second, whether it waits or not; once the other commits, an
    /// update conflict.
    /// </summary>
    [TestCase(FbTransactionBehavior.NoWait, false, new[] { 335544336, 335544451 })]
    [TestCase(FbTransactionBehavior.Wait, false, new[] { 335544336, 335544451 })]
    [TestCase(FbTransactionBehavior.Wait, true, new[] { 335544451 })]
    public async Task ACatalogRowAnotherApplierIsChangingIsALostRace(FbTransactionBehavior behavior, bool otherCommits, int[] numbers)
    {
        const string AlterType = "ALTER TABLE RACE_T ALTER NOTE TYPE VARCHAR(20)";

        await database.ExecuteAsync("CREATE TABLE RACE_T (ID INTEGER NOT NULL, NOTE VARCHAR(10))");
        FbException error = await RaceAsync(AlterType, AlterType, behavior, otherCommits);

        Numbers(error).Should().Contain(numbers, error.Message);
        FirebirdQuartzDatabase.IsLostRace(error).Should().BeTrue(error.Message);
    }

    /// <summary>
    /// An index created on a table another attachment is writing to — a node still running, during a
    /// rolling upgrade — is refused at commit, the table being in use: a lock conflict to a <c>NO WAIT</c>
    /// transaction, a lock time-out to a <c>WAIT</c> one, both SQLSTATE 40001.
    /// </summary>
    [TestCase(FbTransactionBehavior.NoWait, 335544345)]
    [TestCase(FbTransactionBehavior.Wait, 335544510)]
    public async Task AnIndexOnATableAnotherAttachmentIsWritingToIsALostRace(FbTransactionBehavior behavior, int number)
    {
        await database.ExecuteAsync("CREATE TABLE RACE_T (ID INTEGER NOT NULL)");
        FbException error = await RaceAsync("INSERT INTO RACE_T (ID) VALUES (1)", "CREATE INDEX IDX_RACE ON RACE_T (ID)", behavior, otherCommits: false);

        Numbers(error).Should().Contain(number, error.Message);
        error.SQLSTATE.Should().Be("40001");
        FirebirdQuartzDatabase.IsLostRace(error).Should().BeTrue(error.Message);
    }

    /// <summary>
    /// <c>too many keys defined for index</c> under <c>unsuccessful metadata update</c> is what the second
    /// loser of a <c>CREATE INDEX</c> race gets at commit. The one way to raise it at will is an index over
    /// more than 16 columns, which reads the same — and which Weasel refuses before anything runs.
    /// </summary>
    [Test]
    public async Task TooManyKeysUnderAFailedMetadataUpdateIsALostRace()
    {
        string[] columns = [.. Enumerable.Range(1, 17).Select(i => $"C{i}")];
        await database.ExecuteAsync($"CREATE TABLE RACE_T ({string.Join(", ", columns.Select(x => x + " INTEGER"))})");

        Func<Task> execute = () => database.ExecuteAsync($"CREATE INDEX IDX_RACE ON RACE_T ({string.Join(", ", columns)})");
        FbException error = (await execute.Should().ThrowAsync<FbException>()).Which;

        Numbers(error).Should().Contain([335544351, 335544631], error.Message);
        FirebirdQuartzDatabase.IsLostRace(error).Should().BeTrue(error.Message);
    }

    private static IEnumerable<TestCaseData> FailedStatements()
    {
        const string Table = "CREATE TABLE RACE_T (ID INTEGER NOT NULL, NOTE VARCHAR(10))";
        const string Index = "CREATE INDEX IDX_RACE ON RACE_T (ID)";
        const string Procedure = "CREATE PROCEDURE RACE_P AS BEGIN END";
        const string Function = "CREATE FUNCTION RACE_F RETURNS INTEGER AS BEGIN RETURN 1; END";

        yield return Case("A CREATE TABLE another applier made first", [Table], Table, 336068740, true);
        yield return Case("A CREATE INDEX another applier made first", [Table, Index], Index, 336068859, true);
        yield return Case("A CREATE PROCEDURE another applier made first", [Procedure], Procedure, 336068743, true);
        yield return Case("A CREATE FUNCTION another applier made first", [Function], Function, 336068876, true);

        yield return Case("A table that is not there", [], "ALTER TABLE NO_SUCH_TABLE ADD NOTE VARCHAR(10)", 335544351, false);
        yield return Case("A column narrowed", [Table], "ALTER TABLE RACE_T ALTER NOTE TYPE VARCHAR(5)", 335544351, false);
        yield return Case("A syntax error", [], "CREATE TABLE", 335544569, false);

        static TestCaseData Case(string name, string[] setup, string statement, int number, bool lostRace) =>
            new TestCaseData(setup, statement, number, lostRace).SetArgDisplayNames(name);
    }

    private static HashSet<int> Numbers(FbException error) => [error.ErrorCode, .. error.Errors.Select(x => x.Number)];

    /// <summary>
    /// Runs <paramref name="otherSql" /> on the other applier's connection, left uncommitted, and then
    /// <paramref name="sql" /> on this applier's, under <paramref name="behavior" /> with a two-second wait,
    /// committing it. The other commits after this one has started waiting, or rolls back after it failed.
    /// </summary>
    private async Task<FbException> RaceAsync(string otherSql, string sql, FbTransactionBehavior behavior, bool otherCommits)
    {
        // Unpooled, so both attachments end with the race and the database can be dropped.
        string unpooled = new FbConnectionStringBuilder(database.ConnectionString) { Pooling = false }.ConnectionString;

        await using FbConnection other = new(unpooled);
        await other.OpenAsync();
        await using FbTransaction otherTransaction = await other.BeginTransactionAsync();
        await using (FbCommand first = new(otherSql, other, otherTransaction))
        {
            await first.ExecuteNonQueryAsync();
        }

        await using FbConnection applier = new(unpooled);
        await applier.OpenAsync();
        await using FbTransaction transaction = await applier.BeginTransactionAsync(new FbTransactionOptions
        {
            TransactionBehavior = behavior | FbTransactionBehavior.ReadCommitted | FbTransactionBehavior.RecVersion,
            WaitTimeout = behavior == FbTransactionBehavior.Wait ? TimeSpan.FromSeconds(2) : null,
        });

        await using FbCommand second = new(sql, applier, transaction);
        Task losing = Task.Run(async () =>
        {
            await second.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        });

        if (otherCommits)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            await otherTransaction.CommitAsync();
        }

        bool finished = await Task.WhenAny(losing, Task.Delay(TimeSpan.FromSeconds(30))) == losing;
        if (!finished)
        {
            // Closing the other attachment is what lets a statement waiting on it go, so the test fails
            // instead of hanging the leg.
            await other.DisposeAsync();
        }

        finished.Should().BeTrue($"'{sql}' has to fail, not wait on the other applier for good");

        Func<Task> lose = () => losing;
        FbException error = (await lose.Should().ThrowAsync<FbException>()).Which;

        if (!otherCommits)
        {
            await otherTransaction.RollbackAsync();
        }

        return error;
    }

    private static async Task<string> WritePatchAsync(IDatabase weasel)
    {
        string file = Path.Combine(Path.GetTempPath(), $"quartz-weasel-fb-{Guid.NewGuid():N}.sql");

        try
        {
            await weasel.WriteMigrationFileAsync(file);
            return await File.ReadAllTextAsync(file);
        }
        finally
        {
            File.Delete(file);
            File.Delete(file.Replace(".sql", ".drop.sql", StringComparison.Ordinal));
        }
    }

    private async Task<FirebirdWeaselDatabase> CreateDatabaseAsync()
    {
        FirebirdWeaselDatabase created = await FirebirdWeaselDatabase.CreateAsync(charset);
        databases.Add(created);
        return created;
    }

    private static async Task ShouldMatchAsync(FirebirdWeaselDatabase actual, FirebirdWeaselDatabase expected, string because, string prefix = "QRTZ_")
    {
        (SchemaSnapshot actualSchema, List<string> actualDetails) = await actual.ReadAsync(prefix);
        (SchemaSnapshot expectedSchema, List<string> expectedDetails) = await expected.ReadAsync(prefix);

        // The premise, so that two empty readings cannot pass for one schema: fifteen tables, every
        // column read with its details, and fifteen primary keys, five foreign keys and thirteen indexes.
        expectedSchema.Tables.Should().HaveCount(15);
        expectedDetails.Count(x => x.StartsWith("COL ", StringComparison.Ordinal)).Should().Be(expectedSchema.Columns.Count);
        expectedDetails.Count(x => x.StartsWith("IX ", StringComparison.Ordinal)).Should().Be(33);
        expectedDetails.Should().Contain(
            $"IX {prefix}TRIGGERS|FK_{prefix}TRIGGERS_1|0|0|0|FK_{prefix}TRIGGERS_1|FOREIGN KEY|RESTRICT|RESTRICT|PK_{prefix}JOB_DETAILS|SCHED_NAME,JOB_NAME,JOB_GROUP",
            "a foreign key with no rule reads back as RESTRICT, on the key it references");

        actualSchema.Tables.Should().BeEquivalentTo(expectedSchema.Tables, because);
        actualSchema.Columns.Should().BeEquivalentTo(expectedSchema.Columns, because);
        actualSchema.Indexes.Should().BeEquivalentTo(expectedSchema.Indexes, because);
        actualDetails.Except(expectedDetails).Should().BeEmpty(because + "; only in the expected schema: " + string.Join("; ", expectedDetails.Except(actualDetails)));
        expectedDetails.Except(actualDetails).Should().BeEmpty(because);
    }

    private static Task SeedAsync(FirebirdWeaselDatabase database) => database.ExecuteAsync(
        """
        INSERT INTO QRTZ_JOB_DETAILS (SCHED_NAME, JOB_NAME, JOB_GROUP, JOB_CLASS_NAME, IS_DURABLE, IS_NONCONCURRENT, IS_UPDATE_DATA, REQUESTS_RECOVERY)
          VALUES ('weasel', 'job', 'group', 'Weasel.Job', 1, 0, 0, 0)
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
    private static Task SeedHistoryAsync(FirebirdWeaselDatabase database) => database.ExecuteAsync(
        """
        INSERT INTO QRTZ_EXECUTION_HISTORY (SCHED_NAME, ENTRY_ID, INSTANCE_NAME, JOB_NAME, JOB_GROUP, TRIGGER_NAME, TRIGGER_GROUP, FIRED_TIME, RUN_TIME, SUCCEEDED, ERROR_MESSAGE)
          VALUES ('weasel', 'entry-43', 'node-43', 'job', 'group', 'trigger', 'group', 1, 10, 0, 'failed on 4.3')
        """);

    private static async Task ShouldHoldTheSeedAsync(FirebirdWeaselDatabase database)
    {
        (await database.CountAsync("SELECT COUNT(*) FROM QRTZ_JOB_DETAILS")).Should().Be(1);
        (await database.CountAsync("SELECT COUNT(*) FROM QRTZ_TRIGGERS")).Should().Be(1);
        (await database.CountAsync("SELECT REPEAT_COUNT FROM QRTZ_SIMPLE_TRIGGERS")).Should().Be(3,
            "migrating the schema moves no Quartz data");
    }

    private static string Describe(SchemaMigration migration) => string.Join("; ", migration.Deltas
        .Where(x => x.Difference != SchemaPatchDifference.None)
        .Select(x => $"{x.SchemaObject.Identifier} {x.Difference}"));
}
