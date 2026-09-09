using System.Net;
using System.Text;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// What reading a remote scheduler's status costs, and what it says.
/// </summary>
/// <remarks>
/// Every synchronous member of <c>HttpScheduler</c> blocks the calling thread for a round trip, so the
/// number of them a question needs is part of the question's cost. Asking "is it started, is it in
/// standby, has it shut down" used to be three requests to one endpoint, each reading a different field
/// of the same answer.
/// </remarks>
public class HttpSchedulerStatusTest
{
    [Test]
    public void StatusIsOneRoundTrip()
    {
        CountingHandler handler = new(Body(SchedulerStatus.Standby));
        HttpScheduler scheduler = new("TestScheduler", new HttpClient(handler) { BaseAddress = new Uri("http://quartz.test/") });

        SchedulerStatus status = scheduler.Status;

        status.Should().Be(SchedulerStatus.Standby);
        handler.Requests.Should().Be(1,
            "the whole lifecycle is one value now, so a caller pays for one request rather than one per flag");
    }

    [Test]
    public async Task GetStatusIsTheSameRoundTripWithoutBlockingAThread()
    {
        CountingHandler handler = new(Body(SchedulerStatus.Standby));
        HttpScheduler scheduler = new("TestScheduler", new HttpClient(handler) { BaseAddress = new Uri("http://quartz.test/") });

        SchedulerStatus status = await scheduler.GetStatus();

        status.Should().Be(SchedulerStatus.Standby,
            "the asynchronous member reads the same field of the same answer the property does");
        handler.Requests.Should().Be(1,
            "awaiting the answer rather than blocking for it does not make it a second request");
    }

    [Test]
    public async Task GetSchedulerInstanceIdIsOneRoundTrip()
    {
        CountingHandler handler = new(Body(SchedulerStatus.Running));
        HttpScheduler scheduler = new("TestScheduler", new HttpClient(handler) { BaseAddress = new Uri("http://quartz.test/") });

        string instanceId = await scheduler.GetSchedulerInstanceId();

        instanceId.Should().Be("NON_CLUSTERED");
        handler.Requests.Should().Be(1);
    }

    /// <summary>
    /// The two asynchronous members answer what the two properties answer, which is the whole of their
    /// contract — they exist to cost a thread less, not to say anything different.
    /// </summary>
    [Test]
    public async Task TheAsynchronousMembersAnswerWhatThePropertiesAnswer()
    {
        CountingHandler handler = new(Body(SchedulerStatus.Running));
        HttpScheduler scheduler = new("TestScheduler", new HttpClient(handler) { BaseAddress = new Uri("http://quartz.test/") });

        (await scheduler.GetStatus()).Should().Be(scheduler.Status);
        (await scheduler.GetSchedulerInstanceId()).Should().Be(scheduler.SchedulerInstanceId);
    }

    /// <summary>
    /// And the cancellation token reaches the request, which is the other thing a property could not do.
    /// </summary>
    [Test]
    public async Task ACancelledTokenStopsTheRequestRatherThanTheAnswer()
    {
        CountingHandler handler = new(Body(SchedulerStatus.Running));
        HttpScheduler scheduler = new("TestScheduler", new HttpClient(handler) { BaseAddress = new Uri("http://quartz.test/") });

        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        Func<Task> act = async () => await scheduler.GetStatus(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "a caller that gave up on the answer should not go on waiting for the round trip");
        handler.Requests.Should().Be(0);
    }

    [Test]
    public async Task MetadataReportsTheSameStatusTheSchedulerDoes()
    {
        CountingHandler handler = new(Body(SchedulerStatus.ShuttingDown));
        HttpScheduler scheduler = new("TestScheduler", new HttpClient(handler) { BaseAddress = new Uri("http://quartz.test/") });

        SchedulerMetadata metadata = await scheduler.GetMetadata();

        metadata.Status.Should().Be(SchedulerStatus.ShuttingDown,
            "the proxy and the scheduler behind it derive the status from the same field of the same answer");
    }

    private static string Body(SchedulerStatus status)
    {
        return $$"""
            {
              "schedulerInstanceId": "NON_CLUSTERED",
              "name": "TestScheduler",
              "status": "{{status}}",
              "threadPool": { "type": "Quartz.Impl.DefaultThreadPool, Quartz", "size": 10 },
              "jobStore": { "type": "Quartz.Impl.RAMJobStore, Quartz", "clustered": false, "persistent": false },
              "statistics": { "version": "1.2.3", "runningSince": null, "jobsExecuted": 0, "localExecutingJobs": 0 }
            }
            """;
    }

    /// <summary>
    /// Answers every request with one prepared body, and counts how many were asked.
    /// </summary>
    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly string body;
        private int requests;

        public CountingHandler(string body)
        {
            this.body = body;
        }

        public int Requests => Volatile.Read(ref requests);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Interlocked.Increment(ref requests);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
