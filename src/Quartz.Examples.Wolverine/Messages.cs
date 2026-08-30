namespace Quartz.Examples.Wolverine;

/*
 * The Wolverine messages this example passes around.
 *
 * They are ordinary records that know nothing about a scheduler, which is the point: Quartz publishes
 * into Wolverine and the handlers read what any other producer would have sent. They are public
 * because Wolverine compiles its handler dispatch into a dynamic assembly, which cannot see internals.
 */

/// <summary>
/// Published by <see cref="ReconciliationJob" /> on a cron schedule.
/// </summary>
public sealed record RunReconciliation(DateTimeOffset FromUtc, DateTimeOffset ToUtc);

/// <summary>
/// An order was placed and is not paid for yet, so a follow-up is due unless payment arrives first.
/// </summary>
public sealed record OrderPlaced(string OrderId, decimal Amount);

/// <summary>
/// Payment arrived, so the follow-up scheduled for <see cref="OrderPlaced" /> is no longer wanted.
/// </summary>
public sealed record OrderPaid(string OrderId);

/// <summary>
/// Published by <see cref="PaymentReminderJob" /> when the reminder falls due.
/// </summary>
public sealed record SendPaymentReminder(string OrderId, decimal Amount);

/// <summary>
/// Sent as raw bytes by <see cref="DeferredEnvelopeJob" />, so it is never re-serialized from a live
/// object at fire time.
/// </summary>
public sealed record ArchiveOrders(string Reason, int OlderThanDays);

/// <summary>
/// Handled inside the application's own database transaction, in the Postgres mode only.
/// </summary>
public sealed record ApproveRefund(string OrderId, decimal Amount);
