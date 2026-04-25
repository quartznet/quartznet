using Quartz.Impl;
using Quartz.Impl.Redis;
using Quartz.Util;

namespace Quartz;

/// <summary>
/// Extension methods for configuring <see cref="RedisSemaphore"/> as the lock handler
/// for persistent job stores.
/// </summary>
public static class RedisLockHandlerConfigurationExtensions
{
    /// <summary>
    /// Use Redis-based distributed lock handler for clustered scheduling.
    /// This replaces database row locks with Redis distributed locks using
    /// <c>SET NX PX</c>.
    /// </summary>
    /// <param name="persistentStoreOptions">The persistent store options to configure.</param>
    /// <param name="configure">Optional callback to configure Redis lock handler options.</param>
    public static void UseRedisLockHandler(
        this SchedulerBuilder.PersistentStoreOptions persistentStoreOptions,
        Action<RedisLockHandlerOptions>? configure = null)
    {
        persistentStoreOptions.SetProperty(
            StdSchedulerFactory.PropertyJobStoreLockHandlerType,
            typeof(RedisSemaphore).AssemblyQualifiedNameWithoutVersion());

        var options = new RedisLockHandlerOptions();
        configure?.Invoke(options);

        if (options.RedisConfiguration is not null)
        {
            persistentStoreOptions.SetProperty(
                "quartz.jobStore.lockHandler.redisConfiguration",
                options.RedisConfiguration);
        }

        if (options.KeyPrefix is not null)
        {
            persistentStoreOptions.SetProperty(
                "quartz.jobStore.lockHandler.keyPrefix",
                options.KeyPrefix);
        }

        if (options.LockTtlMilliseconds.HasValue)
        {
            persistentStoreOptions.SetProperty(
                "quartz.jobStore.lockHandler.lockTtlMilliseconds",
                options.LockTtlMilliseconds.Value.ToString());
        }

        if (options.LockRetryIntervalMilliseconds.HasValue)
        {
            persistentStoreOptions.SetProperty(
                "quartz.jobStore.lockHandler.lockRetryIntervalMilliseconds",
                options.LockRetryIntervalMilliseconds.Value.ToString());
        }
    }
}

/// <summary>
/// Options for configuring the Redis-based lock handler.
/// </summary>
public sealed class RedisLockHandlerOptions
{
    /// <summary>
    /// Gets or sets the StackExchange.Redis configuration string.
    /// </summary>
    /// <remarks>
    /// When not set, defaults to <c>"localhost:6379"</c>.
    /// </remarks>
    public string? RedisConfiguration { get; set; }

    /// <summary>
    /// Gets or sets the prefix for Redis lock keys.
    /// </summary>
    /// <remarks>
    /// When not set, defaults to <c>"quartz:lock:"</c>.
    /// </remarks>
    public string? KeyPrefix { get; set; }

    /// <summary>
    /// Gets or sets the lock TTL (time-to-live) in milliseconds.
    /// </summary>
    /// <remarks>
    /// When not set, defaults to <c>30000</c> (30 seconds).
    /// </remarks>
    public int? LockTtlMilliseconds { get; set; }

    /// <summary>
    /// Gets or sets the polling interval in milliseconds between <c>SET NX</c> retry attempts.
    /// </summary>
    /// <remarks>
    /// When not set, defaults to <c>100</c> milliseconds.
    /// </remarks>
    public int? LockRetryIntervalMilliseconds { get; set; }
}
