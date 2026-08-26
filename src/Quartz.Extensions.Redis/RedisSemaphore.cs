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

namespace Quartz.Extensions.Redis;

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
/// quartz.jobStore.lockHandler.type = Quartz.Extensions.Redis.RedisSemaphore, Quartz.Extensions.Redis
/// quartz.jobStore.lockHandler.redisConfiguration = localhost:6379
/// </code>
/// </para>
/// </remarks>
public sealed class RedisSemaphore : ISemaphore
{
    // The Redis key keeps the stored lock names rather than the enum member names, so that a rolling
    // upgrade and a mixed-version cluster keep contending for the same key.
    private const string LockTriggerAccess = "TRIGGER_ACCESS";
    private const string LockStateAccess = "STATE_ACCESS";

    private static readonly LuaScript ReleaseLockScript = LuaScript.Prepare(
        "if redis.call('get', @key) == @value then return redis.call('del', @key) else return 0 end");

    private readonly ResourceLock triggerLock = new();
    private readonly ResourceLock stateLock = new();
    private ILogger<RedisSemaphore> logger = LogProvider.CreateLogger<RedisSemaphore>();

    private IConnectionMultiplexer? redis;
    private readonly SemaphoreSlim connectionLock = new(1, 1);
    private TimeSpan lockTimeToLive = TimeSpan.FromSeconds(30);
    private TimeSpan lockRetryInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Gets or sets the StackExchange.Redis configuration string.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>"localhost:6379"</c>.
    /// </remarks>
    public string RedisConfiguration { get; internal set; } = "localhost:6379";

    /// <summary>
    /// Gets or sets the prefix for Redis lock keys.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>"quartz:lock:"</c>. The full key format is
    /// <c>{KeyPrefix}{SchedulerName}:{lockName}</c>.
    /// </remarks>
    public string KeyPrefix { get; internal set; } = "quartz:lock:";

    /// <summary>
    /// Gets or sets the lock time-to-live.
    /// </summary>
    /// <remarks>
    /// Defaults to 30 seconds. The lock automatically expires after this
    /// duration, allowing recovery when a node crashes while holding a lock.
    /// </remarks>
    [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
    public TimeSpan LockTimeToLive
    {
        get => lockTimeToLive;
        internal set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            lockTimeToLive = value;
        }
    }

    /// <summary>
    /// Gets or sets the polling interval between <c>SET NX</c> retry attempts.
    /// </summary>
    /// <remarks>
    /// Defaults to 100 milliseconds.
    /// </remarks>
    [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
    public TimeSpan LockRetryInterval
    {
        get => lockRetryInterval;
        internal set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            lockRetryInterval = value;
        }
    }

    /// <summary>
    /// Gets the scheduler name used to namespace Redis lock keys.
    /// </summary>
    /// <remarks>
    /// Told to the semaphore by the job store through <see cref="Initialize"/>.
    /// </remarks>
    public string? SchedulerName { get; private set; }

    /// <inheritdoc />
    public void Initialize(SemaphoreContext context)
    {
        SchedulerName = context.SchedulerName;

        // Lock contention and expiry are what this handler has to say, and they are of no use to an
        // application that has to set a static before it can hear them. Until the store calls this, the
        // ambient factory still answers.
        logger = context.LoggerFactory.CreateLogger<RedisSemaphore>();
    }

    /// <inheritdoc />
    public bool RequiresConnection => false;

    /// <inheritdoc />
    public async ValueTask<bool> ObtainLock(
        Guid requestorId,
        ConnectionAndTransactionHolder? conn,
        SchedulerLock lockKind,
        CancellationToken cancellationToken = default)
    {
        var isDebugEnabled = logger.IsEnabled(LogLevel.Debug);
        var lockName = LockName(lockKind);

        if (isDebugEnabled)
        {
            logger.LockDesired(lockName, requestorId);
        }

        var lockHandle = GetLock(lockKind);

        if (lockHandle.IsLockOwner(requestorId))
        {
            if (isDebugEnabled)
            {
                logger.LockAlreadyOwned(lockName, requestorId);
            }

            return false;
        }

        if (isDebugEnabled)
        {
            logger.LockBeingObtained(lockName, requestorId);
        }

        try
        {
            await lockHandle.Acquire(requestorId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (isDebugEnabled)
            {
                logger.LockNotObtainedCancelled(lockName, requestorId);
            }

            return false;
        }

        try
        {
            var connection = await GetConnectionAsync(cancellationToken).ConfigureAwait(false);
            var db = connection.GetDatabase();
            var key = BuildKey(lockName);
            var value = requestorId.ToString("N");
            TimeSpan ttl = LockTimeToLive;
            TimeSpan retryInterval = LockRetryInterval;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var acquired = await db.StringSetAsync(key, value, ttl, When.NotExists).ConfigureAwait(false);
                if (acquired)
                {
                    if (isDebugEnabled)
                    {
                        logger.LockGiven(lockName, requestorId);
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
                logger.LockNotObtainedCancelled(lockName, requestorId);
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
        SchedulerLock lockKind,
        CancellationToken cancellationToken = default)
    {
        var lockName = LockName(lockKind);
        var lockHandle = GetLock(lockKind);

        if (!lockHandle.IsLockOwner(requestorId))
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                logger.LockReturnedByNonOwner(lockName, requestorId);
                logger.WrongfulReturnerStack(Environment.StackTrace);
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
                logger.LockReturned(lockName, requestorId);
            }
        }
        catch (Exception ex)
        {
            logger.LockReleaseFailed(lockName, ex);
        }
        finally
        {
            lockHandle.Release();
        }
    }

    private string BuildKey(string lockName)
    {
        if (!string.IsNullOrEmpty(SchedulerName))
        {
            return $"{KeyPrefix}{SchedulerName}:{lockName}";
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

            logger.ConnectingToRedis();
            redis = await ConnectionMultiplexer.ConnectAsync(RedisConfiguration).ConfigureAwait(false);
            return redis;
        }
        finally
        {
            connectionLock.Release();
        }
    }

    private ResourceLock GetLock(SchedulerLock lockKind) => lockKind switch
    {
        SchedulerLock.TriggerAccess => triggerLock,
        SchedulerLock.StateAccess => stateLock,
        _ => throw new NotSupportedException($"Unsupported lock: {lockKind}")
    };

    private static string LockName(SchedulerLock lockKind) => lockKind switch
    {
        SchedulerLock.TriggerAccess => LockTriggerAccess,
        SchedulerLock.StateAccess => LockStateAccess,
        _ => throw new NotSupportedException($"Unsupported lock: {lockKind}")
    };

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
