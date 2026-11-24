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

using AppAny.Quartz.EntityFrameworkCore.Migrations;
using AppAny.Quartz.EntityFrameworkCore.Migrations.SQLite;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Tests.Integration.Impl.AdoJobStore;
using Quartz.Tests.Unit.Weasel;

namespace Quartz.Tests.Unit.EntityFrameworkCore;

/// <summary>
/// Quartz.EntityFrameworkCore's schema on a SQLite file: migrated into an empty database, compared with a fresh
/// install, run by a scheduler, and an AppAny 0.6.3 schema moved onto it.
/// </summary>
/// <remarks>
/// SQLite is the one dialect that needs no server, so this is where the coverage gate sees the model, the
/// migration extensions and the move run for real. The server dialects run the same steps in
/// <c>Quartz.Tests.Integration</c>.
/// </remarks>
public sealed class SqliteMigrationTest
{
    /// <summary>
    /// An empty database migrated from the model, with <c>CompleteQuartzSchema</c> at the end of the migration, is
    /// the database <c>tables_sqlite.sql</c> creates: tables, columns with their types, nullability and defaults,
    /// keys, foreign keys with their delete rules, indexes with their directions, and the delete triggers.
    /// </summary>
    [Test]
    public async Task AMigratedDatabaseIsTheFreshInstall()
    {
        using SqliteTestDatabase migrated = new("efcore-migrated");
        using SqliteTestDatabase fresh = new("efcore-fresh");

        await using (EntityFrameworkCoreContext context = EntityFrameworkCoreContext.Quartz("sqlite", connectionString: migrated.ConnectionString))
        {
            await context.MigrateAsync();
        }

        await SqliteSchema.CreateWithFreshInstallScriptAsync(fresh.ConnectionString);

        (await SqliteSchema.ReadAsync(migrated.ConnectionString)).Should().BeEquivalentTo(await SqliteSchema.ReadAsync(fresh.ConnectionString));
    }

    /// <summary>
    /// A scheduler starts on the migrated tables under <c>SchemaProvisioning.Validate</c> and fires, stores and reads
    /// back one trigger of every family, beside a fresh install in the same file.
    /// </summary>
    [Test]
    public async Task ASchedulerRunsOnAMigratedDatabase()
    {
        using SqliteTestDatabase database = new("efcore-workload");
        await SqliteSchema.CreateWithFreshInstallScriptAsync(database.ConnectionString);

        await using (EntityFrameworkCoreContext context = EntityFrameworkCoreContext.Quartz("sqlite", t => t.TablePrefix = "QRTZE_", database.ConnectionString))
        {
            await context.MigrateAsync(t => t.TablePrefix = "QRTZE_");
        }

        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        await MigratedSchemaWorkload.RunAsync(connection, "sqlite", database.ConnectionString, "QRTZE_");
    }

    /// <summary>
    /// SQLite drops a table's triggers with the table, and EF Core changes a column by rebuilding the table, so a
    /// migration of the application's own that alters <c>QRTZ_TRIGGERS</c> takes the delete triggers with it.
    /// <c>CompleteQuartzSchema</c> at its end puts them back.
    /// </summary>
    [Test]
    public async Task CompleteQuartzSchemaPutsBackTheTriggersARebuildDrops()
    {
        using SqliteTestDatabase database = new("efcore-rebuild");

        await using EntityFrameworkCoreContext context = EntityFrameworkCoreContext.Quartz("sqlite", connectionString: database.ConnectionString);
        await context.MigrateAsync();

        (await Triggers(database)).Should().HaveCount(4, "the premise: the initial migration created them");

        await context.ExecuteAsync(context.Migration(m => m.AlterColumn<string>(
            "DESCRIPTION", "QRTZ_TRIGGERS", type: "NVARCHAR(500)", nullable: true, oldType: "NVARCHAR(250)", oldNullable: true)));

        (await Triggers(database)).Should().BeEmpty("all four fire on a delete from QRTZ_TRIGGERS, and the rebuild dropped that table");

        await context.ExecuteAsync(context.Migration(m => m.CompleteQuartzSchema()));

        (await Triggers(database)).Should().HaveCount(4);
    }

    /// <summary>
    /// A schema AppAny 0.6.3 created, moved by <c>MoveQuartzTablesFromAppAny</c>, has the history indexes and the
    /// delete triggers it lacked, and a scheduler runs on it.
    /// </summary>
    [Test]
    public async Task AnAppAnySchemaMovedRunsTheScheduler()
    {
        using SqliteTestDatabase database = new("efcore-appany");
        await SqliteSchema.CreateWithFreshInstallScriptAsync(database.ConnectionString);

        await using (EntityFrameworkCoreContext appAny = EntityFrameworkCoreContext.For(
                         "sqlite", (model, _) => model.AddQuartz(q => q.UseSqlite("QRTZA_")), database.ConnectionString))
        {
            await appAny.ExecuteAsync(appAny.DifferencesFrom(null));
        }

        (await Triggers(database)).Should().OnlyContain(t => !t.StartsWith("QRTZA_", StringComparison.Ordinal), "the premise: AppAny never created them");

        await using (EntityFrameworkCoreContext ours = EntityFrameworkCoreContext.Quartz("sqlite", t => t.TablePrefix = "QRTZA_", database.ConnectionString))
        {
            await ours.ExecuteAsync(ours.Migration(m => m.MoveQuartzTablesFromAppAny(t => t.TablePrefix = "QRTZA_")));
        }

        (await Triggers(database)).Where(t => t.StartsWith("QRTZA_", StringComparison.Ordinal)).Should().HaveCount(4);
        (await SqliteSchema.ScalarAsync(database.ConnectionString,
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name IN ('IDX_QRTZA_EH_JOB_TIME', 'IDX_QRTZA_EH_FIRE_INST', 'IDX_QRTZA_EH_TRIG_TIME')"))
            .Should().Be(3L);

        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();

        (await Columns(connection, "QRTZA_TRIGGER_STATUS")).Should().Equal(await Columns(connection, "QRTZ_TRIGGER_STATUS"),
            "AppAny 0.6.3 has no trigger rollup, and the move creates it as the fresh install does");

        await MigratedSchemaWorkload.RunAsync(connection, "sqlite", database.ConnectionString, "QRTZA_");

        // A store keeping its history in the database probes the optional tables, and refuses to start without one.
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "efcore-appany-history-" + Guid.NewGuid().ToString("N"));
            q.UsePersistentStore(store =>
            {
                store.ConfigureStore(options => options.TablePrefix = "QRTZA_");
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.UseExecutionHistory();
            });
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        Func<Task> start = async () => await (await provider.GetRequiredService<ISchedulerFactory>().GetScheduler()).Shutdown();
        await start.Should().NotThrowAsync("the moved schema has every table the 4.9 history needs");
    }

    /// <summary>A table's columns, each as SQLite declares it: name, type, NOT NULL, default and key position.</summary>
    private static Task<List<string>> Columns(SqliteConnection connection, string table) => SqliteSchema.RowsAsync(connection,
        $"SELECT name || ' ' || type || ' ' || \"notnull\" || ' ' || IFNULL(dflt_value, '') || ' ' || pk FROM pragma_table_info('{table}') ORDER BY cid");

    private static async Task<List<string>> Triggers(SqliteTestDatabase database)
    {
        await using SqliteConnection connection = new(database.ConnectionString);
        await connection.OpenAsync();
        return await SqliteSchema.RowsAsync(connection, "SELECT name FROM sqlite_master WHERE type = 'trigger'");
    }
}
