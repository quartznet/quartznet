using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// A trigger's upcoming fire times over HTTP, both ways a client can ask: the <c>fire-times</c> route, which
/// runs <see cref="SchedulerFireTimesExtensions.GetUpcomingFireTimes" /> on the host, and the extension called
/// on an <see cref="HttpScheduler" />, which sends that route — or, to a host that has none, computes from the
/// trigger and calendar that host answers with.
/// </summary>
/// <remarks>
/// The trigger fires at 09:00 in Kathmandu, UTC+05:45, every day from 5 October 2030, and its calendar excludes
/// the 7th there. Starting years ahead keeps every fire time independent of the machine's clock, which the
/// host's scheduler reads when it stores the trigger.
/// </remarks>
[NonParallelizable]
public sealed class TriggerFireTimesOverHttpTest
{
    private static readonly JobKey jobKey = new("export", "reports");
    private static readonly TriggerKey triggerKey = new("daily", "reports");
    private static readonly DateTimeOffset nine = new(2030, 10, 5, 3, 15, 0, TimeSpan.Zero);

    private readonly List<WebApplicationFactory<Program>> factories = [];
    private readonly List<IScheduler> schedulers = [];

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
    public async Task TheRouteAnswersTheNextFireTimesInUtcLessWhatTheCalendarExcludes()
    {
        Api api = await StartApi();

        using HttpResponseMessage response = await api.Get("reports/daily/fire-times?count=3");
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using JsonDocument answer = JsonDocument.Parse(body);
        answer.RootElement.EnumerateObject().Select(property => property.Name).Should().Equal(["fireTimesUtc"],
            "the body is one array, named for what it holds");
        answer.RootElement.GetProperty("fireTimesUtc").EnumerateArray().Select(element => element.GetString()).Should().Equal(
            [
                "2030-10-05T03:15:00+00:00",
                "2030-10-06T03:15:00+00:00",
                "2030-10-08T03:15:00+00:00",
            ],
            "09:00 in Kathmandu is 03:15 UTC, and the calendar excludes the 7th there");
    }

    [Test]
    public async Task WithoutACountTheRouteAnswersTen()
    {
        Api api = await StartApi();

        using HttpResponseMessage response = await api.Get("reports/daily/fire-times");
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using JsonDocument answer = JsonDocument.Parse(body);
        answer.RootElement.GetProperty("fireTimesUtc").GetArrayLength().Should().Be(10, "ten is the documented default");
    }

    [Test]
    public async Task ATriggerMeasuredFromCompletionIsAnsweredWithItsNextFireTimeAlone()
    {
        Api api = await StartApi();
        IScheduler scheduler = schedulers[^1];
        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity("fixed-delay", "reports")
            .ForJob(jobKey)
            .StartAt(new DateTimeOffset(2030, 10, 5, 0, 0, 0, TimeSpan.Zero))
            .WithSchedule(SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromMinutes(5)).RepeatForever().MeasureIntervalFromCompletion())
            .Build());

        using HttpResponseMessage response = await api.Get("reports/fixed-delay/fire-times?count=10");
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using JsonDocument answer = JsonDocument.Parse(body);
        answer.RootElement.GetProperty("fireTimesUtc").EnumerateArray().Select(element => element.GetString()).Should().Equal(
            ["2030-10-05T00:00:00+00:00"],
            "each run after the next starts five minutes after the one before it completes, which the host cannot know yet");
    }

    [TestCase(0)]
    [TestCase(101)]
    public async Task ACountOutsideOneToAHundredIsA400(int count)
    {
        Api api = await StartApi();

        using HttpResponseMessage response = await api.Get($"reports/daily/fire-times?count={count}");
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        using JsonDocument problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("detail").GetString().Should().Be($"count must be from 1 to 100, was {count}", "the refusal says what to fix");
        problem.RootElement.GetProperty(HttpApiConstants.ProblemDetailsExceptionType).GetString().Should().Be(HttpApiConstants.RequestRefusedExceptionType,
            "the request is what was wrong, and the API names such a refusal one way whichever layer made it");
    }

    [Test]
    public async Task ACountThatIsNotANumberIsA400()
    {
        Api api = await StartApi();

        using HttpResponseMessage response = await api.Get("reports/daily/fire-times?count=ten");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "count binds as a number, as skip does");
    }

    [Test]
    public async Task AnUnknownTriggerIsA404()
    {
        Api api = await StartApi();

        using HttpResponseMessage response = await api.Get("reports/ghost/fire-times");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "the trigger is in the path, as it is on every single-trigger read");
    }

    [Test]
    public async Task AReadOnlyApiServesTheRoute()
    {
        Api api = await StartApi(readOnly: true);

        using HttpResponseMessage response = await api.Get("reports/daily/fire-times?count=1");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "computing fire times changes nothing, so a read-only API serves it");
    }

    /// <summary>
    /// The extension on an <see cref="HttpScheduler" /> is one request: the host computes with the trigger and
    /// calendar it stores, and nothing is read back to compute here.
    /// </summary>
    [Test]
    public async Task HttpSchedulerAsksTheHostInOneRequest()
    {
        Api api = await StartApi();
        RequestCounter counter = new();
        await using HttpScheduler client = new(api.SchedulerName, api.Factory.CreateDefaultClient(counter));

        List<DateTimeOffset>? fireTimes = await client.GetUpcomingFireTimes(triggerKey, 3);

        fireTimes.Should().Equal([nine, nine.AddDays(1), nine.AddDays(3)]);
        counter.Requests.Should().Equal([$"GET /schedulers/{api.SchedulerName}/triggers/reports/daily/fire-times?count=3"],
            "a remote scheduler computes its own fire times, in one request");
    }

    /// <summary>
    /// Decorators over an <see cref="HttpScheduler" /> — a cluster of targets is one — forward to it, so it is
    /// still the one request.
    /// </summary>
    [Test]
    public async Task DecoratorsOverHttpSchedulerAskInOneRequestToo()
    {
        Api api = await StartApi();
        RequestCounter counter = new();
        await using HttpScheduler client = new(api.SchedulerName, api.Factory.CreateDefaultClient(counter));
        IScheduler decorated = new DelegatingScheduler(new DelegatingScheduler(client));

        List<DateTimeOffset>? fireTimes = await decorated.GetUpcomingFireTimes(triggerKey, 2);

        fireTimes.Should().Equal([nine, nine.AddDays(1)]);
        counter.Requests.Should().Equal([$"GET /schedulers/{api.SchedulerName}/triggers/reports/daily/fire-times?count=2"],
            "each decorator forwards the call, so the host still answers it in one request");
    }

    [Test]
    public async Task HttpSchedulerAnswersNullForATriggerTheHostDoesNotHold()
    {
        Api api = await StartApi();
        await using HttpScheduler client = new(api.SchedulerName, api.Client);

        List<DateTimeOffset>? fireTimes = await client.GetUpcomingFireTimes(new TriggerKey("ghost", "reports"), 3);

        fireTimes.Should().BeNull("the host's 404 is the in-process call's absent trigger");
    }

    /// <summary>
    /// A host older than 4.7 has no <c>fire-times</c> route and answers it with a bare <c>404</c>. Its fire times
    /// are computed here from the trigger and calendar it answers with, as any scheduler's are.
    /// </summary>
    [Test]
    public async Task AHostWithoutTheRouteIsAnsweredFromTheTriggerAndCalendarItHas()
    {
        Api api = await StartApi();
        WithoutFireTimesRoute transport = new(new HttpWireTransport(api.Client));
        await using HttpScheduler client = new(api.SchedulerName, transport, jsonSerializerOptions: null, serializerRegistry: null);

        List<DateTimeOffset>? fireTimes = await client.GetUpcomingFireTimes(triggerKey, 3);

        fireTimes.Should().Equal([nine, nine.AddDays(1), nine.AddDays(3)], "the same times, computed on this side");
        transport.Routes.Should().Equal(
            [SchedulerRoutes.GetTriggerFireTimes.Name, SchedulerRoutes.GetTrigger.Name, SchedulerRoutes.GetCalendar.Name],
            "the route is asked first, and the trigger and its calendar are read once it is missing");
    }

    private async Task<Api> StartApi(bool readOnly = false)
    {
        TestContentRoot.Apply();

        WebApplicationFactory<Program> root = new();
        factories.Add(root);

        string schedulerName = $"fire-times-{Guid.NewGuid():N}";
        WebApplicationFactory<Program> application = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddQuartzHttpApi(options => options.ReadOnly = readOnly);
            services.AddQuartz(schedulerName, quartz => quartz.UseInMemoryStore());
        }));
        factories.Add(application);

        IScheduler scheduler = await application.Services.GetRequiredKeyedService<ISchedulerFactory>(schedulerName).GetScheduler();
        schedulers.Add(scheduler);

        TimeZoneInfo kathmandu = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kathmandu");
        HolidayCalendar holidays = new() { TimeZone = kathmandu };
        holidays.AddExcludedDay(new DateOnly(2030, 10, 7));
        await scheduler.AddCalendar("holidays", holidays);

        await scheduler.ScheduleJob(
            JobBuilder.Create<DummyJob>().WithIdentity(jobKey).Build(),
            TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .StartAt(new DateTimeOffset(2030, 10, 5, 0, 0, 0, TimeSpan.Zero))
                .WithCalendarName("holidays")
                .WithCronSchedule("0 0 9 * * ?", cron => cron.InTimeZone(kathmandu))
                .Build());

        return new Api(schedulerName, application.CreateClient(), application);
    }

    private sealed record Api(string SchedulerName, HttpClient Client, WebApplicationFactory<Program> Factory)
    {
        public Task<HttpResponseMessage> Get(string triggerPath)
        {
            return Client.GetAsync($"schedulers/{SchedulerName}/triggers/{triggerPath}");
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
    /// A host as 4.6 is to a 4.7 client: every route but <c>fire-times</c>, which answers a <c>404</c> with no
    /// problem details, as ASP.NET Core does for a path nothing is mapped at.
    /// </summary>
    private sealed class WithoutFireTimesRoute : IWireTransport
    {
        private readonly IWireTransport inner;
        private readonly ConcurrentQueue<string> routes = new();

        public WithoutFireTimesRoute(IWireTransport inner)
        {
            this.inner = inner;
        }

        public List<string> Routes => [.. routes];

        public ValueTask<WireResponse> Send(WireRequest request, CancellationToken cancellationToken = default)
        {
            routes.Enqueue(request.Route.Name);
            return request.Route == SchedulerRoutes.GetTriggerFireTimes
                ? ValueTask.FromResult(new WireResponse(HttpStatusCode.NotFound, []))
                : inner.Send(request, cancellationToken);
        }
    }
}
