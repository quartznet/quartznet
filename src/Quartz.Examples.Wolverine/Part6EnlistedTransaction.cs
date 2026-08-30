using System.Data.Common;

using Npgsql;

using Wolverine.Persistence.Durability;
using Wolverine.RDBMS;
using Wolverine.Runtime;

namespace Quartz.Examples.Wolverine;

/*
 * Part 6 — one transaction holding the application's row, Wolverine's outgoing envelope and Quartz's
 * trigger.
 *
 * The problem is the one every "schedule something as a side effect" integration hits: a handler
 * writes a row, tells Wolverine to send a message and tells Quartz to schedule a follow-up, and any
 * two of those three can commit while the third does not. Wolverine's outbox already ties the first
 * two together. IScheduler.EnlistTransaction is how the third joins them.
 *
 * What EnlistTransaction does: for the duration of the returned scope, on the current asynchronous
 * flow, the persistent job store uses the given transaction and the connection it belongs to instead
 * of opening its own. Quartz's INSERT into QRTZ_TRIGGERS is then a statement in the caller's
 * transaction, and a rollback takes the trigger with it.
 *
 * The caveats, from SchedulerEnlistmentExtensions' own documentation, all of which bite here:
 *
 *   - It has to be turned on. `ConfigureStore(o => o.AcceptEnlistedTransactions = true)` on the
 *     persistent store builder, or quartz.jobStore.acceptEnlistedTransactions. Without it the store
 *     keeps opening its own connection, and enlisting throws rather than being quietly ignored.
 *
 *   - An ambient TransactionScope on its own is not enough, "because a connection the job store opens
 *     for itself is deliberately kept out of it". Sharing the one connection is also what keeps the
 *     transaction from being promoted to a distributed one, which Npgsql does not support at all.
 *
 *   - The enlistment flows with the current asynchronous context, so it must be established in the
 *     same scope as the scheduler calls it should cover. Establishing it inside an async helper does
 *     not carry it back out to the caller — which is why the `using` below is in the handler body
 *     rather than hidden behind a "ScheduleTransactionally" method.
 *
 *   - The commit belongs INSIDE the using block. Disposing the scope is what signals the scheduling
 *     loop that a trigger appeared, so disposing before the commit would wake it to look for a row it
 *     cannot yet see; the trigger would then wait for the next acquisition sweep instead.
 *
 *   - "While the enlistment is in effect the job store holds its locks in the caller's transaction, so
 *     they are only released once that transaction completes. Keep enlisted transactions short: a long
 *     running one blocks trigger acquisition, the misfire handler and cluster check-in." A message
 *     handler fits that; a batch job that enlists and then works for a minute does not.
 *
 *   - Both stores have to be in one database. Quartz's tables and Wolverine's envelope tables may live
 *     in different schemas, but one DbTransaction cannot span two servers.
 *
 * Why the transaction is opened by hand rather than by [Transactional]: Wolverine's transactional
 * middleware supplies whatever its persistence provider supplies, and on 6.30.3 the raw-ADO.NET
 * Postgres package supplies nothing. Every IPersistenceFrameProvider in the tree belongs to Marten,
 * Entity Framework Core, RavenDB, CosmosDB, Fisher or Polecat; there is none in Wolverine.RDBMS or
 * Wolverine.Postgresql. A handler declaring `[Transactional] Handle(T msg, NpgsqlTransaction tx)`
 * against plain PersistMessagesWithPostgresql compiles and then fails at runtime with
 * "JasperFx was unable to resolve a variable of type Npgsql.NpgsqlTransaction". Adding Marten or EF
 * Core would fix that and is what most Wolverine applications already have — but doing it by hand is
 * the version that shows where the outbox actually joins, and it is the version that lets the commit
 * sit inside the enlistment scope where it belongs.
 */

/// <summary>
/// Writes the application's own row, the outgoing message and the follow-up trigger in one
/// transaction, so that all three commit or none does.
/// </summary>
public static class ApproveRefundHandler
{
    public static async Task Handle(
        ApproveRefund message,
        MessageContext context,
        IWolverineRuntime runtime,
        IScheduler scheduler,
        CancellationToken cancellationToken)
    {
        IMessageDatabase database = (IMessageDatabase) runtime.Storage;

        await using NpgsqlConnection connection = new(ExampleOptions.Current.PostgresConnectionString!);
        await connection.OpenAsync(cancellationToken);
        await using DbTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Wolverine's outgoing envelopes are now written into this transaction rather than sent
        // immediately. This is the manual form of what [Transactional] does for a Marten or EF Core
        // application.
        await context.EnlistInOutboxAsync(new DatabaseEnvelopeTransaction(database, transaction));

        // 1. the application's own state
        await using (NpgsqlCommand command = new(
            "insert into refunds (order_id, amount) values (@order_id, @amount)",
            connection,
            (NpgsqlTransaction) transaction))
        {
            command.Parameters.AddWithValue("order_id", message.OrderId);
            command.Parameters.AddWithValue("amount", message.Amount);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // 2. a message that must not be sent unless the row above survives
        await context.PublishAsync(new SendPaymentReminder(message.OrderId, message.Amount));

        // 3. the trigger, in the same transaction as both, with the commit inside the scope so the
        // scheduler is signalled once the trigger is visible to it
        using (scheduler.EnlistTransaction(transaction))
        {
            await scheduler.ScheduleJob<PaymentReminderJob, PaymentReminder>(
                new PaymentReminder(message.OrderId, message.Amount),
                TimeSpan.FromDays(7),
                new OneOffJobOptions { Group = OrderGroup.For(message.OrderId) },
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }

        // Releases the envelopes the outbox held back. Nothing left the process before the commit.
        await context.FlushOutgoingMessagesAsync();

        Ledger.Record(Events.RefundApprovedInTransaction, message.OrderId);
    }
}
