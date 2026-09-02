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
    private readonly ConcurrentDictionary<string, string> files = new(StringComparer.Ordinal);
    private readonly string prefix;

    public SqliteStores(string prefix) => this.prefix = prefix;

    public void Configure(IQuartzBuilder builder, string schedulerName)
    {
        string file = files.GetOrAdd(
            schedulerName,
            _ => Path.Combine(Path.GetTempPath(), $"quartz-{prefix}-{Guid.NewGuid():N}.db"));

        builder.UsePersistentStore(store =>
        {
            store.UseSqlite(SqliteFactory.Instance, $"Data Source={file}");
            store.ProvisionSchema();
        });
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        foreach (string file in files.Values)
        {
            if (File.Exists(file))
            {
                try
                {
                    File.Delete(file);
                }
                catch (IOException)
                {
                    // A file the store has not finished letting go of is a temp file, not a failure.
                }
            }
        }

        files.Clear();
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
