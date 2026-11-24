using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// Every fire of every trigger in a window over HTTP (#4438): the <c>upcoming-fires</c> route, which runs
/// <see cref="UpcomingFires" /> on the host, and <see cref="HttpScheduler" />, which sends that route — or, to a host
/// that has none, composes the answer from the trigger listing, the triggers and the calendars that host answers with.
/// </summary>
/// <remarks>
/// Two triggers fire every 20 minutes from half an hour and from 50 minutes after a start ten minutes or so ahead of
/// the system clock, which the host's scheduler runs on. The scheduler is never started, so nothing fires.
/// </remarks>
[NonParallelizable]
public sealed class UpcomingFiresOverHttpTest
{
    private static readonly JobKey jobKey = new("agenda-job", "agenda");

    private readonly List<WebApplicationFactory<Program>> factories = [];
    private readonly List<IScheduler> schedulers = [];
    private DateTimeOffset start;

    [SetUp]
    public void SetUp()
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        start = new DateTimeOffset(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerMinute, TimeSpan.Zero).AddMinutes(10);
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (IScheduler scheduler in schedulers)
        {
            await scheduler.Shutdown();
        }

        schedulers.Clear();

        foreach (WebApplicationFactory<Program> factory in factories)
        {
            await factory.DisposeAsync();
        }

        factories.Clear();
    }

    [Test]
    public async Task TheRouteAnswersEveryFireInTheWindowEarliestFirst()
    {
        Api api = await StartApi();

        using HttpResponseMessage response = await api.Get(Window(start, start.AddHours(1)));
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using JsonDocument answer = JsonDocument.Parse(body);
        answer.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal(
            ["fromUtc", "toUtc", "fires", "cut", "cutAtUtc", "triggersNotComputed"]);
        answer.RootElement.GetProperty("fromUtc").GetDateTimeOffset().Should().Be(start);
        answer.RootElement.GetProperty("cut").GetBoolean().Should().BeFalse();
        answer.RootElement.GetProperty("cutAtUtc").ValueKind.Should().Be(JsonValueKind.Null);

        JsonElement[] fires = [.. answer.RootElement.GetProperty("fires").EnumerateArray()];
        fires.Select(fire => (fire.GetProperty("fireTimeUtc").GetDateTimeOffset() - start, fire.GetProperty("trigger").GetProperty("name").GetString()))
            .Should().Equal(
                [(TimeSpan.FromMinutes(30), "a-first"), (TimeSpan.FromMinutes(50), "a-first"), (TimeSpan.FromMinutes(50), "b-second")],
                "earliest first, and a tie by trigger group then name");

        JsonElement first = fires[0];
        first.EnumerateObject().Select(property => property.Name).Should().Equal(
            ["fireTimeUtc", "trigger", "job", "state", "executionGroup", "scheduleDescription", "onlyTheNext"]);
        first.GetProperty("trigger").GetProperty("group").GetString().Should().Be("agenda");
        first.GetProperty("job").GetProperty("name").GetString().Should().Be("agenda-job");
        first.GetProperty("state").GetString().Should().Be("Normal", "a state travels as its name");
        first.GetProperty("scheduleDescription").GetString().Should().Be("Every 20 minutes");
        first.GetProperty("onlyTheNext").GetBoolean().Should().BeFalse();
    }

    [TestCase("", "to is required")]
    [TestCase("to=2000-01-01T00:00:00Z", "History lists what ran")]
    [TestCase("to={0}&take=0", "take must be from 1 to 1000, was 0")]
    [TestCase("to={0}&take=ten", "take must be a number from 1 to 1000")]
    [TestCase("from={0}&to={1}", "is longer than 7 days: narrow it.")]
    public async Task ARefusedQueryIsA400WithTheReason(string query, string words)
    {
        Api api = await StartApi();
        string formatted = string.Format(CultureInfo.InvariantCulture, query,
            Uri.EscapeDataString(start.AddHours(1).ToString("O", CultureInfo.InvariantCulture)),
            Uri.EscapeDataString(start.AddHours(1).AddDays(8).ToString("O", CultureInfo.InvariantCulture)));

        using HttpResponseMessage response = await api.Get(formatted);
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        using JsonDocument problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("detail").GetString().Should().Contain(words);
        problem.RootElement.GetProperty(HttpApiConstants.ProblemDetailsExceptionType).GetString().Should().Be(HttpApiConstants.RequestRefusedExceptionType,
            "the request is what was wrong, and the API names such a refusal one way whichever layer made it");
    }

    [Test]
    public async Task AnUnknownSchedulerIsA404()
    {
        Api api = await StartApi();

        using HttpResponseMessage response = await api.Client.GetAsync("schedulers/no-such-scheduler/triggers/upcoming-fires?" + Window(start, start.AddHours(1)));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// The server's page cap lowers the most fires an answer holds, as it lowers a listing's page: an absent take, or
    /// <c>all</c>, is the cap, and a number above it is refused naming the option.
    /// </summary>
    [Test]
    public async Task MaxPageSizeLowersTheMostFiresAnAnswerHolds()
    {
        Api api = await StartApi(maxPageSize: 2);

        using HttpResponseMessage absent = await api.Get(Window(start, start.AddHours(2)));
        using HttpResponseMessage all = await api.Get(Window(start, start.AddHours(2)) + "&take=all");
        using HttpResponseMessage above = await api.Get(Window(start, start.AddHours(2)) + "&take=3");

        foreach (HttpResponseMessage capped in (HttpResponseMessage[]) [absent, all])
        {
            string body = await capped.Content.ReadAsStringAsync();
            capped.StatusCode.Should().Be(HttpStatusCode.OK, body);
            using JsonDocument answer = JsonDocument.Parse(body);
            answer.RootElement.GetProperty("fires").GetArrayLength().Should().Be(2);
            answer.RootElement.GetProperty("cut").GetBoolean().Should().BeTrue("the window holds more fires than the cap");
        }

        above.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await above.Content.ReadAsStringAsync()).Should().Contain("the server's MaxPageSize");

        using HttpResponseMessage none = await api.Get(Window(start, start.AddHours(2)) + "&take=0");
        none.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await none.Content.ReadAsStringAsync()).Should().Contain("take must be from 1 to 2, was 0", "the refusal names the most this server answers");
    }

    /// <summary>
    /// The dashboard asks for the most fires an answer holds, and a host whose page cap is lower answers as many as
    /// it allows, cut, rather than refuse a request no narrower window could make acceptable.
    /// </summary>
    [Test]
    public async Task HttpSchedulerAskingForTheMostIsAnsweredWithAsManyAsTheHostAllows()
    {
        Api api = await StartApi(maxPageSize: 2);
        RequestCounter counter = new();
        await using HttpScheduler client = new(api.SchedulerName, api.Factory.CreateDefaultClient(counter));

        UpcomingFiresOutcome outcome = await UpcomingFires.Query(client, new UpcomingFiresQuery(start, start.AddHours(2), null, UpcomingFires.MaxTake), CancellationToken.None);

        outcome.Refusal.Should().BeNull("the most is a request for as many as the host answers");
        outcome.Result!.Fires.Should().HaveCount(2);
        outcome.Result.CutAtUtc.Should().Be(start.AddMinutes(50), "the cut says where the host's cap fell");
        counter.Requests.Should().ContainSingle().Which.Should().Contain("take=all");
    }

    [Test]
    public async Task AReadOnlyApiServesTheRoute()
    {
        Api api = await StartApi(readOnly: true);

        using HttpResponseMessage response = await api.Get(Window(start, start.AddHours(1)));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "listing what fires changes nothing, so a read-only API serves it");
    }

    /// <summary>
    /// Through an <see cref="HttpScheduler" /> it is one request, and the answer is the host's own.
    /// </summary>
    [Test]
    public async Task HttpSchedulerAsksTheHostInOneRequestAndAnswersAsTheHostDoes()
    {
        Api api = await StartApi();
        RequestCounter counter = new();
        await using HttpScheduler client = new(api.SchedulerName, api.Factory.CreateDefaultClient(counter));
        UpcomingFiresQuery query = new(start, start.AddHours(2), "agen", 5);

        UpcomingFiresOutcome remote = await UpcomingFires.Query(client, query, CancellationToken.None);
        UpcomingFiresOutcome local = await UpcomingFires.Query(api.Scheduler, query, CancellationToken.None);

        remote.Refusal.Should().BeNull();
        remote.Result!.Fires.Should().Equal(local.Result!.Fires);
        remote.Result.CutAtUtc.Should().Be(local.Result.CutAtUtc).And.Be(start.AddMinutes(70), "the fifth fire is the second trigger's at 70 minutes");
        remote.Result.FromUtc.Should().Be(start);
        counter.Requests.Should().ContainSingle()
            .Which.Should().StartWith($"GET /schedulers/{api.SchedulerName}/triggers/upcoming-fires?from=")
            .And.Contain("&take=5&groupContains=agen", "the host computes over its own store, in one request");
    }

    /// <summary>
    /// Decorators over an <see cref="HttpScheduler" /> — a cluster of targets is one, forwarding to its preferred
    /// member — forward to it, so it is still the one request.
    /// </summary>
    [Test]
    public async Task DecoratorsOverHttpSchedulerForwardTheOneRequest()
    {
        Api api = await StartApi();
        RequestCounter counter = new();
        await using HttpScheduler client = new(api.SchedulerName, api.Factory.CreateDefaultClient(counter));
        IScheduler decorated = new DelegatingScheduler(new DelegatingScheduler(client));

        UpcomingFiresOutcome outcome = await UpcomingFires.Query(decorated, new UpcomingFiresQuery(start, start.AddHours(1), null, 10), CancellationToken.None);

        outcome.Result!.Fires.Should().HaveCount(3);
        counter.Requests.Should().ContainSingle("each decorator forwards the call, so the host still answers it in one request");
    }

    /// <summary>
    /// A cluster of targets asks its preferred member, which answers over the store the members share; the other
    /// member is not asked.
    /// </summary>
    [Test]
    public async Task AClusterOfTargetsAsksItsPreferredMemberOnce()
    {
        Api first = await StartApi();
        Api second = await StartApi();
        RequestCounter toFirst = new();
        RequestCounter toSecond = new();
        await using HttpScheduler a = new(first.SchedulerName, first.Factory.CreateDefaultClient(toFirst));
        await using HttpScheduler b = new(second.SchedulerName, second.Factory.CreateDefaultClient(toSecond));
        ClusterAwareScheduler cluster = new("a+b", [new ClusterMember("a", a, "node-a"), new ClusterMember("b", b, "node-b")], preferred: 1);

        UpcomingFiresOutcome outcome = await UpcomingFires.Query(cluster, new UpcomingFiresQuery(start, start.AddHours(1), null, 10), CancellationToken.None);

        outcome.Result!.Fires.Should().HaveCount(3);
        toSecond.Requests.Should().ContainSingle().Which.Should().Contain("/triggers/upcoming-fires?", "the preferred member answers in one request");
        toFirst.Requests.Should().BeEmpty("a cluster's store-backed reads go to its preferred member alone");
    }

    [Test]
    public async Task ARefusalTheHostMakesComesBackAsTheOutcomesRefusal()
    {
        Api api = await StartApi();
        await using HttpScheduler client = new(api.SchedulerName, api.Client);

        UpcomingFiresOutcome outcome = await UpcomingFires.Query(client, new UpcomingFiresQuery(start, start.AddDays(8), null, 10), CancellationToken.None);

        outcome.Result.Should().BeNull();
        outcome.Refusal.Should().Contain("is longer than 7 days", "the host's own words, decided by the host's clock");
    }

    /// <summary>
    /// A host older than 4.9 has no <c>upcoming-fires</c> route and answers it with a bare <c>404</c>. The answer is
    /// composed here from the listing, the triggers and the calendars it answers with, and is the same.
    /// </summary>
    [Test]
    public async Task AHostWithoutTheRouteIsComposedFromTheRoutesItHas()
    {
        Api api = await StartApi();
        WithoutUpcomingFiresRoute transport = new(new HttpWireTransport(api.Client));
        await using HttpScheduler client = new(api.SchedulerName, transport, jsonSerializerOptions: null, serializerRegistry: null);
        UpcomingFiresQuery query = new(start, start.AddHours(2), null, 100);

        UpcomingFiresOutcome composed = await UpcomingFires.Query(client, query, CancellationToken.None);
        UpcomingFiresOutcome local = await UpcomingFires.Query(api.Scheduler, query, CancellationToken.None);

        composed.Result!.Fires.Should().Equal(local.Result!.Fires, "the same fires, computed on this side");
        transport.Routes.Should().Equal(
            [SchedulerRoutes.QueryUpcomingFires.Name, SchedulerRoutes.QueryTriggers.Name, SchedulerRoutes.FetchTriggers.Name],
            "the route is asked first, and the due triggers are listed and read once it is missing");
    }

    private static string Window(DateTimeOffset from, DateTimeOffset to)
    {
        return "from=" + Uri.EscapeDataString(from.ToString("O", CultureInfo.InvariantCulture))
               + "&to=" + Uri.EscapeDataString(to.ToString("O", CultureInfo.InvariantCulture));
    }

    private async Task<Api> StartApi(bool readOnly = false, int? maxPageSize = null)
    {
        TestContentRoot.Apply();

        WebApplicationFactory<Program> root = new();
        factories.Add(root);

        string schedulerName = $"agenda-{Guid.NewGuid():N}";
        WebApplicationFactory<Program> application = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddQuartzHttpApi(options =>
            {
                options.ReadOnly = readOnly;
                if (maxPageSize is { } cap)
                {
                    options.MaxPageSize = cap;
                }
            });
            services.AddQuartz(schedulerName, quartz => quartz.UseInMemoryStore());
        }));
        factories.Add(application);

        IScheduler scheduler = await application.Services.GetRequiredKeyedService<ISchedulerFactory>(schedulerName).GetScheduler();
        schedulers.Add(scheduler);

        await scheduler.AddJob(JobBuilder.Create<DummyJob>().WithIdentity(jobKey).StoreDurably().Build());
        foreach ((string name, int minutes) in ((string, int)[]) [("b-second", 50), ("a-first", 30)])
        {
            await scheduler.ScheduleJob(TriggerBuilder.Create()
                .WithIdentity(name, "agenda")
                .ForJob(jobKey)
                .StartAt(start.AddMinutes(minutes))
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(20)).RepeatForever())
                .Build());
        }

        return new Api(schedulerName, application.CreateClient(), application, scheduler);
    }

    private sealed record Api(string SchedulerName, HttpClient Client, WebApplicationFactory<Program> Factory, IScheduler Scheduler)
    {
        public Task<HttpResponseMessage> Get(string query)
        {
            return Client.GetAsync($"schedulers/{SchedulerName}/triggers/upcoming-fires?{query}");
        }
    }

    /// <summary>
    /// Every request a client sends, as method, path and query, in the order it sent them.
    /// </summary>
    private sealed class RequestCounter : DelegatingHandler
    {
        private readonly ConcurrentQueue<string> requests = new();

        public List<string> Requests => [.. requests];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Enqueue($"{request.Method} {request.RequestUri!.PathAndQuery}");
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// A host as 4.8 is to a 4.9 client: every route but <c>upcoming-fires</c>, which answers a <c>404</c> with no
    /// problem details, as ASP.NET Core does for a path nothing is mapped at.
    /// </summary>
    private sealed class WithoutUpcomingFiresRoute : IWireTransport
    {
        private readonly IWireTransport inner;
        private readonly ConcurrentQueue<string> routes = new();

        public WithoutUpcomingFiresRoute(IWireTransport inner)
        {
            this.inner = inner;
        }

        public List<string> Routes => [.. routes];

        public ValueTask<WireResponse> Send(WireRequest request, CancellationToken cancellationToken = default)
        {
            routes.Enqueue(request.Route.Name);
            return request.Route == SchedulerRoutes.QueryUpcomingFires
                ? ValueTask.FromResult(new WireResponse(HttpStatusCode.NotFound, []))
                : inner.Send(request, cancellationToken);
        }
    }
}
