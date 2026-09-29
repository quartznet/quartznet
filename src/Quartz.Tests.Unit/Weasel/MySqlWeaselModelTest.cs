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

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using MySqlConnector;

using Quartz.Impl.AdoJobStore;
using Quartz.Weasel.MySQL;

using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.MySql;
using Weasel.MySql.Tables;

using CascadeAction = Weasel.Core.CascadeAction;

namespace Quartz.Tests.Unit.Weasel;

/// <summary>
/// The MySQL model as far as it can be checked without a server: that it names every object the way
/// MySQL names what <c>create_mysql_innodb.sql</c> creates, in the database the store's SQL uses. The
/// integration tests read it back from a real catalog.
/// </summary>
public sealed class MySqlWeaselModelTest
{
    private const string ConnectionString = "Server=db.internal;Port=3307;Database=schedules;User Id=quartz;Password=unused";

    /// <summary>A server nothing answers for, refused at once rather than timed out.</summary>
    private const string Unreachable = "Server=127.0.0.1;Port=1;Database=nowhere;User Id=quartz;Password=unused;Connection Timeout=1;Pooling=false";

    [Test]
    public async Task EveryObjectIsNamedTheWayMySqlNamesIt()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-my-names");
        await using (services)
        {
            List<ISchemaObject> objects = database.BuildFeatureSchemas().Single().Objects.ToList();
            List<Table> tables = objects.OfType<Table>().ToList();

            tables.Select(x => x.Identifier.Name).Should().BeEquivalentTo(
                AdoConstants.AllTableNames.Concat(AdoConstants.OptionalTableNames.Select(x => x.Table)).Select(x => "QRTZ_" + x),
                "the model is every table the store reads, the optional history tables included");
            tables.Should().OnlyContain(x => x.Identifier.Schema == "schedules",
                "a prefix with no database puts the tables in the connection's, where the store's unqualified SQL finds them");
            tables.Should().OnlyContain(x => x.AddOnlyMigrations, "an application's own columns and indexes are never dropped");
            tables.SelectMany(x => x.Columns).Should().OnlyContain(x => x.Name == x.Name.ToUpperInvariant(),
                "the script's names are upper case, and MySQL reads them back as written");

            Dictionary<string, CascadeAction> foreignKeys = tables.SelectMany(x => x.ForeignKeys).ToDictionary(x => x.Name, x => x.DeleteAction);
            foreignKeys.Should().BeEquivalentTo(new Dictionary<string, CascadeAction>
            {
                ["QRTZ_TRIGGERS_ibfk_1"] = CascadeAction.NoAction,
                ["QRTZ_SIMPLE_TRIGGERS_ibfk_1"] = CascadeAction.NoAction,
                ["QRTZ_CRON_TRIGGERS_ibfk_1"] = CascadeAction.NoAction,
                ["QRTZ_SIMPROP_TRIGGERS_ibfk_1"] = CascadeAction.NoAction,
                ["QRTZ_BLOB_TRIGGERS_ibfk_1"] = CascadeAction.NoAction,
            }, "InnoDB names the script's unnamed foreign keys, the script cascades nothing, and Weasel compares a key by its name");
            tables.SelectMany(x => x.ForeignKeys).Should().OnlyContain(x => x.LinkedTable!.Schema == "schedules");

            IndexDefinition acquisition = tables.SelectMany(x => x.Indexes).Single(x => x.Name == "IDX_QRTZ_T_NFT_ST");
            acquisition.Columns.Should().Equal("SCHED_NAME", "TRIGGER_STATE", "NEXT_FIRE_TIME", "PRIORITY", "MISFIRE_INSTR");
            acquisition.DescendingColumns.Should().BeEquivalentTo(new[] { "PRIORITY" });

            tables.Single(x => x.Identifier.Name == "QRTZ_JOB_DETAILS").ColumnFor("IS_DURABLE")!.Type
                .Should().Be("TINYINT(1)", "MySQL stores BOOLEAN as TINYINT(1), and the model says what the catalog reads back");
            tables.Single(x => x.Identifier.Name == "QRTZ_SIMPROP_TRIGGERS").ColumnFor("DEC_PROP_1")!.Type.Should().Be("DECIMAL(13,4)");

            List<string> retired = objects.OfType<RetiredMySqlIndex>().Select(x => x.Identifier.Name).ToList();
            retired.Should().BeEquivalentTo(
                global::Quartz.Weasel.SqlServer.QuartzTables.Build(new global::Quartz.Weasel.SqlServer.QuartzTableNaming("dbo", "QRTZ_"))
                    .OfType<global::Quartz.Weasel.SqlServer.RetiredSqlServerIndex>().Select(x => x.Identifier.Name),
                "every dialect's model drops the same retired names, generated from the one list");
            retired.Should().Contain(["IDX_QRTZ_T_G_J", "IDX_QRTZ_T_NFT_ST_MISFIRE", "IDX_QRTZ_T_NFT_ST_MISFIRE_GRP"]);

            IFeatureSchema feature = database.BuildFeatureSchemas().Single();
            feature.Identifier.Should().Be("quartz");

            StringWriter creation = new();
            feature.WriteFeatureCreation(feature.Migrator, creation);
            creation.ToString().Should().Contain("CREATE TABLE IF NOT EXISTS `schedules`.`QRTZ_JOB_DETAILS`")
                .And.Contain("`PRIORITY` DESC", "the acquisition index keeps its mixed direction")
                .And.Contain("ADD CONSTRAINT `QRTZ_TRIGGERS_ibfk_1`")
                .And.NotContain("IDX_QRTZ_T_G_J", "a retired index is never created");
        }
    }

    [Test]
    public async Task ADatabaseInTheTablePrefixIsWhereTheTablesAre()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-my-prefixed", "`reporting`.QRTZM_");
        await using (services)
        {
            List<ISchemaObject> objects = database.BuildFeatureSchemas().Single().Objects.ToList();
            List<Table> tables = objects.OfType<Table>().ToList();

            objects.Should().OnlyContain(x => x.Identifier.Schema == "reporting", "the backticks delimit the database; they are not part of its name");
            tables.Should().OnlyContain(x => x.Identifier.Name.StartsWith("QRTZM_", StringComparison.Ordinal));
            tables.SelectMany(x => x.ForeignKeys).Select(x => x.Name).Should().Contain("QRTZM_TRIGGERS_ibfk_1",
                "InnoDB builds the name from the table's own name");
            tables.SelectMany(x => x.Indexes).Select(x => x.Name).Should().Contain("IDX_QRTZM_T_NFT_ST");

            database.Describe().SchemaOrNamespace.Should().Be("reporting");
            database.Describe().DatabaseName.Should().Be("schedules", "the connection still opens in its own database");
        }
    }

    [Test]
    public async Task AConnectionThatNamesNoDatabaseNeedsAPrefixThatDoes()
    {
        const string noDatabase = "Server=db.internal;User Id=quartz;Password=unused";

        Func<Task> build = () => BuildAsync("weasel-my-no-database", connectionString: noDatabase);
        await build.Should().ThrowAsync<SchedulerConfigException>()
            .WithMessage("*'weasel-my-no-database'*names none*Database=<name>*'<database>.QRTZ_'*");

        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-my-prefix-database", "jobs.QRTZ_", noDatabase);
        await using (services)
        {
            database.BuildFeatureSchemas().Single().Objects.Should().OnlyContain(x => x.Identifier.Schema == "jobs");
        }
    }

    [Test]
    public async Task TheDatabaseDescribesTheStoresOwnConnection()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-my-described", connectionString: "Server=db1.internal,db2.internal;Port=3307;Database=schedules;User Id=quartz;Password=unused");
        await using (services)
        {
            DatabaseDescriptor descriptor = database.Describe();

            descriptor.Engine.Should().Be(MySqlProvider.EngineName);
            descriptor.ServerName.Should().Be("db1.internal", "the first of the hosts MySqlConnector fails over between");
            descriptor.Port.Should().Be(3307);
            descriptor.DatabaseName.Should().Be("schedules");
            descriptor.SchemaOrNamespace.Should().Be("schedules");
            descriptor.Identifier.Should().Be("weasel-my-described");
            descriptor.SubjectUri.Should().Be(new Uri("quartz://scheduler/weasel-my-described"));

            database.Migrator.Should().BeOfType<MySqlMigrator>();
            database.Migrator.RefuseDestructiveChanges.Should().BeTrue();
            await database.ReleaseConnectionPoolAsync();
        }
    }

    [Test]
    public async Task AStoreOnAnotherDatabaseIsRefused()
    {
        using SqliteTestDatabase sqlite = new("weasel-mysql-on-sqlite");

        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "weasel-mysql-on-sqlite");
            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, sqlite.ConnectionString);
                store.UseWeaselForMySql();
            });
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        Func<Task> build = async () => await provider.GetRequiredService<IDatabaseSource>().BuildDatabases();
        await build.Should().ThrowAsync<SchedulerConfigException>()
            .WithMessage("*UseWeaselForMySql() manages a MySQL schema*SqliteConnection*UseMySqlConnector*");
    }

    /// <summary>
    /// A retired index is dropped when the catalog still has it on its Quartz table, and otherwise left
    /// out of the migration altogether; it is never created.
    /// </summary>
    [Test]
    public async Task ARetiredIndexIsDroppedOnlyWhenItIsStillThere()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-my-retired", "jobs.QRTZ_");
        await using (services)
        {
            ISchemaObject retired = database.BuildFeatureSchemas().Single().Objects
                .OfType<RetiredMySqlIndex>()
                .Single(x => x.Identifier.Name == "IDX_QRTZ_T_G_J");

            retired.Identifier.Schema.Should().Be("jobs");

            await using MySqlConnection unopened = new(ConnectionString);
            global::Weasel.Core.DbCommandBuilder builder = new(unopened);
            retired.ConfigureQueryCommand(builder);
            DbCommand command = builder.Compile();
            command.CommandText.Should().Contain("information_schema.STATISTICS").And.Contain("TABLE_NAME = @")
                .And.EndWith(";", "every introspection query in a batch ends its statement");
            command.Parameters.Cast<DbParameter>().Select(x => x.Value).Should().Equal("jobs", "IDX_QRTZ_T_G_J", "QRTZ_TRIGGERS");

            (await retired.CreateDeltaAsync(Reader(typeof(long), 1L))).Difference.Should().Be(SchemaPatchDifference.Update);
            (await retired.CreateDeltaAsync(Reader(typeof(long), 0L))).Difference.Should().Be(SchemaPatchDifference.None);
            (await retired.CreateDeltaAsync(Reader(typeof(long)))).Difference.Should().Be(SchemaPatchDifference.None);

            StringWriter drop = new();
            retired.WriteDropStatement(new MySqlMigrator(), drop);
            drop.ToString().Should().Contain("DROP INDEX `IDX_QRTZ_T_G_J` ON `jobs`.`QRTZ_TRIGGERS`;");

            StringWriter create = new();
            retired.WriteCreateStatement(new MySqlMigrator(), create);
            create.ToString().Should().BeEmpty("a retired index is never created");
        }
    }

    [Test]
    public void ALockTimeoutThatIsNotPositiveIsRefused()
    {
        ServiceCollection services = new();

        Action register = () => services.AddQuartz(q => q.UsePersistentStore(
            store => store.UseWeaselForMySql(weasel => weasel.LockTimeout = TimeSpan.Zero)));

        register.Should().Throw<SchedulerConfigException>().WithMessage("*LockTimeout*positive*");
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase(65)]
    public void ALockNameGetLockRejectsIsRefused(object? lockName)
    {
        ServiceCollection services = new();
        string? name = lockName is int length ? new string('q', length) : (string?) lockName;

        Action register = () => services.AddQuartz(q => q.UsePersistentStore(
            store => store.UseWeaselForMySql(weasel => weasel.LockName = name!)));

        register.Should().Throw<SchedulerConfigException>().WithMessage("*LockName*1 to 64 characters*");
    }

    [Test]
    public void TheDefaultLockIsQuartzOwn()
    {
        MySqlWeaselOptions options = new();

        options.LockName.Should().Be(MySqlWeaselOptions.DefaultLockName).And.Be("quartz:migrate");
        options.LockTimeout.Should().Be(TimeSpan.FromMinutes(1));
        options.AutoCreate.Should().BeNull("unset follows the JasperFx profile");
    }

    /// <summary>
    /// <c>GET_LOCK</c> waits inside a command and takes whole seconds, and a command the client times out
    /// first ends in an exception instead of the refusal the wait was for.
    /// </summary>
    [Test]
    public void TheLockWaitsInSlicesTheCommandTimeoutLeavesRoomFor()
    {
        MySqlUserLock.SliceSeconds(TimeSpan.FromMinutes(1), 30).Should().Be(15, "half of MySqlConnector's default 30-second command timeout");
        MySqlUserLock.SliceSeconds(TimeSpan.FromMilliseconds(200), 30).Should().Be(1, "what is left of the wait, rounded up to the seconds the server takes");
        MySqlUserLock.SliceSeconds(TimeSpan.FromMinutes(1), 0).Should().Be(60, "a command timeout of zero is none");
        MySqlUserLock.SliceSeconds(TimeSpan.FromMinutes(1), 1).Should().Be(1, "a one-second command timeout still leaves a one-second wait");
        MySqlUserLock.SliceSeconds(TimeSpan.FromSeconds(-1), 30).Should().Be(0, "a spent wait asks once more without waiting");
        MySqlUserLock.SliceSeconds(TimeSpan.MaxValue, 0).Should().Be(int.MaxValue);
    }

    [Test]
    public async Task AnApplyThatCannotReachTheServerFailsWithoutALockToLeave()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-my-unreachable", connectionString: Unreachable);
        await using (services)
        {
            Func<Task> apply = () => database.ApplyAllConfiguredChangesToDatabaseAsync();
            await apply.Should().ThrowAsync<MySqlException>("nothing listens on port 1, and the lock it would have taken is disposed with the failure");
        }
    }

    [Test]
    public async Task ALockWhoseConnectionCannotOpenFailsAndHoldsNothing()
    {
        MySqlUserLock userLock = new(
            () => new MySqlConnection(Unreachable),
            MySqlWeaselOptions.DefaultLockName,
            TimeSpan.FromSeconds(1),
            TimeProvider.System);

        await using (userLock)
        {
            Func<Task> attain = () => userLock.TryAttainLock(null!);
            await attain.Should().ThrowAsync<MySqlException>("nothing listens on port 1");

            Func<Task> release = () => userLock.ReleaseLock(null!);
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
                store.UseMySqlConnector(MySqlConnectorFactory.Instance, connectionString);
                store.ConfigureStore(options => options.TablePrefix = tablePrefix);
                store.UseWeaselForMySql();
            });
        });

        ServiceProvider provider = services.BuildServiceProvider();

        try
        {
            IReadOnlyList<IDatabase> databases = await provider.GetRequiredService<IDatabaseSource>().BuildDatabases();
            return (provider, databases.Single());
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }
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
