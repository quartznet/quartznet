#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// The persistent store builder's own conventions: one name per concept, and a factory overload
/// wherever a component can be registered by type.
/// </summary>
public sealed class PersistentStoreBuilderTest
{
    private const string ConnectionString = "Server=nowhere;Database=quartz;";

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
}
