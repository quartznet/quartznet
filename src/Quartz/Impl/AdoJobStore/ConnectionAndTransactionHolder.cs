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
            if (transaction.Connection == null)
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

    private void CheckNotZombied()
    {
        if (transaction != null && transaction.Connection == null)
        {
            throw new InvalidOperationException("Transaction not connected, or was disconnected");
        }
    }
}