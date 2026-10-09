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
using Quartz.Impl;

namespace Quartz.Dashboard.Services;

/// <summary>
/// The events of one agent's scheduler, as the dashboard reads them: pushed in by the hub from the stream
/// the agent sends up, and handed to whoever is subscribed through the same bounded channel the process's
/// own broker uses.
/// </summary>
/// <remarks>
/// A scheduler nobody watches streams nothing: the first subscriber is what asks the agent to start its
/// stream, and the last one leaving is what asks it to stop. An agent that reconnects while something is
/// still subscribed is asked again on its new connection.
/// </remarks>
internal sealed class AgentEventSource : ISchedulerEventSource
{
    private readonly Func<bool, ValueTask> watch;
    private readonly ConcurrentDictionary<Channel<SchedulerEvent>, byte> subscriptions = new();
    private readonly Lock gate = new();

    private int subscribers;

    /// <param name="watch">Tells the agent to start or stop its stream.</param>
    public AgentEventSource(Func<bool, ValueTask> watch)
    {
        this.watch = watch ?? throw new ArgumentNullException(nameof(watch));
    }

    /// <summary>
    /// Whether anything is subscribed, which is what decides whether a reconnected agent is asked to
    /// stream again.
    /// </summary>
    public bool HasSubscribers => Volatile.Read(ref subscribers) > 0;

    /// <inheritdoc />
    public async IAsyncEnumerable<SchedulerEvent> Subscribe(
        string schedulerName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Channel<SchedulerEvent> stream = Channel.CreateBounded<SchedulerEvent>(new BoundedChannelOptions(SchedulerEventBroker.Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

        subscriptions[stream] = 0;
        bool first;
        lock (gate)
        {
            first = ++subscribers == 1;
        }

        if (first)
        {
            await watch(true).ConfigureAwait(false);
        }

        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((Channel<SchedulerEvent>) state!).Writer.TryComplete(), stream);

        try
        {
            ChannelReader<SchedulerEvent> reader = stream.Reader;
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
            subscriptions.TryRemove(stream, out _);
            stream.Writer.TryComplete();

            bool last;
            lock (gate)
            {
                last = --subscribers == 0;
            }

            if (last)
            {
                await watch(false).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Hands one event the agent sent to every subscriber.
    /// </summary>
    public void Push(SchedulerEvent schedulerEvent)
    {
        foreach (KeyValuePair<Channel<SchedulerEvent>, byte> subscription in subscriptions)
        {
            subscription.Key.Writer.TryWrite(schedulerEvent);
        }
    }

    /// <summary>
    /// Asks the agent to stream again, on the connection it has now, when something is still watching.
    /// </summary>
    public ValueTask Rewatch()
    {
        return HasSubscribers ? watch(true) : default;
    }

    /// <summary>
    /// Ends every subscription, as a forgotten agent's streams end.
    /// </summary>
    public void Complete()
    {
        foreach (KeyValuePair<Channel<SchedulerEvent>, byte> subscription in subscriptions)
        {
            subscription.Key.Writer.TryComplete();
        }
    }
}
