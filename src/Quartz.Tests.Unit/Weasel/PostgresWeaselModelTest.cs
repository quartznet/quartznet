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

using JasperFx.Descriptors;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using Quartz.Weasel.PostgreSQL;

using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Postgresql;
using Weasel.Postgresql.Tables;

namespace Quartz.Tests.Unit.Weasel;

/// <summary>
/// The PostgreSQL model as far as it can be checked without a server: that it is spelled the way
/// PostgreSQL names what <c>create_postgres.sql</c> creates. The integration tests read it back from a
/// real catalog.
/// </summary>
public sealed class PostgresWeaselModelTest
{
    /// <summary>
    /// The constraint names PostgreSQL 17 gave the script's unnamed keys, as the feasibility spike read
    /// them out of <c>pg_constraint</c>. The last one is the one that shows the truncation.
    /// </summary>
    private static readonly string[] ForeignKeysPostgreSqlChose =
    [
        "qrtz_triggers_sched_name_job_name_job_group_fkey",
        "qrtz_simple_triggers_sched_name_trigger_name_trigger_group_fkey",
        "qrtz_cron_triggers_sched_name_trigger_name_trigger_group_fkey",
        "qrtz_blob_triggers_sched_name_trigger_name_trigger_group_fkey",
        "qrtz_simprop_triggers_sched_name_trigger_name_trigger_grou_fkey",
    ];

    [Test]
    public void TheConstraintNamesArePostgreSqlsOwn()
    {
        List<Table> tables = new QuartzPostgresFeatureSchema().Objects.OfType<Table>().ToList();

        tables.Select(x => x.PrimaryKeyName).Should().OnlyContain(x => x.EndsWith("_pkey", StringComparison.Ordinal));
        tables.Single(x => x.Identifier.Name == "qrtz_paused_trigger_grps").PrimaryKeyName.Should().Be("qrtz_paused_trigger_grps_pkey");

        tables.SelectMany(x => x.ForeignKeys).Select(x => x.Name).Should().BeEquivalentTo(ForeignKeysPostgreSqlChose,
            "an unnamed constraint is named by PostgreSQL, and a model naming it anything else reads as a change on every apply");
        tables.SelectMany(x => x.ForeignKeys).Select(x => x.Name).Should().OnlyContain(x => x.Length <= 63);
    }

    [Test]
    public void TheDefaultNameIsCutTheWayPostgreSqlCutsIt()
    {
        QuartzTableNaming.DefaultConstraintName("qrtz_simprop_triggers", "sched_name_trigger_name_trigger_group", "fkey")
            .Should().Be("qrtz_simprop_triggers_sched_name_trigger_name_trigger_grou_fkey",
                "the longer part loses characters first, until the name fits in 63 bytes");

        QuartzTableNaming.DefaultConstraintName(new string('t', 70), null, "pkey")
            .Should().Be(new string('t', 58) + "_pkey");

        QuartzTableNaming.DefaultConstraintName(new string('t', 40), new string('c', 40), "fkey")
            .Should().Be(new string('t', 29) + "_" + new string('c', 28) + "_fkey",
                "with both parts too long they are shortened alternately, the table part first while it is the longer");
    }

    [Test]
    public void ASchemaInTheTablePrefixIsWhereTheTablesAre()
    {
        QuartzPostgresFeatureSchema feature = new("Quartz.QRTZM_");

        List<Table> tables = feature.Objects.OfType<Table>().ToList();
        tables.Should().HaveCount(14);
        tables.Should().OnlyContain(x => x.Identifier.Schema == "quartz" && x.Identifier.Name.StartsWith("qrtzm_", StringComparison.Ordinal),
            "PostgreSQL folds the unquoted names the store writes, so the model does too");
        tables.Should().OnlyContain(x => x.AddOnlyMigrations, "an application's own columns and indexes are never dropped");

        tables.SelectMany(x => x.Indexes).Select(x => x.Name).Should().Contain("idx_qrtzm_t_nft_st");
        feature.Objects.Where(x => x is not Table).Should().HaveCount(23, "the 23 index names 4.x retired, each dropped if still there")
            .And.OnlyContain(x => x.Identifier.Schema == "quartz");

        feature.Identifier.Should().Be("quartz");
        feature.StorageType.Should().Be<QuartzPostgresFeatureSchema>();
        feature.DependentTypes().Should().BeEmpty();
        feature.Migrator.RefuseDestructiveChanges.Should().BeTrue();

        StringWriter permissions = new();
        feature.WritePermissions(feature.Migrator, permissions);
        permissions.ToString().Should().BeEmpty();

        StringWriter creation = new();
        feature.WriteFeatureCreation(feature.Migrator, creation);
        creation.ToString().Should().Contain("priority DESC", "the acquisition index keeps its mixed direction")
            .And.NotContain("idx_qrtzm_t_next_fire_time", "a retired index is never created");
    }

    [Test]
    public async Task ForASchedulerReadsItsPrefixAndRefusesASecondOwner()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UsePostgres(NpgsqlFactory.Instance, "Host=localhost;Database=quartz");
            store.ConfigureStore(options => options.TablePrefix = "marten.QRTZ_");
        }));
        services.AddQuartz("weasel-standalone", q => q.UsePersistentStore(store =>
        {
            store.UsePostgres(NpgsqlFactory.Instance, "Host=localhost;Database=quartz");
            store.UseWeaselForPostgres();
        }));

        await using ServiceProvider provider = services.BuildServiceProvider();

        QuartzPostgresFeatureSchema.ForScheduler(provider).Objects.OfType<Table>()
            .Should().OnlyContain(x => x.Identifier.Schema == "marten");

        Action both = () => QuartzPostgresFeatureSchema.ForScheduler(provider, "weasel-standalone");
        both.Should().Throw<SchedulerConfigException>()
            .WithMessage("*scheduler 'weasel-standalone'*already managed by UseWeaselForPostgres()*two owners*");
    }

    [Test]
    public async Task TheDatabaseDescribesTheStoresOwnConnection()
    {
        ServiceCollection services = new();
        services.AddQuartz("weasel-described", q => q.UsePersistentStore(store =>
        {
            store.UsePostgres(NpgsqlFactory.Instance, "Host=db.internal,db2.internal:5433;Port=6432;Database=schedules");
            store.ConfigureStore(options => options.TablePrefix = "jobs.QRTZ_");
            store.UseWeaselForPostgres();
        }));

        await using ServiceProvider provider = services.BuildServiceProvider();

        IDatabase database = (await provider.GetRequiredService<IDatabaseSource>().BuildDatabases()).Single();
        DatabaseDescriptor descriptor = database.Describe();

        descriptor.ServerName.Should().Be("db.internal", "a multi-host string is described by its first host");
        descriptor.Port.Should().Be(6432);
        descriptor.DatabaseName.Should().Be("schedules");
        descriptor.SchemaOrNamespace.Should().Be("jobs");
        descriptor.Identifier.Should().Be("weasel-described");
        descriptor.SubjectUri.Should().Be(new Uri("quartz://scheduler/weasel-described"));

        database.Migrator.RefuseDestructiveChanges.Should().BeTrue();
        await database.ReleaseConnectionPoolAsync();
    }

    /// <summary>
    /// A registered data source keeps its connection string from Quartz's provider, and the connection it
    /// hands out still says where it goes.
    /// </summary>
    [Test]
    public async Task ADatabaseReachedThroughARegisteredDataSourceIsDescribedByItsConnection()
    {
        ServiceCollection services = new();
        services.AddNpgsqlDataSource("Host=registered.internal;Database=from_container");
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "weasel-data-source");
            q.UsePersistentStore(store =>
            {
                store.UsePostgres(options => options.UseRegisteredDataSource = true);
                store.UseWeaselForPostgres();
            });
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        DatabaseDescriptor descriptor = (await provider.GetRequiredService<IDatabaseSource>().BuildDatabases()).Single().Describe();

        descriptor.ServerName.Should().Be("registered.internal");
        descriptor.DatabaseName.Should().Be("from_container");
    }

    [Test]
    public async Task AStoreOnAnotherDatabaseIsRefused()
    {
        using SqliteTestDatabase sqlite = new("weasel-postgres-on-sqlite");

        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "weasel-postgres-on-sqlite");
            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, sqlite.ConnectionString);
                store.UseWeaselForPostgres();
            });
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        Func<Task> build = async () => await provider.GetRequiredService<IDatabaseSource>().BuildDatabases();
        await build.Should().ThrowAsync<SchedulerConfigException>()
            .WithMessage("*UseWeaselForPostgres() manages a PostgreSQL schema*SqliteConnection*UsePostgres*");
    }

    [Test]
    public async Task AStoreWithNoDatabaseIsRefused()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "weasel-no-database");
            q.UsePersistentStore(store =>
            {
                // Names a data source that nothing registers, which passes the store's options validation
                // and leaves no connection provider in the container.
                store.ConfigureStore(options => options.DataSource = "configured-elsewhere");
                store.UseWeaselForPostgres();
            });
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        Func<Task> build = async () => await provider.GetRequiredService<IDatabaseSource>().BuildDatabases();
        await build.Should().ThrowAsync<SchedulerConfigException>()
            .WithMessage("*UseWeaselForPostgres() was called for scheduler 'weasel-no-database'*UsePostgres(...)*");
    }

    /// <summary>
    /// A retired index is dropped when the catalog still has it on its Quartz table, and otherwise left
    /// out of the migration altogether; it is never created.
    /// </summary>
    [Test]
    public async Task ARetiredIndexIsDroppedOnlyWhenItIsStillThere()
    {
        ISchemaObject retired = new QuartzPostgresFeatureSchema("jobs.QRTZ_").Objects
            .Single(x => x is not Table && x.Identifier.Name == "idx_qrtz_t_next_fire_time");

        retired.Identifier.Schema.Should().Be("jobs");

        await using NpgsqlConnection unopened = new("Host=localhost");
        global::Weasel.Core.DbCommandBuilder builder = new(unopened);
        retired.ConfigureQueryCommand(builder);
        builder.Compile().CommandText.Should().Contain("pg_indexes").And.Contain("tablename")
            .And.EndWith(";", "every introspection query in a batch ends its statement");

        (await retired.CreateDeltaAsync(CountReader(1))).Difference.Should().Be(SchemaPatchDifference.Update);
        (await retired.CreateDeltaAsync(CountReader(0))).Difference.Should().Be(SchemaPatchDifference.None);

        StringWriter drop = new();
        retired.WriteDropStatement(new PostgresqlMigrator(), drop);
        drop.ToString().Should().Contain("drop index if exists jobs.idx_qrtz_t_next_fire_time;");

        StringWriter create = new();
        retired.WriteCreateStatement(new PostgresqlMigrator(), create);
        create.ToString().Should().BeEmpty("a retired index is never created");

        static System.Data.Common.DbDataReader CountReader(long count)
        {
            System.Data.DataTable table = new();
            table.Columns.Add("count", typeof(long));
            table.Rows.Add(count);
            return table.CreateDataReader();
        }
    }

    [Test]
    public void ALockTimeoutThatIsNotPositiveIsRefused()
    {
        ServiceCollection services = new();

        Action register = () => services.AddQuartz(q => q.UsePersistentStore(
            store => store.UseWeaselForPostgres(weasel => weasel.LockTimeout = TimeSpan.Zero)));

        register.Should().Throw<SchedulerConfigException>().WithMessage("*LockTimeout*positive*");
    }

    [Test]
    public void TheDefaultLockIdIsQuartzOwn()
    {
        PostgresWeaselOptions options = new();

        options.LockId.Should().Be(PostgresWeaselOptions.DefaultLockId);
        options.LockId.Should().NotBe(4004, "Marten's default").And.NotBe(4006, "Wolverine's default");
        BitConverter.GetBytes(options.LockId).Reverse().Select(x => (char) x).Should().Equal(['Q', 'R', 'T', 'Z']);
        options.LockTimeout.Should().Be(TimeSpan.FromMinutes(1));
        options.AutoCreate.Should().BeNull("unset follows the JasperFx profile");
    }
}
