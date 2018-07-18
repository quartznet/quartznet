using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl.AdoJobStore;
using Quartz.Logging;

namespace Quartz.Impl.RavenDB
{
    internal class RavenLockHandler : IRavenLockHandler
    {
        private readonly object syncRoot = new object();
        private readonly Dictionary<Guid, HashSet<LockType>> locks = new Dictionary<Guid, HashSet<LockType>>();

        private string schedulerName;

        public RavenLockHandler(string schedulerName)
        {
            Log = LogProvider.GetLogger(GetType());
            this.schedulerName = schedulerName;
        }

        /// <summary>
        /// Gets the log.
        /// </summary>
        /// <value>The log.</value>
        internal ILog Log { get; }

        public async Task<bool> ObtainLock(
            Guid requestorId,
            RavenConnection connection,
            LockType lockType,
            CancellationToken cancellationToken = default)
        {
            if (Log.IsDebugEnabled())
            {
                Log.DebugFormat("Lock '{0}' is desired by: {1}", lockType, requestorId);
            }

            if (!IsLockOwner(requestorId, lockType))
            {
                await Execute(requestorId, connection, lockType, cancellationToken).ConfigureAwait(false);

                if (Log.IsDebugEnabled())
                {
                    Log.DebugFormat("Lock '{0}' given to: {1}", lockType, requestorId);
                }

                lock (syncRoot)
                {
                    if (!locks.TryGetValue(requestorId, out var requestorLocks))
                    {
                        requestorLocks = new HashSet<LockType>();
                        locks[requestorId] = requestorLocks;
                    }

                    requestorLocks.Add(lockType);
                }
            }
            else if (Log.IsDebugEnabled())
            {
                Log.DebugFormat("Lock '{0}' Is already owned by: {1}", lockType, requestorId);
            }

            return true;
        }

        public Task ReleaseLock(
            Guid requestorId,
            LockType lockType,
            CancellationToken cancellationToken = default)
        {
            if (IsLockOwner(requestorId, lockType))
            {
                lock (syncRoot)
                {
                    if (locks.TryGetValue(requestorId, out var requestorLocks))
                    {
                        requestorLocks.Remove(lockType);
                        if (requestorLocks.Count == 0)
                        {
                            locks.Remove(requestorId);
                        }
                    }
                }

                if (Log.IsDebugEnabled())
                {
                    Log.DebugFormat("Lock '{0}' returned by: {1}", lockType, requestorId);
                }
            }
            else
            {
                if (Log.IsWarnEnabled()){
                    Log.WarnException($"Lock '{lockType}' attempt to return by: {requestorId} -- but not owner!",
                    new Exception("stack-trace of wrongful returner"));
                }
            }

            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Determine whether the calling thread owns a lock on the identified
        /// resource.
        /// </summary>
        private bool IsLockOwner(Guid requestorId, LockType lockType)
        {
            lock (syncRoot)
            {
                return locks.TryGetValue(requestorId, out var requestorLocks) && requestorLocks.Contains(lockType);
            }
        }

        private async Task Execute(
            Guid requestorId, 
            RavenConnection connection,
            LockType lockType,
            CancellationToken cancellationToken)
        {
            Exception initCause = null;
            // attempt lock two times (to work-around possible race conditions in inserting the lock the first time running)
            int count = 0;
            do
            {
                count++;
                try
                {
                    //using (DbCommand cmd = AdoUtil.PrepareCommand(conn, expandedSql))
                    {
                        //AdoUtil.AddCommandParameter(cmd, "lockName", lockName);

                        bool found;
                        //using (var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                        {
                            if (Log.IsDebugEnabled())
                            {
                                Log.DebugFormat("Lock '{0}' is being obtained: {1}", lockType, requestorId);
                            }

                            found = false;  //TODO await rs.ReadAsync(cancellationToken).ConfigureAwait(false);
                        }

                        if (!found)
                        {
                            if (Log.IsDebugEnabled())
                            {
                                Log.DebugFormat("Inserting new lock row for lock: '{0}' being obtained by thread: {1}", lockType, requestorId);
                            }

                            //using (DbCommand cmd2 = AdoUtil.PrepareCommand(conn, expandedInsertSql))
                            {
                                //AdoUtil.AddCommandParameter(cmd2, "lockName", lockName);
                                //int res = await cmd2.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                                int res = 0;
                                if (res != 1)
                                {
                                    if (count < 3)
                                    {
                                        // pause a bit to give another thread some time to commit the insert of the new lock row
                                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);

                                        // try again ...
                                        continue;
                                    }
                                    throw new Exception("No document exists, and one could not be inserted for lock named: " + lockType);
                                }
                            }
                        }
                    }

                    // obtained lock, go
                    return;
                }
                catch (Exception ex)
                {
                    if (initCause == null)
                    {
                        initCause = ex;
                    }

                    if (Log.IsDebugEnabled())
                    {
                        Log.DebugFormat("Lock '{0}' was not obtained by: {1}{2}", lockType, requestorId, count < 3 ? " - will try again." : "");
                    }

                    if (count < 3)
                    {
                        // pause a bit to give another thread some time to commit the insert of the new lock row
                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);

                        // try again ...
                        continue;
                    }

                    throw new LockException("Failure obtaining db row lock: " + ex.Message, ex);
                }
            } while (count < 4);

            throw new LockException("Failure obtaining db lock, reached maximum number of attempts. Initial exception (if any) attached as root cause.", initCause);
        }

    }
}