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

using Quartz.Impl.AdoJobStore.Common;
using Quartz.Logging;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Base class for database based lock handlers for providing thread/resource locking
    /// in order to protect resources from being altered by multiple threads at the
    /// same time.
    /// </summary>
    /// <author>Marko Lahma (.NET)</author>
    public abstract class DBSemaphore : StdAdoConstants, ISemaphore, ITablePrefixAware
    {
        private readonly object syncRoot = new object();
        private readonly Dictionary<Guid, HashSet<string>> locks = new Dictionary<Guid, HashSet<string>>();

        private string sql;
        private string insertSql;

        private string tablePrefix;

        private string schedName;

        private string expandedSQL;
        private string expandedInsertSQL;

        /// <summary>
        /// Initializes a new instance of the <see cref="DBSemaphore"/> class.
        /// </summary>
        /// <param name="tablePrefix">The table prefix.</param>
        /// <param name="schedName">the scheduler name</param>
        /// <param name="defaultInsertSQL">The SQL.</param>
        /// <param name="defaultSQL">The default SQL.</param>
        /// <param name="dbProvider">The db provider.</param>
        protected DBSemaphore(
            string tablePrefix, 
            string schedName, 
            string defaultSQL, 
            string defaultInsertSQL, 
            IDbProvider dbProvider)
        {
            Log = LogProvider.GetLogger(GetType());
            this.schedName = schedName;
            this.tablePrefix = tablePrefix;
            SQL = defaultSQL;
            InsertSQL = defaultInsertSQL;
            AdoUtil = new AdoUtil(dbProvider);
        }

        /// <summary>
        /// Gets the log.
        /// </summary>
        /// <value>The log.</value>
        internal ILog Log { get; }

        /// <summary>
        /// Execute the SQL that will lock the proper database row.
        /// </summary>
        protected abstract Task ExecuteSQL(
            Guid requestorId,
            ConnectionAndTransactionHolder conn, 
            string lockName, 
            string expandedSql, 
            string expandedInsertSql,
            CancellationToken cancellationToken);

        /// <summary>
        /// Grants a lock on the identified resource to the calling thread (blocking
        /// until it is available).
        /// </summary>
        /// <returns>true if the lock was obtained.</returns>
        public async Task<bool> ObtainLock(
            Guid requestorId, 
            ConnectionAndTransactionHolder conn,
            string lockName,
            CancellationToken cancellationToken = default)
        {
            if (Log.IsDebugEnabled())
            {
                Log.DebugFormat("Lock '{0}' is desired by: {1}", lockName, requestorId);
            }
            if (!IsLockOwner(requestorId, lockName))
            {
                await ExecuteSQL(requestorId, conn, lockName, expandedSQL, expandedInsertSQL, cancellationToken).ConfigureAwait(false);

                if (Log.IsDebugEnabled())
                {
                    Log.DebugFormat("Lock '{0}' given to: {1}", lockName, requestorId);
                }

                lock (syncRoot)
                {
                    if (!locks.TryGetValue(requestorId, out var requestorLocks))
                    {
                        requestorLocks = new HashSet<string>();
                        locks[requestorId] = requestorLocks;
                    }
                    requestorLocks.Add(lockName);
                }
            }
            else if (Log.IsDebugEnabled())
            {
                Log.DebugFormat("Lock '{0}' Is already owned by: {1}", lockName, requestorId);
            }

            return true;
        }

        /// <summary>
        /// Release the lock on the identified resource if it is held by the calling
        /// thread.
        /// </summary>
        public Task ReleaseLock(
            Guid requestorId, 
            string lockName,
            CancellationToken cancellationToken = default)
        {
            if (IsLockOwner(requestorId, lockName))
            {
                lock (syncRoot)
                {
                    if (locks.TryGetValue(requestorId, out var requestorLocks))
                    {
                        requestorLocks.Remove(lockName);
                        if (requestorLocks.Count == 0)
                        {
                            locks.Remove(requestorId);
                        }
                    }
                }
                if (Log.IsDebugEnabled())
                {
                    Log.DebugFormat("Lock '{0}' returned by: {1}", lockName, requestorId);
                }
            }
            else if (Log.IsWarnEnabled())
            {
                Log.WarnException($"Lock '{lockName}' attempt to return by: {requestorId} -- but not owner!",
                    new Exception("stack-trace of wrongful returner"));
            }

            return TaskUtil.CompletedTask;
        }

        /// <summary>
        /// Determine whether the calling thread owns a lock on the identified
        /// resource.
        /// </summary>
        private bool IsLockOwner(Guid requestorId, string lockName)
        {
            lock (syncRoot)
            {
                return locks.TryGetValue(requestorId, out var requestorLocks) && requestorLocks.Contains(lockName);
            }
        }

        /// <summary>
        /// This Semaphore implementation does use the database.
        /// </summary>
        public bool RequiresConnection => true;

        protected string SQL
        {
            get => sql;
            set
            {
                if (!value.IsNullOrWhiteSpace())
                {
                    sql = value.Trim();
                }
                SetExpandedSql();
            }
        }

        protected string InsertSQL
        {
            set
            {
                if (!value.IsNullOrWhiteSpace())
                {
                    insertSql = value.Trim();
                }

                SetExpandedSql();
            }
        }

        private void SetExpandedSql()
        {
            if (TablePrefix != null && SchedName != null && sql != null && insertSql != null)
            {
                expandedSQL = AdoJobStoreUtil.ReplaceTablePrefix(sql, TablePrefix, SchedulerNameLiteral);
                expandedInsertSQL = AdoJobStoreUtil.ReplaceTablePrefix(insertSql, TablePrefix, SchedulerNameLiteral);
            }
        }

        private string schedNameLiteral;

        protected string SchedulerNameLiteral
        {
            get
            {
                if (schedNameLiteral == null)
                {
                    schedNameLiteral = "'" + schedName + "'";
                }
                return schedNameLiteral;
            }
        }

        public string SchedName
        {
            get => schedName;
            set
            {
                schedName = value;
                SetExpandedSql();
            }
        }

        /// <summary>
        /// Gets or sets the table prefix.
        /// </summary>
        /// <value>The table prefix.</value>
        public string TablePrefix
        {
            get => tablePrefix;
            set
            {
                tablePrefix = value;
                SetExpandedSql();
            }
        }

        protected AdoUtil AdoUtil { get; }
    }
}