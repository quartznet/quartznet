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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Logging;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Internal in-memory lock handler for providing thread/resource locking in
    /// order to protect resources from being altered by multiple threads at the
    /// same time.
    /// </summary>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class SimpleSemaphore : ISemaphore
    {
        private readonly object syncRoot = new object();
        private readonly Dictionary<Guid, HashSet<string>> threadLocks = new Dictionary<Guid, HashSet<string>>();

        private readonly ILog log;
        private readonly HashSet<string> locks = new HashSet<string>();

        public SimpleSemaphore()
        {
            log = LogProvider.GetLogger(GetType());
        }

        /// <summary>
        /// Grants a lock on the identified resource to the calling thread (blocking
        /// until it is available).
        /// </summary>
        /// <returns>True if the lock was obtained.</returns>
        public virtual Task<bool> ObtainLock(
            Guid requestorId, 
            ConnectionAndTransactionHolder conn, 
            string lockName,
            CancellationToken cancellationToken = default)
        {
            if (log.IsDebugEnabled())
            {
                log.Debug($"Lock '{lockName}' is desired by: {requestorId}");
            }

            lock (syncRoot)
            {
                if (!IsLockOwner(requestorId, lockName))
                {
                    if (log.IsDebugEnabled())
                    {
                        log.Debug($"Lock '{lockName}' is being obtained: {requestorId}");
                    }

                    while (locks.Contains(lockName))
                    {
                        try
                        {
                            Monitor.Wait(syncRoot);
                        }
                        catch (ThreadInterruptedException)
                        {
                            if (log.IsDebugEnabled())
                            {
                                log.Debug($"Lock '{lockName}' was not obtained by: {requestorId}");
                            }
                        }
                    }

                    if (!threadLocks.TryGetValue(requestorId, out var requestorLocks))
                    {
                        requestorLocks = new HashSet<string>();
                        threadLocks[requestorId] = requestorLocks;
                    }
                    requestorLocks.Add(lockName);
                    locks.Add(lockName);

                    if (log.IsDebugEnabled())
                    {
                        log.Debug($"Lock '{lockName}' given to: {requestorId}");
                    }
                }
                else if (log.IsDebugEnabled())
                {
                    log.DebugException($"Lock '{lockName}' already owned by: {requestorId} -- but not owner!", new Exception("stack-trace of wrongful returner"));
                }

                return Task.FromResult(true);
            }
        }

        /// <summary> Release the lock on the identified resource if it is held by the calling
        /// thread.
        /// </summary>
        public virtual Task ReleaseLock(
            Guid requestorId, 
            string lockName,
            CancellationToken cancellationToken = default)
        {
            lock (syncRoot)
            {
                if (IsLockOwner(requestorId, lockName))
                {
                    if (threadLocks.TryGetValue(requestorId, out var requestorLocks))
                    {
                        requestorLocks.Remove(lockName);
                        if (requestorLocks.Count == 0)
                        {
                            threadLocks.Remove(requestorId);
                        }
                    }
                    locks.Remove(lockName);

                    if (log.IsDebugEnabled())
                    {
                        log.Debug($"Lock '{lockName}' returned by: {requestorId}");
                    }

                    Monitor.PulseAll(syncRoot);
                }
                else if (log.IsWarnEnabled())
                {
                    log.WarnException($"Lock '{lockName}' attempt to return by: {requestorId} -- but not owner!", new Exception("stack-trace of wrongful returner"));
                }
            }
            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Determine whether the calling thread owns a lock on the identified
        /// resource.
        /// </summary>
        private bool IsLockOwner(Guid requestorId, string lockName)
        {
            return threadLocks.TryGetValue(requestorId, out var requestorLocks) && requestorLocks.Contains(lockName);
        }

        /// <summary>
        /// Whether this Semaphore implementation requires a database connection for
        /// its lock management operations.
        /// </summary>
        /// <value></value>
        /// <seealso cref="IsLockOwner"/>
        /// <seealso cref="ObtainLock"/>
        /// <seealso cref="ReleaseLock"/>
        public bool RequiresConnection => false;
    }
}