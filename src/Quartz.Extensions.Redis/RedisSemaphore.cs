#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;
using Quartz.Impl.AdoJobStore;

using StackExchange.Redis;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Impl.Redis;

/// <summary>
/// A Redis-based <see cref="ISemaphore"/> that uses distributed locks
/// (<c>SET NX PX</c>) instead of database row locks.
/// </summary>
/// <remarks>
/// <para>
/// This lock handler is designed for clustered Quartz.NET setups where jobs are stored
/// in a relational database but lock contention on the <c>QRTZ_LOCKS</c> table causes
/// deadlocks or performance issues under heavy scheduling load.
/// </para>
/// <para>
/// The implementation uses a two-tier locking strategy: a local <see cref="SemaphoreSlim"/>
/// prevents redundant Redis round-trips within the same process, and a Redis
/// <c>SET key value NX PX timeout</c> command provides the cross-node distributed lock.
/// </para>
/// <para>
/// Configure via properties:
/// <code>
/// quartz.jobStore.lockHandler.type = Quartz.Impl.Redis.RedisSemaphore, Quartz.Extensions.Redis
/// quartz.jobStore.lockHandler.redisConfiguration = localhost:6379
/// </code>
/// </para>
/// </remarks>
public sealed class RedisSemaphore : ISemaphore, ITablePrefixAware
{
    private const string LockTriggerAccess = "TRIGGER_ACCESS";
    private const string LockStateAccess = "STATE_ACCESS";

    private static readonly LuaScript ReleaseLockScript = LuaScript.Prepare(
        "if redis.call('get', @key) == @value then return redis.call('del', @key) else return 0 end");

    private readonly ResourceLock triggerLock = new();
    private readonly ResourceLock stateLock = new();
    private readonly ILogger<RedisSemaphore> logger;

    private IConnectionMultiplexer? redis;
    private readonly SemaphoreSlim connectionLock = new(1, 1);
    private int lockTtlMilliseconds = 30_000;
    private int lockRetryIntervalMilliseconds = 100;

    public RedisSemaphore()
    {
        logger = LogProvider.CreateLogger<RedisSemaphore>();
    }

    /// <summary>
    /// Gets or sets the StackExchange.Redis configuration string.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>"localhost:6379"</c>.
    /// </remarks>
    public string RedisConfiguration { get; set; } = "localhost:6379";

    /// <summary>
    /// Gets or sets the prefix for Redis lock keys.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>"quartz:lock:"</c>. The full key format is
    /// <c>{KeyPrefix}{SchedName}:{lockName}</c>.
    /// </remarks>
    public string KeyPrefix { get; set; } = "quartz:lock:";

    /// <summary>
    /// Gets or sets the lock TTL (time-to-live) in milliseconds.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>30000</c> (30 seconds). The lock automatically expires after this
    /// duration, allowing recovery when a node crashes while holding a lock.
    /// </remarks>
    public int LockTtlMilliseconds
    {
        get => lockTtlMilliseconds;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            lockTtlMilliseconds = value;
        }
    }

    /// <summary>
    /// Gets or sets the polling interval in milliseconds between <c>SET NX</c> retry attempts.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>100</c> milliseconds.
    /// </remarks>
    public int LockRetryIntervalMilliseconds
    {
        get => lockRetryIntervalMilliseconds;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            lockRetryIntervalMilliseconds = value;
        }
    }

    /// <summary>
    /// Table prefix (unused, but required by <see cref="ITablePrefixAware"/>
    /// so that <see cref="StdSchedulerFactory"/> auto-injects <see cref="SchedName"/>).
    /// </summary>
    public string TablePrefix { get; set; } = "";

    /// <summary>
    /// Gets or sets the scheduler name used to namespace Redis lock keys.
    /// </summary>
    /// <remarks>
    /// Auto-injected by <see cref="StdSchedulerFactory"/> when
    /// <see cref="ITablePrefixAware"/> is implemented.
    /// </remarks>
    public string? SchedName { get; set; }

    /// <inheritdoc />
    public bool RequiresConnection => false;

    /// <inheritdoc />
    public async ValueTask<bool> ObtainLock(
        Guid requestorId,
        ConnectionAndTransactionHolder? conn,
        string lockName,
        CancellationToken cancellationToken = default)
    {
        var isDebugEnabled = logger.IsEnabled(LogLevel.Debug);

        if (isDebugEnabled)
        {
            logger.LogDebug("Lock '{LockName}' is desired by: {RequestorId}", lockName, requestorId);
        }

        var lockHandle = GetLock(lockName);

        if (lockHandle.IsLockOwner(requestorId))
        {
            if (isDebugEnabled)
            {
                logger.LogDebug("Lock '{LockName}' already owned by: {RequestorId}", lockName, requestorId);
            }

            return false;
        }

        if (isDebugEnabled)
        {
            logger.LogDebug("Lock '{LockName}' is being obtained: {RequestorId}", lockName, requestorId);
        }

        try
        {
            await lockHandle.Acquire(requestorId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (isDebugEnabled)
            {
                logger.LogDebug("Lock '{LockName}' was not obtained by: {RequestorId} - cancelled", lockName, requestorId);
            }

            return false;
        }

        try
        {
            var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            var db = connection.GetDatabase();
            var key = BuildKey(lockName);
            var value = requestorId.ToString("N");
            var ttl = TimeSpan.FromMilliseconds(LockTtlMilliseconds);
            var retryInterval = TimeSpan.FromMilliseconds(LockRetryIntervalMilliseconds);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var acquired = await db.StringSetAsync(key, value, ttl, When.NotExists).ConfigureAwait(false);
                if (acquired)
                {
                    if (isDebugEnabled)
                    {
                        logger.LogDebug("Lock '{LockName}' given to: {RequestorId}", lockName, requestorId);
                    }

                    return true;
                }

                await Task.Delay(retryInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            lockHandle.Release();

            if (isDebugEnabled)
            {
                logger.LogDebug("Lock '{LockName}' was not obtained by: {RequestorId} - cancelled", lockName, requestorId);
            }

            return false;
        }
        catch (Exception ex)
        {
            lockHandle.Release();
            throw new LockException($"Failed to obtain Redis lock '{lockName}'", ex);
        }
    }

    /// <inheritdoc />
    public async ValueTask ReleaseLock(
        Guid requestorId,
        string lockName,
        CancellationToken cancellationToken = default)
    {
        var lockHandle = GetLock(lockName);

        if (!lockHandle.IsLockOwner(requestorId))
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogWarning("Lock '{LockName}' attempt to return by: {RequestorId} -- but not owner!", lockName, requestorId);
                logger.LogWarning("stack-trace of wrongful returner: {StackTrace}", Environment.StackTrace);
            }

            return;
        }

        try
        {
            var connection = await GetConnectionAsync(CancellationToken.None).ConfigureAwait(false);
            var db = connection.GetDatabase();
            var key = BuildKey(lockName);
            var value = requestorId.ToString("N");

            await db.ScriptEvaluateAsync(
                ReleaseLockScript,
                new { key = (RedisKey) key, value = (RedisValue) value }).ConfigureAwait(false);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Lock '{LockName}' returned by: {RequestorId}", lockName, requestorId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to release Redis lock '{LockName}'", lockName);
        }
        finally
        {
            lockHandle.Release();
        }
    }

    private string BuildKey(string lockName)
    {
        if (!string.IsNullOrEmpty(SchedName))
        {
            return $"{KeyPrefix}{SchedName}:{lockName}";
        }

        return $"{KeyPrefix}{lockName}";
    }

    private async Task<IConnectionMultiplexer> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (redis is not null)
        {
            return redis;
        }

        await connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (redis is not null)
            {
                return redis;
            }

            logger.LogInformation("Connecting to Redis");
            redis = await ConnectionMultiplexer.ConnectAsync(RedisConfiguration).ConfigureAwait(false);
            return redis;
        }
        finally
        {
            connectionLock.Release();
        }
    }

    private ResourceLock GetLock(string lockName)
    {
        if (string.Equals(lockName, LockTriggerAccess, StringComparison.Ordinal))
        {
            return triggerLock;
        }

        if (string.Equals(lockName, LockStateAccess, StringComparison.Ordinal))
        {
            return stateLock;
        }

        throw new NotSupportedException($"Unsupported lock name: {lockName}");
    }

    private sealed class ResourceLock
    {
        private readonly SemaphoreSlim semaphore = new(1, 1);
        private Guid? owner;

        public bool IsLockOwner(Guid requestorId)
        {
            var temp = owner;
            return temp is not null && temp.Value == requestorId;
        }

        public async ValueTask Acquire(Guid requestorId, CancellationToken cancellationToken)
        {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            owner = requestorId;
        }

        public void Release()
        {
            owner = null;
            semaphore.Release();
        }
    }
}
