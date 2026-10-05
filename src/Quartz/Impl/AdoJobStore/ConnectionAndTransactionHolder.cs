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
using System.Data;
using System.Data.Common;

using Quartz.Logging;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Unit of work for AdoJobStore operations.
/// </summary>
/// <author>Marko Lahma</author>
public class ConnectionAndTransactionHolder : IDisposable
{
    private DateTimeOffset? sigChangeForTxCompletion;

    private readonly DbConnection connection;
    private DbTransaction? transaction;
    private readonly bool ownsResources;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionAndTransactionHolder"/> class.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="transaction">The transaction.</param>
    public ConnectionAndTransactionHolder(DbConnection connection, DbTransaction? transaction)
        : this(connection, transaction, ownsResources: true)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionAndTransactionHolder"/> class.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="transaction">The transaction.</param>
    /// <param name="ownsResources">
    /// Whether this unit of work owns the connection and transaction. When <see langword="false" />
    /// they belong to the caller, who enlisted them via
    /// <see cref="SchedulerEnlistmentExtensions.EnlistTransaction" />, and this holder will neither
    /// commit, roll back, close nor dispose them.
    /// </param>
    /// <param name="borrowedFrom">
    /// The enlistment the connection was borrowed from, so its single-use claim can be returned to
    /// that exact entry when this unit of work is cleaned up.
    /// </param>
    internal ConnectionAndTransactionHolder(
        DbConnection connection,
        DbTransaction? transaction,
        bool ownsResources,
        EnlistedConnection? borrowedFrom = null)
    {
        this.connection = connection;
        this.transaction = transaction;
        this.ownsResources = ownsResources;
        BorrowedFrom = borrowedFrom;
    }

    /// <summary>
    /// The enlistment this unit of work borrowed its connection from, so that the claim is returned
    /// to that exact entry rather than to whatever is enlisted by the time cleanup runs.
    /// </summary>
    internal EnlistedConnection? BorrowedFrom { get; }

    public DbConnection Connection => connection;

    public DbTransaction? Transaction => transaction;

    /// <summary>
    /// Whether this unit of work owns the connection and the transaction. When it does not, the
    /// application enlisted them and is responsible for committing, rolling back and disposing them.
    /// </summary>
    internal bool OwnsResources => ownsResources;

    public void Attach(DbCommand cmd)
    {
        cmd.Connection = connection;
        cmd.Transaction = transaction;
    }

    public void Commit(bool openNewTransaction)
    {
        if (!ownsResources)
        {
            // The application owns the transaction and decides when it commits.
            return;
        }

        if (transaction != null)
        {
            try
            {
                CheckNotZombied();
                IsolationLevel il = transaction.IsolationLevel;
                transaction.Commit();
                if (openNewTransaction)
                {
                    // open new transaction to go with
                    transaction = connection.BeginTransaction(il);
                }
            }
            catch (Exception e)
            {
                throw new JobPersistenceException("Couldn't commit ADO.NET transaction. " + e.Message, e);
            }
        }
    }

    public void Close()
    {
        if (!ownsResources)
        {
            // Borrowed connection, the application keeps using it after we are done.
            return;
        }

        try
        {
            connection.Close();
        }
        catch (Exception e)
        {
            var log = LogProvider.GetLogger(typeof(ConnectionAndTransactionHolder));

            log.ErrorException(
                "Unexpected exception closing Connection." +
                "  This is often due to a Connection being returned after or during shutdown.", e);
        }
    }

    public void Dispose()
    {
        if (!ownsResources)
        {
            // Hand the enlistment back even when disposed directly rather than through
            // CleanupConnection, or its single-use claim would stay held for the rest of the scope.
            BorrowedFrom?.Release();
            return;
        }

        try
        {
            connection?.Dispose();
        }
        catch
        {
            // ignored
        }
        try
        {
            transaction?.Dispose();
        }
        catch
        {
            // ignored
        }
    }

    internal virtual DateTimeOffset? SignalSchedulingChangeOnTxCompletion
    {
        get => sigChangeForTxCompletion;
        set
        {
            DateTimeOffset? sigTime = sigChangeForTxCompletion;
            if (sigChangeForTxCompletion == null && value.HasValue)
            {
                sigChangeForTxCompletion = value;
            }
            else
            {
                if (sigChangeForTxCompletion == null || value < sigTime)
                {
                    sigChangeForTxCompletion = value;
                }
            }
        }
    }

    public void Rollback(bool transientError)
    {
        if (!ownsResources)
        {
            // The application owns the transaction; the failure propagates to it and it decides
            // whether to roll back. Rolling back here would silently discard its work as well.
            return;
        }

        if (transaction != null)
        {
            if (IsTransactionZombied)
            {
                // Transaction lost its connection - nothing to rollback, the database
                // will have already aborted it. This commonly happens with transient
                // connectivity issues (see https://github.com/quartznet/quartznet/issues/2290)
                var log = LogProvider.GetLogger(typeof(ConnectionAndTransactionHolder));
                log.Debug("Rollback skipped - transaction is no longer connected, database will have aborted it");
                return;
            }

            try
            {
                transaction.Rollback();
            }
            catch (Exception e)
            {
                var log = LogProvider.GetLogger(typeof(ConnectionAndTransactionHolder));
                if (transientError)
                {
                    // original error was transient, ones we have in Azure, don't complain too much about it
                    // we will try again anyway
                    log.Debug("Rollback failed due to transient error");
                }
                else
                {
                    log.ErrorException("Couldn't rollback ADO.NET connection. " + e.Message, e);
                }
            }
        }
    }

    /// <summary>
    /// Whether the database has ended this unit of work's transaction from its side — as it does to a
    /// deadlock victim, or to the loser of a write conflict on a memory-optimized table — which the driver
    /// reports by detaching the transaction from its connection. Nothing can be committed or rolled back
    /// in it any more, and SQL Server runs a command still bound to it outside any transaction rather
    /// than refusing it.
    /// </summary>
    internal bool IsTransactionZombied => transaction != null && transaction.Connection == null;

    /// <summary>
    /// The trigger whose failed fire this unit of work has already settled by storing it <c>ERROR</c> —
    /// its job would not load — so that the fire batch commits the failure instead of rolling the
    /// attempt back as it does for any other failed fire (#3931).
    /// </summary>
    internal TriggerKey? SettledFireFailure { get; set; }

    /// <summary>
    /// The store's own bookkeeping to do once this unit of work has committed; <see langword="null" />
    /// until the first.
    /// </summary>
    private List<Action>? actionsAfterCommit;

    /// <summary>
    /// Records bookkeeping of the store's own to do once this unit of work's transaction has committed,
    /// and to drop if it rolls back: a misfire whose calendar threw is counted only then, because a
    /// transaction that rolls back is retried and would meet the same throw again (#4006).
    /// </summary>
    internal void AfterCommit(Action action)
    {
        (actionsAfterCommit ??= new List<Action>()).Add(action);
    }

    /// <summary>
    /// Does what <see cref="AfterCommit" /> recorded, in order, and forgets it. Called by the store's
    /// transaction wrappers once the work has committed, or, where the transaction is the application's,
    /// once the store's own part is done, which is as late as the store can see.
    /// </summary>
    /// <remarks>
    /// One that throws is logged and the rest are done: the work has committed, and bookkeeping must not
    /// fail it. A wrapper that retries on a failure would otherwise run committed work again, and keep
    /// doing so while the bookkeeping kept throwing.
    /// </remarks>
    internal void RunAfterCommit()
    {
        List<Action>? actions = actionsAfterCommit;
        actionsAfterCommit = null;
        if (actions == null)
        {
            return;
        }

        foreach (Action action in actions)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                try
                {
                    LogProvider.GetLogger(typeof(ConnectionAndTransactionHolder)).ErrorException("Work the store does after a committed operation failed; the operation stands, and the rest of that work is done", e);
                }
                catch
                {
                    // The log is what failed; there is nowhere left to say so.
                }
            }
        }
    }

    private void CheckNotZombied()
    {
        if (IsTransactionZombied)
        {
            throw new InvalidOperationException("Transaction not connected, or was disconnected");
        }
    }
}