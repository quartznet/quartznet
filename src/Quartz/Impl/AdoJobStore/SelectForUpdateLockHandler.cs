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

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Impl.AdoJobStore.Common;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Database based lock handler that takes the lock by issuing
/// <c>SELECT ... FOR UPDATE</c> against the lock row.
/// </summary>
public class SelectForUpdateLockHandler : DbLockHandler
{
    /// <summary>
    /// The statement that takes the lock by selecting its row for update.
    /// </summary>
    protected const string SelectForLock =
        $"SELECT * FROM {StdAdoConstants.TablePrefixSubst}{AdoConstants.TableLocks} WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnLockName} = @lockName FOR UPDATE";

    /// <summary>
    /// The statement that inserts the lock row when it does not exist yet.
    /// </summary>
    protected const string InsertLock =
        $"INSERT INTO {StdAdoConstants.TablePrefixSubst}{AdoConstants.TableLocks}({AdoConstants.ColumnSchedulerName}, {AdoConstants.ColumnLockName}) VALUES (@schedulerName, @lockName)";

    /// <summary>
    /// Initializes a new instance of the <see cref="SelectForUpdateLockHandler"/> class.
    /// </summary>
    /// <remarks>
    /// This is the constructor the container uses. The other one takes strings, which no container can
    /// supply, and marking this one says so rather than leaving the choice ambiguous.
    /// </remarks>
    [ActivatorUtilitiesConstructor]
    public SelectForUpdateLockHandler(IDbProvider dbProvider)
        : base(AdoConstants.DefaultTablePrefix, null, SelectForLock, InsertLock, dbProvider)
    {

    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SelectForUpdateLockHandler"/> class.
    /// </summary>
    /// <param name="tablePrefix">The table prefix.</param>
    /// <param name="schedulerName">the scheduler name</param>
    /// <param name="selectWithLockSql">The select with lock SQL.</param>
    /// <param name="dbProvider"></param>
    public SelectForUpdateLockHandler(string tablePrefix, string schedulerName, string? selectWithLockSql, IDbProvider dbProvider)
        : this(tablePrefix, schedulerName, selectWithLockSql, InsertLock, dbProvider)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SelectForUpdateLockHandler"/> class for a subclass that
    /// needs its own insert statement.
    /// </summary>
    /// <remarks>
    /// The insert arrives here rather than being assigned afterwards, because the base class folds the
    /// table prefix into both statements once, at construction.
    /// </remarks>
    /// <param name="tablePrefix">The table prefix.</param>
    /// <param name="schedulerName">the scheduler name</param>
    /// <param name="selectWithLockSql">The select with lock SQL.</param>
    /// <param name="insertLockSql">The statement that inserts the lock row when it does not exist yet.</param>
    /// <param name="dbProvider">The db provider.</param>
    protected SelectForUpdateLockHandler(
        string tablePrefix,
        string? schedulerName,
        string? selectWithLockSql,
        string insertLockSql,
        IDbProvider dbProvider)
        : base(tablePrefix, schedulerName, selectWithLockSql ?? SelectForLock, insertLockSql, dbProvider)
    {
    }

    // Configurable lock retry parameters

    /// <summary>
    /// Maximum retry attempts, defaults to 3.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="RetryPeriod" path="/remarks" />
    /// </remarks>
    public int MaxRetry { get; init; } = 3;

    /// <summary>
    /// Sleep between attempts, defaults to 1 second.
    /// </summary>
    /// <remarks>
    /// Init-only: how many times a lock attempt is retried is fixed for the life of the handler, and a
    /// setter invited changing it while a contended lock was mid-retry. The flat
    /// <c>quartz.jobStore.lockHandler.maxRetry</c> and <c>…retryPeriod</c> keys still reach it — the
    /// property bridge writes the handler by reflection, and an init accessor is a setter as far as
    /// reflection is concerned.
    /// </remarks>
    [TimeSpanParseRule(TimeSpanParseRule.Milliseconds)]
    public TimeSpan RetryPeriod
    {
        get;

        init
        {
            // Checked here because a lock handler has no options type and so no startup validator. Left
            // unchecked, a period longer than a timer will wait out is refused by the first contended
            // lock attempt instead — with the lock unacquired and nothing naming the setting.
            TimerLimits.EnsureWaitable(value, nameof(RetryPeriod));
            field = value;
        }
    } = TimeSpan.FromMilliseconds(1000);

    /// <summary>
    /// Execute the SQL select for update that will lock the proper database row.
    /// </summary>
    protected override async ValueTask ExecuteSql(
        Guid requestorId,
        ConnectionAndTransactionHolder conn,
        string lockName,
        string expandedSql,
        string expandedInsertSql,
        CancellationToken cancellationToken = default)
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
                using DbCommand cmd = PrepareCommand(conn, expandedSql);
                AddCommandParameter(cmd, "schedulerName", SchedulerName);
                AddCommandParameter(cmd, "lockName", lockName);

                bool found;
                using (var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LockBeingObtained(lockName, requestorId);
                    }

                    found = await rs.ReadAsync(cancellationToken).ConfigureAwait(false);
                }

                if (!found)
                {
                    if (logger.IsEnabled(LogLevel.Debug))
                    {
                        logger.LockRowInsertingForThread(lockName, requestorId);
                    }

                    using DbCommand cmd2 = PrepareCommand(conn, expandedInsertSql);
                    AddCommandParameter(cmd2, "schedulerName", SchedulerName);
                    AddCommandParameter(cmd2, "lockName", lockName);
                    int res = await cmd2.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                    if (res != 1)
                    {
                        if (count < maxRetryLocal)
                        {
                            // pause a bit to give another thread some time to commit the insert of the new lock row
                            await Task.Delay(retryPeriodLocal, TimeProvider, cancellationToken).ConfigureAwait(false);

                            // try again ...
                            continue;
                        }
                        Throw.InvalidOperationException(AdoJobStoreUtil.ReplaceTablePrefix(
                            "No row exists, and one could not be inserted in table " + StdAdoConstants.TablePrefixSubst + AdoConstants.TableLocks +
                            " for lock named: " + lockName, TablePrefix));
                    }
                }

                // obtained lock, go
                return;
            }
            // Cancellation is not lock contention: there is no point backing off and asking again for a
            // lock the caller has stopped waiting for, and reporting it as a LockException - which is a
            // JobPersistenceException - would tell them the database refused the lock.
            catch (Exception sqle) when (sqle is not OperationCanceledException)
            {
                if (initCause is null)
                {
                    initCause = sqle;
                }

                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LockNotObtainedWithRetryNote(lockName, requestorId, count < maxRetryLocal ? " - will try again." : "");
                }

                if (count < maxRetryLocal)
                {
                    // pause a bit to give another thread some time to commit the insert of the new lock row
                    await Task.Delay(retryPeriodLocal, TimeProvider, cancellationToken).ConfigureAwait(false);

                    // try again ...
                    continue;
                }

                Throw.LockException("Failure obtaining db row lock: " + sqle.Message, sqle);
            }
        } while (count < maxRetryLocal + 1);

        Throw.LockException("Failure obtaining db row lock, reached maximum number of attempts. Initial exception (if any) attached as root cause.", initCause);
    }
}