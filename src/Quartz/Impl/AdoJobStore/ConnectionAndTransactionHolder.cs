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

    /// <summary>
    /// Initializes a new instance of the <see cref="ConnectionAndTransactionHolder"/> class.
    /// </summary>
    /// <param name="connection">The connection.</param>
    /// <param name="transaction">The transaction.</param>
    public ConnectionAndTransactionHolder(DbConnection connection, DbTransaction? transaction)
    {
        this.connection = connection;
        this.transaction = transaction;
    }

    public DbConnection Connection => connection;

    public DbTransaction? Transaction => transaction;

    public void Attach(DbCommand cmd)
    {
        cmd.Connection = connection;
        cmd.Transaction = transaction;
    }

    public async ValueTask Commit(bool openNewTransaction)
    {
        if (transaction is not null)
        {
            try
            {
                CheckNotZombied();
                IsolationLevel il = transaction.IsolationLevel;
                await transaction.CommitAsync().ConfigureAwait(false);
                if (openNewTransaction)
                {
                    // open new transaction to go with
                    transaction = await connection.BeginTransactionAsync(il).ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                ThrowHelper.ThrowJobPersistenceException("Couldn't commit ADO.NET transaction. " + e.Message, e);
            }
        }
    }

    public async ValueTask Close()
    {
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

    public async ValueTask Rollback(bool transientError)
    {
        if (transaction is not null)
        {
            try
            {
                CheckNotZombied();
                await transaction.RollbackAsync().ConfigureAwait(false);
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
            ThrowHelper.ThrowInvalidOperationException("Transaction not connected, or was disconnected");
        }
    }
}