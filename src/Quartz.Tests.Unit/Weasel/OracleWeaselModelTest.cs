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

using Oracle.ManagedDataAccess.Client;

using Quartz.Impl.AdoJobStore;
using Quartz.Weasel.Oracle;

using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Oracle;
using Weasel.Oracle.Tables;

using CascadeAction = Weasel.Core.CascadeAction;

namespace Quartz.Tests.Unit.Weasel;

/// <summary>
/// The Oracle model as far as it can be checked without a server: that it names every object the way
/// <c>create_oracle.sql</c> does, in the schema the store's SQL uses. The integration tests read it back
/// from a real catalog.
/// </summary>
public sealed class OracleWeaselModelTest
{
    private const string ConnectionString = "User Id=quartz;Password=unused;Data Source=db.internal:1521/SCHEDULES";

    /// <summary>A server nothing answers for, refused at once rather than timed out.</summary>
    private const string Unreachable = "User Id=quartz;Password=unused;Data Source=127.0.0.1:1/NOWHERE;Connection Timeout=1;Pooling=false";

    [Test]
    public async Task EveryObjectIsNamedTheWayTheScriptNamesIt()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-ora-names", "quartz.qrtz_");
        await using (services)
        {
            List<ISchemaObject> objects = database.BuildFeatureSchemas().Single().Objects.ToList();
            List<Table> tables = objects.OfType<Table>().ToList();

            tables.Select(x => x.Identifier.Name).Should().BeEquivalentTo(
                AdoConstants.AllTableNames.Concat(AdoConstants.OptionalTableNames.Select(x => x.Table)).Select(x => "QRTZ_" + x),
                "the model is every table the store reads, and the store's unquoted names are folded to upper case");
            tables.Should().OnlyContain(x => x.Identifier.Schema == "QUARTZ", "the prefix's schema, folded as Oracle folds it");
            tables.Should().OnlyContain(x => x.AddOnlyMigrations, "an application's own columns and indexes are never dropped");

            tables.Single(x => x.Identifier.Name == "QRTZ_JOB_DETAILS").PrimaryKeyName.Should().Be("QRTZ_JOB_DETAILS_PK");
            tables.Single(x => x.Identifier.Name == "QRTZ_SIMPLE_TRIGGERS").PrimaryKeyName.Should().Be("QRTZ_SIMPLE_TRIG_PK",
                "tables_oracle.sql abbreviates, from the time Oracle's identifiers stopped at 30 characters");
            tables.Single(x => x.Identifier.Name == "QRTZ_FIRED_TRIGGERS").PrimaryKeyName.Should().Be("QRTZ_FIRED_TRIGGER_PK");
            tables.Single(x => x.Identifier.Name == "QRTZ_EXECUTION_HISTORY").PrimaryKeyName.Should().Be("QRTZ_EXEC_HISTORY_PK");

            Dictionary<string, CascadeAction> foreignKeys = tables.SelectMany(x => x.ForeignKeys).ToDictionary(x => x.Name, x => x.DeleteAction);
            foreignKeys.Should().BeEquivalentTo(new Dictionary<string, CascadeAction>
            {
                ["QRTZ_TRIGGER_TO_JOBS_FK"] = CascadeAction.NoAction,
                ["QRTZ_SIMPLE_TRIG_TO_TRIG_FK"] = CascadeAction.NoAction,
                ["QRTZ_CRON_TRIG_TO_TRIG_FK"] = CascadeAction.NoAction,
                ["QRTZ_SIMPROP_TRIG_TO_TRIG_FK"] = CascadeAction.NoAction,
                ["QRTZ_BLOB_TRIG_TO_TRIG_FK"] = CascadeAction.NoAction,
            }, "Weasel compares a foreign key by name, and the script names them and cascades nothing");

            IndexDefinition acquisition = tables.SelectMany(x => x.Indexes).Single(x => x.Name == "IDX_QRTZ_T_NFT_ST");
            acquisition.Columns.Should().Equal("SCHED_NAME", "TRIGGER_STATE", "NEXT_FIRE_TIME", "PRIORITY", "MISFIRE_INSTR");
            acquisition.DescendingColumns.Should().BeEquivalentTo(new[] { "PRIORITY" });
            acquisition.Tablespace.Should().BeNull("an index is compared without its tablespace unless the model names one");

            tables.Single(x => x.Identifier.Name == "QRTZ_SIMPROP_TRIGGERS").ColumnFor("DEC_PROP_1")!.Type
                .Should().Be("NUMBER(13,4)", "Oracle stores NUMERIC as NUMBER, and the model says what the catalog reads back");

            List<string> retired = objects.OfType<RetiredOracleIndex>().Select(x => x.Identifier.Name).ToList();
            retired.Should().BeEquivalentTo(
                global::Quartz.Weasel.SqlServer.QuartzTables.Build(new global::Quartz.Weasel.SqlServer.QuartzTableNaming("dbo", "QRTZ_"))
                    .OfType<global::Quartz.Weasel.SqlServer.RetiredSqlServerIndex>().Select(x => x.Identifier.Name),
                "every dialect's model drops the same retired names, generated from the one list");

            IFeatureSchema feature = database.BuildFeatureSchemas().Single();
            feature.Identifier.Should().Be("quartz");

            StringWriter creation = new();
            feature.WriteFeatureCreation(feature.Migrator, creation);
            creation.ToString().Should().Contain("CREATE TABLE QUARTZ.QRTZ_JOB_DETAILS")
                .And.Contain("CONSTRAINT QRTZ_JOB_DETAILS_PK PRIMARY KEY")
                .And.Contain("ADD CONSTRAINT QRTZ_TRIGGER_TO_JOBS_FK")
                .And.Contain("PRIORITY DESC", "the acquisition index keeps its mixed direction")
                .And.Contain("DEFAULT ''0''", "a string default is doubled inside the guarded CREATE TABLE, which JasperFx/weasel#643 fixed")
                .And.NotContain("IDX_QRTZ_T_G_J", "a retired index is never created");
        }
    }

    /// <summary>
    /// Without a schema in the table prefix the tables are in the session's current schema, which only
    /// the server knows — asked for the first time the model is needed, and not before.
    /// </summary>
    [Test]
    public async Task WithoutASchemaInThePrefixTheServerIsAskedForTheCurrentSchema()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-ora-current-schema", connectionString: Unreachable);
        await using (services)
        {
            database.Describe().SchemaOrNamespace.Should().Be("QUARTZ", "until the server has been asked, the login it would be");

            Action model = () => database.BuildFeatureSchemas();
            model.Should().Throw<OracleException>("the current schema is read from the server, and nothing listens on port 1");
        }
    }

    [Test]
    public async Task TheDatabaseDescribesTheStoresOwnConnection()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-ora-described", "jobs.QRTZ_");
        await using (services)
        {
            DatabaseDescriptor descriptor = database.Describe();

            descriptor.Engine.Should().Be(OracleProvider.EngineName);
            descriptor.ServerName.Should().Be("db.internal");
            descriptor.Port.Should().Be(1521);
            descriptor.DatabaseName.Should().Be("SCHEDULES");
            descriptor.SchemaOrNamespace.Should().Be("JOBS");
            descriptor.Identifier.Should().Be("weasel-ora-described");
            descriptor.SubjectUri.Should().Be(new Uri("quartz://scheduler/weasel-ora-described"));
            descriptor.DatabaseUri().Should().NotBeNull();

            database.Migrator.Should().BeOfType<QuartzOracleMigrator>();
            database.Migrator.RefuseDestructiveChanges.Should().BeTrue();
            await database.ReleaseConnectionPoolAsync();
        }
    }

    [TestCase("db.internal:1521/SCHEDULES", "db.internal", 1521, "SCHEDULES")]
    [TestCase("//db.internal/SCHEDULES", "db.internal", null, "SCHEDULES")]
    [TestCase("(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=db.internal)(PORT=1522))(CONNECT_DATA=(SERVICE_NAME=SCHEDULES)))", "db.internal", 1522, "SCHEDULES")]
    [TestCase("(DESCRIPTION = (ADDRESS = (HOST = db.internal)(PORT = 1521)) (CONNECT_DATA = (SID = XE)))", "db.internal", 1521, "XE")]
    [TestCase("SCHEDULES_TNS", "SCHEDULES_TNS", null, "SCHEDULES_TNS")]
    public void ADataSourceIsSplitIntoItsServerPortAndService(string dataSource, string server, int? port, string database)
    {
        OracleQuartzDatabase.SplitDataSource(dataSource).Should().Be((server, port, database),
            "the descriptor becomes a URI, and a whole connect descriptor is no host name");
    }

    [Test]
    public async Task AStoreOnAnotherDatabaseIsRefused()
    {
        using SqliteTestDatabase sqlite = new("weasel-oracle-on-sqlite");

        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "weasel-oracle-on-sqlite");
            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, sqlite.ConnectionString);
                store.UseWeaselForOracle();
            });
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        Func<Task> build = async () => await provider.GetRequiredService<IDatabaseSource>().BuildDatabases();
        await build.Should().ThrowAsync<SchedulerConfigException>()
            .WithMessage("*UseWeaselForOracle() manages a schema on Oracle*SqliteConnection*UseOracle*");
    }

    /// <summary>
    /// A retired index is dropped when the catalog still has it on its Quartz table, and otherwise left
    /// out of the migration altogether; it is never created.
    /// </summary>
    [Test]
    public async Task ARetiredIndexIsDroppedOnlyWhenItIsStillThere()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-ora-retired", "jobs.QRTZ_");
        await using (services)
        {
            ISchemaObject retired = database.BuildFeatureSchemas().Single().Objects
                .OfType<RetiredOracleIndex>()
                .Single(x => x.Identifier.Name == "IDX_QRTZ_T_G_J");

            retired.Identifier.Schema.Should().Be("JOBS");

            global::Weasel.Core.DbCommandBuilder builder = database.Migrator.CreateCommandBuilder(new OracleConnection(ConnectionString));
            retired.ConfigureQueryCommand(builder);
            DbCommand command = builder.Compile();
            command.CommandText.Should().Contain("all_indexes").And.Contain("table_name = :")
                .And.NotEndWith(";", "ODP.NET refuses a statement that ends in one");
            command.Parameters.Cast<DbParameter>().Select(x => x.Value).Should().Equal("JOBS", "IDX_QRTZ_T_G_J", "QRTZ_TRIGGERS");

            (await retired.CreateDeltaAsync(Reader(typeof(decimal), 1m))).Difference.Should().Be(SchemaPatchDifference.Update);
            (await retired.CreateDeltaAsync(Reader(typeof(decimal), 0m))).Difference.Should().Be(SchemaPatchDifference.None);

            StringWriter drop = new();
            retired.WriteDropStatement(database.Migrator, drop);
            drop.ToString().ReplaceLineEndings("\n").Should().Be("DROP INDEX JOBS.IDX_QRTZ_T_G_J\n/\n",
                "one statement, ended the way Weasel's Oracle scripts end theirs");

            StringWriter create = new();
            retired.WriteCreateStatement(database.Migrator, create);
            create.ToString().Should().BeEmpty("a retired index is never created");
        }
    }

    /// <summary>
    /// A comparison splits its queries into one command per statement, and Weasel 9.36.0's split commands
    /// read a <c>LONG</c> back empty — the expression behind a descending index key among them. The migrator
    /// the model is compared with sets the fetch size on every command it splits off.
    /// </summary>
    [Test]
    public void EveryCommandAComparisonSplitsOffReadsALongWhole()
    {
        static IReadOnlyList<DbCommand> Split(global::Weasel.Core.DbCommandBuilder builder)
        {
            builder.Append("SELECT column_expression FROM all_ind_expressions");
            builder.StartNewCommand();
            builder.Append("SELECT column_name FROM all_ind_columns");
            return builder.CompileCommands();
        }

        Split(new OracleMigrator().CreateCommandBuilder(new OracleConnection(ConnectionString))).Cast<OracleCommand>()
            .Select(x => x.InitialLONGFetchSize).Should().Equal([0, 0], "the premise: Weasel's own split commands fetch none of a LONG");

        Split(new QuartzOracleMigrator().CreateCommandBuilder(new OracleConnection(ConnectionString))).Cast<OracleCommand>()
            .Select(x => x.InitialLONGFetchSize).Should().Equal([-1, -1], "every command reads the whole of a LONG");
    }

    /// <summary>
    /// There is no lock, so a failed apply is read again in case another process was applying the same
    /// schema — and a server that cannot be reached is not one to wait for.
    /// </summary>
    [Test]
    public async Task AnApplyThatCannotReachTheServerFailsAfterReadingAgain()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-ora-unreachable", "jobs.QRTZ_", Unreachable);
        await using (services)
        {
            Func<Task> apply = () => database.ApplyAllConfiguredChangesToDatabaseAsync();
            await apply.Should().ThrowAsync<OracleException>("nothing listens on port 1, for the apply or for the reading again");
        }
    }

    [Test]
    public void TheDefaultsFollowTheProfile()
    {
        new OracleWeaselOptions().AutoCreate.Should().BeNull("unset follows the JasperFx profile");
        OracleQuartzDatabase.ApplyAttempts.Should().Be(10, "as many attempts as ProvisionSchema() gives a create that lost a race");
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
                store.UseOracle(OracleClientFactory.Instance, connectionString);
                store.ConfigureStore(options => options.TablePrefix = tablePrefix);
                store.UseWeaselForOracle();
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
