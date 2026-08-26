#nullable enable

using System.Data.Common;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl.AdoJobStore.Common;
using Quartz.Tests.Unit.Impl.AdoJobStore;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Which half of a driver description each way of reaching a database asks for. A <see cref="DbDataSource"/>
/// hands over the connections, so the description it needs names no type; only the connection string path,
/// where Quartz constructs the driver's own objects, resolves the driver's types by name.
/// </summary>
/// <remarks>
/// The difference is invisible until an application publishes trimmed, and the trimming canary cannot show
/// it — <c>Microsoft.Data.Sqlite</c> ships no <see cref="DbDataSource"/> — so these tests are the guard.
/// They ask the question from both sides: a recording description says which half was asked for, and a
/// driver this test assembly does not reference says what asking for the wrong half costs.
/// </remarks>
public sealed class DataSourceMetadataResolutionTest
{
    private const string FakeProvider = "FakeDriver";

    [Test]
    public void ADataSourceFactory_ResolvesTheDescriptionWithoutTheDriversTypes()
    {
        RecordingDriverDescription driver = new();
        using FakeDataSource source = new();
        using ServiceProvider container = ContainerFor(driver, options => options.DataSourceFactory = _ => source);

        AssertTheTypedHalfWasNeverAskedFor(container, driver);
    }

    [Test]
    public void AKeyedDataSource_ResolvesTheDescriptionWithoutTheDriversTypes()
    {
        RecordingDriverDescription driver = new();
        using ServiceProvider container = ContainerFor(
            driver,
            options => options.DataSourceServiceKey = "tenant-a",
            services => services.AddKeyedSingleton<DbDataSource>("tenant-a", new FakeDataSource()));

        AssertTheTypedHalfWasNeverAskedFor(container, driver);
    }

    [Test]
    public void ARegisteredDataSource_ResolvesTheDescriptionWithoutTheDriversTypes()
    {
        RecordingDriverDescription driver = new();
        using ServiceProvider container = ContainerFor(
            driver,
            options => options.UseRegisteredDataSource = true,
            services => services.AddSingleton<DbDataSource>(new FakeDataSource()));

        AssertTheTypedHalfWasNeverAskedFor(container, driver);
    }

    /// <summary>
    /// The control for the three above: the distinction is between the two halves of a description, not
    /// between asking for one and asking for nothing.
    /// </summary>
    [Test]
    public void AConnectionString_StillResolvesTheDriversTypes()
    {
        RecordingDriverDescription driver = new();
        using ServiceProvider container = ContainerFor(driver, options => options.ConnectionString = "irrelevant");

        IDbProvider provider = container.GetRequiredService<IDbProvider>();

        provider.Should().BeOfType<DbProvider>();
        driver.TypedRequests.Should().Be(1,
            "Quartz constructs the driver's connection and command itself when it holds the connection "
            + "string, so this path cannot do without their types");
        provider.Metadata.ConnectionType.Should().Be(typeof(FakeConnection));
    }

    /// <summary>
    /// The same distinction told by a description Quartz ships, where the typed half is a
    /// <see cref="Type.GetType(string)" /> per driver type. Firebird's driver is nowhere near this test
    /// assembly, which is the position a trimmed application is in for a type the trimmer removed.
    /// </summary>
    [Test]
    public void ADriverThisProcessCannotLoadIsStillReachableThroughADataSource()
    {
        using FakeDataSource source = new();
        ServiceCollection services = new();
        services.AddQuartz(q => q.UsePersistentStore(store =>
            store.UseFirebird(db => db.DataSourceFactory = _ => source)));

        using ServiceProvider container = services.BuildServiceProvider();

        container.GetRequiredService<IDbProvider>().Should().BeOfType<DataSourceDbProvider>(
            "connections come from the data source, so whether the driver's assembly is loadable decides "
            + "nothing here — and it is exactly what a trimmed application cannot promise");
    }

    [Test]
    public void TheSameDriverOnTheConnectionStringPathStillNeedsItsAssembly()
    {
        ServiceCollection services = new();
        services.AddQuartz(q => q.UsePersistentStore(store => store.UseFirebird("irrelevant")));

        using ServiceProvider container = services.BuildServiceProvider();

        Action resolve = () => container.GetRequiredService<IDbProvider>();

        resolve.Should().Throw<ArgumentException>().WithMessage("*'Firebird'*",
            "the driver's types are what this path constructs from, so a driver that is not here is a "
            + "configuration mistake rather than something to work around");
    }

    private static void AssertTheTypedHalfWasNeverAskedFor(ServiceProvider container, RecordingDriverDescription driver)
    {
        IDbProvider provider = container.GetRequiredService<IDbProvider>();

        provider.Should().BeOfType<DataSourceDbProvider>();
        driver.TypedRequests.Should().Be(0,
            "the typed half resolves the driver's connection, command, parameter and exception types by "
            + "name, and a provider built over a DbDataSource constructs none of them");
        driver.TypeFreeRequests.Should().Be(1);
        provider.Metadata.ConnectionType.Should().BeNull(
            "the description that reached the provider is the one that names no type at all");
    }

    /// <summary>
    /// Builds a container whose only description of <see cref="FakeProvider" /> is the one passed in, so
    /// what the store asks of it is what the store asked of any driver description.
    /// </summary>
    private static ServiceProvider ContainerFor(
        DbMetadataFactory driver,
        Action<DataSourceOptions> configureDataSource,
        Action<IServiceCollection>? register = null)
    {
        ServiceCollection services = new();
        register?.Invoke(services);
        services.AddSingleton(driver);
        services.AddQuartz(q => q.UsePersistentStore(store =>
            store.UseGenericDatabase(FakeProvider, configureDataSource)));

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// A driver description that counts which of its two halves was asked for.
    /// </summary>
    /// <remarks>
    /// The halves say the same things about the driver, and differ only in the types — which is the shape
    /// <see cref="BuiltInDbMetadataFactory" /> has, and the only thing under test here.
    /// </remarks>
    private sealed class RecordingDriverDescription : DbMetadataFactory
    {
        public int TypedRequests { get; private set; }

        public int TypeFreeRequests { get; private set; }

        public override List<string> GetProviderNames() => [FakeProvider];

        public override DbMetadata GetDbMetadata(string providerName)
        {
            TypedRequests++;
            return facts with
            {
                ConnectionType = typeof(FakeConnection),
                CommandType = typeof(FakeCommand),
                ParameterType = typeof(FakeParameter),
            };
        }

        public override DbMetadata GetTypeFreeDbMetadata(string providerName)
        {
            TypeFreeRequests++;
            return facts;
        }

        private static readonly DbMetadata facts = new()
        {
            ProductName = "Fake",
            ParameterNamePrefix = "@",
            UseParameterNamePrefixInParameterCollection = true,
            BindByName = true,
        };
    }
}
