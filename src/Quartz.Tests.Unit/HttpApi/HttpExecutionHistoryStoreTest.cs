using System.Net;
using System.Text;

using Quartz.Impl;

namespace Quartz.Tests.Unit.HttpApi;

/// <summary>
/// The reader of a remote scheduler's execution history: what it asks for, what it hands back, and the
/// two things it refuses to pretend about.
/// </summary>
public class HttpExecutionHistoryStoreTest
{
    private StubHandler handler;
    private HttpClient httpClient;

    [SetUp]
    public void SetUp()
    {
        handler = new StubHandler();
        httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:8080/")
        };
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
    public async Task ExecutionsAreReadFromTheTargetsHistoryRoute()
    {
        handler.Respond(HttpStatusCode.OK, """
            {
              "items": [
                {
                  "schedulerInstanceId": "node-a",
                  "jobGroup": "DummyGroup",
                  "jobName": "nightly",
                  "triggerGroup": "DummyTriggerGroup",
                  "triggerName": "at-midnight",
                  "firedAtUtc": "2026-08-26T12:00:00+00:00",
                  "duration": "00:00:01.5000000",
                  "succeeded": false,
                  "exceptionMessage": "the job threw"
                }
              ],
              "hasMore": true,
              "totalCount": 7
            }
            """);

        PagedResult<ExecutionHistoryEntry> page = await Store().QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = "Remote",
            SchedulerInstanceId = "node-a",
            JobContains = "night",
            TriggerContains = "midnight",
            Skip = 10,
            Take = 5,
            IncludeTotalCount = true
        });

        handler.LastRequestUri.Should().Be(
            "http://localhost:8080/schedulers/Remote/history/executions"
            + "?skip=10&take=5&includeTotalCount=true&schedulerInstanceId=node-a&triggerContains=midnight&jobContains=night");

        ExecutionHistoryEntry entry = page.Items.Should().ContainSingle().Subject;
        entry.SchedulerName.Should().Be("Remote", "the rows do not carry it — the route said it, and this client knows it");
        entry.JobName.Should().Be("nightly");
        entry.Duration.Should().Be(TimeSpan.FromMilliseconds(1500));
        entry.Succeeded.Should().BeFalse();
        entry.ExceptionMessage.Should().Be("the job threw");
        page.HasMore.Should().BeTrue();
        page.TotalCount.Should().Be(7);
    }

    [Test]
    public async Task MisfiresAreReadFromTheTargetsHistoryRoute()
    {
        handler.Respond(HttpStatusCode.OK, """
            {
              "items": [
                {
                  "schedulerInstanceId": "node-a",
                  "triggerGroup": "DummyTriggerGroup",
                  "triggerName": "at-midnight",
                  "jobKey": { "name": "nightly", "group": "DummyGroup" },
                  "misfiredAtUtc": "2026-08-26T12:00:00+00:00",
                  "scheduledFireTimeUtc": "2026-08-26T11:55:00+00:00"
                }
              ],
              "hasMore": false,
              "totalCount": 1
            }
            """);

        PagedResult<MisfireHistoryEntry> page = await Store().QueryMisfires(new MisfireHistoryQuery
        {
            SchedulerName = "Remote",
            TriggerContains = "midnight"
        });

        handler.LastRequestUri.Should().Be(
            "http://localhost:8080/schedulers/Remote/history/misfires?take=250&triggerContains=midnight");

        MisfireHistoryEntry entry = page.Items.Should().ContainSingle().Subject;
        entry.JobKey.Should().Be(new JobKey("nightly", "DummyGroup"));
        entry.ScheduledFireTimeUtc.Should().Be(new DateTimeOffset(2026, 8, 26, 11, 55, 0, TimeSpan.Zero));
    }

    [Test]
    public async Task MisfiresAreCountedOverAWindow()
    {
        handler.Respond(HttpStatusCode.OK, """{"count":3}""");

        int count = await Store().CountMisfires("Remote", new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero));

        handler.LastRequestUri.Should().Be(
            "http://localhost:8080/schedulers/Remote/history/misfires/count?since=2026-08-26T12%3A00%3A00.0000000%2B00%3A00");
        count.Should().Be(3);
    }

    /// <summary>
    /// A target whose API has no history route is a target that serves no history, which is something a
    /// page can say — rather than an error a page has to render as a failure.
    /// </summary>
    /// <remarks>
    /// An unmatched route answers <c>404</c> with no body at all, which is what a Quartz HTTP API older
    /// than 4.1 does for these routes.
    /// </remarks>
    [Test]
    public async Task ATargetWithoutTheHistoryRoutesSaysItServesNoHistory()
    {
        handler.Respond(HttpStatusCode.NotFound, body: "");

        Func<Task> act = async () => await Store().QueryExecutions(new ExecutionHistoryQuery { SchedulerName = "Remote" });

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*does not serve history*");
    }

    /// <summary>
    /// The other <c>404</c> — the one that names an unknown scheduler — is a mistake rather than a
    /// missing capability, and still arrives as itself.
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

        Func<Task> act = async () => await Store().QueryMisfires(new MisfireHistoryQuery { SchedulerName = "Remote" });

        await act.Should().ThrowAsync<HttpClientException>()
            .WithMessage("*Scheduler not found*");
    }

    [Test]
    public async Task NothingIsRecordedThroughTheWire()
    {
        ExecutionHistoryEntry execution = new(
            "Remote", "node-a", "DummyGroup", "nightly", "DummyTriggerGroup", "at-midnight",
            DateTimeOffset.UtcNow, TimeSpan.Zero, Succeeded: true, ExceptionMessage: null);

        Func<Task> writeExecution = async () => await Store().AddExecution(execution);
        Func<Task> writeMisfire = async () => await Store().AddMisfire(new MisfireHistoryEntry(
            "Remote", "node-a", "DummyTriggerGroup", "at-midnight", JobKey: null, DateTimeOffset.UtcNow, null));

        await writeExecution.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*history is recorded where the scheduler runs*");
        await writeMisfire.Should().ThrowAsync<NotSupportedException>();
    }

    private HttpExecutionHistoryStore Store() => new("Remote", httpClient);

    private sealed class StubHandler : HttpMessageHandler
    {
        private HttpStatusCode statusCode = HttpStatusCode.OK;
        private string body = "";

        public string LastRequestUri { get; private set; }

        public void Respond(HttpStatusCode status, string body)
        {
            statusCode = status;
            this.body = body;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString();

            HttpResponseMessage response = new(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            return Task.FromResult(response);
        }
    }
}
