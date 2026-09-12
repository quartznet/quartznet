using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

using Quartz.Extensibility;
using Quartz.HttpApiContract;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// An event stream a test pushes into, so that a page or the hub forwarder can be driven without a
/// scheduler, a broker or a socket.
/// </summary>
/// <remarks>
/// It is the seam both of them read: the Live Logs page resolves a source per scheduler, and so does the
/// forwarder that feeds the hub. A fake of it is therefore the whole of what either needs to be exercised —
/// where the page used to need a SignalR connection and the events used to need a scheduler to raise them.
/// </remarks>
internal sealed class FakeSchedulerEventSource : ISchedulerEventSource
{
    private readonly ConcurrentDictionary<string, Channel<SchedulerEvent>> streams = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> subscribed = [];

    /// <summary>
    /// The schedulers that have been subscribed to, in order, so a test can say what was watched — and what
    /// was not.
    /// </summary>
    public List<string> Subscribed
    {
        get
        {
            lock (subscribed)
            {
                return [.. subscribed];
            }
        }
    }

    /// <summary>
    /// What <see cref="Subscribe" /> should raise instead of streaming, for the case a target serves no
    /// event stream at all.
    /// </summary>
    public Exception? Failure { get; set; }

    public async IAsyncEnumerable<SchedulerEvent> Subscribe(
        string schedulerName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        lock (subscribed)
        {
            subscribed.Add(schedulerName);
        }

        if (Failure is not null)
        {
            throw Failure;
        }

        Channel<SchedulerEvent> stream = Stream(schedulerName);

        // Cancellation completes the enumeration rather than raising, which is what the contract says and
        // what the broker and the HTTP reader both do.
        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state => ((Channel<SchedulerEvent>) state!).Writer.TryComplete(), stream);

        while (await stream.Reader.WaitToReadAsync(CancellationToken.None).ConfigureAwait(false))
        {
            while (stream.Reader.TryRead(out SchedulerEvent? schedulerEvent))
            {
                yield return schedulerEvent;
            }
        }
    }

    /// <summary>
    /// Hands one event to whoever is watching the scheduler it names.
    /// </summary>
    public void Push(SchedulerEvent schedulerEvent) => Stream(schedulerEvent.SchedulerName).Writer.TryWrite(schedulerEvent);

    /// <summary>
    /// Ends one scheduler's stream, the way a scheduler that went away does.
    /// </summary>
    public void End(string schedulerName) => Stream(schedulerName).Writer.TryComplete();

    private Channel<SchedulerEvent> Stream(string schedulerName) =>
        streams.GetOrAdd(schedulerName, _ => Channel.CreateUnbounded<SchedulerEvent>());
}
