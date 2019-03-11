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
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl.AdoJobStore.Common;
using Quartz.Logging;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Provide thread/resource locking in order to protect
    /// resources from being altered by multiple threads at the same time using
    /// a db row update.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Note:</b> This Semaphore implementation is useful for databases that do
    /// not support row locking via "SELECT FOR UPDATE" or SQL Server's type syntax.
    /// </para>
    /// <para>
    /// As of Quartz.NET 2.0 version there is no need to use this implementation for
    /// SQL Server databases.
    /// </para>
    /// </remarks>
    /// <author>Marko Lahma (.NET)</author>
    public class UpdateLockRowSemaphore : DBSemaphore
    {
        public static readonly string SqlUpdateForLock =
            $"UPDATE {TablePrefixSubst}{TableLocks} SET {ColumnLockName} = {ColumnLockName} WHERE {ColumnSchedulerName} = {SchedulerNameSubst} AND {ColumnLockName} = @lockName";

        public static readonly string SqlInsertLock =
            $"INSERT INTO {TablePrefixSubst}{TableLocks}({ColumnSchedulerName}, {ColumnLockName}) VALUES ({SchedulerNameSubst}, @lockName)";

        protected virtual int RetryCount => 2;

        /// <summary>
        /// Initializes a new instance of the <see cref="UpdateLockRowSemaphore"/> class.
        /// </summary>
        public UpdateLockRowSemaphore(IDbProvider provider)
            : base(DefaultTablePrefix, null, SqlUpdateForLock, SqlInsertLock, provider)
        {
        }

        protected UpdateLockRowSemaphore(
            string tablePrefix,
            string schedName,
            string defaultSQL,
            string defaultInsertSQL,
            IDbProvider dbProvider) : base(tablePrefix, schedName, defaultSQL, defaultInsertSQL, dbProvider)
        {
        }

        /// <summary>
        /// Execute the SQL that will lock the proper database row.
        /// </summary>
        protected override async Task ExecuteSQL(
            Guid requestorId,
            ConnectionAndTransactionHolder conn,
            string lockName,
            string expandedSql,
            string expandedInsertSql,
            CancellationToken cancellationToken)
        {
            Exception lastFailure = null;
            for (int i = 0; i < RetryCount; i++)
            {
                try
                {
                    if (!await LockViaUpdate(requestorId, conn, lockName, expandedSql, cancellationToken).ConfigureAwait(false))
                    {
                        await LockViaInsert(requestorId, conn, lockName, expandedInsertSql, cancellationToken).ConfigureAwait(false);
                    }
                    return;
                }
                catch (Exception e)
                {
                    lastFailure = e;
                    if (i + 1 == RetryCount)
                    {
                        if (Log.IsDebugEnabled())
                        {
                            Log.DebugFormat("Lock '{0}' was not obtained by: {1}", lockName, requestorId);
                        }
                    }
                    else
                    {
                        if (Log.IsDebugEnabled())
                        {
                            Log.DebugFormat("Lock '{0}' was not obtained by: {1} - will try again.", lockName, requestorId);
                        }

                        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            if (lastFailure != null)
            {
                throw new LockException("Failure obtaining db row lock: " + lastFailure.Message, lastFailure);
            }
        }

        private async Task<bool> LockViaUpdate(
            Guid requestorId,
            ConnectionAndTransactionHolder conn,
            string lockName,
            string sql,
            CancellationToken cancellationToken)
        {
            using (DbCommand cmd = AdoUtil.PrepareCommand(conn, sql))
            {
                AdoUtil.AddCommandParameter(cmd, "lockName", lockName);

                if (Log.IsDebugEnabled())
                {
                    Log.DebugFormat("Lock '{0}' is being obtained: {1}", lockName, requestorId);
                }
                return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) >= 1;
            }
        }

        private async Task LockViaInsert(
            Guid requestorId,
            ConnectionAndTransactionHolder conn,
            string lockName,
            string sql,
            CancellationToken cancellationToken)
        {
            if (sql == null)
            {
                throw new ArgumentNullException(nameof(sql));
            }

            if (Log.IsDebugEnabled())
            {
                Log.DebugFormat("Inserting new lock row for lock: '{0}' being obtained: {1}", lockName, requestorId);
            }
            using (var cmd = AdoUtil.PrepareCommand(conn, sql))
            {
                AdoUtil.AddCommandParameter(cmd, "lockName", lockName);

                if (await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                {
                    throw new InvalidOperationException(
                        AdoJobStoreUtil.ReplaceTablePrefix("No row exists, and one could not be inserted in table " + TablePrefixSubst + TableLocks + " for lock named: " + lockName, TablePrefix, SchedulerNameLiteral));
                }
            }
        }
    }
}