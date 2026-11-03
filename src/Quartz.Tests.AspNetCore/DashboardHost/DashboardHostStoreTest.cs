using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Dashboard.Host;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;

using HostStores = Quartz.Dashboard.Host.DashboardHost;

namespace Quartz.Tests.AspNetCore.DashboardHost;

/// <summary>
/// What a store-attached target's settings make of the window's store: the dialect of each of the six
/// databases, through the driver the host carries, the table prefix, the serializer and the history.
/// </summary>
/// <remarks>
/// Read off the store a window is built with, without connecting to anything: every provider but SQLite
/// would need a server, and what is under test is which one the settings choose.
/// </remarks>
public sealed class DashboardHostStoreTest
{
    [TestCase("SqlServer", "Server=db;Database=quartz;User Id=quartz;Password=p", typeof(SqlServerDelegate))]
    [TestCase("Npgsql", "Host=db;Database=quartz;Username=quartz;Password=p", typeof(PostgreSQLDelegate))]
    [TestCase("MySqlConnector", "Server=db;Port=3306;Database=quartz;Uid=quartz;Pwd=p", typeof(MySQLDelegate))]
    [TestCase("OracleODPManaged", "Data Source=db:1521/quartz;User Id=quartz;Password=p", typeof(OracleDelegate))]
    [TestCase("SQLite-Microsoft", "Data Source=quartz.db", typeof(SQLiteDelegate))]
    [TestCase("Firebird", "DataSource=db;Database=quartz;User=quartz;Password=p", typeof(FirebirdDelegate))]
    public void EachProviderGetsItsOwnDialect(string provider, string connectionString, Type dialect)
    {
        using ServiceProvider window = Window(new StoreTargetPlan("orders", provider, connectionString, TablePrefix: null, "SystemTextJson", ExecutionHistory: false, RediscoveryInterval: null));

        window.GetRequiredService<IDriverDelegate>().Should().BeOfType(dialect);
        window.GetRequiredService<IObjectSerializer>().Should().BeOfType<SystemTextJsonObjectSerializer>();
        window.GetRequiredService<IOptions<AdoJobStoreOptions>>().Value.TablePrefix.Should().Be(AdoConstants.DefaultTablePrefix);
    }

    [Test]
    public void TheTablePrefixSerializerAndHistoryAreTheNodesOwn()
    {
        using ServiceProvider window = Window(new StoreTargetPlan("orders", "Npgsql", "Host=db;Database=quartz", "BILLING_", "Newtonsoft", ExecutionHistory: true, RediscoveryInterval: null));

        window.GetRequiredService<IOptions<AdoJobStoreOptions>>().Value.TablePrefix.Should().Be("BILLING_");
        window.GetRequiredService<IObjectSerializer>().Should().BeOfType<NewtonsoftJsonObjectSerializer>();
        window.GetRequiredService<IExecutionHistoryStore>().Should().NotBeOfType<InMemoryExecutionHistoryStore>(
            "ExecutionHistory reads the history the nodes keep in the database");
    }

    [Test]
    public void AProviderTheHostHasNoDriverForIsAPlanThatCannotBeBuilt()
    {
        Action build = () => Window(new StoreTargetPlan("orders", "Db2", "Server=db", null, "SystemTextJson", false, null)).Dispose();

        build.Should().Throw<InvalidOperationException>().WithMessage("*No driver for provider 'Db2'*");
    }

    private static ServiceProvider Window(StoreTargetPlan target)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddQuartz(quartz => quartz.UsePersistentStore(store => HostStores.ConfigureStore(store, target)));

        ServiceProvider provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IOptions<AdoJobStoreOptions>>().Value;
        return provider;
    }
}
