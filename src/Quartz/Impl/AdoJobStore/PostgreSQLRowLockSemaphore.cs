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

using System.Data.Common;

using Microsoft.Extensions.Logging;

using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// PostgreSQL-specific row lock semaphore that uses INSERT ... ON CONFLICT DO NOTHING
/// to handle race conditions when multiple threads try to insert the same lock row.
/// </summary>
/// <remarks>
/// This implementation fixes the transaction abort issue that occurs in PostgreSQL
/// when two threads simultaneously attempt to insert a lock row, causing a primary key
/// violation that aborts the transaction.
/// </remarks>
public class PostgreSQLRowLockSemaphore : StdRowLockSemaphore
{
    // PostgreSQL-specific INSERT statement that uses ON CONFLICT DO NOTHING to prevent
    // transaction aborts when multiple threads try to insert the same lock row
    private static readonly string PostgreSQLInsertLock =
        $"INSERT INTO {TablePrefixSubst}{TableLocks}({ColumnSchedulerName}, {ColumnLockName}) VALUES (@schedulerName, @lockName) ON CONFLICT DO NOTHING";

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLRowLockSemaphore"/> class.
    /// </summary>
    public PostgreSQLRowLockSemaphore(IDbProvider dbProvider)
        : base(dbProvider)
    {
        InsertSQL = PostgreSQLInsertLock;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgreSQLRowLockSemaphore"/> class.
    /// </summary>
    /// <param name="tablePrefix">The table prefix.</param>
    /// <param name="schedName">the scheduler name</param>
    /// <param name="selectWithLockSQL">The select with lock SQL.</param>
    /// <param name="dbProvider">The db provider.</param>
    public PostgreSQLRowLockSemaphore(string tablePrefix, string schedName, string? selectWithLockSQL, IDbProvider dbProvider)
        : base(tablePrefix, schedName, selectWithLockSQL, dbProvider)
    {
        InsertSQL = PostgreSQLInsertLock;
    }

    /// <summary>
    /// Execute the SQL select for update that will lock the proper database row.
    /// Overridden to handle PostgreSQL's ON CONFLICT DO NOTHING behavior where
    /// a conflicting insert returns 0 rows affected instead of throwing an exception.
    /// </summary>
    protected override async ValueTask ExecuteSQL(
        Guid requestorId,
        ConnectionAndTransactionHolder conn,
        string lockName,
        string expandedSql,
        string expandedInsertSql,
        CancellationToken cancellationToken)
    {
        Exception? initCause = null;
        int count = 0;

        var maxRetryLocal = MaxRetry;
        var retryPeriodLocal = RetryPeriod;

        do
        {
            count++;
            try
            {
                using DbCommand cmd = AdoUtil.PrepareCommand(conn, expandedSql);
                AdoUtil.AddCommandParameter(cmd, "schedulerName", SchedName);
                AdoUtil.AddCommandParameter(cmd, "lockName", lockName);

                bool found;
                using (var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug("Lock '{LockName}' is being obtained: {RequestorId}", lockName, requestorId);
                    }

                    found = await rs.ReadAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!found)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LogDebug("Inserting new lock row for lock: '{LockName}' being obtained by thread: {RequestorId}", lockName, requestorId);
                    }

                    using DbCommand cmd2 = AdoUtil.PrepareCommand(conn, expandedInsertSql);
                    AdoUtil.AddCommandParameter(cmd2, "schedulerName", SchedName);
                    AdoUtil.AddCommandParameter(cmd2, "lockName", lockName);
                    int res = await cmd2.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                    // PostgreSQL's ON CONFLICT DO NOTHING returns 0 when there's a conflict
                    // This means another thread already inserted the row, so we need to
                    // loop back and try the SELECT...FOR UPDATE again to acquire the lock
                    if (res == 0)
                    {
                        if (logger.IsEnabled(LogLevel.Debug))
                        {
                            logger.LogDebug("Lock row already exists for lock: '{LockName}', retrying SELECT...FOR UPDATE", lockName);
                        }

                        if (count < maxRetryLocal)
                        {
                            // Brief pause before retrying
                            await Task.Delay(retryPeriodLocal, cancellationToken).ConfigureAwait(false);
                            continue;
                        }
                    }
                    else if (res != 1)
                    {
                        if (count < maxRetryLocal)
                        {
                            await Task.Delay(retryPeriodLocal, cancellationToken).ConfigureAwait(false);
                            continue;
                        }
                        Throw.InvalidOperationException(AdoJobStoreUtil.ReplaceTablePrefix(
                            "No row exists, and one could not be inserted in table " + TablePrefixSubst + TableLocks +
                            " for lock named: " + lockName, TablePrefix));
                    }
                }

                // obtained lock, go
                return;
            }
            catch (Exception sqle)
            {
                if (initCause is null)
                {
                    initCause = sqle;
                }

                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("Lock '{LockName}' was not obtained by: {RequestorId}{RetryMessage}", lockName, requestorId, count < maxRetryLocal ? " - will try again." : "");
                }

                if (count < maxRetryLocal)
                {
                    await Task.Delay(retryPeriodLocal, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                Throw.LockException("Failure obtaining db row lock: " + sqle.Message, sqle);
            }
        } while (count < maxRetryLocal + 1);

        Throw.LockException("Failure obtaining db row lock, reached maximum number of attempts. Initial exception (if any) attached as root cause.", initCause);
    }
}
