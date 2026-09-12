using System.IO.Pipelines;
using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Time.Testing;

using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Serialization.SystemTextJson;

namespace Quartz.Tests.Unit.HttpApi;

/// <summary>
/// The reader of a remote scheduler's event stream: what it hands a subscriber, what it swallows, and what
/// it does when the connection goes.
/// </summary>
/// <remarks>
/// The claim a page depends on is that one subscription is one enumeration however many connections it
/// takes — a target that restarts is a gap rather than the end of the feed. The other half is the pair of
/// <c>404</c>s: a target too old to serve the route and a scheduler nothing goes by are different answers,
/// and only one of them is worth reporting as a capability.
/// </remarks>
public class HttpSchedulerEventReaderTest
{
    private static readonly TimeSpan waitForEvent = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions wireOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        .ConfigureWireFormat(new SystemTextJsonSerializerRegistry());

    private SseHandler handler;
    private HttpClient httpClient;
    private FakeTimeProvider clock;

    [SetUp]
    public void SetUp()
    {
        handler = new SseHandler();
        httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:8080/") };
        clock = new FakeTimeProvider(new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.Zero));
    }

    [TearDown]
    public void TearDown()
    {
        httpClient?.Dispose();
        handler?.Dispose();
        httpClient = null;
        handler = null;
    }

    [Test]
    public async Task EventsAreReadFromTheTargetsEventRoute()
    {
        await using Subscriber subscriber = await Subscriber.Watching(Reader(), handler);

        handler.LastRequestUri.Should().Be("http://localhost:8080/schedulers/Remote/events");

        await handler.Send(Event(SchedulerEventKind.JobExecuting) with { JobKey = new KeyDto("nightly", "reports") });

        SchedulerEvent read = await subscriber.Next();
        read.Kind.Should().Be(SchedulerEventKind.JobExecuting);
        read.JobKey.Should().Be(new KeyDto("nightly", "reports"));
        read.SchedulerInstanceId.Should().Be("node-a", "the event says which node of the target raised it");
    }

    /// <summary>
    /// A heartbeat is why this reader can tell a quiet scheduler from a dead connection, so it is this
    /// reader's to consume rather than a subscriber's to filter.
    /// </summary>
    [Test]
    public async Task HeartbeatsAreConsumedRatherThanHandedOn()
    {
        await using Subscriber subscriber = await Subscriber.Watching(Reader(), handler);

        await handler.Send(Event(SchedulerEventKind.Heartbeat));
        await handler.Send(Event(SchedulerEventKind.Heartbeat));
        await handler.Send(Event(SchedulerEventKind.TriggerFired));

        (await subscriber.Next()).Kind.Should().Be(SchedulerEventKind.TriggerFired,
            "a page showing a heartbeat as an event would fill its ring buffer with the transport talking to itself");
    }

    /// <summary>
    /// A stream that ends is reopened, so a subscriber sees one enumeration across a target that restarted.
    /// </summary>
    /// <remarks>
    /// After the delay, which is why the clock is a fake one: the wait is real in a deployment and would be
    /// a second of nothing in a test.
    /// </remarks>
    [Test]
    public async Task AStreamThatEndsIsReopenedAfterTheDelay()
    {
        await using Subscriber subscriber = await Subscriber.Watching(Reader(), handler);

        await handler.Send(Event(SchedulerEventKind.TriggerFired));
        (await subscriber.Next()).Kind.Should().Be(SchedulerEventKind.TriggerFired);

        // The read is started before the stream is closed: an enumeration is pulled, so a subscriber that
        // is not asking for the next event is a subscriber the drop has not reached yet.
        Task<SchedulerEvent> next = subscriber.Reading();

        await handler.Close();

        // Advanced until the reader has asked again: the delay is registered on this clock when the stream
        // ends, so the first advance can land before the reader gets there.
        using CancellationTokenSource waitingForTheReader = new(waitForEvent);
        while (handler.Requests < 2)
        {
            clock.Advance(HttpSchedulerEventReader.FirstRetryDelay);
            await Task.Delay(10, waitingForTheReader.Token);
        }

        await handler.Send(Event(SchedulerEventKind.JobPaused));

        (await next).Kind.Should().Be(SchedulerEventKind.JobPaused,
            "one subscription is one enumeration, whatever happened to the connections under it");
    }

    /// <summary>
    /// A target whose API has no event route is a target that streams no events, which is something a page
    /// can say — rather than an error a page has to render as a failure.
    /// </summary>
    /// <remarks>
    /// An unmatched route answers <c>404</c> with no body at all, which is what a Quartz HTTP API older
    /// than 4.1 does for this route.
    /// </remarks>
    [Test]
    public async Task ATargetWithoutTheEventRouteSaysItStreamsNoEvents()
    {
        handler.Respond(HttpStatusCode.NotFound, body: "");

        Func<Task> act = async () =>
        {
            await foreach (SchedulerEvent _ in Reader().Subscribe("Remote"))
            {
                break;
            }
        };

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*serves no event stream*");
    }

    /// <summary>
    /// The other <c>404</c> — the one that names an unknown scheduler — is a mistake rather than a missing
    /// capability, and is not something to reconnect over either.
    /// </summary>
    [Test]
    public async Task AnUnknownSchedulerIsStillAnUnknownScheduler()
    {
        handler.Respond(HttpStatusCode.NotFound, """
            {
              "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
              "title": "Not Found",
              "status": 404,
              "detail": "Unknown scheduler Remote"
            }
            """);

        Func<Task> act = async () =>
        {
            await foreach (SchedulerEvent _ in Reader().Subscribe("Remote"))
            {
                break;
            }
        };

        await act.Should().ThrowAsync<HttpClientException>().WithMessage("*Scheduler not found*");
    }

    [Test]
    public async Task CancellingTheSubscriptionEndsTheEnumerationRatherThanFailingIt()
    {
        Subscriber subscriber = await Subscriber.Watching(Reader(), handler);

        await subscriber.Cancel();

        (await subscriber.Ended()).Should().BeTrue(
            "a reader that has gone away is the ordinary end of a subscription, not a failure of one");

        await subscriber.DisposeAsync();
    }

    /// <summary>
    /// <see cref="HttpClient.Timeout" /> does not cut a stream opened with
    /// <see cref="HttpCompletionOption.ResponseHeadersRead" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the question the documentation's advice raises: it tells a deployment to give the client a
    /// short timeout because every page that reads the target waits for it, and a stream is a response that
    /// deliberately never ends. The timeout bounds the send — the headers — and not the reading of a body
    /// the caller has taken responsibility for.
    /// </para>
    /// <para>
    /// The clock is a fake one, so a stream that <em>was</em> cut could not be reopened without an advance:
    /// the event below arrives on the first connection or not at all, which is what makes this a test of
    /// the timeout rather than of the reconnection.
    /// </para>
    /// </remarks>
    [Test]
    public async Task AShortClientTimeoutDoesNotCutTheStream()
    {
        httpClient.Timeout = TimeSpan.FromMilliseconds(250);

        await using Subscriber subscriber = await Subscriber.Watching(Reader(), handler);

        // Several times the timeout, with the stream open and silent.
        await Task.Delay(TimeSpan.FromSeconds(2));

        await handler.Send(Event(SchedulerEventKind.TriggerFired));

        (await subscriber.Next()).Kind.Should().Be(SchedulerEventKind.TriggerFired);
        handler.Requests.Should().Be(1, "the stream was never cut, so it was never reopened");
    }

    private HttpSchedulerEventReader Reader() => new("Remote", httpClient, jsonSerializerOptions: null, clock);

    private static SchedulerEvent Event(SchedulerEventKind kind) => new()
    {
        Kind = kind,
        SchedulerName = "Remote",
        SchedulerInstanceId = "node-a",
        OccurredAtUtc = new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.Zero)
    };

    /// <summary>
    /// One subscriber of the reader, held at a read the test controls.
    /// </summary>
    private sealed class Subscriber : IAsyncDisposable
    {
        private readonly CancellationTokenSource cancellation = new();
        private readonly IAsyncEnumerator<SchedulerEvent> events;

        private Task<bool> pending;

        private Subscriber(HttpSchedulerEventReader reader)
        {
            events = reader.Subscribe("Remote", cancellation.Token).GetAsyncEnumerator(CancellationToken.None);
        }

        /// <summary>
        /// Subscribes and waits until the target has been asked, which is what the first read does.
        /// </summary>
        public static async Task<Subscriber> Watching(HttpSchedulerEventReader reader, SseHandler handler)
        {
            Subscriber subscriber = new(reader);
            subscriber.pending = subscriber.events.MoveNextAsync().AsTask();

            using CancellationTokenSource timeout = new(waitForEvent);
            while (handler.Requests == 0)
            {
                await Task.Delay(5, timeout.Token);
            }

            return subscriber;
        }

        public Task<SchedulerEvent> Next() => Reading();

        /// <summary>
        /// Starts the next read and answers it, so a test can have a read in flight while it breaks the
        /// connection under it.
        /// </summary>
        public Task<SchedulerEvent> Reading()
        {
            Task<bool> read = Read();
            pending = null;

            return Await(read);

            async Task<SchedulerEvent> Await(Task<bool> read)
            {
                (await read.WaitAsync(waitForEvent)).Should().BeTrue("the stream is still open");
                return events.Current;
            }
        }

        public Task Cancel() => cancellation.CancelAsync();

        public async Task<bool> Ended() => !await Read().WaitAsync(waitForEvent);

        private Task<bool> Read() => pending ??= events.MoveNextAsync().AsTask();

        public async ValueTask DisposeAsync()
        {
            await cancellation.CancelAsync();

            if (pending is not null)
            {
                await pending;
            }

            await events.DisposeAsync();
            cancellation.Dispose();
        }
    }

    /// <summary>
    /// A target that serves an event stream the test writes into, or the error response the test asked for.
    /// </summary>
    /// <remarks>
    /// A pipe rather than a fixed body: the frames have to arrive while the response is open, which is the
    /// whole difference between this route and every other one. Completing the writer is a target that went
    /// away.
    /// </remarks>
    private sealed class SseHandler : HttpMessageHandler
    {
        private readonly List<Pipe> streams = [];

        private HttpStatusCode statusCode = HttpStatusCode.OK;
        private string body = "";

        public string LastRequestUri { get; private set; }

        public int Requests { get; private set; }

        public void Respond(HttpStatusCode status, string body)
        {
            statusCode = status;
            this.body = body;
        }

        /// <summary>
        /// Writes one frame onto the stream that is open now, the way the route formats it.
        /// </summary>
        public async Task Send(SchedulerEvent schedulerEvent)
        {
            string json = JsonSerializer.Serialize(schedulerEvent, wireOptions);
            string frame = $"event: {schedulerEvent.Kind}\ndata: {json}\nid: 1\n\n";

            Pipe stream = Current();
            await stream.Writer.WriteAsync(Encoding.UTF8.GetBytes(frame));
            await stream.Writer.FlushAsync();
        }

        /// <summary>
        /// Ends the stream that is open now, the way a restarted target does.
        /// </summary>
        public async Task Close() => await Current().Writer.CompleteAsync();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString();
            Requests++;

            if (statusCode != HttpStatusCode.OK)
            {
                return Task.FromResult(new HttpResponseMessage(statusCode)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }

            Pipe stream = new();
            lock (streams)
            {
                streams.Add(stream);
            }

            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StreamContent(stream.Reader.AsStream())
            };

            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(response);
        }

        private Pipe Current()
        {
            lock (streams)
            {
                streams.Should().NotBeEmpty("nothing has opened a stream yet");
                return streams[^1];
            }
        }

        protected override void Dispose(bool disposing)
        {
            lock (streams)
            {
                foreach (Pipe stream in streams)
                {
                    stream.Writer.Complete();
                }
            }

            base.Dispose(disposing);
        }
    }
}
