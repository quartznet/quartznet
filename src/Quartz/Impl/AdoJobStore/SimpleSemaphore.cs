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

using Quartz.Collections;
using Quartz.Logging;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Internal in-memory lock handler for providing thread/resource locking in
/// order to protect resources from being altered by multiple threads at the
/// same time.
/// </summary>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public class SimpleSemaphore : ISemaphore
{
    private readonly ResourceLock triggerLock = new();
    private readonly ResourceLock stateLock = new();

    private readonly ILog log;

    public SimpleSemaphore()
    {
        log = LogProvider.GetLogger(GetType());
    }

    /// <summary>
    /// Grants a lock on the identified resource to the calling thread (blocking
    /// until it is available).
    /// </summary>
    /// <returns>True if the lock was obtained.</returns>
    public virtual async Task<bool> ObtainLock(
        Guid requestorId,
        ConnectionAndTransactionHolder? conn,
        string lockName,
        CancellationToken cancellationToken = default)
    {
        var isDebugEnabled = log.IsDebugEnabled();

        if (isDebugEnabled)
        {
            log.Debug($"Lock '{lockName}' is desired by: {requestorId}");
        }

        var gotLock = false;
        var lockHandle = GetLock(lockName);
        if (!lockHandle.IsLockOwner(requestorId))
        {
            if (isDebugEnabled)
            {
                log.Debug($"Lock '{lockName}' is being obtained: {requestorId}");
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
                    log.Debug($"Lock '{lockName}' was not obtained by: {requestorId}");
                }
            }

            if (isDebugEnabled)
            {
                log.Debug($"Lock '{lockName}' given to: {requestorId}");
            }
        }
        else if (isDebugEnabled)
        {
            log.Debug($"Lock '{lockName}' already owned by: {requestorId} -- but not owner!");
            log.Debug("stack-trace of wrongful returner: " + Environment.StackTrace);
        }

        return gotLock;
    }

    /// <summary> Release the lock on the identified resource if it is held by the calling
    /// thread.
    /// </summary>
    public virtual Task ReleaseLock(
        Guid requestorId, 
        string lockName,
        CancellationToken cancellationToken = default)
    {
        var lockHandle = GetLock(lockName);
        if (lockHandle.IsLockOwner(requestorId))
        {
            lockHandle.Release();

            if (log.IsDebugEnabled())
            {
                log.Debug($"Lock '{lockName}' returned by: {requestorId}");
            }
        }
        else if (log.IsWarnEnabled())
        {
            log.Warn($"Lock '{lockName}' attempt to return by: {requestorId} -- but not owner!");
            log.Warn("stack-trace of wrongful returner: " + Environment.StackTrace);
        }

        return Task.CompletedTask;
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
        private SemaphoreSlim semaphore = new(1, 1);
        private Guid? owner;

        public bool IsLockOwner(Guid requestorId)
        {
            var temp = owner;
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