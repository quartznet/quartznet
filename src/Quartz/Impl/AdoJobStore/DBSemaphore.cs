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

using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;

using Quartz.Impl.AdoJobStore.Common;
using Quartz.Diagnostics;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Base class for database based lock handlers for providing thread/resource locking
/// in order to protect resources from being altered by multiple threads at the
/// same time.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
public abstract class DBSemaphore : StdAdoConstants, ISemaphore, ITablePrefixAware
{
    private readonly ConcurrentDictionary<ThreadLockKey, object?> locks = new();

    private string sql = null!;
    private string insertSql = null!;

    private string tablePrefix = null!;

    private string? schedName;

    private string expandedSQL = null!;
    private string expandedInsertSQL = null!;

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
        string? schedName,
        string defaultSQL,
        string defaultInsertSQL,
        IDbProvider dbProvider)
    {
        logger = LogProvider.CreateLogger<DBSemaphore>();
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
    internal ILogger<DBSemaphore> logger { get; }

    /// <summary>
    /// Execute the SQL that will lock the proper database row.
    /// </summary>
    protected abstract ValueTask ExecuteSQL(
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
    public async ValueTask<bool> ObtainLock(
        Guid requestorId,
        ConnectionAndTransactionHolder? conn,
        string lockName,
        CancellationToken cancellationToken = default)
    {
        var isDebugEnabled = logger.IsEnabled(LogLevel.Debug);
        if (isDebugEnabled)
        {
            logger.LogDebug("Lock '{LockName}' is desired by: {RequestorId}", lockName, requestorId);
        }

        var key = new ThreadLockKey(requestorId, lockName);
        if (!IsLockOwner(key))
        {
            await ExecuteSQL(requestorId, conn!, lockName, expandedSQL, expandedInsertSQL, cancellationToken)
                .ConfigureAwait(false);

            if (isDebugEnabled)
            {
                logger.LogDebug("Lock '{LockName}' given to: {RequestorId}", lockName, requestorId);
            }

            return locks.TryAdd(key, null);
        }
        else
        {
            if (isDebugEnabled)
            {
                logger.LogDebug("Lock '{LockName}' Is already owned by: {RequestorId}", lockName, requestorId);
            }
            return false;
        }
    }

    /// <summary>
    /// Release the lock on the identified resource if it is held by the calling
    /// thread.
    /// </summary>
    public ValueTask ReleaseLock(
        Guid requestorId,
        string lockName,
        CancellationToken cancellationToken = default)
    {
        var key = new ThreadLockKey(requestorId, lockName);
        if (IsLockOwner(key))
        {
            locks.TryRemove(key, out _);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Lock '{LockName}' returned by: {RequestorId}", lockName, requestorId);
            }
        }
        else if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning("Lock '{LockName}' attempt to return by: {RequestorId} -- but not owner!", lockName, requestorId);
            logger.LogWarning("stack-trace of wrongful returner: {Stacktrace}", Environment.StackTrace);
        }

        return default;
    }

    /// <summary>
    /// Determine whether the calling thread owns a lock on the identified
    /// resource.
    /// </summary>
    private bool IsLockOwner(in ThreadLockKey key)
    {
        return locks.ContainsKey(key);
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
            if (!string.IsNullOrWhiteSpace(value))
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
            if (!string.IsNullOrWhiteSpace(value))
            {
                insertSql = value.Trim();
            }

            SetExpandedSql();
        }
    }

    private void SetExpandedSql()
    {
        if (TablePrefix is not null && sql is not null && insertSql is not null)
        {
            expandedSQL = AdoJobStoreUtil.ReplaceTablePrefix(sql, TablePrefix);
            expandedInsertSQL = AdoJobStoreUtil.ReplaceTablePrefix(insertSql, TablePrefix);
        }
    }

    public string? SchedName
    {
        get => schedName;
        set => schedName = value;
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

    protected IAdoUtil AdoUtil { get; }

    private readonly struct ThreadLockKey : IEquatable<ThreadLockKey>
    {
        private readonly Guid requestorId;
        private readonly string lockName;
        private readonly int hashCode;

        public ThreadLockKey(Guid requestorId, string lockName)
        {
            this.requestorId = requestorId;
            this.lockName = lockName;
            hashCode = (requestorId.GetHashCode() * 397) ^ lockName.GetHashCode();
        }

        public bool Equals(ThreadLockKey other)
            => requestorId.Equals(other.requestorId) && ReferenceEquals(lockName, other.lockName);

        public override bool Equals(object? obj) => obj is ThreadLockKey other && Equals(other);

        public override int GetHashCode() => hashCode;
    }
}