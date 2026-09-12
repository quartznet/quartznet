using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;

using Quartz.Extensibility;
using Quartz.HttpApiContract;

namespace Quartz.AspNetCore.HttpApi.Util;

/// <summary>
/// One scheduler's events as the frames of a server-sent event stream.
/// </summary>
/// <remarks>
/// <para>
/// Each frame's <c>event:</c> type is the event's kind, so a reader can subscribe to what it cares about
/// without parsing a body, and its <c>id:</c> is a counter for this stream. The id is not a cursor:
/// nothing is replayed, so it says how many frames a reader has seen and nothing more.
/// </para>
/// <para>
/// A stream that has said nothing for <see cref="DefaultHeartbeatInterval" /> emits a
/// <see cref="SchedulerEventKind.Heartbeat" />. That is what keeps a proxy from closing an idle
/// connection, and what lets a reader tell a quiet scheduler from a dead one — silence is otherwise the
/// same shape as a socket that went away. One goes out the moment the stream opens, which is what sends
/// the response's headers: nothing is written to a response until its body is, so a reader of an idle
/// scheduler would otherwise be left waiting for the response itself.
/// </para>
/// </remarks>
internal static class SchedulerEventStream
{
    /// <summary>
    /// How long the stream may be silent before it says it is still there.
    /// </summary>
    /// <remarks>
    /// Fifteen seconds is below the idle timeout of every reverse proxy's default — nginx reads 60,
    /// Azure's front doors 90 — and far enough above a page's own patience to cost nothing.
    /// </remarks>
    internal static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Reads <paramref name="schedulerName" />'s events until the request is aborted, emitting a
    /// heartbeat whenever <paramref name="heartbeatInterval" /> passes with nothing to send.
    /// </summary>
    /// <remarks>
    /// The read that is already in flight is kept across a heartbeat rather than restarted, so a
    /// heartbeat cannot lose an event that arrived while it was being written.
    /// </remarks>
    public static async IAsyncEnumerable<SseItem<SchedulerEvent>> Read(
        ISchedulerEventSource source,
        string schedulerName,
        TimeSpan heartbeatInterval,
        TimeProvider timeProvider,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        long frames = 0;

        // The subscription's own token is the request's; the enumerator is asked for none of its own, so
        // cancellation ends this enumeration the one way it ends — the source completes it.
        IAsyncEnumerator<SchedulerEvent> events = source
            .Subscribe(schedulerName, cancellationToken)
            .GetAsyncEnumerator(CancellationToken.None);

        await using ConfiguredAsyncDisposable subscription = events.ConfigureAwait(false);

        // Read before the first frame is written, so that an event raised while the response was opening
        // is queued rather than missed: the subscription exists from this call onwards.
        //
        // Each ValueTask is consumed exactly once, by the AsTask that turns it into the Task below; it is
        // that Task which is waited on more than once, which is what it is for. S5034 reads the two waits
        // as two consumptions of one ValueTask.
#pragma warning disable S5034 // ValueTask should be consumed only once
        Task<bool>? pending = events.MoveNextAsync().AsTask();

        // And a frame straight away, before the scheduler has done anything. ASP.NET Core sends a
        // response's headers when something is first written to its body, so until a frame exists the
        // reader is still waiting for the response to begin — a page watching an idle scheduler would show
        // nothing at all, for as long as the scheduler stayed idle. A heartbeat is the frame that says
        // only "I am here", and every reader already consumes those without showing them.
        yield return Frame(Heartbeat(timeProvider), ++frames);

        while (true)
        {
            pending ??= events.MoveNextAsync().AsTask();
#pragma warning restore S5034

            if (!pending.IsCompleted && await Silent(pending, heartbeatInterval, timeProvider).ConfigureAwait(false))
            {
                yield return Frame(Heartbeat(timeProvider), ++frames);
                continue;
            }

            bool moved = await pending.ConfigureAwait(false);
            pending = null;

            if (!moved)
            {
                break;
            }

            yield return Frame(events.Current, ++frames);
        }
    }

    /// <summary>
    /// Whether <paramref name="heartbeatInterval" /> passed before <paramref name="pending" /> had
    /// anything to report.
    /// </summary>
    private static async Task<bool> Silent(Task<bool> pending, TimeSpan heartbeatInterval, TimeProvider timeProvider)
    {
        using CancellationTokenSource waited = new();

        Task delay = Task.Delay(heartbeatInterval, timeProvider, waited.Token);
        Task first = await Task.WhenAny(pending, delay).ConfigureAwait(false);

        // Cancelled either way: the timer is of no further use once this has answered, and a cancelled
        // delay raises nothing for anybody to observe.
        await waited.CancelAsync().ConfigureAwait(false);

        return first != pending;
    }

    private static SchedulerEvent Heartbeat(TimeProvider timeProvider) => new()
    {
        Kind = SchedulerEventKind.Heartbeat,
        OccurredAtUtc = timeProvider.GetUtcNow()
    };

    private static SseItem<SchedulerEvent> Frame(SchedulerEvent schedulerEvent, long id) =>
        new(schedulerEvent, schedulerEvent.Kind.ToString())
        {
            EventId = id.ToString(CultureInfo.InvariantCulture)
        };
}
