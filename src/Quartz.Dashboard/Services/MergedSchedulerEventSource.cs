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

using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Impl;

namespace Quartz.Dashboard.Services;

/// <summary>
/// One stream out of several: the events of every member of a cluster, interleaved as they arrive.
/// </summary>
/// <remarks>
/// <para>
/// Every event already names the node it happened on, so nothing is added to tell the members apart. A
/// member that serves no stream — a target too old for the route — simply contributes nothing; a member
/// whose stream drops is reopened by its own reader, which is what each member's source does for itself.
/// </para>
/// <para>
/// Bounded the way the broker is, dropping the oldest: a reader that has stopped reading loses what it
/// was never going to show.
/// </para>
/// </remarks>
internal sealed class MergedSchedulerEventSource : ISchedulerEventSource
{
    private readonly IReadOnlyList<ISchedulerEventSource> sources;

    public MergedSchedulerEventSource(IReadOnlyList<ISchedulerEventSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        this.sources = sources;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A page subscribes by the cluster's key, <c>a+b/name</c>; each member is asked for the scheduler's
    /// own name, which is what its process knows the scheduler as.
    /// </remarks>
    public IAsyncEnumerable<SchedulerEvent> Subscribe(string schedulerName, CancellationToken cancellationToken = default)
    {
        // Validated here rather than in the iterator, which would only report it on the first read.
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);

        return Read(SchedulerRef.Parse(schedulerName).SchedulerName, cancellationToken);
    }

    private async IAsyncEnumerable<SchedulerEvent> Read(
        string schedulerName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Channel<SchedulerEvent> merged = Channel.CreateBounded<SchedulerEvent>(new BoundedChannelOptions(SchedulerEventBroker.Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });

        using CancellationTokenSource pumps = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task[] reading = new Task[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            reading[i] = Pump(sources[i], schedulerName, merged.Writer, pumps.Token);
        }

        // The merged stream ends when every member's has: a reader that went away cancels them all, and
        // a member whose stream ended on its own is one member fewer.
        _ = Task.WhenAll(reading).ContinueWith(
            static (_, state) => ((ChannelWriter<SchedulerEvent>) state!).TryComplete(),
            merged.Writer,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        try
        {
            ChannelReader<SchedulerEvent> reader = merged.Reader;
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
            await pumps.CancelAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads one member's stream into the merged channel until it ends.
    /// </summary>
    /// <remarks>
    /// Every ending is an ending: a cancelled read is the reader going away, a target that serves no
    /// stream says so with <see cref="NotSupportedException" /> and contributes nothing, and any other
    /// failure ends this member's share of the stream without ending the others'.
    /// </remarks>
    private static async Task Pump(
        ISchedulerEventSource source,
        string schedulerName,
        ChannelWriter<SchedulerEvent> writer,
        CancellationToken cancellationToken)
    {
        // Off the caller's stack, so that a member whose Subscribe faults synchronously cannot fault the
        // merged enumeration before it has yielded anything.
        await Task.Yield();

        try
        {
            await foreach (SchedulerEvent schedulerEvent in source.Subscribe(schedulerName, cancellationToken).ConfigureAwait(false))
            {
                writer.TryWrite(schedulerEvent);
            }
        }
        catch (OperationCanceledException)
        {
            // The reader went away, which is the ordinary end of a subscription.
        }
        catch (NotSupportedException)
        {
            // The target serves no event stream, so this member contributes nothing to the merged one.
        }
        catch (Exception)
        {
            // This member's stream is over; the others go on, and the page reads the same sources for
            // what it wants to say about a member that cannot be reached.
        }
    }
}
