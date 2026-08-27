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
/// Internal in-memory lock handler for providing thread/resource locking in
/// order to protect resources from being altered by multiple threads at the
/// same time.
/// </summary>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
internal sealed class InProcessLockHandler : ILockHandler
{
    private readonly ResourceLock triggerLock = new();
    private readonly ResourceLock stateLock = new();

    private ILogger<InProcessLockHandler> logger = LogProvider.CreateLogger<InProcessLockHandler>();

    /// <summary>
    /// Takes the logger from the job store's factory, so lock contention is visible to an application
    /// that never set <see cref="LogProvider" />. Until the store calls this — a handler constructed and
    /// used directly — the ambient factory is still what answers.
    /// </summary>
    public void Initialize(LockHandlerContext context)
    {
        logger = context.LoggerFactory.CreateLogger<InProcessLockHandler>();
    }

    /// <summary>
    /// Grants a lock on the identified resource to the calling thread (blocking
    /// until it is available).
    /// </summary>
    /// <returns>True if the lock was obtained.</returns>
    public async ValueTask<bool> AcquireLock(
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

        var gotLock = false;
        var lockHandle = GetLock(lockKind);
        if (!lockHandle.IsLockOwner(requestorId))
        {
            if (isDebugEnabled)
            {
                logger.LockBeingObtained(lockName, requestorId);
            }

            try
            {
                await lockHandle.Acquire(requestorId, cancellationToken).ConfigureAwait(false);
                gotLock = true;
            }
            catch (OperationCanceledException)
            {
                if (isDebugEnabled)
                {
                    logger.LockNotObtained(lockName, requestorId);
                }
            }

            if (isDebugEnabled)
            {
                logger.LockGiven(lockName, requestorId);
            }
        }
        else if (isDebugEnabled)
        {
            logger.LockAlreadyOwnedByOther(lockName, requestorId);
            logger.WrongfulReturnerStackDebug(Environment.StackTrace);
        }

        return gotLock;
    }

    /// <summary> Release the lock on the identified resource if it is held by the calling
    /// thread.
    /// </summary>
    public ValueTask ReleaseLock(
        Guid requestorId,
        SchedulerLock lockKind,
        CancellationToken cancellationToken = default)
    {
        var lockHandle = GetLock(lockKind);
        if (lockHandle.IsLockOwner(requestorId))
        {
            lockHandle.Release();

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LockReturned(lockKind.ToLockName(), requestorId);
            }
        }
        else if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LockReturnedByNonOwner(lockKind.ToLockName(), requestorId);
            logger.WrongfulReturnerStack(Environment.StackTrace);
        }

        return default;
    }

    /// <summary>
    /// Whether this lock handler requires a database connection for its lock
    /// management operations.
    /// </summary>
    /// <value></value>
    /// <seealso cref="AcquireLock"/>
    /// <seealso cref="ReleaseLock"/>
    public bool RequiresConnection => false;

    private ResourceLock GetLock(SchedulerLock lockKind)
    {
        switch (lockKind)
        {
            case SchedulerLock.TriggerAccess:
                return triggerLock;
            case SchedulerLock.StateAccess:
                return stateLock;
            default:
                Throw.NotSupportedException();
                return null!;
        }
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