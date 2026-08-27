using System.Collections.Specialized;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Quartz.Documentation.Samples.Packages;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/packages/redis.md.
/// </summary>
public static class RedisSamples
{
    public static void RedisLockHandler(IHostApplicationBuilder builder, string connectionString)
    {
        #region sample_redis_lock_handler

        builder.Services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.UseSqlServer(connectionString);
            store.UseSystemTextJsonSerializer();
            store.UseClustering();
            store.UseRedisLockHandler(redis =>
            {
                redis.RedisConfiguration = "redis-server:6379";
            });
        }));

        #endregion
    }

    public static void RedisLockHandlerOptions(IServiceCollection services)
    {
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            #region sample_redis_lock_handler_options

            store.UseRedisLockHandler(redis =>
            {
                redis.RedisConfiguration = "redis-server:6379";
                redis.LockTimeToLive = TimeSpan.FromSeconds(30);
                redis.LockRetryInterval = TimeSpan.FromMilliseconds(100);
            });

            #endregion
        }));
    }

    public static async ValueTask RedisFromProperties()
    {
        #region sample_redis_properties

        NameValueCollection properties = new()
        {
            ["quartz.jobStore.type"] = "Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz",
            ["quartz.jobStore.clustered"] = "true",
            ["quartz.jobStore.lockHandler.type"] = "Quartz.Extensions.Redis.RedisLockHandler, Quartz.Extensions.Redis",
            ["quartz.jobStore.lockHandler.redisConfiguration"] = "redis-server:6379",
            ["quartz.jobStore.lockHandler.lockTimeToLive"] = "30000"
        };

        await using StandaloneSchedulerFactory schedulerFactory = QuartzSchedulerBuilder.Create()
            .UseProperties(properties)
            .Build();

        #endregion
    }
}
