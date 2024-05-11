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
internal sealed class SimpleSemaphore : ISemaphore
{
    private readonly ResourceLock triggerLock = new();
    private readonly ResourceLock stateLock = new();

    private readonly ILogger<SimpleSemaphore> logger;

    public SimpleSemaphore()
    {
        logger = LogProvider.CreateLogger<SimpleSemaphore>();
    }

    /// <summary>
    /// Grants a lock on the identified resource to the calling thread (blocking
    /// until it is available).
    /// </summary>
    /// <returns>True if the lock was obtained.</returns>
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

        var gotLock = false;
        var lockHandle = GetLock(lockName);
        if (!lockHandle.IsLockOwner(requestorId))
        {
            if (isDebugEnabled)
            {
                logger.LogDebug("Lock '{LockName}' is being obtained: {RequestorId}", lockName, requestorId);
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
                    logger.LogDebug("Lock '{LockName}' was not obtained by: {RequestorId}", lockName, requestorId);
                }
            }

            if (isDebugEnabled)
            {
                logger.LogDebug("Lock '{LockName}' given to: {RequestorId}", lockName, requestorId);
            }
        }
        else if (isDebugEnabled)
        {
            logger.LogDebug("Lock '{LockName}' already owned by: {RequestorId} -- but not owner!", lockName, requestorId);
            logger.LogDebug("stack-trace of wrongful returner: {StackTrace}", Environment.StackTrace);
        }

        return gotLock;
    }

    /// <summary> Release the lock on the identified resource if it is held by the calling
    /// thread.
    /// </summary>
    public ValueTask ReleaseLock(
        Guid requestorId,
        string lockName,
        CancellationToken cancellationToken = default)
    {
        var lockHandle = GetLock(lockName);
        if (lockHandle.IsLockOwner(requestorId))
        {
            lockHandle.Release();

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Lock '{LockName}' returned by: {RequestorId}", lockName, requestorId);
            }
        }
        else if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning("Lock '{LockName}' attempt to return by: {RequestorId} -- but not owner!", lockName, requestorId);
            logger.LogWarning("stack-trace of wrongful returner: {Stacktrace}", Environment.StackTrace);
        }

        return default;
    }

    /// <summary>
    /// Whether this Semaphore implementation requires a database connection for
    /// its lock management operations.
    /// </summary>
    /// <value></value>
    /// <seealso cref="ObtainLock"/>
    /// <seealso cref="ReleaseLock"/>
    public bool RequiresConnection => false;

    private ResourceLock GetLock(string lockName)
    {
        if (ReferenceEquals(lockName, JobStoreSupport.LockTriggerAccess))
        {
            return triggerLock;
        }

        if (ReferenceEquals(lockName, JobStoreSupport.LockStateAccess))
        {
            return stateLock;
        }

        ThrowHelper.ThrowNotSupportedException();
        return null!;
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