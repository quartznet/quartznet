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

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Unit of work for AdoJobStore operations.
    /// </summary>
    /// <author>Marko Lahma</author>
    public class ConnectionAndTransactionHolder : IDisposable
    {
        private static readonly ILog log = LogProvider.GetLogger(typeof(ConnectionAndTransactionHolder));

        private DateTimeOffset? sigChangeForTxCompletion;

        private readonly DbConnection connection;
        private DbTransaction transaction;

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionAndTransactionHolder"/> class.
        /// </summary>
        /// <param name="connection">The connection.</param>
        /// <param name="transaction">The transaction.</param>
        public ConnectionAndTransactionHolder(DbConnection connection, DbTransaction transaction)
        {
            this.connection = connection;
            this.transaction = transaction;
        }

        public DbConnection Connection => connection;

        public DbTransaction Transaction => transaction;

        public void Attach(DbCommand cmd)
        {
            cmd.Connection = connection;
            cmd.Transaction = transaction;
        }

        public void Commit(bool openNewTransaction)
        {
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
            if (connection != null)
            {
                try
                {
                    connection.Close();
                }
                catch (Exception e)
                {
                    log.ErrorException(
                        "Unexpected exception closing Connection." +
                        "  This is often due to a Connection being returned after or during shutdown.", e);
                }
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
            if (transaction != null)
            {
                try
                {
                    CheckNotZombied();
                    transaction.Rollback();
                }
                catch (Exception e)
                {
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
}