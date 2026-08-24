using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Quartz.Extensibility;
using Quartz.Impl.AdoJobStore;

namespace Quartz.Documentation.Samples.HowTos;

#region sample_lock_handler_semaphore

public sealed class LeaseSemaphore : ISemaphore
{
    private string schedulerName = "";

    public bool RequiresConnection => false;

    public void Initialize(SemaphoreContext context) => schedulerName = context.SchedulerName;

    public async ValueTask<bool> ObtainLock(
        Guid requestorId,
        ConnectionAndTransactionHolder? conn,
        SchedulerLock lockKind,
        CancellationToken cancellationToken = default)
    {
        // ... acquire, honouring the re-entry rule ...
        return true;
    }

    public ValueTask ReleaseLock(
        Guid requestorId,
        SchedulerLock lockKind,
        CancellationToken cancellationToken = default)
    {
        // ...
        return default;
    }
}

#endregion

/// <summary>
/// Samples for docs/documentation/quartz-4.x/how-tos/lock-handler.md.
/// </summary>
public static class LockHandlerSamples
{
    public static void Registration(IHostApplicationBuilder builder, string connectionString)
    {
        #region sample_lock_handler_registration

        builder.Services.AddQuartz(q =>
        {
            q.UsePersistentStore(s =>
            {
                s.UseLockHandler<LeaseSemaphore>();
                s.UseSqlServer(connectionString);
                s.UseClustering();
            });
        });

        #endregion
    }

    public static void RedisLockHandler(IPersistentStoreBuilder s)
    {
        #region sample_lock_handler_redis

        s.UseRedisLockHandler(o =>
        {
            o.RedisConfiguration = "localhost:6379";
            o.KeyPrefix = "quartz:";
            o.LockTimeToLive = TimeSpan.FromSeconds(30);
        });

        #endregion
    }
}
