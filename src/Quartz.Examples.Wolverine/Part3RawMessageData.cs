using Wolverine;
using Wolverine.Runtime;
using Wolverine.Util;

namespace Quartz.Examples.Wolverine;

/*
 * Part 3 — Wolverine's raw-message-data hook.
 *
 * This is the seam Wolverine shipped for exactly this integration. Its own documentation says so, in
 * the "Sending Raw Message Data" section of guide/messaging/message-bus.html:
 *
 *     "An example use case is integrating scheduling libraries like Quartz.NET or Hangfire where you
 *      might be persisting a byte[] for a message to be sent via Wolverine at a certain time."
 *
 * What Wolverine lacks: nothing here — this is the one place Wolverine has already met the scheduler
 * halfway. What it does not supply is the "at a certain time" half, or the byte[] itself: the section
 * shows how to send raw data, not how to produce it. Options.DefaultSerializer.WriteMessage(message)
 * is the missing line.
 *
 * What Quartz supplies: the durable place to keep the bytes until they are due. The payload is the
 * job's typed input, so it lands in the same store, under the same lock and in the same transaction as
 * the trigger that will send it.
 *
 * Why bother, when part 2 publishes a live object instead: the envelope is serialized at the moment
 * the decision was made. A payload stored as bytes is not re-derived from application state that has
 * since moved on, and the message contract can change under it without the stored firing changing
 * meaning. That is the same argument the outbox pattern makes.
 */

/// <summary>
/// A Wolverine envelope's payload, kept until the trigger that sends it comes due.
/// </summary>
/// <param name="EndpointName">
/// Which endpoint to send to. <c>SendRawMessageAsync</c> has no routing to fall back on — Wolverine
/// routes on the message's .NET type and there is no live message here — so the destination is part of
/// what has to be stored.
/// </param>
/// <param name="MessageTypeName">
/// Wolverine's own name for the message type, which is what the receiving side matches on. Produced by
/// <c>typeof(T).ToMessageTypeName()</c> so that a <c>[MessageIdentity]</c> alias is honoured rather
/// than bypassed by a raw <c>FullName</c>.
/// </param>
/// <param name="Data">The serialized message body.</param>
public sealed record DeferredEnvelope(string EndpointName, string MessageTypeName, byte[] Data);

/// <summary>
/// Stores a Wolverine message as bytes now and sends it later.
/// </summary>
public static class Part3RawMessageData
{
    /// <summary>
    /// The named local queue the deferred envelope is addressed to. Any Wolverine endpoint would do —
    /// a Rabbit queue, an Azure Service Bus topic — but a local queue keeps the example free of a
    /// broker.
    /// </summary>
    public const string EndpointName = "archive";

    public static async ValueTask<TriggerKey> ScheduleSend<TMessage>(
        IScheduler scheduler,
        IWolverineRuntime runtime,
        TMessage message,
        TimeSpan delay,
        CancellationToken cancellationToken = default) where TMessage : notnull
    {
        // Serialized here, at the moment the decision was made, rather than at fire time.
        DeferredEnvelope envelope = new(
            EndpointName,
            typeof(TMessage).ToMessageTypeName(),
            runtime.Options.DefaultSerializer.WriteMessage(message));

        return await scheduler.ScheduleJob<DeferredEnvelopeJob, DeferredEnvelope>(
            envelope,
            delay,
            new OneOffJobOptions { Group = "deferred-envelopes" },
            cancellationToken);
    }
}

/// <summary>
/// Hands the stored bytes to Wolverine when the trigger fires.
/// </summary>
public sealed class DeferredEnvelopeJob : IJob<DeferredEnvelope>
{
    private readonly IMessageBus bus;

    public DeferredEnvelopeJob(IMessageBus bus)
    {
        this.bus = bus;
    }

    public async ValueTask Execute(
        IJobExecutionContext context,
        DeferredEnvelope input,
        CancellationToken cancellationToken = default)
    {
        IDestinationEndpoint endpoint = bus.EndpointFor(input.EndpointName);

        await endpoint.SendRawMessageAsync(input.Data, configure: envelope =>
        {
            // The stored name rather than SetMessageType<T>(): the type this envelope describes is
            // whatever was serialized, which this job has no static knowledge of.
            envelope.MessageType = input.MessageTypeName;

            // Setting Destination is not optional, and Wolverine 6.30.3 does not do it for you.
            // DestinationEndpoint.SendRawMessageAsync assigns Sender but leaves Destination null, and
            // Executor.ExecuteAsync logs both success and failure through envelope.Destination!, so a
            // raw message that is handled perfectly still ends the pipeline with a
            // NullReferenceException out of the logging call. One line here avoids it.
            envelope.Destination = endpoint.Uri;
        });
    }
}

/// <summary>
/// Receives the message Wolverine rebuilt from the stored bytes.
/// </summary>
public static class ArchiveOrdersHandler
{
    public static void Handle(ArchiveOrders message)
    {
        Ledger.Record(Events.RawEnvelopeDelivered, $"{message.Reason}, older than {message.OlderThanDays} days");
    }
}
