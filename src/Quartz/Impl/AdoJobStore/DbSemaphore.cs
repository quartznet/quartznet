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
using Quartz.Diagnostics;
using Quartz.Impl.AdoJobStore.Common;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Base class for database based lock handlers for providing thread/resource locking
/// in order to protect resources from being altered by multiple threads at the
/// same time.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
public abstract class DbSemaphore : ISemaphore
{
    private readonly ConcurrentDictionary<ThreadLockKey, object?> locks = new();

    private readonly string sql;
    private readonly string insertSql;
    private readonly IDbProvider dbProvider;

    private string tablePrefix;

    private string? schedulerName;

    private string expandedSql = null!;
    private string expandedInsertSql = null!;

    /// <summary>
    /// Initializes a new instance of the <see cref="DbSemaphore"/> class.
    /// </summary>
    /// <remarks>
    /// The two statements are fixed at construction. They were settable, which meant a subclass could
    /// swap them after the table prefix had already been folded in, and the two halves of the lock -
    /// the select and the insert that backs it - could disagree about which table they were talking to.
    /// </remarks>
    /// <param name="tablePrefix">The table prefix.</param>
    /// <param name="schedulerName">the scheduler name</param>
    /// <param name="insertSql">The statement that inserts the lock row when it does not exist yet.</param>
    /// <param name="sql">The statement that takes the lock.</param>
    /// <param name="dbProvider">The db provider.</param>
    protected DbSemaphore(
        string tablePrefix,
        string? schedulerName,
        string sql,
        string insertSql,
        IDbProvider dbProvider)
    {
        logger = LogProvider.CreateLogger<DbSemaphore>();
        this.schedulerName = schedulerName;
        this.tablePrefix = tablePrefix;
        this.sql = sql.Trim();
        this.insertSql = insertSql.Trim();
        this.dbProvider = dbProvider;
        adoUtil = new AdoUtil(dbProvider);
        SetExpandedSql();
    }

    /// <summary>
    /// Gets the log.
    /// </summary>
    /// <value>The log.</value>
    internal ILogger<DbSemaphore> logger { get; }

    /// <summary>
    /// Learns which scheduler this semaphore locks for and folds the store's table prefix into
    /// both statements. The job store calls this once before the semaphore is used, whether the
    /// store built the handler itself or the container supplied it.
    /// </summary>
    /// <remarks>
    /// The command timeout arrives here too, so the accessor is rebuilt rather than reconfigured: it is
    /// only ever replaced on this one call, before any lock has been taken.
    /// </remarks>
    public void Initialize(SemaphoreContext context)
    {
        schedulerName = context.SchedulerName;
        tablePrefix = context.TablePrefix;
        TimeProvider = context.TimeProvider;
        adoUtil = new AdoUtil(dbProvider, context.CommandTimeout);
        SetExpandedSql();
    }

    /// <summary>
    /// Execute the SQL that will lock the proper database row.
    /// </summary>
    protected abstract ValueTask ExecuteSql(
        Guid requestorId,
        ConnectionAndTransactionHolder conn,
        string lockName,
        string expandedSql,
        string expandedInsertSql,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Grants a lock on the identified resource to the calling thread (blocking
    /// until it is available).
    /// </summary>
    /// <returns>true if the lock was obtained.</returns>
    public async ValueTask<bool> ObtainLock(
        Guid requestorId,
        ConnectionAndTransactionHolder? conn,
        SchedulerLock lockKind,
        CancellationToken cancellationToken = default)
    {
        string lockName = lockKind.ToLockName();
        var isDebugEnabled = logger.IsEnabled(LogLevel.Debug);
        if (isDebugEnabled)
        {
            logger.LogDebug("Lock '{LockName}' is desired by: {RequestorId}", lockName, requestorId);
        }

        var key = new ThreadLockKey(requestorId, lockKind);
        if (!IsLockOwner(key))
        {
            await ExecuteSql(requestorId, conn!, lockName, expandedSql, expandedInsertSql, cancellationToken)
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
        SchedulerLock lockKind,
        CancellationToken cancellationToken = default)
    {
        var key = new ThreadLockKey(requestorId, lockKind);
        if (IsLockOwner(key))
        {
            locks.TryRemove(key, out _);

            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Lock '{LockName}' returned by: {RequestorId}", lockKind.ToLockName(), requestorId);
            }
        }
        else if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LogWarning("Lock '{LockName}' attempt to return by: {RequestorId} -- but not owner!", lockKind.ToLockName(), requestorId);
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

    /// <summary>
    /// The statement that takes the lock, before the table prefix is folded in.
    /// </summary>
    protected string LockSql => sql;

    /// <summary>
    /// The statement that inserts the lock row when it does not exist yet, before the table prefix is
    /// folded in.
    /// </summary>
    protected string InsertSql => insertSql;

    private void SetExpandedSql()
    {
        expandedSql = AdoJobStoreUtil.ReplaceTablePrefix(sql, tablePrefix);
        expandedInsertSql = AdoJobStoreUtil.ReplaceTablePrefix(insertSql, tablePrefix);
    }

    /// <summary>
    /// Name of the scheduler whose lock rows this semaphore contends for, told to the semaphore
    /// through <see cref="Initialize" />.
    /// </summary>
    public string? SchedulerName => schedulerName;

    /// <summary>
    /// Table prefix of the tables the ADO.NET job store uses, told to the semaphore through
    /// <see cref="Initialize" />.
    /// </summary>
    public string TablePrefix => tablePrefix;

    /// <summary>
    /// The clock this semaphore backs off on between attempts, told to it through
    /// <see cref="Initialize" />. Defaults to <see cref="System.TimeProvider.System" /> for a handler
    /// used before the store has initialized it.
    /// </summary>
    protected TimeProvider TimeProvider { get; private set; } = TimeProvider.System;

    /// <remarks>
    /// <c>private protected</c> because <see cref="IAdoUtil" /> is an implementation detail: command
    /// preparation and parameter naming are not something an out-of-assembly semaphore should reach into.
    /// </remarks>
    private protected IAdoUtil AdoUtil => adoUtil;

    private AdoUtil adoUtil;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private readonly struct ThreadLockKey : IEquatable<ThreadLockKey>
    {
        private readonly Guid requestorId;
        private readonly SchedulerLock lockKind;
        private readonly int hashCode;

        public ThreadLockKey(Guid requestorId, SchedulerLock lockKind)
        {
            this.requestorId = requestorId;
            this.lockKind = lockKind;
            hashCode = (requestorId.GetHashCode() * 397) ^ (int) lockKind;
        }

        public bool Equals(ThreadLockKey other)
            => requestorId.Equals(other.requestorId) && lockKind == other.lockKind;

        public override bool Equals(object? obj) => obj is ThreadLockKey other && Equals(other);

        public override int GetHashCode() => hashCode;
    }
}