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

using System;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Logging;

using StackExchange.Redis;

namespace Quartz.Impl.AdoJobStore;

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
/// quartz.jobStore.lockHandler.type = Quartz.Impl.AdoJobStore.RedisSemaphore, Quartz.Redis
/// quartz.jobStore.lockHandler.redisConfiguration = localhost:6379
/// </code>
/// </para>
/// </remarks>
public sealed class RedisSemaphore : ISemaphore, ITablePrefixAware
{
    private static readonly LuaScript ReleaseLockScript = LuaScript.Prepare(
        "if redis.call('get', @key) == @value then return redis.call('del', @key) else return 0 end");

    private readonly ResourceLock triggerLock = new();
    private readonly ResourceLock stateLock = new();
    private readonly ILog log;

    private IConnectionMultiplexer? redis;
    private readonly SemaphoreSlim connectionLock = new(1, 1);

    public RedisSemaphore()
    {
        log = LogProvider.GetLogger(GetType());
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
    public int LockTtlMilliseconds { get; set; } = 30_000;

    /// <summary>
    /// Gets or sets the polling interval in milliseconds between <c>SET NX</c> retry attempts.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>100</c> milliseconds.
    /// </remarks>
    public int LockRetryIntervalMilliseconds { get; set; } = 100;

    /// <summary>
    /// Table prefix (unused, but required by <see cref="ITablePrefixAware"/>
    /// so that <see cref="Quartz.Impl.StdSchedulerFactory"/> auto-injects <see cref="SchedName"/>).
    /// </summary>
    public string TablePrefix { get; set; } = "";

    /// <summary>
    /// Gets or sets the scheduler name used to namespace Redis lock keys.
    /// </summary>
    /// <remarks>
    /// Auto-injected by <see cref="Quartz.Impl.StdSchedulerFactory"/> when
    /// <see cref="ITablePrefixAware"/> is implemented.
    /// </remarks>
    public string? SchedName { get; set; }

    /// <inheritdoc />
    public bool RequiresConnection => false;

    /// <inheritdoc />
    public async Task<bool> ObtainLock(
        Guid requestorId,
        ConnectionAndTransactionHolder? conn,
        string lockName,
        CancellationToken cancellationToken = default)
    {
        bool isDebugEnabled = log.IsDebugEnabled();

        if (isDebugEnabled)
        {
            log.Debug($"Lock '{lockName}' is desired by: {requestorId}");
        }

        ResourceLock lockHandle = GetLock(lockName);

        if (lockHandle.IsLockOwner(requestorId))
        {
            if (isDebugEnabled)
            {
                log.Debug($"Lock '{lockName}' already owned by: {requestorId}");
            }

            return false;
        }

        if (isDebugEnabled)
        {
            log.Debug($"Lock '{lockName}' is being obtained: {requestorId}");
        }

        try
        {
            await lockHandle.Acquire(requestorId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (isDebugEnabled)
            {
                log.Debug($"Lock '{lockName}' was not obtained by: {requestorId} - cancelled");
            }

            return false;
        }

        try
        {
            IConnectionMultiplexer connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            IDatabase db = connection.GetDatabase();
            string key = BuildKey(lockName);
            string value = requestorId.ToString("N");
            TimeSpan ttl = TimeSpan.FromMilliseconds(LockTtlMilliseconds);
            TimeSpan retryInterval = TimeSpan.FromMilliseconds(LockRetryIntervalMilliseconds);

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool acquired = await db.StringSetAsync(key, value, ttl, When.NotExists).ConfigureAwait(false);
                if (acquired)
                {
                    if (isDebugEnabled)
                    {
                        log.Debug($"Lock '{lockName}' given to: {requestorId}");
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
                log.Debug($"Lock '{lockName}' was not obtained by: {requestorId} - cancelled");
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
    public async Task ReleaseLock(
        Guid requestorId,
        string lockName,
        CancellationToken cancellationToken = default)
    {
        ResourceLock lockHandle = GetLock(lockName);

        if (!lockHandle.IsLockOwner(requestorId))
        {
            if (log.IsWarnEnabled())
            {
                log.Warn($"Lock '{lockName}' attempt to return by: {requestorId} -- but not owner!");
                log.Warn("stack-trace of wrongful returner: " + Environment.StackTrace);
            }

            return;
        }

        try
        {
            IConnectionMultiplexer connection = await GetConnectionAsync().ConfigureAwait(false);
            IDatabase db = connection.GetDatabase();
            string key = BuildKey(lockName);
            string value = requestorId.ToString("N");

            await db.ScriptEvaluateAsync(
                ReleaseLockScript,
                new { key = (RedisKey) key, value = (RedisValue) value }).ConfigureAwait(false);

            if (log.IsDebugEnabled())
            {
                log.Debug($"Lock '{lockName}' returned by: {requestorId}");
            }
        }
        catch (Exception ex)
        {
            log.WarnException($"Failed to release Redis lock '{lockName}'", ex);
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
        if (redis != null)
        {
            return redis;
        }

        await connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (redis != null)
            {
                return redis;
            }

            log.Info("Connecting to Redis");
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
        if (string.Equals(lockName, JobStoreSupport.LockTriggerAccess, StringComparison.Ordinal))
        {
            return triggerLock;
        }

        if (string.Equals(lockName, JobStoreSupport.LockStateAccess, StringComparison.Ordinal))
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
            Guid? temp = owner;
            return temp != null && temp.Value == requestorId;
        }

        public async Task Acquire(Guid requestorId, CancellationToken cancellationToken)
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
