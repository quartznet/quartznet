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

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Quartz.Extensibility;
using Quartz.HttpApiContract;

namespace Quartz.Impl;

/// <summary>
/// Carries the events of the schedulers in this process to whoever is watching them.
/// </summary>
/// <remarks>
/// <para>
/// One bounded channel per subscriber, and <see cref="Publish" /> is a <c>TryWrite</c> into each channel
/// of the scheduler the event belongs to. Nothing is awaited and nothing blocks: the caller is a
/// scheduler listener, so a reader that has stopped reading must cost the scheduler the price of a queue
/// write and nothing more. A process nobody is watching does no work at all.
/// </para>
/// <para>
/// A subscriber that falls more than <see cref="Capacity" /> events behind loses its <em>oldest</em>
/// ones, which is the right end to lose: a live view is read from the top, and the alternative — refusing
/// the newest — freezes the view on whatever was on screen when the reader stalled.
/// <see cref="DroppedEvents" /> counts what went, so "the stream skipped" is answerable rather than
/// deduced.
/// </para>
/// <para>
/// Names are compared the way <see cref="ISchedulerRepository" /> indexes them, ignoring case, so a
/// subscriber that asked for <c>"reporting"</c> is fed a scheduler that calls itself <c>"Reporting"</c>.
/// </para>
/// </remarks>
internal sealed class SchedulerEventBroker : ISchedulerEventSource
{
    /// <summary>
    /// How many events one subscriber may fall behind before the oldest of them are dropped.
    /// </summary>
    /// <remarks>
    /// A thousand-odd events is minutes of a busy scheduler and more than any live view shows — the
    /// dashboard's page keeps a hundred — so a subscriber that hits this is one that has stopped reading
    /// rather than one reading slowly.
    /// </remarks>
    internal const int Capacity = 1024;

    private readonly ConcurrentDictionary<Subscription, byte> subscriptions = new();

    private long droppedEvents;

    /// <summary>
    /// How many events have been dropped because a subscriber was not keeping up, since this process
    /// started.
    /// </summary>
    /// <remarks>
    /// The whole broker's rather than one subscription's: a dropped event is a fact about this process's
    /// streams, and a subscription that has gone would otherwise take its count with it. No metric in
    /// 4.1 — the number is here for a test and for a debugger.
    /// </remarks>
    internal long DroppedEvents => Interlocked.Read(ref droppedEvents);

    /// <summary>
    /// Whether anything is watching <paramref name="schedulerName" />.
    /// </summary>
    /// <remarks>
    /// Asked by <see cref="SchedulerEventPlugin" /> before it builds an event, so that a process serving
    /// the event route with nobody connected pays one dictionary walk per notification rather than an
    /// allocation.
    /// </remarks>
    public bool HasSubscribers(string schedulerName)
    {
        foreach (KeyValuePair<Subscription, byte> entry in subscriptions)
        {
            if (entry.Key.Matches(schedulerName))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Hands one event to every subscriber of the scheduler it belongs to, and returns.
    /// </summary>
    public void Publish(SchedulerEvent schedulerEvent)
    {
        ArgumentNullException.ThrowIfNull(schedulerEvent);

        foreach (KeyValuePair<Subscription, byte> entry in subscriptions)
        {
            if (entry.Key.Matches(schedulerEvent.SchedulerName))
            {
                entry.Key.Channel.Writer.TryWrite(schedulerEvent);
            }
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SchedulerEvent> Subscribe(
        string schedulerName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);

        Subscription subscription = new(schedulerName, this);
        subscriptions[subscription] = 0;

        // Cancellation completes the channel rather than cancelling the read, so the enumeration ends the
        // way a closed stream ends: the loop below drains what is already queued and returns. A reader
        // that has gone away is the ordinary end of a subscription, and every caller would otherwise be
        // writing the same catch.
        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((Subscription) state!).Complete(), subscription);

        try
        {
            ChannelReader<SchedulerEvent> reader = subscription.Channel.Reader;
            while (await reader.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
            {
                while (reader.TryRead(out SchedulerEvent? schedulerEvent))
                {
                    yield return schedulerEvent;
                }
            }
        }
        finally
        {
            // Both, in this order: removed so nothing publishes into it again, completed so a publication
            // that was already inside Publish cannot leave the channel waiting for a reader that has gone.
            subscriptions.TryRemove(subscription, out _);
            subscription.Complete();
        }
    }

    /// <summary>
    /// One reader's queue, and the scheduler it asked about.
    /// </summary>
    /// <remarks>
    /// Reference equality is the identity: two readers of the same scheduler are two subscriptions, and
    /// the dictionary they live in is keyed by the object rather than by the name.
    /// </remarks>
    private sealed class Subscription
    {
        private readonly string schedulerName;

        public Subscription(string schedulerName, SchedulerEventBroker broker)
        {
            this.schedulerName = schedulerName;

            Channel = System.Threading.Channels.Channel.CreateBounded<SchedulerEvent>(
                new BoundedChannelOptions(Capacity)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = true
                },
                _ => Interlocked.Increment(ref broker.droppedEvents));
        }

        public Channel<SchedulerEvent> Channel { get; }

        public bool Matches(string candidate) => string.Equals(schedulerName, candidate, StringComparison.OrdinalIgnoreCase);

        public void Complete() => Channel.Writer.TryComplete();
    }
}
