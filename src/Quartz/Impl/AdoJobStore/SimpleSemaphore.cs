#region License

/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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
using System.Runtime.CompilerServices;
using System.Runtime.Remoting.Messaging;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl.AdoJobStore.Common;
using Quartz.Logging;
using Quartz.Util;

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
        private const string KeyThreadLockOwners = "quartz_semaphore_lock_owners";

        private readonly ILog log;
        private readonly HashSet<string> locks = new HashSet<string>();

        public SimpleSemaphore()
        {
            log = LogProvider.GetLogger(GetType());
        }

        /// <summary>
        /// Gets the thread locks.
        /// </summary>
        /// <returns>The thread locks.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static HashSet<string> GetThreadLocks()
        {
            HashSet<string> threadLocks = (HashSet<string>) CallContext.LogicalGetData(KeyThreadLockOwners);
            if (threadLocks == null)
            {
                threadLocks = new HashSet<string>();
                CallContext.LogicalSetData(KeyThreadLockOwners, threadLocks);
            }
            return threadLocks;
        }

        /// <summary> 
        /// Grants a lock on the identified resource to the calling thread (blocking
        /// until it is available).
        /// </summary>
        /// <returns>True if the lock was obtained.</returns>
        public virtual Task<bool> ObtainLock(DbMetadata metadata, ConnectionAndTransactionHolder conn, string lockName)
        {
            lock (this)
            {
                lockName = string.Intern(lockName);

                if (log.IsDebugEnabled())
                {
                    log.Debug($"Lock '{lockName}' is desired by: {Thread.CurrentThread.Name}");
                }

                if (!IsLockOwner(lockName))
                {
                    if (log.IsDebugEnabled())
                    {
                        log.Debug($"Lock '{lockName}' is being obtained: {Thread.CurrentThread.Name}");
                    }

                    while (locks.Contains(lockName))
                    {
                        try
                        {
                            Monitor.Wait(this);
                        }
                        catch (ThreadInterruptedException)
                        {
                            if (log.IsDebugEnabled())
                            {
                                log.Debug($"Lock '{lockName}' was not obtained by: {Thread.CurrentThread.Name}");
                            }
                        }
                    }

                    if (log.IsDebugEnabled())
                    {
                        log.Debug($"Lock '{lockName}' given to: {Thread.CurrentThread.Name}");
                    }
                    GetThreadLocks().Add(lockName);
                    locks.Add(lockName);
                }
                else if (log.IsDebugEnabled())
                {
                    log.DebugException($"Lock '{lockName}' already owned by: {Thread.CurrentThread.Name} -- but not owner!", new Exception("stack-trace of wrongful returner"));
                }

                return Task.FromResult(true);
            }
        }

        /// <summary> Release the lock on the identified resource if it is held by the calling
        /// thread.
        /// </summary>
        public virtual Task ReleaseLock(string lockName)
        {
            lock (this)
            {
                lockName = string.Intern(lockName);

                if (IsLockOwner(lockName))
                {
                    if (log.IsDebugEnabled())
                    {
                        log.Debug($"Lock '{lockName}' returned by: {Thread.CurrentThread.Name}");
                    }
                    GetThreadLocks().Remove(lockName);
                    locks.Remove(lockName);
                    Monitor.PulseAll(this);
                }
                else if (log.IsDebugEnabled())
                {
                    log.DebugException($"Lock '{lockName}' attempt to return by: {Thread.CurrentThread.Name} -- but not owner!", new Exception("stack-trace of wrongful returner"));
                }
            }
            return TaskUtil.CompletedTask;
        }

        /// <summary> 
        /// Determine whether the calling thread owns a lock on the identified
        /// resource.
        /// </summary>
        public virtual bool IsLockOwner(string lockName)
        {
            lock (this)
            {
                lockName = string.Intern(lockName);
                return GetThreadLocks().Contains(lockName);
            }
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