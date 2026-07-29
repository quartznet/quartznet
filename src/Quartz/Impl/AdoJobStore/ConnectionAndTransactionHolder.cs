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

using System.Data;
using System.Data.Common;

using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Unit of work for AdoJobStore operations.
/// </summary>
/// <author>Marko Lahma</author>
public sealed class ConnectionAndTransactionHolder : IDisposable
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
    /// Whether this unit of work owns the connection and the transaction. When it does not, the
    /// application enlisted them and is responsible for committing, rolling back and disposing them.
    /// </summary>
    internal bool OwnsResources => ownsResources;

    /// <summary>
    /// The enlistment this unit of work borrowed its connection from, so that the claim is returned
    /// to that exact entry rather than to whatever is enlisted by the time cleanup runs.
    /// </summary>
    internal EnlistedConnection? BorrowedFrom { get; }

    public DbConnection Connection => connection;

    public DbTransaction? Transaction => transaction;

    public void Attach(DbCommand cmd)
    {
        cmd.Connection = connection;
        cmd.Transaction = transaction;
    }

    /// <summary>
    /// Whether the underlying provider can execute several statements as one <see cref="DbBatch" />,
    /// i.e. in a single round-trip. Defaults to <see langword="false" /> on <see cref="DbConnection" />,
    /// so providers without batching support simply report no support and callers fall back to issuing
    /// the statements one at a time.
    /// </summary>
    public bool CanCreateBatch => connection.CanCreateBatch;

    /// <summary>
    /// Creates a <see cref="DbBatch" /> enlisted in this unit of work. Only valid when
    /// <see cref="CanCreateBatch" /> is <see langword="true" />.
    /// </summary>
    public DbBatch CreateBatch()
    {
        DbBatch batch = connection.CreateBatch();
        batch.Connection = connection;
        batch.Transaction = transaction;
        return batch;
    }

    public async ValueTask Commit(bool openNewTransaction, CancellationToken cancellationToken = default)
    {
        if (!ownsResources)
        {
            // The application owns the transaction and decides when it commits.
            return;
        }

        if (transaction is not null)
        {
            try
            {
                CheckNotZombied();
                IsolationLevel il = transaction.IsolationLevel;
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                if (openNewTransaction)
                {
                    // open new transaction to go with
                    transaction = await connection.BeginTransactionAsync(il, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Throw.JobPersistenceException("Couldn't commit ADO.NET transaction. " + e.Message, e);
            }
        }
    }

    public async ValueTask Close(CancellationToken cancellationToken = default)
    {
        if (!ownsResources)
        {
            // Borrowed connection, the application keeps using it after we are done.
            return;
        }

        try
        {
            await connection.CloseAsync().ConfigureAwait(false);
        }
        catch (Exception e)
        {
            var log = LogProvider.CreateLogger<ConnectionAndTransactionHolder>();

            log.LogError(e,
                "Unexpected exception closing Connection." +
                "  This is often due to a Connection being returned after or during shutdown.");
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

    internal DateTimeOffset? SignalSchedulingChangeOnTxCompletion
    {
        get => sigChangeForTxCompletion;
        set
        {
            DateTimeOffset? sigTime = sigChangeForTxCompletion;
            if (sigChangeForTxCompletion is null && value.HasValue)
            {
                sigChangeForTxCompletion = value;
            }
            else
            {
                if (sigChangeForTxCompletion is null || value < sigTime)
                {
                    sigChangeForTxCompletion = value;
                }
            }
        }
    }

    public async ValueTask Rollback(bool transientError, CancellationToken cancellationToken = default)
    {
        if (!ownsResources)
        {
            // The application owns the transaction; the failure propagates to it and it decides
            // whether to roll back. Rolling back here would silently discard its work as well.
            return;
        }

        if (transaction is not null)
        {
            if (transaction.Connection is null)
            {
                // Transaction lost its connection - nothing to rollback, the database
                // will have already aborted it. This commonly happens with transient
                // connectivity issues (see https://github.com/quartznet/quartznet/issues/2290)
                var log = LogProvider.CreateLogger<ConnectionAndTransactionHolder>();
                log.LogDebug("Rollback skipped - transaction is no longer connected, database will have aborted it");
                return;
            }

            try
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                var log = LogProvider.CreateLogger<ConnectionAndTransactionHolder>();
                if (transientError)
                {
                    // original error was transient, ones we have in Azure, don't complain too much about it
                    // we will try again anyway
                    log.LogDebug("Rollback failed due to transient error");
                }
                else
                {
                    log.LogError(e, "Couldn't rollback ADO.NET connection. {ExceptionMessage}", e.Message);
                }
            }
        }
    }

    private void CheckNotZombied()
    {
        if (transaction is not null && transaction.Connection is null)
        {
            Throw.InvalidOperationException("Transaction not connected, or was disconnected");
        }
    }
}