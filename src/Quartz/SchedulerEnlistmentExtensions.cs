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
using System.Data.Common;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Spi;

namespace Quartz;

/// <summary>
/// Lets application code hand its own ADO.NET connection and transaction to the persistent job
/// store, so that scheduling operations take part in a unit of work the application already owns.
/// </summary>
/// <remarks>
/// <para>
/// Requires the job store to be configured with <c>quartz.jobStore.acceptEnlistedTransactions</c> set to
/// <see langword="true" /> (<c>AcceptEnlistedTransactions()</c> when configuring with
/// <see cref="SchedulerBuilder" />). Without it the job store keeps opening its own connection and
/// managing its own transaction, and the enlistment is ignored.
/// </para>
/// <para>
/// Enlisting is the only way to take part: an ambient
/// <see cref="System.Transactions.TransactionScope" /> on its own is not enough, because a
/// connection the job store opens for itself is deliberately kept out of it. Open the connection
/// inside the scope and enlist that, which also keeps the transaction from having to be promoted to
/// a distributed one.
/// </para>
/// <para>
/// The enlistment flows with the current asynchronous context, so it must be established in the
/// same scope as the scheduler calls it should cover - the same rule that applies to
/// <see cref="System.Transactions.TransactionScope" />, and for the same reason. In particular,
/// establishing it inside an <c>async</c> helper does not carry it back out to the caller.
/// </para>
/// <para>
/// While the enlistment is in effect the job store holds its locks in the caller's transaction, so
/// they are only released once that transaction completes. Keep enlisted transactions short: a long
/// running one blocks trigger acquisition, the misfire handler and cluster check-in.
/// </para>
/// <example>
/// <code>
/// await using var tx = await dbContext.Database.BeginTransactionAsync();
/// dbContext.Add(entity);
/// await dbContext.SaveChangesAsync();
///
/// using (scheduler.EnlistTransaction(tx.GetDbTransaction()))
/// {
///     await scheduler.ScheduleJob(job, trigger);
///     await tx.CommitAsync();
/// }
/// </code>
/// </example>
/// </remarks>
public static class SchedulerEnlistmentExtensions
{
    /// <summary>
    /// Makes the persistent job store use the given transaction, and the connection it belongs to,
    /// for every operation performed on the current asynchronous flow until the returned scope is
    /// disposed.
    /// </summary>
    /// <param name="scheduler">The scheduler whose job store should join the transaction.</param>
    /// <param name="transaction">The transaction to join. Must still be associated with a connection.</param>
    /// <returns>
    /// A scope that ends the enlistment when disposed. Dispose it after committing, so that any
    /// scheduling change the job store recorded is signalled to the scheduler once it is visible.
    /// </returns>
    public static IDisposable EnlistTransaction(this IScheduler scheduler, DbTransaction transaction)
    {
        if (scheduler is null)
        {
            throw new ArgumentNullException(nameof(scheduler));
        }

        if (transaction is null)
        {
            throw new ArgumentNullException(nameof(transaction));
        }

        DbConnection connection = transaction.Connection
                                  ?? throw new ArgumentException(
                                      "The transaction is not associated with a connection, it has already been committed, rolled back or disposed.",
                                      nameof(transaction));

        EnsureEnlistmentAccepted(scheduler, connection);

        return AmbientConnection.Enlist(scheduler.SchedulerName, connection, transaction);
    }

    /// <summary>
    /// Makes the persistent job store use the given connection, and optionally the given
    /// transaction, for every operation performed on the current asynchronous flow until the
    /// returned scope is disposed.
    /// </summary>
    /// <remarks>
    /// Pass only a connection when the transaction is an ambient
    /// <see cref="System.Transactions.TransactionScope" /> the connection is already enlisted in.
    /// Sharing the one connection is what keeps such a scope from being promoted to a distributed
    /// transaction, which providers like Npgsql do not support at all.
    /// </remarks>
    /// <param name="scheduler">The scheduler whose job store should use the connection.</param>
    /// <param name="connection">The connection to use. Opened if it is not open already.</param>
    /// <param name="transaction">The transaction to enlist in, if any.</param>
    /// <returns>
    /// A scope that ends the enlistment when disposed. Dispose it after committing, so that any
    /// scheduling change the job store recorded is signalled to the scheduler once it is visible.
    /// </returns>
    public static IDisposable EnlistConnection(
        this IScheduler scheduler,
        DbConnection connection,
        DbTransaction? transaction = null)
    {
        if (scheduler is null)
        {
            throw new ArgumentNullException(nameof(scheduler));
        }

        if (connection is null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        // A transaction from some other connection governs nothing here: providers take the
        // connection's own transaction and ignore the one on the command, so every statement would
        // commit on the spot while the caller believes rolling that transaction back undoes them.
        if (transaction is not null && !ReferenceEquals(transaction.Connection, connection))
        {
            throw new ArgumentException(
                "The transaction belongs to a different connection, so it would not govern anything the job store does on "
                + "this one. Pass the transaction that was begun on this connection.",
                nameof(transaction));
        }

        // With neither a transaction of its own nor an ambient one to enlist in, every statement the
        // job store issues on this connection commits on the spot. That looks exactly like a working
        // enlistment right up to the point where the caller rolls back and the job fires anyway.
        if (transaction is null && System.Transactions.Transaction.Current is null)
        {
            throw new ArgumentException(
                "The connection has no transaction to join: pass the DbTransaction, or call this from inside a "
                + "TransactionScope created with TransactionScopeAsyncFlowOption.Enabled. Enlisting a connection with "
                + "neither would let scheduling commit on its own.",
                nameof(connection));
        }

        EnsureEnlistmentAccepted(scheduler, connection);

        System.Transactions.Transaction? ambient = transaction is null ? System.Transactions.Transaction.Current : null;
        if (ambient is not null)
        {
            JoinAmbientTransaction(scheduler, connection, ambient);
        }

        // The ambient transaction is remembered so a later operation can tell that the scope has since
        // ended, instead of quietly running with no transaction at all.
        return AmbientConnection.Enlist(scheduler.SchedulerName, connection, transaction, ambient);
    }

    /// <summary>
    /// Establishes that the connection really does take part in the ambient transaction, rather than
    /// assuming it does because there is one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A connection opened inside a <see cref="System.Transactions.TransactionScope" /> joins it as it
    /// opens — unless its driver implements no enlistment at all, in which case nothing happens and
    /// nothing says so. <c>Microsoft.Data.Sqlite</c> is such a driver: its connection overrides no
    /// <see cref="DbConnection.EnlistTransaction" />, so a call reaches the base implementation, which
    /// throws <see cref="NotSupportedException" />. Accepting that connection would have the job store
    /// write through it with every statement committing on the spot, and a scope that was never
    /// completed would leave the schedule behind — which looks exactly like a working enlistment right
    /// up to the first rollback.
    /// </para>
    /// <para>
    /// Asking the connection to join the transaction it is supposed to be in already is what tells the
    /// two apart, and every driver Quartz ships a delegate for treats that as a no-op: SqlClient and
    /// Npgsql return early when their enlisted transaction equals this one, MySqlConnector and Firebird
    /// on the same comparison, and Oracle re-records the transaction and traces "already enlisted" when
    /// the local identifier matches. A connection that is <em>not</em> yet enlisted — opened before the
    /// scope, or with enlistment switched off in its connection string — is enlisted by the call, which
    /// is what the caller was asking for either way.
    /// </para>
    /// <para>
    /// Only <see cref="NotSupportedException" /> is a refusal. Anything else a driver answers means it
    /// has an enlistment implementation and an opinion about this particular connection — "the
    /// connection is not open" is the ordinary one, since an enlisted connection is opened by the job
    /// store rather than here — and having an implementation at all is the whole of what this asks.
    /// </para>
    /// </remarks>
    private static void JoinAmbientTransaction(IScheduler scheduler, DbConnection connection, System.Transactions.Transaction ambient)
    {
        try
        {
            connection.EnlistTransaction(ambient);
        }
        catch (NotSupportedException e)
        {
            throw new SchedulerException(
                $"Scheduler '{scheduler.SchedulerName}' cannot take part in the ambient transaction through a "
                + $"{connection.GetType().FullName} ({connection.GetType().Assembly.GetName().Name}): the driver implements no "
                + "DbConnection.EnlistTransaction, so the connection never joined the TransactionScope and every statement the "
                + "job store issued on it would commit on the spot - a scope that rolled back would leave the schedule behind. "
                + "Begin a transaction on the connection and enlist that instead: "
                + "scheduler.EnlistTransaction(connection.BeginTransaction()).",
                e);
        }
        catch (Exception)
        {
            // See the remarks: a driver that answers anything but "not supported" has an enlistment of
            // its own, which is what was being established. Letting its answer out would refuse the
            // ordinary case, where the connection is still closed because the job store opens it.
        }
    }

    /// <summary>
    /// Fails fast when the job store would ignore the enlistment. Without this the scheduling would
    /// quietly commit in a transaction of its own while the caller believes it is part of theirs -
    /// the exact split unit of work these methods exist to prevent, and one that only shows up as a
    /// job firing for an entity that was rolled back.
    /// </summary>
    private static void EnsureEnlistmentAccepted(IScheduler scheduler, DbConnection connection)
    {
#if REMOTING
        // A remoting proxy's job store lives in another process, so an enlistment on this side can
        // never reach it - the one case where the answer is knowable without inspecting anything.
        if (scheduler is Quartz.Impl.RemoteScheduler)
        {
            throw new InvalidOperationException(
                $"Scheduler '{scheduler.SchedulerName}' is a remote proxy, so its job store cannot use a connection from "
                + "this process and the enlistment would be ignored. Take part in the application's transaction only "
                + "through a scheduler backed by a local persistent job store.");
        }
#endif

        // A decorator - logging, tenant routing, metrics - forwards SchedulerName, so look the real
        // scheduler up by name rather than giving up on anything that is not StdScheduler. Giving up
        // would silently skip every check below for exactly the wrappers that are common in the
        // applications this feature is aimed at.
        StdScheduler? local = scheduler as StdScheduler
                              ?? SchedulerRepository.Instance.Lookup(scheduler.SchedulerName) as StdScheduler;

        if (local is null)
        {
            return;
        }

        IJobStore jobStore = local.sched.JobStore;

        if (jobStore is not JobStoreSupport adoJobStore)
        {
            throw new InvalidOperationException(
                $"Scheduler '{scheduler.SchedulerName}' uses {jobStore.GetType().Name}, which does not store anything in the "
                + "application's database, so the enlistment would be ignored and scheduling would survive a rollback. "
                + "Taking part in the application's transaction requires a persistent ADO.NET job store.");
        }

        if (!adoJobStore.AcceptEnlistedTransactions)
        {
            throw new InvalidOperationException(
                $"Scheduler '{scheduler.SchedulerName}' is not configured to take part in transactions the application owns, "
                + "so the enlistment would be ignored and scheduling would commit on its own. Set "
                + "'quartz.jobStore.acceptEnlistedTransactions' to true, or call AcceptEnlistedTransactions() when configuring the persistent store.");
        }

        // The job store builds its commands from its own configured provider, and assigning a
        // connection of a different provider's type to one of those commands throws a cast error deep
        // in the first statement, naming two identically-named connection types and nothing else.
        Type? expected = adoJobStore.ConnectionType;
        if (expected != null && !expected.IsInstanceOfType(connection))
        {
            throw new ArgumentException(
                $"Scheduler '{scheduler.SchedulerName}' is configured with a data source that produces {expected.FullName} "
                + $"({expected.Assembly.GetName().Name}), but the enlisted connection is {connection.GetType().FullName} "
                + $"({connection.GetType().Assembly.GetName().Name}). The job store cannot use a connection from a "
                + "different provider - configure both against the same one.",
                nameof(connection));
        }
    }
}
