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
using System.Threading.Tasks;

using Npgsql;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// The store's unit of work over an Npgsql transaction whose backend the server has terminated (#4255).
/// </summary>
/// <remarks>
/// Npgsql learns that the backend is gone the next time it talks to it, and breaking the connector then
/// disposes the transaction: from that point <c>NpgsqlTransaction.Connection</c> throws
/// <see cref="ObjectDisposedException" /> rather than answering <see langword="null" /> as SqlClient's
/// does. The unit tests stand in a transaction that behaves the same way; this is the driver itself.
/// </remarks>
[NonParallelizable]
[Category("db-postgres")]
public sealed class DisposedTransactionPostgresTest
{
    [Test]
    public async Task ATransactionWhoseBackendWasTerminatedIsReportedDisconnectedAndItsRollbackSkipped()
    {
        string connectionString = TestConstants.PostgresConnectionString;

        await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        NpgsqlTransaction transaction = connection.BeginTransaction();

        await using (NpgsqlConnection admin = new NpgsqlConnection(connectionString))
        {
            await admin.OpenAsync();
            using NpgsqlCommand terminate = new NpgsqlCommand("SELECT pg_terminate_backend(@pid)", admin);
            terminate.Parameters.AddWithValue("pid", connection.ProcessID);
            (await terminate.ExecuteScalarAsync()).Should().Be(true, "the premise: the backend was there to terminate");
        }

        using (NpgsqlCommand probe = new NpgsqlCommand("SELECT 1", connection, transaction))
        {
            Func<Task> touch = async () => await probe.ExecuteScalarAsync();
            await touch.Should().ThrowAsync<Exception>("the backend is gone");
        }

        Func<NpgsqlConnection> askForConnection = () => transaction.Connection;
        askForConnection.Should().Throw<ObjectDisposedException>(
            "the premise: this is how Npgsql answers once breaking the connector has disposed the transaction");

        using ConnectionAndTransactionHolder holder = new ConnectionAndTransactionHolder(connection, transaction);

        holder.IsTransactionZombied.Should().BeTrue("a disposed transaction has ended");

        Action commit = () => holder.Commit(openNewTransaction: false);
        commit.Should().Throw<JobPersistenceException>()
            .WithInnerExceptionExactly<InvalidOperationException>()
            .WithMessage("Transaction not connected, or was disconnected");

        Action rollback = () => holder.Rollback(transientError: false);
        rollback.Should().NotThrow(
            "the database has aborted the transaction already, and the store's own failure is the one to report");
    }
}
