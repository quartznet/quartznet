using System.Collections.Concurrent;

using Microsoft.Data.Sqlite;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// A SQLite file per scheduler, created and removed by the fixture that asked for it.
/// </summary>
/// <remarks>
/// A file rather than <c>:memory:</c>, because an in-memory SQLite database belongs to its connection
/// and the store opens one per operation — the second call would find an empty schema. The store creates
/// the tables itself, so no script is needed; see #3550.
/// </remarks>
internal sealed class SqliteStores : IDisposable
{
    private readonly ConcurrentDictionary<string, SqliteTestDatabase> databases = new(StringComparer.Ordinal);
    private readonly string prefix;

    public SqliteStores(string prefix) => this.prefix = prefix;

    public void Configure(IQuartzBuilder builder, string schedulerName)
    {
        SqliteTestDatabase database = databases.GetOrAdd(schedulerName, _ => new SqliteTestDatabase(prefix));

        builder.UsePersistentStore(store =>
        {
            store.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
            store.ProvisionSchema();
        });
    }

    public void Dispose()
    {
        foreach (SqliteTestDatabase database in databases.Values)
        {
            database.Dispose();
        }

        databases.Clear();
    }
}

/// <summary>
/// Every assertion <see cref="SchedulerAuthorizationEndpointTest" /> makes, over <c>RAMJobStore</c>.
/// </summary>
[NonParallelizable]
public sealed class SchedulerAuthorizationEndpointInMemoryStoreTest : SchedulerAuthorizationEndpointTest;

/// <summary>
/// The same assertions, over a persistent store.
/// </summary>
/// <remarks>
/// The per-scheduler authorization contract was tested over <c>RAMJobStore</c> alone, so nothing said
/// whether it held for the store most deployments that need multi-tenancy actually run. It is the same
/// tests: what changes is what is underneath them.
/// </remarks>
[NonParallelizable]
public sealed class SchedulerAuthorizationEndpointPersistentStoreTest : SchedulerAuthorizationEndpointTest
{
    private readonly SqliteStores stores = new("api-authorization");
    private int scheduler;

    protected override void ConfigureStore(IQuartzBuilder builder)
    {
        // The builder does not carry the scheduler's name here, and each tenant needs its own database:
        // two schedulers sharing one would share QRTZ_LOCKS and every row in it.
        stores.Configure(builder, $"tenant-{Interlocked.Increment(ref scheduler)}");
    }

    [OneTimeTearDown]
    public void DeleteDatabases() => stores.Dispose();
}

/// <summary>
/// Every assertion <see cref="JobTypeAllowListTest" /> makes, over <c>RAMJobStore</c>.
/// </summary>
[NonParallelizable]
public sealed class JobTypeAllowListInMemoryStoreTest : JobTypeAllowListTest;

/// <summary>
/// The same assertions, over a persistent store.
/// </summary>
/// <remarks>
/// "Nothing was stored" is what a refusal has to mean, and the store that has to mean it in production is
/// the one a deployment with an HTTP API in front of it runs. It is the same tests: what changes is what
/// is underneath them.
/// </remarks>
[NonParallelizable]
public sealed class JobTypeAllowListPersistentStoreTest : JobTypeAllowListTest
{
    private readonly SqliteStores stores = new("api-job-type-allow-list");
    private int scheduler;

    protected override void ConfigureStore(IQuartzBuilder builder)
    {
        stores.Configure(builder, $"scheduler-{Interlocked.Increment(ref scheduler)}");
    }

    [OneTimeTearDown]
    public void DeleteDatabases() => stores.Dispose();
}

/// <summary>
/// Every assertion <see cref="TenantSchedulerRoutingTest" /> makes, over <c>RAMJobStore</c>.
/// </summary>
[NonParallelizable]
public sealed class TenantSchedulerRoutingInMemoryStoreTest : TenantSchedulerRoutingTest;

/// <inheritdoc cref="SchedulerAuthorizationEndpointPersistentStoreTest" />
[NonParallelizable]
public sealed class TenantSchedulerRoutingPersistentStoreTest : TenantSchedulerRoutingTest
{
    private readonly SqliteStores stores = new("tenant-routing");
    private int scheduler;

    protected override void ConfigureStore(IQuartzBuilder builder)
    {
        stores.Configure(builder, $"tenant-{Interlocked.Increment(ref scheduler)}");
    }

    [OneTimeTearDown]
    public void DeleteDatabases() => stores.Dispose();
}
