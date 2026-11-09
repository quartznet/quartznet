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

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Asks a <see cref="DbTransaction" /> for its connection in a way every driver answers alike.
/// </summary>
internal static class DbTransactionConnections
{
    /// <summary>
    /// The connection <paramref name="transaction" /> is bound to, or <see langword="null" /> when the
    /// driver has detached the transaction from it or disposed the transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Drivers say that a transaction has ended in one of two ways. SqlClient, MySqlConnector and
    /// Microsoft.Data.Sqlite detach it, and <see cref="DbTransaction.Connection" /> reads
    /// <see langword="null" />. Npgsql disposes it when its connector breaks — a command timeout, a
    /// backend the server terminated — and from then on its getter throws
    /// <see cref="ObjectDisposedException" /> instead of answering (#4255). A disposed transaction has
    /// ended whoever disposed it, so the two mean the same here.
    /// </para>
    /// <para>
    /// Only <see cref="ObjectDisposedException" /> is caught: it is the one answer that says the
    /// transaction is gone, and anything else a driver throws is a failure of its own.
    /// </para>
    /// </remarks>
    /// <param name="transaction">The transaction to ask.</param>
    internal static DbConnection? ConnectionUnlessDisposed(this DbTransaction transaction)
    {
        try
        {
            return transaction.Connection;
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }
}
