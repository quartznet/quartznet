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
using System.Data.Common;
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
public abstract class DbLockHandler : ILockHandler
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
    /// Initializes a new instance of the <see cref="DbLockHandler"/> class.
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
    protected DbLockHandler(
        string tablePrefix,
        string? schedulerName,
        string sql,
        string insertSql,
        IDbProvider dbProvider)
    {
        logger = LogProvider.CreateLogger<DbLockHandler>();
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
    /// <remarks>
    /// Replaced by <see cref="Initialize" /> with one from the job store's factory. The category stays
    /// this type's rather than the subclass's, so a filter written against it matches whichever row-lock
    /// dialect a scheduler ends up with.
    /// </remarks>
    /// <value>The log.</value>
    internal ILogger<DbLockHandler> logger { get; private set; }

    /// <summary>
    /// Learns which scheduler this handler locks for and folds the store's table prefix into
    /// both statements. The job store calls this once before the handler is used, whether the
    /// store built the handler itself or the container supplied it.
    /// </summary>
    /// <remarks>
    /// The command timeout arrives here too, so the accessor is rebuilt rather than reconfigured: it is
    /// only ever replaced on this one call, before any lock has been taken.
    /// </remarks>
    public void Initialize(LockHandlerContext context)
    {
        schedulerName = context.SchedulerName;
        tablePrefix = context.TablePrefix;
        TimeProvider = context.TimeProvider;
        logger = context.LoggerFactory.CreateLogger<DbLockHandler>();
        adoUtil = new AdoUtil(dbProvider, context.CommandTimeout, context.LoggerFactory.CreateLogger<AdoUtil>());
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

    /// <inheritdoc />
    /// <remarks>
    /// Ownership is recorded only once <see cref="ExecuteSql" /> has returned, so a statement that
    /// fails — including one the caller cancelled — leaves this handler holding nothing and the next
    /// acquire by the same requestor is a fresh one rather than a re-entrant one. The row lock itself
    /// belongs to <paramref name="conn" />'s transaction and is given back when that transaction ends.
    /// </remarks>
    public async ValueTask<bool> AcquireLock(
        Guid requestorId,
        ConnectionAndTransactionHolder? conn,
        SchedulerLock lockKind,
        CancellationToken cancellationToken = default)
    {
        string lockName = lockKind.ToLockName();
        var isDebugEnabled = logger.IsEnabled(LogLevel.Debug);
        if (isDebugEnabled)
        {
            logger.LockDesired(lockName, requestorId);
        }

        var key = new ThreadLockKey(requestorId, lockKind);
        if (!IsLockOwner(key))
        {
            await ExecuteSql(requestorId, conn!, lockName, expandedSql, expandedInsertSql, cancellationToken)
                .ConfigureAwait(false);

            if (isDebugEnabled)
            {
                logger.LockGiven(lockName, requestorId);
            }

            return locks.TryAdd(key, null);
        }
        else
        {
            if (isDebugEnabled)
            {
                logger.LockAlreadyHeld(lockName, requestorId);
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
                logger.LockReturned(lockKind.ToLockName(), requestorId);
            }
        }
        else if (logger.IsEnabled(LogLevel.Warning))
        {
            logger.LockReturnedByNonOwner(lockKind.ToLockName(), requestorId);
            logger.WrongfulReturnerStack(Environment.StackTrace);
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
    /// This lock handler does use the database.
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
    /// Name of the scheduler whose lock rows this handler contends for, told to the handler
    /// through <see cref="Initialize" />.
    /// </summary>
    public string? SchedulerName => schedulerName;

    /// <summary>
    /// Table prefix of the tables the ADO.NET job store uses, told to the handler through
    /// <see cref="Initialize" />.
    /// </summary>
    public string TablePrefix => tablePrefix;

    /// <summary>
    /// The clock this handler backs off on between attempts, told to it through
    /// <see cref="Initialize" />. Defaults to <see cref="System.TimeProvider.System" /> for a handler
    /// used before the store has initialized it.
    /// </summary>
    protected TimeProvider TimeProvider { get; private set; } = TimeProvider.System;

    /// <summary>
    /// Prepares one of this handler's statements against the unit of work
    /// <see cref="ExecuteSql" /> was handed, attached to its connection and transaction and carrying the
    /// store's command timeout.
    /// </summary>
    /// <remarks>
    /// This and <see cref="AddCommandParameter" /> are what a lock handler of your own issues its lock
    /// statement through. The accessor behind them stays out of reach — how a command is minted and how
    /// a parameter is named differ by driver, and are not a contract — but a subclass that could not
    /// prepare a command at all had no way to implement <see cref="ExecuteSql" />, which is the one
    /// method it exists to implement.
    /// </remarks>
    /// <param name="conn">The unit of work the statement runs in.</param>
    /// <param name="commandText">The statement, with its table prefix already folded in.</param>
    protected DbCommand PrepareCommand(ConnectionAndTransactionHolder conn, string commandText)
    {
        return adoUtil.PrepareCommand(conn, commandText);
    }

    /// <summary>
    /// Binds a parameter to a command prepared by <see cref="PrepareCommand" />, rewriting the
    /// statement's <c>@name</c> placeholder for drivers that do not use <c>@</c> or that bind by
    /// position.
    /// </summary>
    /// <remarks>
    /// There is no overload taking a provider-specific data type or a size, because a lock statement
    /// binds a scheduler name and a lock name and both are strings. A handler that needs to bind
    /// something else is not locking a Quartz lock row.
    /// </remarks>
    /// <param name="command">The command to bind to.</param>
    /// <param name="paramName">Name of the parameter, without the driver's prefix.</param>
    /// <param name="paramValue">Value to bind; <see langword="null" /> binds as <see cref="DBNull" />.</param>
    protected void AddCommandParameter(DbCommand command, string paramName, object? paramValue)
    {
        adoUtil.AddCommandParameter(command, paramName, paramValue);
    }

    /// <remarks>
    /// <c>private protected</c> because <see cref="IAdoUtil" /> is an implementation detail: command
    /// preparation and parameter naming are not something an out-of-assembly handler should reach into.
    /// <see cref="PrepareCommand" /> and <see cref="AddCommandParameter" /> are what it uses instead.
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