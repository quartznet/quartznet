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
            Interlocked.Increment(ref requests);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
