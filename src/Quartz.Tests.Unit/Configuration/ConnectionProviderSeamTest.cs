#nullable enable

using System.Collections.Specialized;
using System.Data.Common;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// <c>UseConnectionProvider</c> is the seam that replaced <c>DBConnectionManager.AddConnectionProvider</c>,
/// and the one thing it must not inherit from the rest of the builder is first-wins registration: it
/// answers the same question <c>UseSqlServer</c> does, so whichever order the two are called in, the
/// explicit provider has to be the one that survives.
/// </summary>
public sealed class ConnectionProviderSeamTest
{
    private const string ConnectionString = "Server=nowhere;Database=quartz;";

    [Test]
    public void UseConnectionProvider_AfterTheDatabaseChoice_Wins()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseSqlServer(ConnectionString);
            store.UseConnectionProvider<TrackingDbProvider>();
        }));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDbProvider>().Should().BeOfType<TrackingDbProvider>();
    }

    [Test]
    public void UseConnectionProvider_BeforeTheDatabaseChoice_Wins()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseConnectionProvider<TrackingDbProvider>();
            store.UseSqlServer(ConnectionString);
        }));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDbProvider>().Should().BeOfType<TrackingDbProvider>(
            "the data source path registers with TryAdd, so it can never overwrite an explicit choice");
    }

    [Test]
    public void UseConnectionProvider_NamesTheDataSourceItself()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.UseConnectionProvider<TrackingDbProvider>()));

        using var provider = services.BuildServiceProvider();

        // The store refuses to initialize without a data source name, and a provider of one's own is
        // otherwise a complete configuration - so UseConnectionProvider names it, as UseDataSource would.
        provider.GetSchedulerOptions<AdoJobStoreOptions>(null).DataSource.Should().Be("quartz");
    }

    [Test]
    public void UseConnectionProvider_WithAFactory_RunsTheFactory()
    {
        var built = new TrackingDbProvider();

        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.UseConnectionProvider(_ => built)));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDbProvider>().Should().BeSameAs(built);
    }

    [Test]
    public void UseConnectionProvider_OnANamedScheduler_LeavesTheOtherSchedulersAlone()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.UseSqlServer(ConnectionString)));
        services.AddQuartz("reporting", q => q.UsePersistentStore(store =>
        {
            store.UseSqlServer(ConnectionString);
            store.UseConnectionProvider<TrackingDbProvider>();
        }));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<IDbProvider>("reporting").Should().BeOfType<TrackingDbProvider>();
        provider.GetRequiredService<IDbProvider>().Should().BeOfType<DbProvider>(
            "removing a registration to replace it must not reach past this scheduler's own key");
    }

    [Test]
    public void UseConnectionProvider_OnANamedScheduler_NamesTheDataSourceAfterTheScheduler()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting", q => q.UsePersistentStore(store => store.UseConnectionProvider<TrackingDbProvider>()));

        using var provider = services.BuildServiceProvider();

        provider.GetSchedulerOptions<AdoJobStoreOptions>("reporting").DataSource.Should().Be("reporting");
    }

    [Test]
    public void TheLegacyConnectionProviderTypeKeyStillRegistersTheProvider()
    {
        var properties = new NameValueCollection
        {
            ["quartz.jobStore.type"] = typeof(Quartz.Impl.AdoJobStore.LocalTransactionJobStore).AssemblyQualifiedName,
            ["quartz.jobStore.dataSource"] = "myDs",
            ["quartz.dataSource.myDs.provider"] = "SqlServer",
            ["quartz.dataSource.myDs.connectionString"] = ConnectionString,
            ["quartz.dataSource.myDs.connectionProvider.type"] = typeof(TrackingDbProvider).AssemblyQualifiedName,
        };

        var services = new ServiceCollection();
        services.AddQuartz(properties);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDbProvider>().Should().BeOfType<TrackingDbProvider>(
            "quartz.dataSource.<name>.connectionProvider.type selected the provider in 3.x, and a "
            + "configuration that still carries it must not fall back to the connection string");
    }

    [Test]
    public void AConnectionProviderChosenInCodeBeatsTheLegacyTypeKey()
    {
        var properties = new NameValueCollection
        {
            ["quartz.jobStore.dataSource"] = "quartz",
            ["quartz.dataSource.quartz.connectionProvider.type"] = typeof(OtherDbProvider).AssemblyQualifiedName,
        };

        var services = new ServiceCollection();
        services.AddQuartz(properties, q => q.UsePersistentStore(store =>
        {
            store.UseSqlServer(ConnectionString);
            store.UseConnectionProvider<TrackingDbProvider>();
        }));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDbProvider>().Should().BeOfType<TrackingDbProvider>(
            "registrations from code run before the bridge's, and the bridge TryAdds");
    }

    [Test]
    public void ALegacyConnectionProviderTypeThatIsNotAProviderIsRejectedByName()
    {
        var properties = new NameValueCollection
        {
            ["quartz.jobStore.dataSource"] = "quartz",
            ["quartz.dataSource.quartz.connectionProvider.type"] = typeof(ConnectionProviderSeamTest).AssemblyQualifiedName,
        };

        var services = new ServiceCollection();
        var act = () => services.AddQuartz(properties);

        act.Should().Throw<SchedulerConfigException>().WithMessage("*does not implement IDbProvider*");
    }

    private class TrackingDbProvider : IDbProvider
    {
        public string ConnectionString => "";

        public DbMetadata Metadata { get; } = new();

        public DbCommand CreateCommand() => throw new NotSupportedException();

        public DbConnection CreateConnection() => throw new NotSupportedException();

        public void Shutdown()
        {
        }
    }

    private sealed class OtherDbProvider : TrackingDbProvider;
}
