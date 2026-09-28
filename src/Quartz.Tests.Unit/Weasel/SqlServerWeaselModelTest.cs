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

using System.Data;
using System.Data.Common;

using JasperFx.Descriptors;

using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Weasel.SqlServer;

using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.SqlServer;
using Weasel.SqlServer.Tables;

using CascadeAction = Weasel.Core.CascadeAction;

namespace Quartz.Tests.Unit.Weasel;

/// <summary>
/// The SQL Server model as far as it can be checked without a server: that it names every object the
/// way <c>create_sqlServer.sql</c> does. The integration tests read it back from a real catalog.
/// </summary>
public sealed class SqlServerWeaselModelTest
{
    private const string ConnectionString = "Server=tcp:db.internal,1433;Database=schedules;User Id=quartz;Password=unused;TrustServerCertificate=true";

    /// <summary>A server nothing answers for, refused at once rather than timed out.</summary>
    private const string Unreachable = "Server=tcp:127.0.0.1,1;Database=nowhere;User Id=quartz;Password=unused;Connect Timeout=1;Pooling=false;TrustServerCertificate=true";

    [Test]
    public async Task EveryObjectIsNamedTheWayTheScriptNamesIt()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-ss-names");
        await using (services)
        {
            List<ISchemaObject> objects = database.BuildFeatureSchemas().Single().Objects.ToList();
            List<Table> tables = objects.OfType<Table>().ToList();

            tables.Should().HaveCount(14);
            tables.Should().OnlyContain(x => x.Identifier.Schema == "dbo" && x.Identifier.Name.StartsWith("QRTZ_", StringComparison.Ordinal),
                "a prefix with no schema puts the tables where the store's unqualified SQL finds them for a default login");
            tables.Should().OnlyContain(x => x.AddOnlyMigrations, "an application's own columns and indexes are never dropped");
            tables.Should().OnlyContain(x => !x.DetectColumnDrift,
                "no Quartz migration changes an existing column, so drift detection could only undo an application's own change");
            tables.Should().OnlyContain(x => x.PrimaryKeyName == "PK_" + x.Identifier.Name);
            tables.SelectMany(x => x.Columns).Should().OnlyContain(x => x.Name == x.Name.ToUpperInvariant(),
                "the script's names are upper case, and SQL Server reads them back as written");

            Dictionary<string, CascadeAction> foreignKeys = tables.SelectMany(x => x.ForeignKeys).ToDictionary(x => x.Name, x => x.DeleteAction);
            foreignKeys.Should().BeEquivalentTo(new Dictionary<string, CascadeAction>
            {
                ["FK_QRTZ_TRIGGERS_QRTZ_JOB_DETAILS"] = CascadeAction.NoAction,
                ["FK_QRTZ_SIMPLE_TRIGGERS_QRTZ_TRIGGERS"] = CascadeAction.Cascade,
                ["FK_QRTZ_CRON_TRIGGERS_QRTZ_TRIGGERS"] = CascadeAction.Cascade,
                ["FK_QRTZ_SIMPROP_TRIGGERS_QRTZ_TRIGGERS"] = CascadeAction.Cascade,
            }, "Weasel compares a foreign key by name, and tables_sqlServer.sql creates none on QRTZ_BLOB_TRIGGERS");

            IndexDefinition acquisition = tables.SelectMany(x => x.Indexes).Single(x => x.Name == "IDX_QRTZ_T_NFT_ST");
            acquisition.Columns.Should().Equal("SCHED_NAME", "TRIGGER_STATE", "NEXT_FIRE_TIME", "PRIORITY", "MISFIRE_INSTR");
            acquisition.DescendingColumns.Should().BeEquivalentTo(new[] { "PRIORITY" });
            tables.SelectMany(x => x.Indexes).Should().OnlyContain(x => x.CompareColumnDirection,
                "an index that does not compare its direction renders without it, and reads PRIORITY DESC as changed on every apply");

            objects[0].Should().BeOfType<MemoryOptimizedTableGuard>("the check is read before any table is compared");

            List<string> retired = objects.OfType<RetiredSqlServerIndex>().Select(x => x.Identifier.Name).ToList();
            retired.Should().HaveCount(23, "every index name 4.x retired, each dropped if still there")
                .And.Contain(new[] { "IDX_QRTZ_T_G_J", "IDX_QRTZ_T_NFT_ST_MISFIRE", "IDX_QRTZ_T_NFT_ST_MISFIRE_GRP" });

            IFeatureSchema feature = database.BuildFeatureSchemas().Single();
            feature.Identifier.Should().Be("quartz");

            StringWriter creation = new();
            feature.WriteFeatureCreation(feature.Migrator, creation);
            creation.ToString().Should().Contain("PRIORITY DESC", "the acquisition index keeps its mixed direction")
                .And.Contain("CONSTRAINT PK_QRTZ_TRIGGERS PRIMARY KEY")
                .And.NotContain("IDX_QRTZ_T_G_J", "a retired index is never created")
                .And.NotContain("MEMORY_OPTIMIZED_CHECK", "the check creates nothing");
        }
    }

    [Test]
    public async Task ASchemaInTheTablePrefixIsWhereTheTablesAre()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-ss-prefixed", "[scheduling].QRTZM_");
        await using (services)
        {
            List<ISchemaObject> objects = database.BuildFeatureSchemas().Single().Objects.ToList();
            List<Table> tables = objects.OfType<Table>().ToList();

            objects.Should().OnlyContain(x => x.Identifier.Schema == "scheduling", "the brackets delimit the schema; they are not part of its name");
            tables.Should().OnlyContain(x => x.Identifier.Name.StartsWith("QRTZM_", StringComparison.Ordinal));
            tables.SelectMany(x => x.ForeignKeys).Select(x => x.Name).Should().Contain("FK_QRTZM_TRIGGERS_QRTZM_JOB_DETAILS",
                "the script writes the prefix without its schema into both halves of the name");
            tables.SelectMany(x => x.Indexes).Select(x => x.Name).Should().Contain("IDX_QRTZM_T_NFT_ST");
            tables.Single(x => x.Identifier.Name == "QRTZM_LOCKS").PrimaryKeyName.Should().Be("PK_QRTZM_LOCKS");

            database.Describe().SchemaOrNamespace.Should().Be("scheduling");
        }
    }

    [Test]
    public async Task TheDatabaseDescribesTheStoresOwnConnection()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-ss-described");
        await using (services)
        {
            DatabaseDescriptor descriptor = database.Describe();

            descriptor.Engine.Should().Be(SqlServerProvider.EngineName);
            descriptor.ServerName.Should().Be("tcp:db.internal,1433");
            descriptor.DatabaseName.Should().Be("schedules");
            descriptor.SchemaOrNamespace.Should().Be("dbo");
            descriptor.Identifier.Should().Be("weasel-ss-described");
            descriptor.SubjectUri.Should().Be(new Uri("quartz://scheduler/weasel-ss-described"));

            database.Migrator.Should().BeOfType<SqlServerMigrator>();
            database.Migrator.RefuseDestructiveChanges.Should().BeTrue();
            await database.ReleaseConnectionPoolAsync();
        }
    }

    [Test]
    public async Task AStoreOnAnotherDatabaseIsRefused()
    {
        using SqliteTestDatabase sqlite = new("weasel-sqlserver-on-sqlite");

        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "weasel-sqlserver-on-sqlite");
            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, sqlite.ConnectionString);
                store.UseWeaselForSqlServer();
            });
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        Func<Task> build = async () => await provider.GetRequiredService<IDatabaseSource>().BuildDatabases();
        await build.Should().ThrowAsync<SchedulerConfigException>()
            .WithMessage("*UseWeaselForSqlServer() manages a SQL Server schema*SqliteConnection*UseSqlServer*");
    }

    /// <summary>
    /// A retired index is dropped when the catalog still has it on its Quartz table, and otherwise left
    /// out of the migration altogether; it is never created.
    /// </summary>
    [Test]
    public async Task ARetiredIndexIsDroppedOnlyWhenItIsStillThere()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-ss-retired", "jobs.QRTZ_");
        await using (services)
        {
            ISchemaObject retired = database.BuildFeatureSchemas().Single().Objects
                .OfType<RetiredSqlServerIndex>()
                .Single(x => x.Identifier.Name == "IDX_QRTZ_T_G_J");

            retired.Identifier.Schema.Should().Be("jobs");

            await using SqlConnection unopened = new(ConnectionString);
            global::Weasel.Core.DbCommandBuilder builder = new(unopened);
            retired.ConfigureQueryCommand(builder);
            builder.Compile().CommandText.Should().Contain("sys.indexes").And.Contain("t.name = @")
                .And.EndWith(";", "every introspection query in a batch ends its statement");

            (await retired.CreateDeltaAsync(Reader(typeof(int), 1))).Difference.Should().Be(SchemaPatchDifference.Update);
            (await retired.CreateDeltaAsync(Reader(typeof(int), 0))).Difference.Should().Be(SchemaPatchDifference.None);

            StringWriter drop = new();
            retired.WriteDropStatement(new SqlServerMigrator(), drop);
            drop.ToString().Should().Contain("drop index if exists IDX_QRTZ_T_G_J on jobs.QRTZ_TRIGGERS;");

            StringWriter create = new();
            retired.WriteCreateStatement(new SqlServerMigrator(), create);
            create.ToString().Should().BeEmpty("a retired index is never created");
        }
    }

    [Test]
    public async Task AMemoryOptimizedSchemaIsRefusedBeforeATableIsCompared()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-ss-mot");
        await using (services)
        {
            ISchemaObject guard = database.BuildFeatureSchemas().Single().Objects[0];

            await using SqlConnection unopened = new(ConnectionString);
            global::Weasel.Core.DbCommandBuilder builder = new(unopened);
            guard.ConfigureQueryCommand(builder);
            DbCommand command = builder.Compile();
            command.CommandText.Should().Contain("is_memory_optimized = 1").And.EndWith(";");
            command.Parameters.Cast<DbParameter>().Select(x => x.Value).Should().Contain(new object[] { "dbo", "QRTZ_TRIGGERS", "QRTZ_LOCKS" })
                .And.HaveCount(15, "the schema and the fourteen tables");

            Func<Task> memoryOptimized = () => guard.CreateDeltaAsync(Reader(typeof(string), "QRTZ_LOCKS", "QRTZ_TRIGGERS"));
            await memoryOptimized.Should().ThrowAsync<SchedulerException>()
                .WithMessage("*'weasel-ss-mot'*memory-optimized tables (QRTZ_LOCKS, QRTZ_TRIGGERS in schema dbo)*Nothing was changed*tables_sqlServerMOT.sql*");

            (await guard.CreateDeltaAsync(Reader(typeof(string)))).Difference.Should().Be(SchemaPatchDifference.None);

            StringWriter ddl = new();
            guard.WriteCreateStatement(new SqlServerMigrator(), ddl);
            guard.WriteDropStatement(new SqlServerMigrator(), ddl);
            ddl.ToString().Should().BeEmpty("a check creates and drops nothing");
        }
    }

    [Test]
    public void ALockTimeoutThatIsNotPositiveIsRefused()
    {
        ServiceCollection services = new();

        Action register = () => services.AddQuartz(q => q.UsePersistentStore(
            store => store.UseWeaselForSqlServer(weasel => weasel.LockTimeout = TimeSpan.Zero)));

        register.Should().Throw<SchedulerConfigException>().WithMessage("*LockTimeout*positive*");
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase(256)]
    public void ALockResourceSpGetapplockRejectsIsRefused(object? resource)
    {
        ServiceCollection services = new();
        string? name = resource is int length ? new string('q', length) : (string?) resource;

        Action register = () => services.AddQuartz(q => q.UsePersistentStore(
            store => store.UseWeaselForSqlServer(weasel => weasel.LockResource = name!)));

        register.Should().Throw<SchedulerConfigException>().WithMessage("*LockResource*1 to 255 characters*");
    }

    [Test]
    public void TheDefaultLockResourceIsQuartzOwn()
    {
        SqlServerWeaselOptions options = new();

        options.LockResource.Should().Be(SqlServerWeaselOptions.DefaultLockResource).And.Be("quartz:migrate");
        options.LockResource.Should().NotBe("4006", "Wolverine's migration lock on SQL Server")
            .And.NotStartWith("polecat:", "Polecat's");
        options.LockTimeout.Should().Be(TimeSpan.FromMinutes(1));
        options.AutoCreate.Should().BeNull("unset follows the JasperFx profile");
    }

    /// <summary>
    /// <c>sp_getapplock</c> waits inside a command, and a command the client times out first ends in an
    /// exception instead of the refusal the wait was for.
    /// </summary>
    [Test]
    public void TheLockWaitsInSlicesTheCommandTimeoutLeavesRoomFor()
    {
        SqlServerApplicationLock.SliceMilliseconds(TimeSpan.FromMinutes(1), 30).Should().Be(15_000,
            "half of SqlClient's default 30-second command timeout");
        SqlServerApplicationLock.SliceMilliseconds(TimeSpan.FromMilliseconds(200), 30).Should().Be(200, "what is left of the wait, when that is less");
        SqlServerApplicationLock.SliceMilliseconds(TimeSpan.FromMinutes(1), 0).Should().Be(60_000, "a command timeout of zero is none");
        SqlServerApplicationLock.SliceMilliseconds(TimeSpan.FromSeconds(-1), 30).Should().Be(0, "a spent wait asks once more without waiting");
        SqlServerApplicationLock.SliceMilliseconds(TimeSpan.MaxValue, 0).Should().Be(int.MaxValue);
    }

    [Test]
    public async Task AnApplyThatCannotReachTheServerFailsWithoutALockToLeave()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-ss-unreachable", connectionString: Unreachable);
        await using (services)
        {
            Func<Task> apply = () => database.ApplyAllConfiguredChangesToDatabaseAsync();
            await apply.Should().ThrowAsync<SqlException>("nothing listens on port 1, and the lock it would have taken is disposed with the failure");
        }
    }

    [Test]
    public async Task ALockWhoseConnectionCannotOpenFailsAndHoldsNothing()
    {
        SqlServerApplicationLock appLock = new(
            () => new SqlConnection(Unreachable),
            SqlServerWeaselOptions.DefaultLockResource,
            TimeSpan.FromSeconds(1),
            TimeProvider.System);

        await using (appLock)
        {
            Func<Task> attain = () => appLock.TryAttainLock(null!);
            await attain.Should().ThrowAsync<SqlException>("nothing listens on port 1");

            Func<Task> release = () => appLock.ReleaseLock(null!);
            await release.Should().NotThrowAsync("a lock that was never attained has nothing to release");
        }
    }

    private static async Task<(ServiceProvider Services, IDatabase Database)> BuildAsync(
        string schedulerName,
        string tablePrefix = "QRTZ_",
        string connectionString = ConnectionString)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = schedulerName);
            q.UsePersistentStore(store =>
            {
                store.UseSqlServer(SqlClientFactory.Instance, connectionString);
                store.ConfigureStore(options => options.TablePrefix = tablePrefix);
                store.UseWeaselForSqlServer();
            });
        });

        ServiceProvider provider = services.BuildServiceProvider();
        IReadOnlyList<IDatabase> databases = await provider.GetRequiredService<IDatabaseSource>().BuildDatabases();
        return (provider, databases.Single());
    }

    private static DbDataReader Reader(Type type, params object[] rows)
    {
        DataTable table = new();
        table.Columns.Add("value", type);
        foreach (object row in rows)
        {
            table.Rows.Add(row);
        }

        return table.CreateDataReader();
    }
}
