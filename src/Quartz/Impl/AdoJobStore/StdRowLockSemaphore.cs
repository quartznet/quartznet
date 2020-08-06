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
    /// Internal database based lock handler for providing thread/resource locking
    /// in order to protect resources from being altered by multiple threads at the
    /// same time.
    /// </summary>
    /// <author>James House</author>
    /// <author>Marko Lahma (.NET)</author>
    public class StdRowLockSemaphore : DBSemaphore
    {
        public static readonly string SelectForLock =
            $"SELECT * FROM {TablePrefixSubst}{TableLocks} WHERE {ColumnSchedulerName} = @schedulerName AND {ColumnLockName} = @lockName FOR UPDATE";

        public static readonly string InsertLock =
            $"INSERT INTO {TablePrefixSubst}{TableLocks}({ColumnSchedulerName}, {ColumnLockName}) VALUES (@schedulerName, @lockName)";

        /// <summary>
        /// Initializes a new instance of the <see cref="StdRowLockSemaphore"/> class.
        /// </summary>
        public StdRowLockSemaphore(IDbProvider dbProvider)
            : base(DefaultTablePrefix, null, SelectForLock, InsertLock, dbProvider)
        {

        }

        /// <summary>
        /// Initializes a new instance of the <see cref="StdRowLockSemaphore"/> class.
        /// </summary>
        /// <param name="tablePrefix">The table prefix.</param>
        /// <param name="schedName">the scheduler name</param>
        /// <param name="selectWithLockSQL">The select with lock SQL.</param>
        /// <param name="dbProvider"></param>
        public StdRowLockSemaphore(string tablePrefix, string schedName, string? selectWithLockSQL, IDbProvider dbProvider)
            : base(tablePrefix, schedName, selectWithLockSQL ?? SelectForLock, InsertLock, dbProvider)
        {
        }

        // Configurable lock retry parameters

        /// <summary>
        /// Maximum retry attempts, defaults to 3.
        /// </summary>
        public int MaxRetry { get; set; } = 3;

        /// <summary>
        /// Sleep between attempts, defaults to 1 second.
        /// </summary>
        [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
        public TimeSpan RetryPeriod { get; set; } = TimeSpan.FromMilliseconds(1000);

        /// <summary>
        /// Execute the SQL select for update that will lock the proper database row.
        /// </summary>
        protected override async Task ExecuteSQL(
            Guid requestorId,
            ConnectionAndTransactionHolder conn,
            string lockName,
            string expandedSql,
            string expandedInsertSql,
            CancellationToken cancellationToken)
        {
            Exception? initCause = null;
            // attempt lock two times (to work-around possible race conditions in inserting the lock row the first time running)
            int count = 0;

            // Configurable lock retry attempts
            var maxRetryLocal = MaxRetry;
            var retryPeriodLocal = RetryPeriod;

            do
            {
                count++;
                try
                {
                    using (DbCommand cmd = AdoUtil.PrepareCommand(conn, expandedSql))
                    {
                        AdoUtil.AddCommandParameter(cmd, "schedulerName", SchedName);
                        AdoUtil.AddCommandParameter(cmd, "lockName", lockName);

                        bool found;
                        using (var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                        {
                            if (Log.IsDebugEnabled())
                            {
                                Log.DebugFormat("Lock '{0}' is being obtained: {1}", lockName, requestorId);
                            }

                            found = await rs.ReadAsync(cancellationToken).ConfigureAwait(false);
                        }

                        if (!found)
                        {
                            if (Log.IsDebugEnabled())
                            {
                                Log.DebugFormat("Inserting new lock row for lock: '{0}' being obtained by thread: {1}", lockName, requestorId);
                            }

                            using (DbCommand cmd2 = AdoUtil.PrepareCommand(conn, expandedInsertSql))
                            {
                                AdoUtil.AddCommandParameter(cmd2, "schedulerName", SchedName);
                                AdoUtil.AddCommandParameter(cmd2, "lockName", lockName);
                                int res = await cmd2.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                                if (res != 1)
                                {
                                    if (count < maxRetryLocal)
                                    {
                                        // pause a bit to give another thread some time to commit the insert of the new lock row
                                        await Task.Delay(retryPeriodLocal, cancellationToken).ConfigureAwait(false);

                                        // try again ...
                                        continue;
                                    }
                                    throw new Exception(AdoJobStoreUtil.ReplaceTablePrefix(
                                        "No row exists, and one could not be inserted in table " + TablePrefixSubst + TableLocks +
                                        " for lock named: " + lockName, TablePrefix));
                                }
                            }
                        }
                    }

                    // obtained lock, go
                    return;
                }
                catch (Exception sqle)
                {
                    if (initCause == null)
                    {
                        initCause = sqle;
                    }

                    if (Log.IsDebugEnabled())
                    {
                        Log.DebugFormat("Lock '{0}' was not obtained by: {1}{2}", lockName, requestorId, count < maxRetryLocal ? " - will try again." : "");
                    }

                    if (count < maxRetryLocal)
                    {
                        // pause a bit to give another thread some time to commit the insert of the new lock row
                        await Task.Delay(retryPeriodLocal, cancellationToken).ConfigureAwait(false);

                        // try again ...
                        continue;
                    }

                    throw new LockException("Failure obtaining db row lock: " + sqle.Message, sqle);
                }
            } while (count < maxRetryLocal + 1);

            throw new LockException("Failure obtaining db row lock, reached maximum number of attempts. Initial exception (if any) attached as root cause.", initCause);
        }
    }
}