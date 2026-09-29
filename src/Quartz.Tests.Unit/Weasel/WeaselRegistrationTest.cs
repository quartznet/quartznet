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
using JasperFx.CommandLine.Descriptions;
using JasperFx.Descriptors;
using JasperFx.Environment;
using JasperFx.Resources;

using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Quartz.Impl.AdoJobStore;
using Quartz.Weasel;

using Weasel.Core;
using Weasel.Core.CommandLine;
using Weasel.Core.Migrations;

namespace Quartz.Tests.Unit.Weasel;

/// <summary>
/// What the registration puts in the container, and how JasperFx's resource model and Weasel's database
/// source see a scheduler through it.
/// </summary>
public sealed class WeaselRegistrationTest
{
    private SqliteTestDatabase database = null!;

    [SetUp]
    public void CreateEmptyDatabase()
    {
        database = new SqliteTestDatabase("weasel-registration");
    }

    [TearDown]
    public void DeleteDatabase()
    {
        database.Dispose();
    }

    [Test]
    public async Task EachSchedulerIsADatabaseOfItsOwnNamedAfterIt()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "weasel-default");
            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.UseWeaselForSqlite();
            });
        });
        services.AddQuartz("weasel-reporting", q => q.UsePersistentStore(store =>
        {
            store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
            store.ConfigureStore(options => options.TablePrefix = "REPORTING_");
            store.UseWeaselForSqlite();
        }));

        await using ServiceProvider provider = services.BuildServiceProvider();

        IDatabaseSource source = provider.GetRequiredService<IDatabaseSource>();
        source.Should().BeSameAs(provider.GetServices<ISystemPart>().OfType<IDatabaseSource>().Single(),
            "resources and db-* walk the same databases");
        source.Cardinality.Should().Be(DatabaseCardinality.StaticMultiple);

        IReadOnlyList<IDatabase> databases = await source.BuildDatabases();
        databases.Select(x => x.Describe().SubjectUri.ToString()).Should().BeEquivalentTo(
            ["quartz://scheduler/weasel-default", "quartz://scheduler/weasel-reporting"],
            "a scheduler's name is how db-patch -d picks its database");

        foreach (IDatabase each in databases)
        {
            await each.ApplyAllConfiguredChangesToDatabaseAsync();
        }

        (await SqliteSchema.ScalarAsync(database.ConnectionString, "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name LIKE 'REPORTING!_%' ESCAPE '!'"))
            .Should().Be((long) (AdoConstants.AllTableNames.Length + AdoConstants.OptionalTableNames.Length),
                "every table the store knows, required and optional, is created under the named scheduler's own prefix");

        DatabaseUsage usage = await source.DescribeDatabasesAsync(CancellationToken.None);
        usage.Databases.Should().HaveCount(2);
        usage.MainDatabase.Should().BeNull("there is no one main database when there are two");
    }

    [Test]
    public async Task TheSystemPartDescribesChecksAndSetsUpTheDatabase()
    {
        await using ServiceProvider provider = BuildProvider("weasel-resources");

        ISystemPart part = provider.GetServices<ISystemPart>().Single();
        part.Title.Should().Be("Quartz.NET");
        ((IDatabaseSource) part).Cardinality.Should().Be(DatabaseCardinality.Single);

        EnvironmentCheckResults results = new();
        await part.AssertEnvironmentAsync(provider, results, CancellationToken.None);
        results.Succeeded().Should().BeTrue("the database file is reachable");

        DatabaseUsage usage = await ((IDatabaseSource) part).DescribeDatabasesAsync(CancellationToken.None);
        usage.MainDatabase.Should().NotBeNull();
        usage.MainDatabase!.Identifier.Should().Be("weasel-resources");

        await part.WriteToConsole();

        IStatefulResource resource = (await part.FindResources()).Should().ContainSingle().Subject;
        await resource.Setup(CancellationToken.None);
        await resource.Check(CancellationToken.None);

        await SqliteSchema.ExecuteAsync(database.ConnectionString, """
            INSERT INTO QRTZ_JOB_DETAILS (SCHED_NAME, JOB_NAME, JOB_GROUP, JOB_CLASS_NAME, IS_DURABLE, IS_NONCONCURRENT, IS_UPDATE_DATA, REQUESTS_RECOVERY)
              VALUES ('weasel', 'job', 'group', 'Weasel.Job', 1, 0, 0, 0);
            """);

        await resource.ClearState(CancellationToken.None);

        (await SqliteSchema.ScalarAsync(database.ConnectionString, "SELECT count(*) FROM QRTZ_JOB_DETAILS")).Should().Be(1L,
            "resources clear must not empty the tables: they hold the schedule, not state that can be rebuilt");
    }

    [Test]
    public async Task AnUnreachableDatabaseIsAnEnvironmentFailure()
    {
        string missingDirectory = Path.Combine(Path.GetTempPath(), $"quartz-weasel-missing-{Guid.NewGuid():N}", "quartz.db");
        string connectionString = new SqliteConnectionStringBuilder { DataSource = missingDirectory, Pooling = false }.ToString();

        await using ServiceProvider provider = BuildProvider("weasel-unreachable", connectionString);

        EnvironmentCheckResults results = new();
        await provider.GetServices<ISystemPart>().Single().AssertEnvironmentAsync(provider, results, CancellationToken.None);

        results.Failures.Should().ContainSingle().Which.Description.Should().Contain("weasel-unreachable");
    }

    [Test]
    public async Task CallingTheRegistrationTwiceKeepsOneWithTheLastOptions()
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "weasel-twice");
            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                store.UseWeaselForSqlite(weasel => weasel.AutoCreate = AutoCreate.None);
                store.UseWeaselForSqlite(weasel => weasel.AutoCreate = AutoCreate.CreateOnly);
            });
        });

        services.Where(x => x.ServiceType == typeof(QuartzWeaselRegistration)).Should().ContainSingle()
            .Which.ImplementationInstance.Should().BeOfType<QuartzWeaselRegistration>()
            .Which.AutoCreate.Should().Be(AutoCreate.CreateOnly);

        services.Count(x => x.ServiceType == typeof(QuartzWeaselSystemPart)).Should().Be(1);
        services.Count(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(QuartzWeaselStartup)).Should().Be(1);

        await using ServiceProvider provider = services.BuildServiceProvider();
        (await provider.GetRequiredService<IDatabaseSource>().BuildDatabases()).Should().ContainSingle();
    }

    [Test]
    public void AnotherDialectForTheSameStoreIsRefused()
    {
        ServiceCollection services = new();

        Action register = () => services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
            store.UseWeaselForSqlite();
            store.UseWeaselForPostgres();
        }));

        register.Should().Throw<SchedulerConfigException>()
            .WithMessage("*UseWeaselForPostgres()*UseWeaselForSqlite() already handed to Weasel*");
    }

    [Test]
    public void AMigrationStatementThatFailsStopsTheMigrationNamingIt()
    {
        QuartzWeaselMigrationLogger logger = new(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, "weasel-logger");

        using SqliteCommand command = new("ALTER TABLE QRTZ_TRIGGERS ADD COLUMN X INTEGER");

        Action fail = () => logger.OnFailure(command, new InvalidOperationException("duplicate column name: X"));

        fail.Should().Throw<SchedulerException>()
            .WithMessage("*scheduler 'weasel-logger'*ALTER TABLE QRTZ_TRIGGERS ADD COLUMN X INTEGER*")
            .WithInnerException<InvalidOperationException>();

        logger.Invoking(x => x.SchemaChange("CREATE TABLE X (ID INTEGER)")).Should().NotThrow();
        logger.Invoking(x => x.DestructiveChange("dropping X")).Should().NotThrow();
        logger.Invoking(x => x.WithheldDrop("column user_note")).Should().NotThrow();
    }

    [Test]
    public void ATablePrefixIsSplitWhereTheStoresOwnSqlSplitsIt()
    {
        QuartzWeaselSystemPart.SplitTablePrefix("QRTZ_").Should().Be((null, "QRTZ_"));
        QuartzWeaselSystemPart.SplitTablePrefix("quartz.QRTZ_").Should().Be(("quartz", "QRTZ_"));
        QuartzWeaselSystemPart.SplitTablePrefix("db.quartz.QRTZ_").Should().Be(("db.quartz", "QRTZ_"),
            "only the last dot separates the prefix; everything before it qualifies the table");
    }

    private ServiceProvider BuildProvider(string schedulerName, string? connectionString = null)
    {
        ServiceCollection services = new();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = schedulerName);
            q.UsePersistentStore(store =>
            {
                store.UseSqlite(SqliteFactory.Instance, connectionString ?? database.ConnectionString);
                store.UseWeaselForSqlite();
            });
        });

        return services.BuildServiceProvider();
    }
}
