using System.Net;
using System.Net.ServerSentEvents;
using System.Text;
using System.Text.Json;

using FakeItEasy;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Serialization.SystemTextJson;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// The event route: one scheduler's events as they happen, as server-sent events.
/// </summary>
/// <remarks>
/// <para>
/// Every other route answers a question and closes; this one stays open, so what it has to be held to is
/// different. The frames have to arrive while the request is still running rather than at the end of it,
/// the scheduler has to be resolved and the caller authorized <em>before</em> the stream opens, a quiet
/// scheduler has to be distinguishable from a dead connection, and a closed tab must not be reported as a
/// server fault.
/// </para>
/// <para>
/// A host per test, because a scheduler is started and driven in several of them and the event stream is
/// the process's.
/// </para>
/// </remarks>
public sealed class EventsEndpointTest
{
    private static readonly TimeSpan waitForFrame = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions wireOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        .ConfigureWireFormat(new SystemTextJsonSerializerRegistry());

    private readonly List<WebApplicationFactory<Program>> factories = [];
    private readonly List<IScheduler> schedulers = [];

    [SetUp]
    public void SetUp() => TestContentRoot.Apply();

    [TearDown]
    public async Task TearDown()
    {
        foreach (IScheduler scheduler in schedulers)
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }

        schedulers.Clear();

        foreach (WebApplicationFactory<Program> factory in factories)
        {
            await factory.DisposeAsync();
        }

        factories.Clear();
    }

    /// <summary>
    /// What a scheduler running one job puts on the stream, in the order the scheduler reports it.
    /// </summary>
    /// <remarks>
    /// The host's own scheduler rather than a fake, because the claim is about the four notifications a
    /// firing raises and the order they arrive in — which is a property of the scheduler, not of the
    /// plugin. Each frame's <c>event:</c> is what a reader subscribes by, so it is asserted as such rather
    /// than read out of the body.
    /// </remarks>
    [Test]
    public async Task WhatASchedulerDoesArrivesAsFramesNamedByTheirKind()
    {
        WebApplicationFactory<Program> application = Application();
        IScheduler scheduler = await Scheduler(application);
        await scheduler.Start();

        using HttpClient client = application.CreateClient();
        await using EventStream stream = await EventStream.Open(client, Url(scheduler));

        await scheduler.ScheduleJob(
            JobBuilder.Create<QuietJob>().WithIdentity("streamed", "events").Build(),
            TriggerBuilder.Create().WithIdentity("now", "events").StartNow().Build());

        List<string> kinds = [];
        while (!kinds.Contains(nameof(SchedulerEventKind.TriggerCompleted)))
        {
            kinds.Add((await stream.NextEvent()).EventType);
        }

        kinds.Should().Equal(
            [
                nameof(SchedulerEventKind.TriggerFired),
                nameof(SchedulerEventKind.JobExecuting),
                nameof(SchedulerEventKind.JobExecuted),
                nameof(SchedulerEventKind.TriggerCompleted)
            ],
            "a firing is four notifications, and a live view shows them in the order the scheduler raised them");
    }

    /// <summary>
    /// The body of a frame is the event, in the same JSON every other body on this API is written as.
    /// </summary>
    [Test]
    public async Task AFramesBodyIsTheEventAsJson()
    {
        WebApplicationFactory<Program> application = Application();
        IScheduler scheduler = await Scheduler(application);

        using HttpClient client = application.CreateClient();
        await using EventStream stream = await EventStream.Open(client, Url(scheduler));

        await PauseAJob(scheduler);

        SseItem<string> frame = await stream.NextEvent();
        frame.EventType.Should().Be(nameof(SchedulerEventKind.JobPaused));
        frame.EventId.Should().Be("2",
            "the id counts this stream's frames - the opening heartbeat was the first - and is not a cursor, because nothing is replayed");

        SchedulerEvent published = JsonSerializer.Deserialize<SchedulerEvent>(frame.Data, wireOptions)!;
        published.Kind.Should().Be(SchedulerEventKind.JobPaused);
        published.SchedulerName.Should().Be(scheduler.SchedulerName);
        published.SchedulerInstanceId.Should().Be(scheduler.SchedulerInstanceId,
            "a reader watching a clustered scheduler has to be able to tell which node an event came from");
        published.JobKey.Should().Be(new KeyDto("nightly", "reports"));
    }

    /// <summary>
    /// A scheduler with nothing to say still says so, which is what keeps a proxy from closing the
    /// connection and what lets a reader tell a quiet scheduler from a dead one.
    /// </summary>
    [Test]
    public async Task AQuietSchedulerStillSendsAHeartbeat()
    {
        WebApplicationFactory<Program> application = Application(
            options => options.EventStreamHeartbeatInterval = TimeSpan.FromMilliseconds(100));
        IScheduler scheduler = await Scheduler(application);

        using HttpClient client = application.CreateClient();
        await using EventStream stream = await EventStream.Open(client, Url(scheduler));

        // The second one: the first goes out when the stream opens, and a connection that says hello and
        // nothing else again is one a proxy will close.
        await stream.Next();
        SseItem<string> frame = await stream.Next();

        frame.EventType.Should().Be(nameof(SchedulerEventKind.Heartbeat),
            "silence is the same shape as a socket that went away, so an idle stream has to keep saying something");

        SchedulerEvent heartbeat = JsonSerializer.Deserialize<SchedulerEvent>(frame.Data, wireOptions)!;
        heartbeat.OccurredAtUtc.Should().NotBe(default);
        heartbeat.SchedulerName.Should().BeEmpty(
            "a heartbeat is the route saying the connection is alive, not the scheduler saying anything");
    }

    /// <summary>
    /// The response is a stream rather than a body: its headers arrive before the first frame does, and a
    /// reader gets each frame as it is written.
    /// </summary>
    [Test]
    public async Task TheResponseIsAnUnbufferedEventStream()
    {
        WebApplicationFactory<Program> application = Application();
        IScheduler scheduler = await Scheduler(application);

        using HttpClient client = application.CreateClient();
        await using EventStream stream = await EventStream.Open(client, Url(scheduler));

        stream.ContentType.Should().Be("text/event-stream");
        stream.NoCache.Should().BeTrue("a proxy that cached this would serve one reader's stream to another");
        stream.NoStore.Should().BeTrue();

        SseItem<string> hello = await stream.Next();
        hello.EventType.Should().Be(nameof(SchedulerEventKind.Heartbeat),
            "a response's headers are sent when its body is first written, so the stream opens by saying something - "
            + "a reader of an idle scheduler would otherwise still be waiting for the response");
        hello.EventId.Should().Be("1");
    }

    [Test]
    public async Task AnUnknownSchedulerIsNotFoundBeforeTheStreamOpens()
    {
        WebApplicationFactory<Program> application = Application();
        await Scheduler(application);

        using HttpClient client = application.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(
            "schedulers/no-such-scheduler/events", HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "an unknown scheduler is the same 404 here as on every other route, rather than a stream that says nothing");
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    /// <summary>
    /// A caller the per-scheduler policy refuses is refused before the stream opens, so no frame of a
    /// scheduler they may not see is ever written.
    /// </summary>
    [Test]
    public async Task ACallerThePolicyRefusesGetsNoFrames()
    {
        WebApplicationFactory<Program> application = Application(
            options => options.SchedulerAuthorizationPolicy = TenantAuthenticationExtensions.SchedulerOwnerPolicy,
            services => services.AddTenantAuthorization());
        IScheduler scheduler = await Scheduler(application);

        using HttpClient client = application.CreateClient();
        client.DefaultRequestHeaders.Add(TenantAuthenticationHandler.TenantHeaderName, "somebody-else");

        using HttpResponseMessage response = await client.GetAsync(Url(scheduler), HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json",
            "the refusal is the API's own problem details, which is what it answers on every other route");
    }

    /// <summary>
    /// A reader that goes away is not a fault: nothing is logged as one, and nothing tries to write a
    /// <c>500</c> onto a response whose headers are long gone.
    /// </summary>
    /// <remarks>
    /// The wrapper around every endpoint catches everything, and a closed tab is what ends every live
    /// stream there has ever been. The frames are published while nobody is reading them and the reader
    /// then goes, which is the case that would have been reported as a server fault.
    /// </remarks>
    [Test]
    public async Task AReaderThatGoesAwayIsNotAServerFault()
    {
        CapturingLoggerFactory logs = new();
        WebApplicationFactory<Program> application = Application(logs: logs);
        IScheduler scheduler = await Scheduler(application);

        using HttpClient client = application.CreateClient();
        EventStream stream = await EventStream.Open(client, Url(scheduler));

        await PauseAJob(scheduler);
        (await stream.NextEvent()).EventType.Should().Be(nameof(SchedulerEventKind.JobPaused));

        for (int index = 0; index < 500; index++)
        {
            await scheduler.PauseJob(new JobKey("nightly", "reports"));
        }

        await stream.DisposeAsync();
        await Task.Delay(500);

        logs.Snapshot().Should().NotContain(entry => entry.EventId == 9004,
            "a closed tab is the ordinary end of a live stream, not a server fault an operator has to look at");
        logs.Snapshot().Should().NotContain(entry => entry.Level == LogLevel.Error,
            "the subscription is completed when the request is aborted, so the stream ends rather than failing");
    }

    /// <summary>
    /// The same for a request that is not a stream: a caller who goes away mid-request is a
    /// <c>Debug</c> line naming the request, not an <c>Error</c> line and not a <c>500</c> written onto a
    /// response nobody is reading.
    /// </summary>
    /// <remarks>
    /// Every endpoint is behind one wrapper that catches everything, so this is the wrapper's rule rather
    /// than the event route's — and it is reachable on any route, because a handler is handed the
    /// request's own token and the scheduler behind it abandons its work on that token.
    /// </remarks>
    [Test]
    public async Task ARequestTheCallerAbandonedIsADebugLineRatherThanAFault()
    {
        CapturingLoggerFactory logs = new();
        WebApplicationFactory<Program> application = Application(logs: logs);

        TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IScheduler waiting = A.Fake<IScheduler>();
        A.CallTo(() => waiting.SchedulerName).Returns("waiting");
        A.CallTo(() => waiting.GetMetadata(A<CancellationToken>._))
            .ReturnsLazily((CancellationToken token) => new ValueTask<SchedulerMetadata>(NeverAnswers(reached, token)));

        application.Services.GetRequiredService<ISchedulerRepository>().Bind(waiting, "waiting");

        using HttpClient client = application.CreateClient();
        using CancellationTokenSource cancellation = new();

        Task<HttpResponseMessage> request = client.GetAsync("schedulers/waiting", cancellation.Token);
        await reached.Task.WaitAsync(waitForFrame);
        await cancellation.CancelAsync();

        Func<Task> abandoned = () => request;
        await abandoned.Should().ThrowAsync<TaskCanceledException>("the caller is the one who stopped waiting");

        using CancellationTokenSource waitingForTheServer = new(waitForFrame);
        while (!logs.Snapshot().Any(entry => entry.EventId == 9006))
        {
            await Task.Delay(25, waitingForTheServer.Token);
        }

        List<(int EventId, LogLevel Level, string Message)> written = logs.Snapshot();

        written.Should().NotContain(entry => entry.EventId == 9004,
            "the request was abandoned by its caller, which is not something this server did wrong");
        written.Where(entry => entry.EventId == 9006).Should().OnlyContain(entry => entry.Level == LogLevel.Debug,
            "it is a fact about a caller rather than about this server, so it is there to be turned on rather than alerted on");
    }

    /// <summary>
    /// A scheduler call that never answers until the caller's token says to stop, which is what every
    /// endpoint hands the scheduler.
    /// </summary>
    private static async Task<SchedulerMetadata> NeverAnswers(TaskCompletionSource reached, CancellationToken cancellationToken)
    {
        reached.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return TestData.Metadata;
    }

    private static string Url(IScheduler scheduler) => $"schedulers/{scheduler.SchedulerName}/events";

    /// <summary>
    /// Pauses a job the scheduler holds, which is the smallest event a test can raise on demand.
    /// </summary>
    /// <remarks>
    /// The job is added first because the scheduler notifies for a job it found and for no other: pausing
    /// a key nothing goes by raises nothing, which reads in a test exactly like a stream that is broken.
    /// </remarks>
    private static async Task PauseAJob(IScheduler scheduler)
    {
        await scheduler.AddJob(
            JobBuilder.Create<QuietJob>().WithIdentity("nightly", "reports").StoreDurably().Build(),
            new AddJobOptions { Replace = true });

        (await scheduler.PauseJob(new JobKey("nightly", "reports"))).Should().BeTrue(
            "the scheduler holds the job, so it has something to pause and something to report");
    }

    private WebApplicationFactory<Program> Application(
        Action<QuartzHttpApiOptions>? configure = null,
        Action<IServiceCollection>? register = null,
        CapturingLoggerFactory? logs = null)
    {
        WebApplicationFactory<Program> root = new();
        factories.Add(root);

        WebApplicationFactory<Program> configured = root.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                register?.Invoke(services);
                services.AddQuartzHttpApi(options =>
                {
                    options.ApiPath = "/";
                    configure?.Invoke(options);
                });

                // The factory rather than a provider: a provider is filtered by the host's own minimum
                // level, which is Information, and what an abandoned request writes is a Debug line. The
                // last registration of a service is the one that resolves, so this is the factory
                // everything in the host logs through.
                if (logs is not null)
                {
                    services.AddSingleton<ILoggerFactory>(logs);
                }
            }));

        factories.Add(configured);
        return configured;
    }

    /// <summary>
    /// The host's own scheduler, built — which is also what binds it into the repository the route looks
    /// names up in.
    /// </summary>
    private async Task<IScheduler> Scheduler(WebApplicationFactory<Program> application)
    {
        IScheduler scheduler = await application.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        schedulers.Add(scheduler);
        return scheduler;
    }

    /// <summary>
    /// A job that does nothing, so that a firing is four notifications rather than four and an error.
    /// </summary>
    /// <remarks>
    /// <c>Support.DummyJob</c> throws, which the scheduler reports as a <c>SchedulerError</c> — a real
    /// event, and one this test is not about.
    /// </remarks>
    private sealed class QuietJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// One open event stream, read frame by frame.
    /// </summary>
    /// <remarks>
    /// Opened with <see cref="HttpCompletionOption.ResponseHeadersRead" />, because reading the body to
    /// its end is what a client must never do with a stream that has no end. The events published after
    /// it opens are the ones it sees: there is no replay, so a test that publishes first sees nothing.
    /// </remarks>
    private sealed class EventStream : IAsyncDisposable
    {
        private readonly CancellationTokenSource cancellation = new();
        private readonly HttpResponseMessage response;
        private readonly Stream body;
        private readonly IAsyncEnumerator<SseItem<string>> frames;

        private EventStream(HttpResponseMessage response, Stream body)
        {
            this.response = response;
            this.body = body;

            frames = SseParser
                .Create(body, static (_, data) => Encoding.UTF8.GetString(data))
                .EnumerateAsync(cancellation.Token)
                .GetAsyncEnumerator(CancellationToken.None);
        }

        public static async Task<EventStream> Open(HttpClient client, string url)
        {
            HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"GET {url} opens a stream");

            Stream body = await response.Content.ReadAsStreamAsync();
            return new EventStream(response, body);
        }

        public string? ContentType => response.Content.Headers.ContentType?.MediaType;

        public bool NoCache => response.Headers.CacheControl?.NoCache ?? false;

        public bool NoStore => response.Headers.CacheControl?.NoStore ?? false;

        public async Task<SseItem<string>> Next()
        {
            (await frames.MoveNextAsync().AsTask().WaitAsync(waitForFrame)).Should().BeTrue("the stream is still open");
            return frames.Current;
        }

        /// <summary>
        /// The next frame a scheduler produced, skipping the heartbeats a reader consumes without showing.
        /// </summary>
        public async Task<SseItem<string>> NextEvent()
        {
            while (true)
            {
                SseItem<string> frame = await Next();
                if (frame.EventType != nameof(SchedulerEventKind.Heartbeat))
                {
                    return frame;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await cancellation.CancelAsync();

            body.Dispose();
            response.Dispose();
            cancellation.Dispose();
        }
    }

    /// <summary>
    /// Every log line the host wrote, with the event id an operator would filter on.
    /// </summary>
    /// <remarks>
    /// The whole factory rather than a provider behind the host's own: a provider is subject to the
    /// filters the host configured, and the lines this fixture is about are Debug ones on a host whose
    /// minimum is Information.
    /// </remarks>
    private sealed class CapturingLoggerFactory : ILoggerFactory
    {
        private readonly List<(int EventId, LogLevel Level, string Message)> entries = [];

        /// <summary>
        /// What has been logged so far. A copy, because the host goes on logging from its own threads
        /// while a test reads this.
        /// </summary>
        public List<(int EventId, LogLevel Level, string Message)> Snapshot()
        {
            lock (entries)
            {
                return [.. entries];
            }
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingLoggerFactory factory) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (factory.entries)
                {
                    factory.entries.Add((eventId.Id, logLevel, formatter(state, exception)));
                }
            }
        }
    }
}
