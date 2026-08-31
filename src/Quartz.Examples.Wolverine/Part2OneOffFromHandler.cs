using Wolverine;

namespace Quartz.Examples.Wolverine;

/*
 * Part 2 — a one-off typed job scheduled from a Wolverine handler, and cancelled by correlation.
 *
 * What Wolverine lacks: a handle on a scheduled message. IMessageBus.ScheduleAsync returns ValueTask —
 * no id, no token, nothing to cancel with. Wolverine's saga timeouts (TimeoutMessage) are the same
 * shape: completing the saga does not withdraw the timeout, the timeout still fires and the generated
 * handler discards it because the saga is gone. The one cancellation API that does exist,
 * IScheduledMessages.CancelAsync, filters on message type, execution time or envelope id — there is no
 * correlation-id filter, so cancelling "everything for order 42" means having stashed each envelope's
 * Guid yourself, and on a store-less host it is a silent no-op.
 *
 * What Quartz supplies: the trigger's group as a correlation axis. OneOffJobOptions.Group sets the
 * trigger key's group (the job key stays "QRTZ_SCHEDULED"/typeof(TJob).Name, one durable job per job
 * type, many triggers), so every firing arranged for one order shares a group and the whole group is
 * listed, paused or unscheduled in one call. That is a first-class store operation, not bookkeeping
 * the application has to keep.
 */

/// <summary>
/// What a payment reminder needs to know. Serialized into the trigger by <c>UsingInput</c> and handed
/// back to <see cref="PaymentReminderJob" /> as a typed parameter.
/// </summary>
public sealed record PaymentReminder(string OrderId, decimal Amount);

/// <summary>
/// The correlation axis. Every trigger scheduled for one order lives in this group, which is what
/// makes "cancel everything for order 42" one call.
/// </summary>
public static class OrderGroup
{
    public static string For(string orderId) => $"order:{orderId}";
}

/// <summary>
/// Schedules the follow-up when an order is placed.
/// </summary>
public static class OrderPlacedHandler
{
    public static async Task Handle(OrderPlaced message, IScheduler scheduler, CancellationToken cancellationToken)
    {
        ScheduledOneOffJob scheduled = await scheduler.ScheduleJob<PaymentReminderJob, PaymentReminder>(
            new PaymentReminder(message.OrderId, message.Amount),
            ExampleOptions.Current.ReminderDelay,
            new OneOffJobOptions { Group = OrderGroup.For(message.OrderId) },
            cancellationToken);

        // The call answers with the trigger's key and the time the store says it will first fire, so
        // "scheduled for" is what will happen rather than what was asked for.
        Ledger.Record(Events.ReminderScheduled, $"{scheduled.TriggerKey} at {scheduled.FirstFireTimeUtc:u}");
    }
}

/// <summary>
/// Withdraws every firing arranged for an order once it has been paid for.
/// </summary>
public static class OrderPaidHandler
{
    public static async Task Handle(OrderPaid message, IScheduler scheduler, CancellationToken cancellationToken)
    {
        // The whole cancellation, in one store operation. The matcher is evaluated where the triggers
        // are, so no key list round-trips through this process and there is no window in which a
        // trigger listed a moment ago fires before it can be removed. What comes back is the keys that
        // were actually withdrawn, which is how the caller learns whether it beat the firing.
        List<TriggerKey> cancelled = await scheduler.UnscheduleJobs(
            GroupMatcher<TriggerKey>.GroupEquals(OrderGroup.For(message.OrderId)),
            cancellationToken);

        Ledger.Record(Events.RemindersCancelled, $"{cancelled.Count} for {message.OrderId}");
    }
}

/// <summary>
/// Publishes the reminder back into Wolverine when the trigger fires.
/// </summary>
public sealed class PaymentReminderJob : IJob<PaymentReminder>
{
    private readonly IMessageBus bus;

    public PaymentReminderJob(IMessageBus bus)
    {
        this.bus = bus;
    }

    public async ValueTask Execute(
        IJobExecutionContext context,
        PaymentReminder input,
        CancellationToken cancellationToken = default)
    {
        await bus.PublishAsync(new SendPaymentReminder(input.OrderId, input.Amount));
    }
}

/// <summary>
/// Consumes the reminder, standing in for whatever the application would really do.
/// </summary>
public static class SendPaymentReminderHandler
{
    public static void Handle(SendPaymentReminder message)
    {
        Ledger.Record(Events.ReminderFired, $"{message.OrderId} for {message.Amount}");
    }
}
