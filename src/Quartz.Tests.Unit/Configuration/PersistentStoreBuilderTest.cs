#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// The persistent store builder's own conventions: one name per concept, and a factory overload
/// wherever a component can be registered by type.
/// </summary>
public sealed class PersistentStoreBuilderTest
{
    private const string ConnectionString = "Server=nowhere;Database=quartz;";
    private const string ReportingConnectionString = "Server=nowhere;Database=reporting;";

    [Test]
    public void UseDataSource_WithAName_IsWhatTheStoreReadsItsConnectionUnder()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseDataSource("reporting-db");
            store.UseSqlServer(ConnectionString);
        }));

        using var provider = services.BuildServiceProvider();

        provider.GetSchedulerOptions<AdoJobStoreOptions>(null).DataSource.Should().Be("reporting-db",
            "naming a data source and defining one are the same concept, so the name overload has to "
            + "reach the same setting the callback overload does");
        provider.GetRequiredService<IOptionsMonitor<DataSourceOptions>>().Get("reporting-db")
            .ConnectionString.Should().Be(ConnectionString,
                "the database method configures DataSourceOptions under whatever name was chosen");
    }

    [Test]
    public void UseDataSource_WithNoName_FallsBackToTheSchedulersOwnName()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting", q => q.UsePersistentStore(store => store.UseSqlServer(ConnectionString)));

        using var provider = services.BuildServiceProvider();

        provider.GetSchedulerOptions<AdoJobStoreOptions>("reporting").DataSource.Should().Be("reporting",
            "a name that never has to be invented is the reason naming one is optional");
    }

    [Test]
    public void UseDataSource_WithANameAfterTheDatabaseChoice_SaysSo()
    {
        var services = new ServiceCollection();
        var act = () => services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseSqlServer(ConnectionString);
            store.UseDataSource("reporting-db");
        }));

        act.Should().Throw<SchedulerConfigException>().WithMessage("*already been configured*",
            "the name is what the connection provider is registered under, so renaming afterwards would "
            + "leave the store looking for settings nobody wrote");
    }

    [Test]
    public void UseDataSource_WithACallback_StillDefinesTheDataSource()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.UseDataSource(options =>
        {
            options.Provider = "SqlServer";
            options.ConnectionString = ConnectionString;
        })));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptionsMonitor<DataSourceOptions>>().Get("quartz")
            .Provider.Should().Be("SqlServer",
                "the two overloads are told apart by what they are handed, so neither may shadow the other");
    }

    [Test]
    public void UseDriverDelegate_WithAFactory_RunsTheFactory()
    {
        var built = new CountingDriverDelegate();

        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseDriverDelegate(_ => built);
            store.UseSqlServer(ConnectionString);
        }));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDriverDelegate>().Should().BeSameAs(built,
            "a dialect that needs building is the case the type-argument overload cannot serve");
    }

    [Test]
    public void UseDriverDelegate_WithAFactory_IsGivenTheSchedulersOwnProvider()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.UseSqlServer(ConnectionString)));
        services.AddQuartz("reporting", q => q.UsePersistentStore(store =>
        {
            store.UseDriverDelegate(scoped => new CountingDriverDelegate
            {
                Provider = scoped.GetRequiredService<IDbProvider>(),
            });
            store.UseSqlServer(ReportingConnectionString);
        }));

        using var container = services.BuildServiceProvider();

        container.GetRequiredKeyedService<IDriverDelegate>("reporting").Should().BeOfType<CountingDriverDelegate>()
            .Which.Provider!.ConnectionString.Should().Be(ReportingConnectionString,
                "the factory runs against a provider keyed to this scheduler, so a named scheduler's "
                + "delegate is handed its own database rather than the default scheduler's");
    }

    [Test]
    public void UseDriverDelegate_WithAFactory_RefusesNull()
    {
        var services = new ServiceCollection();
        var act = () => services.AddQuartz(q => q.UsePersistentStore(store => store.UseDriverDelegate(factory: null!)));

        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void ProvisionSchema_TurnsCreateIfMissingOn()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseSqlServer(ConnectionString);
            store.ProvisionSchema();
        }));

        using var provider = services.BuildServiceProvider();

        provider.GetSchedulerOptions<AdoJobStoreOptions>(null).SchemaProvisioning
            .Should().Be(SchemaProvisioning.CreateIfMissing,
                "the shorthand exists to spell the decision, so it has to reach the same setting "
                + "ConfigureStore does");
    }

    [Test]
    public void ProvisionSchema_IsNotWhatAStoreDoesUnasked()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.UseSqlServer(ConnectionString)));

        using var provider = services.BuildServiceProvider();

        provider.GetSchedulerOptions<AdoJobStoreOptions>(null).SchemaProvisioning
            .Should().Be(SchemaProvisioning.Validate,
                "creating tables needs DDL permission a production database is usually right not to "
                + "grant, so provisioning is asked for rather than assumed");
    }

    [Test]
    public void ProvisionSchema_AppliesToTheSchedulerThatAskedForIt()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.UseSqlServer(ConnectionString)));
        services.AddQuartz("reporting", q => q.UsePersistentStore(store =>
        {
            store.UseSqlServer(ReportingConnectionString);
            store.ProvisionSchema();
        }));

        using var provider = services.BuildServiceProvider();

        provider.GetSchedulerOptions<AdoJobStoreOptions>("reporting").SchemaProvisioning
            .Should().Be(SchemaProvisioning.CreateIfMissing);
        provider.GetSchedulerOptions<AdoJobStoreOptions>(null).SchemaProvisioning
            .Should().Be(SchemaProvisioning.Validate,
                "a named scheduler's options are keyed by its name, so one scheduler granted DDL does "
                + "not hand it to every other scheduler in the container");
    }

    [Test]
    public void UseAmbientTransactions_BuildsTheStoreThatRunsInSomebodyElsesTransaction()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseSqlServer(ConnectionString);
            store.UseAmbientTransactions();
        }));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IJobStore>().Should().BeOfType<ExternalTransactionJobStore>(
            "the store that neither commits nor rolls back is one of the two Quartz ships, and this is "
            + "the only way to choose it in code now that the type itself is internal");
    }

    [Test]
    public void UsePersistentStore_WithoutIt_StillBuildsTheStoreThatManagesItsOwnTransaction()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.UseSqlServer(ConnectionString)));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IJobStore>().Should().BeOfType<LocalTransactionJobStore>(
            "a store that commits its own work is what nearly everybody wants, so adding a way to ask "
            + "for the other one must not change what asking for nothing means");
    }

    [Test]
    public void UseAmbientTransactions_AppliesToTheSchedulerThatAskedForIt()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store => store.UseSqlServer(ConnectionString)));
        services.AddQuartz("reporting", q => q.UsePersistentStore(store =>
        {
            store.UseSqlServer(ReportingConnectionString);
            store.UseAmbientTransactions();
        }));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<IJobStore>("reporting").Should().BeOfType<ExternalTransactionJobStore>();
        provider.GetRequiredService<IJobStore>().Should().BeOfType<LocalTransactionJobStore>(
            "the store is registered under the scheduler's own key, so one scheduler handing its "
            + "transactions to a container does not hand every scheduler's over");
    }

    [Test]
    public void UseAmbientTransactions_InsideAStoreNamedByItsType_SaysSo()
    {
        var services = new ServiceCollection();
        var act = () => services.AddQuartz(q => q.UsePersistentStore<LocalTransactionJobStore>(store =>
        {
            store.UseSqlServer(ConnectionString);
            store.UseAmbientTransactions();
        }));

        act.Should().Throw<SchedulerConfigException>().WithMessage("*UseAmbientTransactions*",
            "the type argument and the selector name different stores, and keeping the type argument "
            + "silently would leave a scheduler committing transactions its caller believed somebody "
            + "else owned");
    }

    private sealed class CountingDriverDelegate : StdAdoDelegate
    {
        public IDbProvider? Provider { get; init; }
    }
}
