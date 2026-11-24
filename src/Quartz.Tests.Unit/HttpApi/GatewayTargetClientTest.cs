using System.Net;
using System.Text;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Impl;

namespace Quartz.Tests.Unit.HttpApi;

/// <summary>
/// A client of a scheduler behind a host's target (#4218): every request names the target as
/// <c>?target=</c>, and a success that does not echo it is refused rather than taken as the target's.
/// </summary>
public class GatewayTargetClientTest
{
    private EchoingHandler handler;
    private HttpClient httpClient;

    [SetUp]
    public void SetUp()
    {
        handler = new EchoingHandler();
        httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://gateway.test/quartz-api/")
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
    public async Task EveryRequestOfTheSchedulerTheHistoryAndTheEventsNamesTheTarget()
    {
        HttpScheduler scheduler = new(new SchedulerRef("QuartzScheduler", "w1"), httpClient);
        HttpExecutionHistoryStore history = new("QuartzScheduler", httpClient, gatewayTarget: "w1");
        HttpSchedulerEventReader events = new("QuartzScheduler", httpClient, gatewayTarget: "w1");

        await scheduler.GetMetadata();
        await scheduler.PauseAll();
        await history.GetExecution("QuartzScheduler", "entry-1");

        // The stream is answered 404 without problem details here, which the reader raises as "serves no
        // event stream": the request it made is what this test reads.
        Func<Task> subscribe = async () =>
        {
            await foreach (SchedulerEvent _ in events.Subscribe("QuartzScheduler"))
            {
            }
        };
        await subscribe.Should().ThrowAsync<NotSupportedException>();

        handler.Requests.Should().HaveCount(4);
        handler.Requests.Should().OnlyContain(uri => uri.Query.Contains("target=w1"),
            "the host serves the target's scheduler only when the request names the target; without it the answer is the host's own scheduler of that name");
        handler.Requests.Should().Contain(uri => uri.AbsolutePath == "/quartz-api/schedulers/QuartzScheduler" && uri.Query == "?target=w1");
        handler.Requests.Should().Contain(uri => uri.AbsolutePath == "/quartz-api/schedulers/QuartzScheduler/pause-all" && uri.Query == "?target=w1");
    }

    [Test]
    public void ATargetIsAppendedToAQueryAndEscaped()
    {
        GatewayTarget.Append("schedulers/QuartzScheduler/jobs?skip=0&take=50", "w1").Should()
            .Be("schedulers/QuartzScheduler/jobs?skip=0&take=50&target=w1", "a request that already has a query gets the target appended to it");
        GatewayTarget.Append("schedulers/QuartzScheduler", "eu west").Should().Be("schedulers/QuartzScheduler?target=eu%20west");
        GatewayTarget.Append("schedulers/QuartzScheduler", null).Should().Be("schedulers/QuartzScheduler");
    }

    [Test]
    public async Task AKeyWithNoTargetIsTheHostsOwnSchedulerAndSendsNone()
    {
        HttpScheduler scheduler = new(new SchedulerRef("QuartzScheduler"), httpClient);

        await scheduler.GetMetadata();

        handler.Requests.Should().ContainSingle().Which.Query.Should().BeEmpty();
        scheduler.SchedulerName.Should().Be("QuartzScheduler");
    }

    [Test]
    public async Task ASuccessWithoutTheEchoIsRefusedAsAnOlderHostsAnswer()
    {
        handler.Echo = false;
        HttpScheduler scheduler = new(new SchedulerRef("QuartzScheduler", "w1"), httpClient);
        HttpExecutionHistoryStore history = new("QuartzScheduler", httpClient, gatewayTarget: "w1");
        HttpSchedulerEventReader events = new("QuartzScheduler", httpClient, gatewayTarget: "w1");
        handler.StreamAnswers = true;

        Func<Task> metadata = async () => await scheduler.GetMetadata();
        Func<Task> pause = async () => await scheduler.PauseAll();
        Func<Task> statistics = async () => await history.QueryExecutionStatistics(new ExecutionStatisticsQuery { SchedulerName = "QuartzScheduler" });
        Func<Task> stream = async () =>
        {
            await foreach (SchedulerEvent _ in events.Subscribe("QuartzScheduler"))
            {
            }
        };

        await metadata.Should().ThrowAsync<HttpClientException>().WithMessage("*'w1'*Quartz-Target*older than 4.9*");
        await pause.Should().ThrowAsync<HttpClientException>().WithMessage("*Quartz-Target*",
            "a 4.8 host would have paused its own scheduler of that name, and saying it was the target's would hide that");
        await statistics.Should().ThrowAsync<HttpClientException>().WithMessage("*Quartz-Target*");
        await stream.Should().ThrowAsync<HttpClientException>().WithMessage("*Quartz-Target*",
            "the events of another scheduler are not the target's");
    }

    [Test]
    public async Task AnEchoNamingAnotherTargetIsRefusedAndTheEchoIgnoresCase()
    {
        handler.EchoValue = "W1";
        HttpScheduler scheduler = new(new SchedulerRef("QuartzScheduler", "w1"), httpClient);
        await scheduler.GetMetadata();

        handler.EchoValue = "w2";
        Func<Task> other = async () => await scheduler.GetMetadata();
        await other.Should().ThrowAsync<HttpClientException>().WithMessage("*Quartz-Target*");
    }

    [Test]
    public async Task TheEchoIsReadPercentEncodedAsTheHostWritesIt()
    {
        handler.EchoValue = "eu%20west";
        HttpScheduler scheduler = new(new SchedulerRef("QuartzScheduler", "eu west"), httpClient);

        await scheduler.GetMetadata();

        handler.Requests.Should().ContainSingle().Which.Query.Should().Be("?target=eu%20west",
            "a header carries ASCII only, so the host echoes the target as it was sent");
    }

    [Test]
    public async Task AFailureWithoutTheEchoKeepsItsOwnMeaning()
    {
        handler.Echo = false;
        handler.Status = HttpStatusCode.NotFound;
        handler.Body = """{ "status": 404, "detail": "Unknown scheduler QuartzScheduler" }""";
        HttpScheduler scheduler = new(new SchedulerRef("QuartzScheduler", "w1"), httpClient);

        Func<Task> metadata = async () => await scheduler.GetMetadata();

        await metadata.Should().ThrowAsync<HttpClientException>().WithMessage("Scheduler not found*",
            "a 4.9 host's own 401, or a route it does not map, is written before Quartz runs and cannot carry the header");
    }

    [Test]
    public async Task AStatusFailureCarriesTheStatusAndTheServersDetailAsItsInnerException()
    {
        handler.Status = HttpStatusCode.Forbidden;
        handler.Body = """{ "status": 403, "detail": "Not authorized for PauseAll on scheduler QuartzScheduler: it needs Operate" }""";
        HttpScheduler scheduler = new("QuartzScheduler", httpClient);

        Func<Task> pause = async () => await scheduler.PauseAll();

        HttpClientException thrown = (await pause.Should().ThrowAsync<HttpClientException>()
            .WithMessage("Received response with status code Forbidden, error details: Not authorized*")).Which;
        HttpRequestException status = thrown.InnerException.Should().BeOfType<HttpRequestException>(
            "a carrier relaying this answer reads the status off the BCL type, as it does not reference this package").Subject;
        status.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        status.Message.Should().Be("Not authorized for PauseAll on scheduler QuartzScheduler: it needs Operate");
    }

    [TestCase("a/b")]
    [TestCase(" ")]
    public void AGatewayTargetIsATargetName(string gatewayTarget)
    {
        ServiceCollection services = new();

        Action register = () => services.AddQuartzHttpClient(options =>
        {
            options.SchedulerName = "QuartzScheduler";
            options.CreateHttpClient = _ => httpClient;
            options.GatewayTarget = gatewayTarget;
        });

        register.Should().Throw<OptionsValidationException>().WithMessage("*GatewayTarget*");
    }

    /// <summary>
    /// A merged cluster's key is a target the host lists and serves, so a gateway target may name one.
    /// </summary>
    [Test]
    public async Task AGatewayTargetMayNameAMergedCluster()
    {
        ServiceCollection services = new();
        services.AddQuartzHttpClient(options =>
        {
            options.SchedulerName = "QuartzScheduler";
            options.CreateHttpClient = _ => httpClient;
            options.GatewayTarget = "a+b";
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        await provider.GetRequiredKeyedService<IScheduler>("QuartzScheduler").GetMetadata();

        handler.Requests.Should().ContainSingle().Which.Query.Should().Be("?target=a%2Bb", "a '+' is escaped, as a query reads it as a space");
    }

    [Test]
    public async Task AddQuartzHttpClientSendsTheGatewayTargetFromEveryPartOfTheRegistration()
    {
        ServiceCollection services = new();
        services.AddQuartzHttpClient(options =>
        {
            options.SchedulerName = "QuartzScheduler";
            options.CreateHttpClient = _ => httpClient;
            options.Target = "gateway-w1";
            options.GatewayTarget = "w1";
        });

        await using ServiceProvider provider = services.BuildServiceProvider();
        IScheduler scheduler = provider.GetRequiredKeyedService<IScheduler>("gateway-w1");
        IExecutionHistoryStore history = provider.GetRequiredKeyedService<IExecutionHistoryStore>("gateway-w1");

        await scheduler.GetMetadata();
        await history.GetExecution("QuartzScheduler", "entry-1");

        handler.Requests.Should().HaveCount(2).And.OnlyContain(uri => uri.Query == "?target=w1",
            "GatewayTarget is the host's name for the target, and Target is this process's label, which is never sent");
        ((IProxyScheduler) scheduler).Target.Should().Be("gateway-w1");
    }

    /// <summary>
    /// A host that answers every request, echoing <c>Quartz-Target</c> back when the request names one.
    /// </summary>
    private sealed class EchoingHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        public bool Echo { get; set; } = true;

        public string EchoValue { get; set; }

        public bool StreamAnswers { get; set; }

        public HttpStatusCode? Status { get; set; }

        public string Body { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri uri = request.RequestUri!;
            Requests.Add(uri);

            HttpResponseMessage response = Answer(uri);

            string target = uri.Query.Split('&', '?').FirstOrDefault(part => part.StartsWith("target=", StringComparison.Ordinal))?["target=".Length..];
            if (Echo && target is not null)
            {
                response.Headers.Add("Quartz-Target", EchoValue ?? Uri.UnescapeDataString(target));
            }

            return Task.FromResult(response);
        }

        private HttpResponseMessage Answer(Uri uri)
        {
            if (Status is { } status)
            {
                return Json(status, Body);
            }

            string path = uri.AbsolutePath;
            if (path.EndsWith("/events", StringComparison.Ordinal))
            {
                return StreamAnswers
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("", Encoding.UTF8, "text/event-stream") }
                    : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (path.Contains("/history/executions/", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.NotFound, """{ "status": 404, "detail": "Unknown execution entry-1" }""");
            }

            if (path.EndsWith("/history/statistics", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, """{ "buckets": [] }""");
            }

            if (path.EndsWith("/pause-all", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return Json(HttpStatusCode.OK, """
                {
                  "schedulerInstanceId": "NON_CLUSTERED",
                  "name": "QuartzScheduler",
                  "status": "Running",
                  "threadPool": { "type": "Quartz.Impl.DefaultThreadPool", "size": 10 },
                  "jobStore": { "type": "Quartz.Impl.RAMJobStore", "clustered": false, "persistent": false },
                  "statistics": { "version": "4.9.0.0", "runningSince": null, "jobsExecuted": 0, "localExecutingJobs": 0 }
                }
                """);
        }

        private static HttpResponseMessage Json(HttpStatusCode status, string body)
        {
            return new HttpResponseMessage(status) { Content = new StringContent(body ?? "", Encoding.UTF8, "application/json") };
        }
    }
}
