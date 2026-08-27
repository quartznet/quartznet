using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl.AdoJobStore;
using Quartz.Extensions.Redis;

namespace Quartz;

/// <summary>
/// Configures <see cref="RedisLockHandler"/> as the lock handler for a persistent job store.
/// </summary>
public static class RedisLockHandlerConfigurationExtensions
{
    /// <summary>
    /// Coordinates clustered schedulers with Redis distributed locks rather than database row locks.
    /// </summary>
    /// <param name="builder">The persistent store being configured.</param>
    /// <param name="configure">Optional configuration for the Redis lock handler.</param>
    public static IPersistentStoreBuilder UseRedisLockHandler(
        this IPersistentStoreBuilder builder,
        Action<RedisLockHandlerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new RedisLockHandlerOptions();
        configure?.Invoke(options);

        builder.UseLockHandler(provider =>
        {
            var lockHandler = ActivatorUtilities.CreateInstance<RedisLockHandler>(provider);
            if (options.RedisConfiguration is not null)
            {
                lockHandler.RedisConfiguration = options.RedisConfiguration;
            }

            if (options.KeyPrefix is not null)
            {
                lockHandler.KeyPrefix = options.KeyPrefix;
            }

            if (options.LockTimeToLive.HasValue)
            {
                lockHandler.LockTimeToLive = options.LockTimeToLive.Value;
            }

            if (options.LockRetryInterval.HasValue)
            {
                lockHandler.LockRetryInterval = options.LockRetryInterval.Value;
            }

            return lockHandler;
        });

        return builder;
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
    /// Gets or sets the lock time-to-live.
    /// </summary>
    /// <remarks>
    /// When not set, defaults to 30 seconds.
    /// </remarks>
    public TimeSpan? LockTimeToLive { get; set; }

    /// <summary>
    /// Gets or sets the polling interval between <c>SET NX</c> retry attempts.
    /// </summary>
    /// <remarks>
    /// When not set, defaults to 100 milliseconds.
    /// </remarks>
    public TimeSpan? LockRetryInterval { get; set; }
}
