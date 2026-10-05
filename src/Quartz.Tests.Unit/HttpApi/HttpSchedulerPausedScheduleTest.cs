using System.Net;
using System.Text;

namespace Quartz.Tests.Unit.HttpApi;

/// <summary>
/// <see cref="ScheduleJobOptions.Paused" /> over HTTP: sent to a host that stores it, and refused before it
/// reaches one that would store the trigger unpaused (#4018).
/// </summary>
public class HttpSchedulerPausedScheduleTest
{
    private const string DetailsPath = "/schedulers/Remote";
    private const string SchedulePath = "/schedulers/Remote/triggers/schedule";
    private const string ScheduleManyPath = "/schedulers/Remote/triggers/schedule-multiple";

    private static readonly ScheduleJobOptions awaitingApproval = new() { PauseReason = "awaiting approval", PauseRequestedBy = "alice" };

    private StubHandler handler;
    private HttpClient httpClient;

    [SetUp]
    public void SetUp()
    {
        handler = new StubHandler();
        handler.RespondTo(SchedulePath, HttpStatusCode.OK, """{ "firstFireTimeUtc": "2031-06-17T10:00:00+00:00" }""");
        handler.RespondTo(ScheduleManyPath, HttpStatusCode.OK, "");
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
    public async Task APausedScheduleIsSentToAHostThatStoresIt()
    {
        handler.RespondTo(DetailsPath, HttpStatusCode.OK, SchedulerDetails("4.4.0.0"));
        HttpScheduler scheduler = Scheduler();

        await scheduler.ScheduleJob(Job(), Trigger(), awaitingApproval);

        handler.Requests.Should().Equal(
        [
            "GET http://localhost:8080/schedulers/Remote",
            "POST http://localhost:8080/schedulers/Remote/triggers/schedule"
        ], "the host's version is read before the first paused schedule");
        handler.LastRequestBody.Should().Contain("\"paused\":true")
            .And.Contain("\"pauseReason\":\"awaiting approval\"")
            .And.Contain("\"pauseRequestedBy\":\"alice\"");
    }

    [Test]
    public async Task AHostSeenToStorePausedIsNotAskedAgain()
    {
        handler.RespondTo(DetailsPath, HttpStatusCode.OK, SchedulerDetails("4.4.1.0"));
        HttpScheduler scheduler = Scheduler();

        await scheduler.ScheduleJob(Trigger(), awaitingApproval);
        await scheduler.ScheduleJob(Job("second"), [Trigger("second")], awaitingApproval);

        handler.Requests.Count(request => request.EndsWith(DetailsPath, StringComparison.Ordinal)).Should().Be(1,
            "a 4.4 answer is kept for the client's lifetime");
        handler.LastRequestBody.Should().Contain("\"paused\":true", "the batch route carries the pause too");
    }

    [Test]
    public async Task APausedScheduleIsRefusedBeforeItReachesAnOlderHost()
    {
        handler.RespondTo(DetailsPath, HttpStatusCode.OK, SchedulerDetails("4.3.0.0"));
        HttpScheduler scheduler = Scheduler();

        Func<Task> one = async () => await scheduler.ScheduleJob(Job(), Trigger(), awaitingApproval);
        Func<Task> many = async () => await scheduler.ScheduleJobs(
            new Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> { [Job()] = [Trigger()] },
            new ScheduleJobOptions { Paused = true });

        await one.Should().ThrowAsync<NotSupportedException>().WithMessage("*'Remote'*4.3.0.0*Paused*4.4*");
        await many.Should().ThrowAsync<NotSupportedException>();

        handler.Requests.Should().OnlyContain(request => request.EndsWith(DetailsPath, StringComparison.Ordinal),
            "a 4.3 host ignores the pause and would store the trigger unpaused, so the schedule is never sent");
    }

    [Test]
    public async Task AScheduleThatIsNotPausedAsksNothingFirst()
    {
        HttpScheduler scheduler = Scheduler();

        await scheduler.ScheduleJob(Job(), Trigger());

        handler.Requests.Should().Equal(["POST http://localhost:8080/schedulers/Remote/triggers/schedule"],
            "only a paused schedule needs the host's version; every other one is the request it always was");
        handler.LastRequestBody.Should().Contain("\"paused\":false");
    }

    private HttpScheduler Scheduler() => new("Remote", httpClient);

    private static IJobDetail Job(string name = "nightly") => JobBuilder.Create<NoOpJob>().WithIdentity(name, "reports").Build();

    private static ITrigger Trigger(string name = "nightly") => TriggerBuilder.Create()
        .WithIdentity(name, "reports")
        .ForJob(name, "reports")
        .StartNow()
        .Build();

    private static string SchedulerDetails(string version) => $$"""
        {
          "schedulerInstanceId": "NON_CLUSTERED",
          "name": "Remote",
          "status": "Running",
          "threadPool": { "type": "Quartz.Impl.DefaultThreadPool", "size": 10 },
          "jobStore": { "type": "Quartz.Impl.RAMJobStore", "clustered": false, "persistent": false },
          "statistics": { "version": "{{version}}", "runningSince": null, "jobsExecuted": 0, "localExecutingJobs": 0 }
        }
        """;

    public sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// Answers a request by its path, recording every request it was sent and the last body.
    /// </summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> byPath = new(StringComparer.Ordinal);

        public string LastRequestBody { get; private set; }

        public List<string> Requests { get; } = [];

        public void RespondTo(string path, HttpStatusCode status, string body)
        {
            byPath[path] = (status, body);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            Requests.Add($"{request.Method} {request.RequestUri}");

            (HttpStatusCode status, string content) = byPath.TryGetValue(request.RequestUri!.AbsolutePath, out (HttpStatusCode Status, string Body) routed)
                ? routed
                : (HttpStatusCode.NotFound, "");

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            };
        }
    }
}
