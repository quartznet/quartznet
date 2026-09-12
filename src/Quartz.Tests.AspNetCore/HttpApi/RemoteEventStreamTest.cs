using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// The client's reader against the route that serves it, so that the two halves of the event stream are
/// held to each other rather than each to a stub of the other.
/// </summary>
/// <remarks>
/// What a stub cannot say: that the frames the route writes are the frames the reader parses, field for
/// field and kind for kind. The reader's own behaviour — the reconnection, the two <c>404</c>s, the
/// heartbeats it swallows — is <c>HttpSchedulerEventReaderTest</c>'s subject, over a handler it can break
/// at will.
/// </remarks>
public sealed class RemoteEventStreamTest
{
    private static readonly TimeSpan waitForEvent = TimeSpan.FromSeconds(30);

    private WebApplicationFactory<Program> host = null!;
    private WebApplicationFactory<Program> configured = null!;
    private IScheduler scheduler = null!;

    [SetUp]
    public async Task SetUp()
    {
        TestContentRoot.Apply();

        host = new WebApplicationFactory<Program>();

        // A heartbeat every tenth of a second, so that "the reader swallows them" is a claim this fixture
        // can make in a second rather than in a minute.
        configured = host.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddQuartzHttpApi(options =>
            {
                options.ApiPath = "/";
                options.EventStreamHeartbeatInterval = TimeSpan.FromMilliseconds(100);
            })));

        scheduler = await configured.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
    }

    [TearDown]
    public async Task TearDown()
    {
        await scheduler.Shutdown(waitForJobsToComplete: false);
        await configured.DisposeAsync();
        await host.DisposeAsync();
    }

    /// <summary>
    /// An event raised on the host arrives at the reader as the event it was, and the heartbeats between
    /// them do not.
    /// </summary>
    [Test]
    public async Task AnEventRaisedOnTheHostArrivesAtTheReader()
    {
        using HttpClient client = configured.CreateClient();
        HttpSchedulerEventReader source = new(scheduler.SchedulerName, client);

        using CancellationTokenSource subscription = new();
        await using IAsyncEnumerator<SchedulerEvent> events = source
            .Subscribe(scheduler.SchedulerName, subscription.Token)
            .GetAsyncEnumerator(CancellationToken.None);

        Task<bool> reading = events.MoveNextAsync().AsTask();

        SchedulerEventBroker broker = configured.Services.GetRequiredService<SchedulerEventBroker>();
        using CancellationTokenSource waitingForTheSubscription = new(waitForEvent);
        while (!broker.HasSubscribers(scheduler.SchedulerName))
        {
            await Task.Delay(5, waitingForTheSubscription.Token);
        }

        // Several heartbeat intervals of silence before anything happens, which the reader consumes: the
        // read below is still waiting for a scheduler event.
        await Task.Delay(500);
        reading.IsCompleted.Should().BeFalse(
            "a heartbeat is the transport talking to itself, and a page showing it as an event would fill its ring buffer with them");

        await scheduler.AddJob(
            JobBuilder.Create<DummyJob>().WithIdentity("nightly", "reports").StoreDurably().Build(),
            new AddJobOptions { Replace = true });
        (await scheduler.PauseJob(new JobKey("nightly", "reports"))).Should().BeTrue();

        (await reading.WaitAsync(waitForEvent)).Should().BeTrue("the stream is open and the scheduler raised an event");

        SchedulerEvent read = events.Current;
        read.Kind.Should().Be(SchedulerEventKind.JobPaused);
        read.SchedulerName.Should().Be(scheduler.SchedulerName);
        read.SchedulerInstanceId.Should().Be(scheduler.SchedulerInstanceId,
            "the node is the host's, read from the event rather than from the reader's own process");
        read.JobKey.Should().Be(new KeyDto("nightly", "reports"),
            "the frame the route wrote and the record the reader built are the same event");
        read.OccurredAtUtc.Should().NotBe(default);

        await subscription.CancelAsync();
    }
}
