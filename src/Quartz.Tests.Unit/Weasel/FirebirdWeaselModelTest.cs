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
using System.Text;

using FirebirdSql.Data.FirebirdClient;

using JasperFx.Descriptors;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Weasel.Firebird;

using Weasel.Core;
using Weasel.Core.Migrations;
using Weasel.Firebird;
using Weasel.Firebird.Tables;

using CascadeAction = Weasel.Core.CascadeAction;

namespace Quartz.Tests.Unit.Weasel;

/// <summary>
/// The Firebird model as far as it can be checked without a server: that it names every object the
/// way <c>create_firebird.sql</c> does, and that a table prefix Firebird cannot take is refused before a
/// command runs. The integration tests read it back from real Firebird 3, 4 and 5 catalogs.
/// </summary>
public sealed class FirebirdWeaselModelTest
{
    private const string ConnectionString = "DataSource=db.internal;Port=3050;Database=/var/lib/firebird/data/schedules.fdb;User=SYSDBA;Password=unused";

    /// <summary>A server nothing answers for, refused at once rather than timed out.</summary>
    private const string Unreachable = "DataSource=127.0.0.1;Port=1;Database=/nowhere.fdb;User=SYSDBA;Password=unused;Connection Timeout=1;Pooling=false";

    [Test]
    public async Task EveryObjectIsNamedTheWayTheScriptNamesIt()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-fb-names");
        await using (services)
        {
            List<ISchemaObject> objects = database.BuildFeatureSchemas().Single().Objects.ToList();
            List<Table> tables = objects.OfType<Table>().ToList();

            tables.Should().HaveCount(15);
            tables.Should().OnlyContain(x => x.Identifier.Schema == FirebirdObjectName.DefaultSchema && x.Identifier.Name.StartsWith("QRTZ_", StringComparison.Ordinal),
                "Firebird has no schemas, and Weasel's pseudo-schema is never written into DDL");
            tables.Should().OnlyContain(x => x.AddOnlyMigrations, "an application's own columns and indexes are never dropped");
            tables.Should().OnlyContain(x => !x.DetectColumnDrift,
                "no Quartz migration changes an existing column, so drift detection could only undo an application's own change");
            tables.Should().OnlyContain(x => x.PrimaryKeyName == "PK_" + x.Identifier.Name);
            tables.SelectMany(x => x.Columns).Should().OnlyContain(x => x.Name == x.Name.ToUpperInvariant(),
                "Firebird folds the script's undelimited names to upper case");

            Dictionary<string, CascadeAction> foreignKeys = tables.SelectMany(x => x.ForeignKeys).ToDictionary(x => x.Name, x => x.DeleteAction);
            foreignKeys.Should().BeEquivalentTo(new Dictionary<string, CascadeAction>
            {
                ["FK_QRTZ_TRIGGERS_1"] = CascadeAction.NoAction,
                ["FK_QRTZ_SIMPLE_TRIGGERS_1"] = CascadeAction.NoAction,
                ["FK_QRTZ_CRON_TRIGGERS_1"] = CascadeAction.NoAction,
                ["FK_QRTZ_SIMPROP_TRIGGERS_1"] = CascadeAction.NoAction,
                ["FK_QRTZ_BLOB_TRIGGERS_1"] = CascadeAction.NoAction,
            }, "the script numbers its foreign keys and declares no rule, which the catalog reads back as RESTRICT and Weasel as NoAction");

            IndexDefinition acquisition = tables.SelectMany(x => x.Indexes).Single(x => x.Name == "IDX_QRTZ_T_NFT_ST");
            acquisition.Columns.Should().Equal(["SCHED_NAME", "TRIGGER_STATE", "NEXT_FIRE_TIME"],
                "a Firebird index has one direction, so it keeps the three-column acquisition index it can express");
            acquisition.SortOrder.Should().Be(SortOrder.Asc);
            acquisition.DescendingColumns.Should().BeEmpty();

            tables.Single(x => x.Identifier.Name == "QRTZ_EXECUTION_HISTORY").Columns
                .Where(x => x.Type.StartsWith("BLOB", StringComparison.Ordinal))
                .Select(x => (x.Name, x.Type))
                .Should().BeEquivalentTo([("EXECUTION_LOG", "BLOB SUB_TYPE TEXT"), ("METRICS", "BLOB SUB_TYPE TEXT")]);

            List<string> retired = objects.OfType<RetiredFirebirdIndex>().Select(x => x.Identifier.Name).ToList();
            retired.Should().HaveCount(23, "every index name 4.x retired, each dropped if still there")
                .And.Contain(["IDX_QRTZ_T_NFT_ST_MISFIRE", "IDX_QRTZ_T_NFT_ST_MISFIRE_GRP", "IDX_QRTZ_J_REQ_RECOVERY"]);

            IFeatureSchema feature = database.BuildFeatureSchemas().Single();
            feature.Identifier.Should().Be("quartz");

            StringWriter creation = new();
            feature.WriteFeatureCreation(feature.Migrator, creation);
            creation.ToString().Should().Contain("CONSTRAINT PK_QRTZ_TRIGGERS PRIMARY KEY")
                .And.Contain("SET TERM ^ ;", "every CREATE is guarded through an EXECUTE BLOCK, written the way isql runs it")
                .And.NotContain("IDX_QRTZ_T_G_J", "a retired index is never created")
                .And.NotContain("CASCADE", "no Quartz foreign key cascades on Firebird")
                .And.NotContain("PUBLIC", "the pseudo-schema is never written into DDL");
        }
    }

    /// <summary>
    /// The longest name the model creates is <c>IDX_QRTZ_FT_INST_JOB_REQ_RCVRY</c>, 30 bytes under the
    /// default prefix, so every name fits Firebird 3.
    /// </summary>
    [Test]
    public async Task EveryNameFitsFirebird3UnderTheDefaultPrefix()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-fb-lengths");
        await using (services)
        {
            List<string> names = database.BuildFeatureSchemas().Single().Objects
                .OfType<Table>()
                .SelectMany(x => x.AllNames().Select(n => n.Name).Concat(x.LocalIdentifiers()))
                .ToList();

            names.Max(x => Encoding.UTF8.GetByteCount(x)).Should().Be(30);
            names.Should().Contain("IDX_QRTZ_FT_INST_JOB_REQ_RCVRY");
            (names.Max(x => x.Length) - "QRTZ_".Length).Should().Be(FirebirdQuartzDatabase.LongestNameBeyondThePrefix,
                "the refusal's arithmetic is taken from the longest name");
        }
    }

    [Test]
    public async Task ATablePrefixNamingASchemaIsRefused()
    {
        Func<Task> build = () => BuildAsync("weasel-fb-schema", "scheduling.QRTZ_");

        await build.Should().ThrowAsync<SchedulerConfigException>()
            .WithMessage("*'weasel-fb-schema'*schema 'scheduling'*Firebird 3, 4 and 5 have no schemas*");
    }

    [Test]
    public async Task APrefixTooLongForFirebird3IsRefusedBeforeAnythingRuns()
    {
        Func<Task> build = () => BuildAsync("weasel-fb-long", "QRTZ12_");

        (await build.Should().ThrowAsync<SchedulerConfigException>()
                .WithMessage("*'weasel-fb-long'*'QRTZ12_'*IDX_QRTZ12_FT_INST_JOB_REQ_RCVRY*31*Nothing was changed*at most 6 characters*MaxIdentifierLength to 63*"))
            .Which.InnerException.Should().BeOfType<InvalidOperationException>("Weasel's own refusal is kept as the cause");

        (ServiceProvider services, IDatabase _) = await BuildAsync("weasel-fb-six", "QRTZ1_");
        await services.DisposeAsync();
    }

    [Test]
    public async Task TheFirebird4LimitTakesALongerPrefix()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync(
            "weasel-fb-63", "QRTZ_REPORTING_", options => options.MaxIdentifierLength = FirebirdWeaselOptions.Firebird4MaxIdentifierLength);

        await using (services)
        {
            ((FirebirdMigrator) database.Migrator).MaxIdentifierLength.Should().Be(63);
            ((FirebirdMigrator) database.BuildFeatureSchemas().Single().Migrator).MaxIdentifierLength.Should().Be(63,
                "the feature's migrator writes the DDL, and checks the names again as it does");
            database.BuildFeatureSchemas().Single().Objects.OfType<Table>()
                .Should().OnlyContain(x => x.Identifier.Name.StartsWith("QRTZ_REPORTING_", StringComparison.Ordinal));
        }

        Func<Task> build = () => BuildAsync(
            "weasel-fb-63-long", new string('Q', 39), options => options.MaxIdentifierLength = FirebirdWeaselOptions.Firebird4MaxIdentifierLength);

        (await build.Should().ThrowAsync<SchedulerConfigException>().WithMessage("*at most 38 characters.*"))
            .Which.Message.Should().NotContain("MaxIdentifierLength", "there is no longer limit to suggest");
    }

    [TestCase(30)]
    [TestCase(64)]
    public void AnIdentifierLimitFirebirdDoesNotHaveIsRefused(int limit)
    {
        ServiceCollection services = new();

        Action register = () => services.AddQuartz(q => q.UsePersistentStore(
            store => store.UseWeaselForFirebird(weasel => weasel.MaxIdentifierLength = limit)));

        register.Should().Throw<SchedulerConfigException>().WithMessage($"*MaxIdentifierLength is {limit}*31 for Firebird 3*63*");
    }

    [Test]
    public void TheDefaultsAreFirebird3AndTheJasperFxProfile()
    {
        FirebirdWeaselOptions options = new();

        options.MaxIdentifierLength.Should().Be(31, "a schema Weasel creates has to open on every Firebird Quartz supports");
        options.AutoCreate.Should().BeNull("unset follows the JasperFx profile");
    }

    [Test]
    public async Task TheDatabaseDescribesTheStoresOwnConnection()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-fb-described");
        await using (services)
        {
            DatabaseDescriptor descriptor = database.Describe();

            descriptor.Engine.Should().Be(FirebirdProvider.EngineName);
            descriptor.ServerName.Should().Be("db.internal");
            descriptor.DatabaseName.Should().Be("/var/lib/firebird/data/schedules.fdb");
            descriptor.SchemaOrNamespace.Should().Be(FirebirdObjectName.DefaultSchema);
            descriptor.Identifier.Should().Be("weasel-fb-described");
            descriptor.SubjectUri.Should().Be(new Uri("quartz://scheduler/weasel-fb-described"));

            database.Migrator.Should().BeOfType<FirebirdMigrator>();
            database.Migrator.RefuseDestructiveChanges.Should().BeTrue();
            ((FirebirdMigrator) database.Migrator).MaxIdentifierLength.Should().Be(31);
            await database.ReleaseConnectionPoolAsync();
        }
    }

    [Test]
    public async Task AStoreOnAnotherDatabaseIsRefused()
    {
        using SqliteTestDatabase sqlite = new("weasel-firebird-on-sqlite");

        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "weasel-firebird-on-sqlite");
            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, sqlite.ConnectionString);
                store.UseWeaselForFirebird();
            });
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        Func<Task> build = async () => await provider.GetRequiredService<IDatabaseSource>().BuildDatabases();
        await build.Should().ThrowAsync<SchedulerConfigException>()
            .WithMessage("*UseWeaselForFirebird() manages a schema on Firebird*SqliteConnection*UseFirebird*");
    }

    /// <summary>
    /// A retired index is dropped when the catalog still has it on its Quartz table, and otherwise left
    /// out of the migration altogether; it is never created, and its drop is guarded so racing appliers
    /// both succeed.
    /// </summary>
    [Test]
    public async Task ARetiredIndexIsDroppedOnlyWhenItIsStillThere()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-fb-retired", "qrtz_");
        await using (services)
        {
            ISchemaObject retired = database.BuildFeatureSchemas().Single().Objects
                .OfType<RetiredFirebirdIndex>()
                .Single(x => x.Identifier.Name == "IDX_qrtz_T_G_J");

            await using FbConnection unopened = new(ConnectionString);
            FirebirdDbCommandBuilder builder = new(unopened);
            retired.ConfigureQueryCommand(builder);
            DbCommand command = builder.Compile();
            command.CommandText.Should().Contain("RDB$INDICES").And.Contain("RDB$RELATION_NAME = @");
            command.Parameters.Cast<DbParameter>().Select(x => x.Value).Should().Equal(["IDX_QRTZ_T_G_J", "QRTZ_TRIGGERS"],
                "the catalog stores an undelimited name folded to upper case, and that is the spelling to bind");

            (await retired.CreateDeltaAsync(Reader(typeof(long), 1L))).Difference.Should().Be(SchemaPatchDifference.Update);
            (await retired.CreateDeltaAsync(Reader(typeof(long), 0L))).Difference.Should().Be(SchemaPatchDifference.None);

            StringWriter drop = new();
            retired.WriteDropStatement(new FirebirdMigrator(), drop);
            drop.ToString().Should().Contain("IF (EXISTS(SELECT 1 FROM RDB$INDICES WHERE RDB$INDEX_NAME = 'IDX_QRTZ_T_G_J' AND RDB$RELATION_NAME = 'QRTZ_TRIGGERS'))")
                .And.Contain("EXECUTE STATEMENT 'DROP INDEX IDX_qrtz_T_G_J'");

            StringWriter create = new();
            retired.WriteCreateStatement(new FirebirdMigrator(), create);
            create.ToString().Should().BeEmpty("a retired index is never created");
        }
    }

    [Test]
    public async Task AnApplyThatCannotReachTheServerFailsAfterReadingAgain()
    {
        (ServiceProvider services, IDatabase database) = await BuildAsync("weasel-fb-unreachable", connectionString: Unreachable);
        await using (services)
        {
            Func<Task> apply = () => database.ApplyAllConfiguredChangesToDatabaseAsync();
            // FbException where the refusal is immediate, TimeoutException where Windows retries the
            // connect for longer than the one-second connection timeout.
            await apply.Should().ThrowAsync<Exception>("nothing listens on port 1, and reading the schema again after the failure fails the same way");
        }
    }

    private static async Task<(ServiceProvider Services, IDatabase Database)> BuildAsync(
        string schedulerName,
        string tablePrefix = "QRTZ_",
        Action<FirebirdWeaselOptions>? configure = null,
        string connectionString = ConnectionString)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = schedulerName);
            q.UsePersistentStore(store =>
            {
                store.UseFirebird(FirebirdClientFactory.Instance, connectionString);
                store.ConfigureStore(options => options.TablePrefix = tablePrefix);
                store.UseWeaselForFirebird(configure);
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
