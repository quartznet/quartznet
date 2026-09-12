using System.Net;
using System.Text;

using FakeItEasy;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// Pins the frames the event route puts on the wire: one per kind, as the bytes a reader parses.
/// </summary>
/// <remarks>
/// <para>
/// The other wire snapshots are JSON bodies; this one is the framing as well — the <c>event:</c> name a
/// reader subscribes by, the <c>data:</c> line it parses, and the <c>id:</c> that counts the stream's
/// frames. All three are contract, and a reader that is not this repository's client reads them as they
/// are written here.
/// </para>
/// <para>
/// The events are published into the broker rather than raised by a scheduler, because a snapshot has to
/// be the same bytes on every run: every instant, key and message below is fixed.
/// </para>
/// </remarks>
public sealed class SchedulerEventWireFormatTest
{
    private static readonly DateTimeOffset occurredAt = new(2026, 9, 12, 10, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset firedAt = new(2026, 9, 12, 10, 29, 55, TimeSpan.Zero);

    private WebApplicationFactory<Program> application = null!;
    private IScheduler fake = null!;

    [SetUp]
    public void SetUp()
    {
        TestContentRoot.Apply();

        application = new WebApplicationFactory<Program>();

        fake = A.Fake<IScheduler>();
        A.CallTo(() => fake.SchedulerName).Returns(TestData.SchedulerName);

        ISchedulerRepository repository = application.Services.GetRequiredService<ISchedulerRepository>();
        foreach (IScheduler bound in repository.LookupAll())
        {
            repository.Remove(bound.SchedulerName);
        }

        repository.Bind(fake);
    }

    [TearDown]
    public async Task TearDown()
    {
        await fake.DisposeAsync();
        await application.DisposeAsync();
    }

    [Test]
    public async Task EveryKindOnTheWire()
    {
        List<SchedulerEvent> events = Published();

        using HttpClient client = application.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(
            $"schedulers/{TestData.SchedulerName}/events", HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using Stream body = await response.Content.ReadAsStreamAsync();
        using StreamReader reader = new(body, Encoding.UTF8);

        SchedulerEventBroker broker = application.Services.GetRequiredService<SchedulerEventBroker>();
        while (!broker.HasSubscribers(TestData.SchedulerName))
        {
            await Task.Delay(5);
        }

        foreach (SchedulerEvent published in events)
        {
            broker.Publish(published);
        }

        // One more than the events, because the stream opens by saying hello: the response's headers are
        // sent when its body is first written, so an idle scheduler would otherwise leave a reader waiting
        // for the response itself.
        string frames = await Read(reader, events.Count + 1);

        await Verify(WithoutTheOpeningHeartbeat(frames))
            .UseDirectory("../Verify")
            .UseFileName("SchedulerEventWireFormatTest_EveryKindOnTheWire")
            .DontScrubDateTimes()
            .DontScrubGuids()
            .DisableRequireUniquePrefix();
    }

    /// <summary>
    /// One event of every kind a scheduler publishes, with every facet that kind carries filled.
    /// </summary>
    /// <remarks>
    /// <see cref="SchedulerEventKind.Heartbeat" /> is not here: it is the route's own frame rather than
    /// anything a scheduler says, and the one this stream opened with is dropped from the snapshot because
    /// its instant is the clock's.
    /// </remarks>
    private static List<SchedulerEvent> Published() =>
    [
        Event(SchedulerEventKind.JobExecuting) with
        {
            JobKey = new KeyDto("nightly", "reports"),
            TriggerKey = new KeyDto("at-midnight", "reports"),
            FireTimeUtc = firedAt,
            FireInstanceId = "fire-1"
        },
        Event(SchedulerEventKind.JobExecuted) with
        {
            JobKey = new KeyDto("nightly", "reports"),
            TriggerKey = new KeyDto("at-midnight", "reports"),
            FireTimeUtc = firedAt,
            FireInstanceId = "fire-1",
            RunTime = TimeSpan.FromMilliseconds(1500),
            Vetoed = false,
            ExceptionMessage = "the job threw"
        },
        Event(SchedulerEventKind.TriggerFired) with
        {
            JobKey = new KeyDto("nightly", "reports"),
            TriggerKey = new KeyDto("at-midnight", "reports"),
            FireTimeUtc = firedAt,
            FireInstanceId = "fire-1"
        },
        Event(SchedulerEventKind.TriggerCompleted) with
        {
            JobKey = new KeyDto("nightly", "reports"),
            TriggerKey = new KeyDto("at-midnight", "reports"),
            FireTimeUtc = firedAt,
            FireInstanceId = "fire-1"
        },
        Event(SchedulerEventKind.TriggerMisfired) with
        {
            JobKey = new KeyDto("nightly", "reports"),
            TriggerKey = new KeyDto("at-midnight", "reports")
        },
        Event(SchedulerEventKind.TriggerPaused) with { TriggerKey = new KeyDto("at-midnight", "reports") },
        Event(SchedulerEventKind.TriggerResumed) with { TriggerKey = new KeyDto("at-midnight", "reports") },
        Event(SchedulerEventKind.JobPaused) with { JobKey = new KeyDto("nightly", "reports") },
        Event(SchedulerEventKind.JobResumed) with { JobKey = new KeyDto("nightly", "reports") },
        Event(SchedulerEventKind.JobInterrupted) with
        {
            JobKey = new KeyDto("nightly", "reports"),
            FireInstanceId = "fire-1"
        },
        Event(SchedulerEventKind.TriggerInError) with { TriggerKey = new KeyDto("at-midnight", "reports") },
        Event(SchedulerEventKind.SchedulerStateChanged) with { Status = SchedulerStatus.Standby },
        Event(SchedulerEventKind.SchedulerError) with
        {
            Message = "the job could not be built",
            Cause = "no such type",
            JobKey = new KeyDto("nightly", "reports"),
            TriggerKey = new KeyDto("at-midnight", "reports"),
            FireInstanceId = "fire-1"
        }
    ];

    private static SchedulerEvent Event(SchedulerEventKind kind) => new()
    {
        Kind = kind,
        SchedulerName = TestData.SchedulerName,
        SchedulerInstanceId = TestData.SchedulerInstanceId,
        OccurredAtUtc = occurredAt
    };

    /// <summary>
    /// Reads until <paramref name="frameCount" /> frames have arrived, which is what says the stream is
    /// read as it is written rather than at the end of a response.
    /// </summary>
    private static async Task<string> Read(StreamReader reader, int frameCount)
    {
        StringBuilder text = new();
        char[] buffer = new char[1024];

        while (Frames(text) < frameCount)
        {
            int read = await reader.ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(30));
            read.Should().BeGreaterThan(0, "the stream is still open");
            text.Append(buffer, 0, read);
        }

        return text.ToString();
    }

    private static int Frames(StringBuilder text)
    {
        int frames = 0;
        string read = text.ToString();
        for (int index = read.IndexOf("\n\n", StringComparison.Ordinal); index >= 0;
             index = read.IndexOf("\n\n", index + 2, StringComparison.Ordinal))
        {
            frames++;
        }

        return frames;
    }

    /// <summary>
    /// Drops the frame the stream opened with, whose instant is whatever the clock said.
    /// </summary>
    private static string WithoutTheOpeningHeartbeat(string frames)
    {
        int firstFrameEnd = frames.IndexOf("\n\n", StringComparison.Ordinal);
        firstFrameEnd.Should().BeGreaterThan(0);

        frames.Substring(0, firstFrameEnd).Should().Contain("event: Heartbeat");
        return frames.Substring(firstFrameEnd + 2);
    }
}
