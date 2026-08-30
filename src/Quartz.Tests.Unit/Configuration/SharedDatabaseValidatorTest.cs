using System.Data.Common;

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Tests.Unit.Plugin.History;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Two schedulers sharing a database are separated by <c>SCHED_NAME</c> and share one table prefix. Get
/// the prefix wrong and the misconfigured scheduler connects, validates its schema against the tables it
/// was pointed at, starts, looks healthy and never sees its tenant's data — which is the failure shape
/// that has to be said out loud, because nothing else in the system will.
/// </summary>
/// <remarks>
/// Non-parallelizable because one of these builds two schedulers out of a container.
/// </remarks>
[NonParallelizable]
public sealed class SharedDatabaseValidatorTest
{
    private const string ConnectionString = "Data Source=tenants;Initial Catalog=quartz;User ID=sa;Password=hunter2";

    [Test]
    public void TwoSchedulersOnOneDatabaseWithDifferentPrefixesAreReported()
    {
        List<LogEntry> entries = Validate(
            ("acme", TestJobStores.Tx(tablePrefix: "QRTZ_", dbProvider: TestJobStores.DbProvider(ConnectionString))),
            ("initech", TestJobStores.Tx(tablePrefix: "QRTZ2_", dbProvider: TestJobStores.DbProvider(ConnectionString))));

        LogEntry entry = entries.Should().ContainSingle().Subject;

        entry.Level.Should().Be(LogLevel.Warning, "a legal-but-unusual arrangement is not an error");

        string message = entry.Message;
        message.Should().Contain("'acme'").And.Contain("'initech'",
            "a reader has to know which two schedulers disagreed without going back to the configuration");
        message.Should().Contain("'QRTZ_'").And.Contain("'QRTZ2_'",
            "and which two prefixes, since either one of them may be the intended one");
        message.Should().NotContain("hunter2", "a connection string is never part of a diagnostic");
    }

    [Test]
    public void TheSupportedArrangementIsNotReported()
    {
        List<LogEntry> entries = Validate(
            ("acme", TestJobStores.Tx(tablePrefix: "QRTZ_", dbProvider: TestJobStores.DbProvider(ConnectionString))),
            ("initech", TestJobStores.Tx(tablePrefix: "QRTZ_", dbProvider: TestJobStores.DbProvider(ConnectionString))));

        entries.Should().BeEmpty(
            "one database, one table prefix and two scheduler names is exactly the arrangement multi-tenancy is built on");
    }

    [Test]
    public void PrefixesThatDifferOnlyByCaseAreNotReported()
    {
        List<LogEntry> entries = Validate(
            ("acme", TestJobStores.Tx(tablePrefix: "QRTZ_", dbProvider: TestJobStores.DbProvider(ConnectionString))),
            ("initech", TestJobStores.Tx(tablePrefix: "qrtz_", dbProvider: TestJobStores.DbProvider(ConnectionString))));

        entries.Should().BeEmpty(
            "every database Quartz supports folds an unquoted identifier to one case, so these are the same table set");
    }

    [Test]
    public void SchedulersOnDifferentDatabasesAreNotReported()
    {
        List<LogEntry> entries = Validate(
            ("acme", TestJobStores.Tx(tablePrefix: "QRTZ_", dbProvider: TestJobStores.DbProvider(ConnectionString))),
            ("initech", TestJobStores.Tx(tablePrefix: "QRTZ2_", dbProvider: TestJobStores.DbProvider("Data Source=other;Initial Catalog=quartz"))));

        entries.Should().BeEmpty(
            "a prefix per database is the database-per-tenant model, and nothing about it is suspicious");
    }

    [Test]
    public void ConnectionStringsSpellingTheSameSettingsDifferentlyAreOneDatabase()
    {
        List<LogEntry> entries = Validate(
            ("acme", TestJobStores.Tx(tablePrefix: "QRTZ_", dbProvider: TestJobStores.DbProvider("Data Source=tenants;Initial Catalog=quartz"))),
            ("initech", TestJobStores.Tx(tablePrefix: "QRTZ2_", dbProvider: TestJobStores.DbProvider("initial catalog=quartz; data source=tenants"))));

        entries.Should().ContainSingle(
            "two appsettings entries that describe one database rarely agree on the order or the casing of the keywords");
    }

    [Test]
    public void AnInMemorySchedulerBesideAPersistentOneIsNotReported()
    {
        List<LogEntry> entries = Validate(
            ("acme", TestJobStores.Tx(tablePrefix: "QRTZ_", dbProvider: TestJobStores.DbProvider(ConnectionString))),
            ("initech", TestJobStores.Ram()));

        entries.Should().BeEmpty("an in-memory scheduler shares a database with nobody");
    }

    [Test]
    public void AProviderThatKeepsItsConnectionDetailsToItselfIsNotGuessedAbout()
    {
        List<LogEntry> entries = Validate(
            ("acme", TestJobStores.Tx(tablePrefix: "QRTZ_", dbProvider: TestJobStores.DbProvider())),
            ("initech", TestJobStores.Tx(tablePrefix: "QRTZ2_", dbProvider: TestJobStores.DbProvider())));

        entries.Should().BeEmpty(
            "two providers that report no connection string are not evidence of one database, and a check that "
            + "fires on a legitimate arrangement is worse than the silence it replaces");
    }

    [Test]
    public void OneRegisteredDataSourceServingTwoSchedulersIsOneDatabase()
    {
        using StubDataSource shared = new();

        List<LogEntry> entries = Validate(
            ("acme", TestJobStores.Tx(tablePrefix: "QRTZ_", dbProvider: new DataSourceDbProvider(Metadata(), shared))),
            ("initech", TestJobStores.Tx(tablePrefix: "QRTZ2_", dbProvider: new DataSourceDbProvider(Metadata(), shared))));

        entries.Should().ContainSingle(
            "a DbDataSource reports no connection string, so the data source object itself is what says the two "
            + "schedulers talk to one database");
    }

    [Test]
    public void RecordingTheSameSchedulerTwiceDoesNotMakeItItsOwnNeighbour()
    {
        List<LogEntry> entries = Validate(
            ("acme", TestJobStores.Tx(tablePrefix: "QRTZ_", dbProvider: TestJobStores.DbProvider(ConnectionString))),
            ("acme", TestJobStores.Tx(tablePrefix: "QRTZ2_", dbProvider: TestJobStores.DbProvider(ConnectionString))));

        entries.Should().BeEmpty("a scheduler cannot disagree with itself about its own table prefix");
    }

    [Test]
    public void AConnectionStringNoBuilderCanParseIsStillComparedAsWritten()
    {
        const string Opaque = "an opaque handle a custom provider understands";

        List<LogEntry> entries = Validate(
            ("acme", TestJobStores.Tx(tablePrefix: "QRTZ_", dbProvider: TestJobStores.DbProvider(Opaque))),
            ("initech", TestJobStores.Tx(tablePrefix: "QRTZ2_", dbProvider: TestJobStores.DbProvider(Opaque))));

        entries.Should().ContainSingle(
            "a string that is not keyword/value pairs still identifies a database, and two schedulers holding the "
            + "same one are on the same database whether or not it can be parsed");
    }

    [Test]
    public void AProviderThatRefusesToAnswerCannotStopASchedulerFromStarting()
    {
        List<LogEntry> entries = Validate(
            ("acme", TestJobStores.Tx(tablePrefix: "QRTZ_", dbProvider: new UnanswerableDbProvider())),
            ("initech", TestJobStores.Tx(tablePrefix: "QRTZ2_", dbProvider: new UnanswerableDbProvider())));

        entries.Should().OnlyContain(x => x.Level == LogLevel.Debug,
            "this runs on the path that creates a scheduler, so a diagnostic that throws would turn a warning "
            + "nobody asked for into a startup failure");
    }

    [Test]
    public async Task TheCheckRunsWhereSchedulersAreCreated()
    {
        RecordingLoggerProvider recorder = new();

        ServiceCollection services = new();
        services.AddLogging(logging => logging.AddProvider(recorder));
        services.AddQuartz("acme", q => StubbedPersistentStore(q, "QRTZ_"));
        services.AddQuartz("initech", q => StubbedPersistentStore(q, "QRTZ2_"));

        using ServiceProvider provider = services.BuildServiceProvider();

        IScheduler acme = await provider.GetRequiredKeyedService<ISchedulerFactory>("acme").GetScheduler();
        IScheduler initech = await provider.GetRequiredKeyedService<ISchedulerFactory>("initech").GetScheduler();
        try
        {
            recorder.Entries.Should().ContainSingle(x => x.Message.Contains("different table prefixes"),
                "the check is only worth anything if something calls it - and creating the second scheduler is "
                + "the first moment at which the arrangement exists to be judged");
        }
        finally
        {
            await acme.Shutdown();
            await initech.Shutdown();
        }
    }

    /// <summary>
    /// A persistent store that never reaches a database: schema validation off, and a provider that
    /// refuses to connect. What is under test is the arrangement, not the driver.
    /// </summary>
    private static void StubbedPersistentStore(IQuartzBuilder builder, string tablePrefix)
    {
        builder.UsePersistentStore(store =>
        {
            store.ConfigureStore(options =>
            {
                options.DataSource = "tenants";
                options.TablePrefix = tablePrefix;
                options.SchemaProvisioning = SchemaProvisioning.None;
            });

            store.Services.AddKeyedSingleton<IDbProvider>(builder.SchedulerName, new StubDbProvider(ConnectionString));
        });
    }

    private static List<LogEntry> Validate(params (string SchedulerName, IJobStore JobStore)[] schedulers)
    {
        RecordingLoggerProvider recorder = new();
        using LoggerFactory factory = new();
        factory.AddProvider(recorder);

        SharedDatabaseValidator validator = new(factory.CreateLogger<SharedDatabaseValidator>());
        foreach ((string schedulerName, IJobStore jobStore) in schedulers)
        {
            validator.Validate(schedulerName, jobStore);
        }

        return recorder.Entries;
    }

    /// <summary>
    /// A driver description good enough to construct a provider: <see cref="DbProvider"/> builds command
    /// and connection constructors from it eagerly, so the types have to be real ones.
    /// </summary>
    private static DbMetadata Metadata()
    {
        return new DbMetadata { ConnectionType = typeof(SqlConnection), CommandType = typeof(SqlCommand) };
    }

    /// <summary>
    /// A provider that will not say what it connects to, the way a driver wrapper written elsewhere
    /// might.
    /// </summary>
    private sealed class UnanswerableDbProvider : IDbProvider
    {
        public string ConnectionString => throw new InvalidOperationException("This provider does not publish its connection string.");

        public DbMetadata Metadata { get; } = new();

        public DbCommand CreateCommand() => throw new NotSupportedException();

        public DbConnection CreateConnection() => throw new NotSupportedException();

        public void Shutdown()
        {
        }
    }

    /// <summary>
    /// A data source that never connects. It exists to be compared by reference, which is all the
    /// shared-database check asks of one.
    /// </summary>
    private sealed class StubDataSource : DbDataSource
    {
        public override string ConnectionString => "Data Source=held-by-the-data-source";

        protected override DbConnection CreateDbConnection()
        {
            throw new NotSupportedException("StubDataSource does not connect.");
        }
    }
}
