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

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// In-memory semaphore for SQLite that uses a single global lock to serialize
/// all database access. SQLite only supports one writer at a time and concurrent
/// serializable transactions cause "database is locked" errors. This semaphore
/// ensures only one operation accesses the database at a time by using a single
/// <see cref="SemaphoreSlim"/> regardless of lock name.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="SimpleSemaphore"/> which uses separate locks per lock name
/// (allowing concurrent access with different lock names), this semaphore uses a
/// single global gate. This prevents the scenario where Thread A holds TRIGGER_ACCESS
/// and Thread B holds STATE_ACCESS, both with open serializable transactions that
/// cause SQLite contention.
/// </para>
/// <para>
/// This semaphore does not require a database connection (<see cref="RequiresConnection"/>
/// returns <c>false</c>), which is critical: it allows <see cref="AdoJobStoreBase"/>
/// to acquire the lock before opening a connection/transaction, eliminating the
/// chicken-and-egg problem where a serializable transaction was needed just to
/// acquire a database-based lock.
/// </para>
/// </remarks>
/// <author>Marko Lahma</author>
internal sealed class SQLiteSemaphore : ISemaphore
{
    private readonly SemaphoreSlim globalLock = new(1, 1);
    private readonly Lock syncRoot = new();
    private Guid? currentOwner;
    private int lockCount;

    private ILogger<SQLiteSemaphore> logger = LogProvider.CreateLogger<SQLiteSemaphore>();

    /// <summary>
    /// Takes the logger from the job store's factory, so the global gate's contention is visible to an
    /// application that never set <see cref="LogProvider" />. Until the store calls this — a handler
    /// constructed and used directly — the ambient factory is still what answers.
    /// </summary>
    public void Initialize(SemaphoreContext context)
    {
        logger = context.LoggerFactory.CreateLogger<SQLiteSemaphore>();
    }

    /// <summary>
    /// Grants a lock on the identified resource to the calling thread (blocking
    /// until it is available).
    /// </summary>
    /// <returns>True if the lock was obtained.</returns>
    public ValueTask<bool> ObtainLock(
        Guid requestorId,
        ConnectionAndTransactionHolder? conn,
        SchedulerLock lockKind,
        CancellationToken cancellationToken = default)
    {
        var isDebugEnabled = logger.IsEnabled(LogLevel.Debug);
        var lockName = lockKind.ToLockName();

        if (isDebugEnabled)
        {
            logger.LockDesired(lockName, requestorId);
        }

        // Fast path: re-entrant acquisition by the same requestor avoids
        // the async state machine allocation entirely.
        lock (syncRoot)
        {
            if (currentOwner == requestorId)
            {
                lockCount++;
                if (isDebugEnabled)
                {
                    logger.LockReentrantAcquisition(lockName, requestorId, lockCount);
                }
                return new ValueTask<bool>(true);
            }
        }

        return ObtainLockCore(requestorId, lockName, isDebugEnabled, cancellationToken);
    }

    private async ValueTask<bool> ObtainLockCore(
        Guid requestorId,
        string lockName,
        bool isDebugEnabled,
        CancellationToken cancellationToken)
    {
        if (isDebugEnabled)
        {
            logger.LockBeingObtained(lockName, requestorId);
        }

        try
        {
            await globalLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (isDebugEnabled)
            {
                logger.LockNotObtained(lockName, requestorId);
            }

            return false;
        }

        lock (syncRoot)
        {
            currentOwner = requestorId;
            lockCount = 1;
        }

        if (isDebugEnabled)
        {
            logger.LockGiven(lockName, requestorId);
        }

        return true;
    }

    /// <summary>
    /// Release the lock on the identified resource if it is held by the calling thread.
    /// </summary>
    public ValueTask ReleaseLock(
        Guid requestorId,
        SchedulerLock lockKind,
        CancellationToken cancellationToken = default)
    {
        lock (syncRoot)
        {
            if (currentOwner != requestorId)
            {
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    logger.LockReturnedByNonOwner(lockKind.ToLockName(), requestorId);
                    logger.WrongfulReturnerStack(Environment.StackTrace);
                }

                return default;
            }

            lockCount--;
            if (lockCount > 0)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LockReentrantRelease(lockKind.ToLockName(), requestorId, lockCount);
                }

                return default;
            }

            currentOwner = null;
        }

        globalLock.Release();

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LockReturned(lockKind.ToLockName(), requestorId);
        }

        return default;
    }

    /// <summary>
    /// Whether this Semaphore implementation requires a database connection for
    /// its lock management operations.
    /// </summary>
    /// <seealso cref="ObtainLock"/>
    /// <seealso cref="ReleaseLock"/>
    public bool RequiresConnection => false;
}
