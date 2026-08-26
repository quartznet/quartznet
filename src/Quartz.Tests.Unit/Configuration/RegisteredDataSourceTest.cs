#nullable enable

using System.Data;
using System.Data.Common;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Tests.Unit.Impl.AdoJobStore;

using Npgsql;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// The three ways a data source can hand Quartz a <see cref="DbDataSource"/> rather than a connection
/// string, and the one thing they all have to get right: a process talking to two databases must be able
/// to tell its two data sources apart.
/// </summary>
public sealed class RegisteredDataSourceTest
{
    private const string FirstConnectionString = "Host=first;Username=u;Password=p;Database=first";
    private const string SecondConnectionString = "Host=second;Username=u;Password=p;Database=second";

    [Test]
    public void TwoNamedSchedulers_EachResolveTheirOwnKeyedDataSource()
    {
        var services = new ServiceCollection();
        services.AddNpgsqlDataSource(FirstConnectionString, serviceKey: "tenant-a");
        services.AddNpgsqlDataSource(SecondConnectionString, serviceKey: "tenant-b");

        services.AddQuartz("tenant-a", q => q.UsePersistentStore(store =>
            store.UsePostgres(db => db.DataSourceServiceKey = "tenant-a")));
        services.AddQuartz("tenant-b", q => q.UsePersistentStore(store =>
            store.UsePostgres(db => db.DataSourceServiceKey = "tenant-b")));

        using var container = services.BuildServiceProvider();

        ConnectionStringOf(container.GetRequiredKeyedService<IDbProvider>("tenant-a")).Should().Contain("Database=first");
        ConnectionStringOf(container.GetRequiredKeyedService<IDbProvider>("tenant-b")).Should().Contain("Database=second",
            "keying the data sources apart is the only way two schedulers in one container can reach two databases");
    }

    [Test]
    public void AServiceKey_ImpliesUseRegisteredDataSource()
    {
        var services = new ServiceCollection();
        services.AddNpgsqlDataSource(FirstConnectionString, serviceKey: "tenant-a");
        services.AddQuartz(q => q.UsePersistentStore(store =>
            store.UsePostgres(db => db.DataSourceServiceKey = "tenant-a")));

        using var container = services.BuildServiceProvider();

        // No connection string was configured, so falling through to the connection-string branch would
        // fail validation rather than reaching the keyed data source.
        container.GetRequiredService<IDbProvider>().Should().BeOfType<DataSourceDbProvider>();
    }

    [Test]
    public void ADataSourceFactory_WinsOverBothOtherWays()
    {
        var built = NpgsqlDataSource.Create(SecondConnectionString);

        var services = new ServiceCollection();
        services.AddNpgsqlDataSource(FirstConnectionString);
        services.AddQuartz(q => q.UsePersistentStore(store => store.UsePostgres(db =>
        {
            db.UseRegisteredDataSource = true;
            db.DataSourceFactory = _ => built;
        })));

        using var container = services.BuildServiceProvider();

        ConnectionStringOf(container.GetRequiredService<IDbProvider>()).Should().Contain("Database=second",
            "a data source the caller built is the most specific answer, so it wins");
    }

    [Test]
    public void ADataSourceFactoryNeedsNoConnectionString()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.UsePostgres(db =>
            db.DataSourceFactory = _ => NpgsqlDataSource.Create(FirstConnectionString))));

        using var container = services.BuildServiceProvider();

        Validate(container).Failed.Should().BeFalse(
            "a DbDataSource carries its own connection details, however it was reached");
    }

    [Test]
    public void AServiceKeyNeedsNoConnectionString()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.UsePostgres(db =>
            db.DataSourceServiceKey = "tenant-a")));

        using var container = services.BuildServiceProvider();

        Validate(container).Failed.Should().BeFalse();
    }

    [Test]
    public void NothingButAProviderIsStillRejected()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.UsePostgres(_ => { })));

        using var container = services.BuildServiceProvider();

        var act = () => container.GetRequiredService<IOptionsMonitor<DataSourceOptions>>().Get("quartz");
        act.Should().Throw<OptionsValidationException>().WithMessage("*ConnectionString*",
            "a data source with neither a connection string nor a DbDataSource cannot reach a database");
    }

    private static ValidateOptionsResult Validate(IServiceProvider container)
    {
        var options = container.GetRequiredService<IOptionsMonitor<DataSourceOptions>>().Get("quartz");
        return new DataSourceOptionsValidator().Validate("quartz", options);
    }

    /// <summary>
    /// The connection string a provider's data source hands out, read off a connection rather than from
    /// the provider — a <see cref="DataSourceDbProvider"/> holds none of its own.
    /// </summary>
    private static string ConnectionStringOf(IDbProvider provider)
    {
        provider.Should().BeOfType<DataSourceDbProvider>();
        using DbConnection connection = provider.CreateConnection();
        return connection.ConnectionString;
    }
}

/// <summary>
/// A command for the data source path comes from the connection the unit of work is running on, so that
/// whatever the data source configured on that connection is in play — an <c>NpgsqlDataSource</c>'s type
/// mappers, for one. A command built by reflection over the driver description reaches none of it.
/// </summary>
/// <remarks>
/// The driver here is a fake, because what is under test is which of the two ways of minting a command
/// was taken; a real driver would only add the requirement that its connections be reachable.
/// </remarks>
public sealed class DataSourceCommandMintingTest
{
    private static readonly DbMetadata FakeDriver = new()
    {
        ProductName = "Fake",
        ConnectionType = typeof(FakeConnection),
        CommandType = typeof(FakeCommand),
        ParameterType = typeof(FakeParameter),
        ParameterNamePrefix = "@",
        BindByName = true,
    };

    [Test]
    public void PrepareCommand_OnTheDataSourcePath_AsksTheConnection()
    {
        using var dataSource = new FakeDataSource();
        IDbProvider provider = new DataSourceDbProvider(FakeDriver, dataSource);

        var connection = new FakeConnection();
        using var holder = new ConnectionAndTransactionHolder(connection, transaction: null);

        using DbCommand command = new AdoUtil(provider).PrepareCommand(holder, "SELECT 1");

        connection.CommandsCreated.Should().Be(1,
            "a command built from the driver description starts out attached to nothing, so it would miss "
            + "whatever the data source configured on the connection it is handed afterwards");
        command.CommandText.Should().Be("SELECT 1");
        command.Connection.Should().BeSameAs(connection);
    }

    [Test]
    public void PrepareCommand_OnTheConnectionStringPath_StillBuildsFromTheDescription()
    {
        IDbProvider provider = new DbProvider(FakeDriver, "irrelevant");

        var connection = new FakeConnection();
        using var holder = new ConnectionAndTransactionHolder(connection, transaction: null);

        using DbCommand command = new AdoUtil(provider).PrepareCommand(holder, "SELECT 1");

        connection.CommandsCreated.Should().Be(0,
            "when Quartz owns the connection string the driver description is all there is to go on, and "
            + "gating the change leaves that path exactly as it was");
        command.Connection.Should().BeSameAs(connection);
    }

    [Test]
    public void PrepareCommand_OnTheDataSourcePath_StillAppliesTheDriversBindByName()
    {
        using var dataSource = new FakeDataSource();
        IDbProvider provider = new DataSourceDbProvider(FakeDriver with { BindByName = false }, dataSource);

        var connection = new FakeConnection();
        using var holder = new ConnectionAndTransactionHolder(connection, transaction: null);

        using DbCommand command = new AdoUtil(provider).PrepareCommand(holder, "SELECT 1");

        // BindByName belongs to the driver rather than to the command, so a command that came from a
        // connection needs it set just as much as one built from the description. Managed Oracle is the
        // driver that binds by position without it.
        command.Should().BeOfType<FakeCommand>().Which.BindByName.Should().BeFalse();
    }

    [Test]
    public void PrepareCommand_WithNoCommandTimeoutConfigured_LeavesTheProvidersDefaultAlone()
    {
        IDbProvider provider = new DbProvider(FakeDriver, "irrelevant");

        var connection = new FakeConnection();
        using var holder = new ConnectionAndTransactionHolder(connection, transaction: null);

        using DbCommand command = new AdoUtil(provider).PrepareCommand(holder, "SELECT 1");

        command.CommandTimeout.Should().Be(0,
            "the fake's default is 0; a real provider's is its own, and neither should be overwritten "
            + "when AdoJobStoreOptions.CommandTimeout is unset");
    }

    [Test]
    public void PrepareCommand_WithACommandTimeout_PutsItOnTheCommand()
    {
        IDbProvider provider = new DbProvider(FakeDriver, "irrelevant");

        var connection = new FakeConnection();
        using var holder = new ConnectionAndTransactionHolder(connection, transaction: null);

        using DbCommand command = new AdoUtil(provider, TimeSpan.FromSeconds(45)).PrepareCommand(holder, "SELECT 1");

        command.CommandTimeout.Should().Be(45);
    }

    /// <summary>
    /// <see cref="DbCommand.CommandTimeout" /> counts whole seconds, and rounding a sub-second value
    /// down would produce <c>0</c> — which every provider reads as "no timeout at all", the exact
    /// opposite of what was configured.
    /// </summary>
    [TestCase(1500, 2)]
    [TestCase(1, 1)]
    [TestCase(60_000, 60)]
    public void PrepareCommand_RoundsAPartialSecondUp(int configuredMilliseconds, int expectedSeconds)
    {
        IDbProvider provider = new DbProvider(FakeDriver, "irrelevant");

        var connection = new FakeConnection();
        using var holder = new ConnectionAndTransactionHolder(connection, transaction: null);

        using DbCommand command = new AdoUtil(provider, TimeSpan.FromMilliseconds(configuredMilliseconds))
            .PrepareCommand(holder, "SELECT 1");

        command.CommandTimeout.Should().Be(expectedSeconds);
    }
}
