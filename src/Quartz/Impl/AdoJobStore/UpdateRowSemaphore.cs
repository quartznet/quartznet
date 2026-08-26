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
public class UpdateRowSemaphore : DbSemaphore
{
    /// <summary>
    /// The statement that takes the lock by updating its row.
    /// </summary>
    protected const string UpdateForLock =
        $"UPDATE {StdAdoConstants.TablePrefixSubst}{AdoConstants.TableLocks} SET {AdoConstants.ColumnLockName} = {AdoConstants.ColumnLockName} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnLockName} = @lockName";

    /// <summary>
    /// The statement that inserts the lock row when it does not exist yet.
    /// </summary>
    protected const string InsertLock =
        $"INSERT INTO {StdAdoConstants.TablePrefixSubst}{AdoConstants.TableLocks}({AdoConstants.ColumnSchedulerName}, {AdoConstants.ColumnLockName}) VALUES (@schedulerName, @lockName)";

    protected virtual int RetryCount => 2;

    /// <summary>
    /// Sleep between attempts, defaults to 1 second.
    /// </summary>
    /// <remarks>
    /// It was a literal <c>TimeSpan.FromSeconds(1)</c> in the retry loop, which meant this handler
    /// ignored <c>quartz.jobStore.lockHandler.retryPeriod</c> while its sibling
    /// <see cref="SelectForUpdateSemaphore" /> honoured it. Init-only for the same reason as there: the
    /// value is fixed for the life of the handler, and the property bridge writes it by reflection,
    /// which an init accessor does not stop.
    /// </remarks>
    [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
    public TimeSpan RetryPeriod { get; init; } = TimeSpan.FromMilliseconds(1000);

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateRowSemaphore"/> class.
    /// </summary>
    public UpdateRowSemaphore(IDbProvider provider)
        : base(AdoConstants.DefaultTablePrefix, null, UpdateForLock, InsertLock, provider)
    {
    }

    protected UpdateRowSemaphore(
        string tablePrefix,
        string? schedulerName,
        string updateForLockSql,
        string insertLockSql,
        IDbProvider dbProvider) : base(tablePrefix, schedulerName, updateForLockSql, insertLockSql, dbProvider)
    {
    }

    /// <summary>
    /// Execute the SQL that will lock the proper database row.
    /// </summary>
    protected override async ValueTask ExecuteSql(
        Guid requestorId,
        ConnectionAndTransactionHolder conn,
        string lockName,
        string expandedSql,
        string expandedInsertSql,
        CancellationToken cancellationToken = default)
    {
        Exception? lastFailure = null;
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
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LockNotObtained(lockName, requestorId);
                    }
                }
                else
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LockNotObtainedWillRetry(lockName, requestorId);
                    }

                    await Task.Delay(RetryPeriod, TimeProvider, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        if (lastFailure is not null)
        {
            Throw.LockException("Failure obtaining db row lock: " + lastFailure.Message, lastFailure);
        }
    }

    private async ValueTask<bool> LockViaUpdate(
        Guid requestorId,
        ConnectionAndTransactionHolder conn,
        string lockName,
        string sql,
        CancellationToken cancellationToken)
    {
        using DbCommand cmd = PrepareCommand(conn, sql);
        AddCommandParameter(cmd, "schedulerName", SchedulerName);
        AddCommandParameter(cmd, "lockName", lockName);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LockBeingObtained(lockName, requestorId);
        }
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) >= 1;
    }

    private async ValueTask LockViaInsert(
        Guid requestorId,
        ConnectionAndTransactionHolder conn,
        string lockName,
        string sql,
        CancellationToken cancellationToken)
    {
        if (sql is null)
        {
            Throw.ArgumentNullException(nameof(sql));
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LockRowInserting(lockName, requestorId);
        }

        using var cmd = PrepareCommand(conn, sql);
        AddCommandParameter(cmd, "schedulerName", SchedulerName);
        AddCommandParameter(cmd, "lockName", lockName);

        if (await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
        {
            Throw.InvalidOperationException(
                AdoJobStoreUtil.ReplaceTablePrefix("No row exists, and one could not be inserted in table " + StdAdoConstants.TablePrefixSubst + AdoConstants.TableLocks + " for lock named: " + lockName, TablePrefix));
        }
    }
}