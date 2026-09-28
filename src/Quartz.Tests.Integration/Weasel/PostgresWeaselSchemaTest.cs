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

using Npgsql;

using Quartz.Tests.Integration.Impl.AdoJobStore;

using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Quartz.Tests.Integration.Weasel;

/// <summary>
/// What Weasel makes of Quartz's PostgreSQL schema, read back from a real catalog: every route to a
/// schema arrives at the same one, an application's own objects and a second Weasel model survive, and
/// appliers racing on one database take turns.
/// </summary>
[Category("db-postgres")]
public sealed class PostgresWeaselSchemaTest
{
    private PostgresWeaselDatabase database = null!;

    [SetUp]
    public async Task CreateEmptyDatabase()
    {
        database = await PostgresWeaselDatabase.CreateAsync();
    }

    [TearDown]
    public async Task DropDatabase()
    {
        await database.DisposeAsync();
    }

    [Test]
    public async Task WeaselCreatesTheSchemaAFreshInstallCreates()
    {
        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-pg-fresh");
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Create);
            (await weasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None,
                "what Weasel just created reads back as the model");
        }

        await using PostgresWeaselDatabase fresh = await PostgresWeaselDatabase.CreateAsync();
        await fresh.RunRepositoryScriptAsync("database", "tables", "tables_postgres.sql");

        await ShouldMatchAsync(database, fresh, "a schema is one schema, whichever route built it — constraint names included");
    }

    [Test]
    public async Task SchemasTheScriptsCreatedReadAsUnchanged()
    {
        await database.RunRepositoryScriptAsync("database", "tables", "tables_postgres.sql");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-pg-fresh-install");
        await using (services)
        {
            SchemaMigration migration = await weasel.CreateMigrationAsync();
            migration.Difference.Should().Be(SchemaPatchDifference.None, "tables_postgres.sql is the model: " + Describe(migration));

            await weasel.Invoking(x => x.AssertDatabaseMatchesConfigurationAsync()).Should().NotThrowAsync("db-assert passes");
        }

        await using PostgresWeaselDatabase provisioned = await PostgresWeaselDatabase.CreateAsync();
        await provisioned.ProvisionAsync();

        (ServiceProvider provisionedServices, IDatabase provisionedWeasel) = await provisioned.WeaselAsync("weasel-pg-provisioned");
        await using (provisionedServices)
        {
            SchemaMigration migration = await provisionedWeasel.CreateMigrationAsync();
            migration.Difference.Should().Be(SchemaPatchDifference.None, "create_postgres.sql is the model: " + Describe(migration));
        }
    }

    /// <summary>
    /// A longer prefix, in a schema of its own, moves every name PostgreSQL derives from the table name —
    /// and past 63 bytes, where it cuts them.
    /// </summary>
    [Test]
    public async Task ASchemaUnderAPrefixAndASchemaOfItsOwnReadsAsUnchanged()
    {
        await database.ExecuteAsync("CREATE SCHEMA scheduling");
        await database.ProvisionAsync("scheduling.QRTZ_REPORTING_");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-pg-prefixed", tablePrefix: "scheduling.QRTZ_REPORTING_");
        await using (services)
        {
            SchemaMigration migration = await weasel.CreateMigrationAsync();
            migration.Difference.Should().Be(SchemaPatchDifference.None,
                "the constraint names follow the prefix the way PostgreSQL derives and truncates them: " + Describe(migration));
        }

        (await database.ScalarAsync(
                "SELECT count(*) FROM pg_constraint WHERE connamespace = 'scheduling'::regnamespace AND length(conname) = 63"))
            .Should().Be(4L, "the premise: under this prefix four of the five foreign keys were cut to 63 bytes");
    }

    [Test]
    public async Task A42SchemaIsBroughtToAFreshInstall()
    {
        await database.RunRepositoryScriptAsync("src", "Quartz.Tests.Integration", "SchemaBaselines", "4.2", "tables_postgres.sql");
        await SeedAsync(database);

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-pg-from-42");
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Update);
            (await weasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);
        }

        await using PostgresWeaselDatabase fresh = await PostgresWeaselDatabase.CreateAsync();
        await fresh.RunRepositoryScriptAsync("database", "tables", "tables_postgres.sql");

        await ShouldMatchAsync(database, fresh, "4.3's columns are all a 4.2 schema lacks");
        await ShouldHoldTheSeedAsync(database);
    }

    [Test]
    public async Task A320SchemaIsBroughtToAFreshInstallAndItsRetiredIndexesGo()
    {
        await database.RunRepositoryScriptAsync("src", "Quartz.Tests.Integration", "SchemaBaselines", "3.20", "tables_postgres.sql");
        await SeedAsync(database);

        (await database.ScalarAsync("SELECT count(*) FROM pg_indexes WHERE indexname IN ('idx_qrtz_j_req_recovery', 'idx_qrtz_t_next_fire_time')"))
            .Should().Be(2L, "the premise: 3.20 created the two indexes 4.0 retired on PostgreSQL");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-pg-from-320");
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().NotBe(SchemaPatchDifference.None);
            (await weasel.CreateMigrationAsync()).Difference.Should().Be(SchemaPatchDifference.None);
        }

        await using PostgresWeaselDatabase fresh = await PostgresWeaselDatabase.CreateAsync();
        await fresh.RunRepositoryScriptAsync("database", "tables", "tables_postgres.sql");

        await ShouldMatchAsync(database, fresh,
            "the 4.0 columns, table and index shapes arrive, and the retired indexes go although the tables are add-only");
        await ShouldHoldTheSeedAsync(database);
    }

    [Test]
    public async Task ObjectsTheApplicationAddedAndASecondModelSurviveAnApply()
    {
        await database.RunRepositoryScriptAsync("database", "tables", "tables_postgres.sql");
        await SeedAsync(database);

        await database.ExecuteAsync("""
            ALTER TABLE qrtz_triggers ADD COLUMN user_note text;
            UPDATE qrtz_triggers SET user_note = 'keep me';
            CREATE INDEX idx_user_description ON qrtz_triggers (description);
            ALTER TABLE qrtz_job_details ADD CONSTRAINT user_class_name_not_blank CHECK (job_class_name <> '');
            CREATE TABLE app_audit (
              id serial PRIMARY KEY,
              sched_name text, job_name text, job_group text,
              FOREIGN KEY (sched_name, job_name, job_group) REFERENCES qrtz_job_details (sched_name, job_name, job_group));
            INSERT INTO app_audit (sched_name, job_name, job_group) VALUES ('weasel', 'job', 'group');
            """);

        Table settings = new("app_settings");
        settings.AddColumn("key", "text").NotNull().AsPrimaryKey();
        settings.AddColumn("value", "text");

        await using (NpgsqlConnection connection = new(database.ConnectionString))
        {
            await connection.OpenAsync();
            PostgresqlMigrator migrator = new();
            await migrator.ApplyAllAsync(connection, await SchemaMigration.DetermineAsync(connection, migrator, default, settings), AutoCreate.CreateOrUpdate);
        }

        await database.ExecuteAsync("INSERT INTO app_settings (key, value) VALUES ('k', 'v')");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-pg-coexisting");
        await using (services)
        {
            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.None,
                "the add-only model withholds the drops of what it does not declare, and there is nothing else to do");
        }

        (await database.ScalarAsync("SELECT user_note FROM qrtz_triggers")).Should().Be("keep me");
        (await database.ScalarAsync("SELECT count(*) FROM pg_indexes WHERE indexname = 'idx_user_description'")).Should().Be(1L);
        (await database.ScalarAsync("SELECT count(*) FROM pg_constraint WHERE conname = 'user_class_name_not_blank'")).Should().Be(1L);
        (await database.ScalarAsync("SELECT count(*) FROM app_audit")).Should().Be(1L);
        (await database.ScalarAsync("SELECT value FROM app_settings")).Should().Be("v");

        await using NpgsqlConnection check = new(database.ConnectionString);
        await check.OpenAsync();
        (await SchemaMigration.DetermineAsync(check, new PostgresqlMigrator(), default, settings)).Difference
            .Should().Be(SchemaPatchDifference.None, "Weasel only ever looks at the objects a model declares");
    }

    /// <summary>
    /// Nodes coming up at once against an empty database: the advisory lock lets one of them create the
    /// schema, and the others, once they hold it, read a schema with nothing left to do.
    /// </summary>
    [Test]
    public async Task ConcurrentAppliersTakeTurnsAndOneCreates()
    {
        List<(ServiceProvider Services, IDatabase Database)> appliers = [];
        for (int i = 0; i < 5; i++)
        {
            appliers.Add(await database.WeaselAsync($"weasel-pg-race-{i}"));
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
        // A view where Quartz wants a table: CREATE TABLE IF NOT EXISTS passes over the name, and creating
        // an index on a view then fails.
        await database.ExecuteAsync("CREATE VIEW qrtz_calendars AS SELECT 'x'::text AS sched_name, 'y'::text AS calendar_name");

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-pg-failing");
        await using (services)
        {
            Func<Task> apply = () => weasel.ApplyAllConfiguredChangesToDatabaseAsync();
            await apply.Should().ThrowAsync<Exception>();
        }

        await using NpgsqlConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand tryLock = new($"SELECT pg_try_advisory_lock({PostgresWeaselOptions.DefaultLockId})", connection);
        (await tryLock.ExecuteScalarAsync()).Should().Be(true, "the failed apply gave the lock back");
        await using NpgsqlCommand unlock = new($"SELECT pg_advisory_unlock({PostgresWeaselOptions.DefaultLockId})", connection);
        await unlock.ExecuteScalarAsync();
    }

    [Test]
    public async Task AnApplyWaitsForAnotherHolderOfTheLock()
    {
        await using NpgsqlConnection holder = new(database.ConnectionString);
        await holder.OpenAsync();
        await using (NpgsqlCommand hold = new("SELECT pg_advisory_lock(4711)", holder))
        {
            await hold.ExecuteScalarAsync();
        }

        (ServiceProvider services, IDatabase weasel) = await database.WeaselAsync("weasel-pg-waiting", configure: options =>
        {
            options.LockId = 4711;
            options.LockTimeout = TimeSpan.FromMilliseconds(500);
        });

        await using (services)
        {
            Func<Task> apply = () => weasel.ApplyAllConfiguredChangesToDatabaseAsync();
            await apply.Should().ThrowAsync<InvalidOperationException>().WithMessage("*global lock*",
                "the lock is held elsewhere for longer than LockTimeout, and FailFast is the default");

            await using (NpgsqlCommand release = new("SELECT pg_advisory_unlock(4711)", holder))
            {
                await release.ExecuteScalarAsync();
            }

            (await weasel.ApplyAllConfiguredChangesToDatabaseAsync()).Should().Be(SchemaPatchDifference.Create,
                "with the lock free the same apply goes ahead");
        }
    }

    private static async Task ShouldMatchAsync(PostgresWeaselDatabase actual, PostgresWeaselDatabase expected, string because)
    {
        (SchemaSnapshot actualSchema, List<string> actualConstraints) = await actual.ReadAsync();
        (SchemaSnapshot expectedSchema, List<string> expectedConstraints) = await expected.ReadAsync();

        actualSchema.Tables.Should().BeEquivalentTo(expectedSchema.Tables, because);
        actualSchema.Columns.Should().BeEquivalentTo(expectedSchema.Columns, because);
        actualSchema.Indexes.Should().BeEquivalentTo(expectedSchema.Indexes, because);
        actualConstraints.Should().BeEquivalentTo(expectedConstraints, because);
    }

    private static Task SeedAsync(PostgresWeaselDatabase database) => database.ExecuteAsync("""
        INSERT INTO qrtz_job_details (sched_name, job_name, job_group, job_class_name, is_durable, is_nonconcurrent, is_update_data, requests_recovery)
          VALUES ('weasel', 'job', 'group', 'Weasel.Job', true, false, false, false);
        INSERT INTO qrtz_triggers (sched_name, trigger_name, trigger_group, job_name, job_group, trigger_state, trigger_type, start_time)
          VALUES ('weasel', 'trigger', 'group', 'job', 'group', 'WAITING', 'SIMPLE', 1);
        INSERT INTO qrtz_simple_triggers (sched_name, trigger_name, trigger_group, repeat_count, repeat_interval, times_triggered)
          VALUES ('weasel', 'trigger', 'group', 3, 1000, 0);
        """);

    private static async Task ShouldHoldTheSeedAsync(PostgresWeaselDatabase database)
    {
        (await database.ScalarAsync("SELECT count(*) FROM qrtz_job_details")).Should().Be(1L);
        (await database.ScalarAsync("SELECT count(*) FROM qrtz_triggers")).Should().Be(1L);
        (await database.ScalarAsync("SELECT repeat_count FROM qrtz_simple_triggers")).Should().Be(3L,
            "migrating the schema moves no Quartz data");
    }

    private static string Describe(SchemaMigration migration) => string.Join("; ", migration.Deltas
        .Where(x => x.Difference != SchemaPatchDifference.None)
        .Select(x => $"{x.SchemaObject.Identifier} {x.Difference}"));
}
