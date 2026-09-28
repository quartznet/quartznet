using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// A backfill over HTTP, both ways a client can ask for one: the <c>backfill</c> route, which runs
/// <see cref="SchedulerBackfillExtensions.Backfill" /> on the host, and the extension called on an
/// <see cref="HttpScheduler" />, which sends that route — or, to a host that has none, composes the reads
/// and writes every scheduler has.
/// </summary>
/// <remarks>
/// A real scheduler behind the API, over the in-memory store and over a SQLite file, never started: what is
/// under test is what a backfill stores, and a running scheduler would fire it.
/// </remarks>
[NonParallelizable]
public sealed class BackfillOverHttpTest
{
    public enum StoreKind
    {
        InMemory,
        Sqlite,
    }

    private const int AuditEventId = 9007;

    private static readonly JobKey jobKey = new("export", "reports");
    private static readonly TriggerKey triggerKey = new("hourly", "reports");

    private readonly SqliteStores stores = new("backfill-over-http");
    private readonly List<WebApplicationFactory<Program>> factories = [];
    private readonly List<IScheduler> schedulers = [];
    private readonly RecordingLoggerProvider logs = new();

    /// <summary>
    /// The top of the current hour, so a range ending there is in the past and holds whole hours.
    /// </summary>
    private DateTimeOffset hour;

    [SetUp]
    public void SetUp()
    {
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        hour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
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
        logs.Clear();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        stores.Dispose();
    }

    [TestCase(StoreKind.InMemory)]
    [TestCase(StoreKind.Sqlite)]
    public async Task TheRouteBackfillsTheRangeAndAnswersWhatItDid(StoreKind store)
    {
        Api api = await StartApi(store);

        using HttpResponseMessage response = await api.Post(new { from = hour.AddHours(-6), to = hour, spacing = "00:00:30", maxSlots = 10 });
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        using JsonDocument answer = JsonDocument.Parse(body);
        JsonElement root = answer.RootElement;
        root.GetProperty("slotsFound").GetInt32().Should().Be(6, "an hourly trigger has six fire times in six hours");
        root.GetProperty("scheduled").GetInt32().Should().Be(6);
        root.GetProperty("alreadyScheduled").GetInt32().Should().Be(0);
        root.GetProperty("firstSlot").GetDateTimeOffset().Should().Be(hour.AddHours(-6));
        root.GetProperty("lastSlot").GetDateTimeOffset().Should().Be(hour.AddHours(-1));

        JsonElement first = root.GetProperty("triggers")[0];
        first.GetProperty("group").GetString().Should().Be("backfill:reports");
        first.GetProperty("name").GetString().Should().Be("hourly@" + hour.AddHours(-6).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + "Z");

        List<TriggerKey> stored = await api.Scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("backfill:reports"));
        stored.Should().HaveCount(6, "the route stores what it answers with");

        ITrigger last = (await api.Scheduler.GetTrigger(stored.OrderBy(key => key.Name, StringComparer.Ordinal).Last()))!;
        ITrigger firstStored = (await api.Scheduler.GetTrigger(stored.OrderBy(key => key.Name, StringComparer.Ordinal).First()))!;
        (last.StartTimeUtc - firstStored.StartTimeUtc).Should().Be(TimeSpan.FromSeconds(150),
            "the body's spacing staggers the starts: the sixth slot starts five spacings after the first");
        last.JobDataMap.GetString(SchedulerConstants.BackfillOriginalFireTime).Should().NotBeNull("the slot travels with the trigger into the store");
    }

    /// <summary>
    /// The extension on an <see cref="HttpScheduler" /> is one request, however many slots the range holds:
    /// the host backfills, and its audit records the one thing the operator did.
    /// </summary>
    [Test]
    public async Task HttpSchedulerBackfillsInOneRequestThatIsAuditedOnce()
    {
        Api api = await StartApi(StoreKind.Sqlite);
        RequestCounter counter = new();
        await using HttpScheduler client = new(api.SchedulerName, api.Factory.CreateDefaultClient(counter));

        BackfillResult result = await client.Backfill(triggerKey, hour.AddHours(-6), hour, new BackfillOptions { Spacing = TimeSpan.FromSeconds(30) });

        result.Scheduled.Should().Be(6, "an hourly trigger has six fire times in six hours");
        result.FirstSlot.Should().Be(hour.AddHours(-6));
        result.ScheduledTriggers.Should().HaveCount(6).And.AllSatisfy(key => key.Group.Should().Be("backfill:reports"));
        counter.Requests.Should().Equal([$"POST /schedulers/{api.SchedulerName}/triggers/reports/hourly/backfill"],
            "a remote backfill is one request, not a read and a write per slot");
        logs.WithEventId(AuditEventId).Select(entry => entry.Properties["Operation"]).Should().Equal(["BackfillTrigger"],
            "the host audits one backfill, and no ScheduleJob of its own per slot");

        BackfillResult again = await client.Backfill(triggerKey, hour.AddHours(-6), hour);

        again.AlreadyScheduled.Should().Be(6, "the host finds what the first run stored");
        again.Scheduled.Should().Be(0);
        (await api.Scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("backfill:reports"))).Should().HaveCount(6);
    }

    /// <summary>
    /// Decorators over an <see cref="HttpScheduler" /> forward the backfill to it, so it is still the one
    /// request rather than a read and a write per slot.
    /// </summary>
    [Test]
    public async Task DecoratorsOverHttpSchedulerBackfillInOneRequestToo()
    {
        Api api = await StartApi(StoreKind.InMemory);
        RequestCounter counter = new();
        await using HttpScheduler client = new(api.SchedulerName, api.Factory.CreateDefaultClient(counter));
        IScheduler decorated = new DelegatingScheduler(new DelegatingScheduler(client));

        BackfillResult result = await decorated.Backfill(triggerKey, hour.AddHours(-3), hour);

        result.Scheduled.Should().Be(3, "an hourly trigger has three fire times in three hours");
        counter.Requests.Should().Equal([$"POST /schedulers/{api.SchedulerName}/triggers/reports/hourly/backfill"],
            "each decorator forwards the backfill, so the host still backfills in one request");
    }

    /// <summary>
    /// What the host refuses arrives as what an in-process call would have raised: the same exception type,
    /// the same words, and the argument they are about. The host's clock is the one that decides "now".
    /// </summary>
    [TestCase("reversed", "to", "The range must end after it starts*")]
    [TestCase("future", "to", "*after the scheduler's current time*")]
    [TestCase("too many", "options", "The range holds 6 slots of trigger 'reports.hourly', more than BackfillOptions.MaxSlots (2) allows*")]
    [TestCase("negative spacing", "options", "BackfillOptions.Spacing must not be negative*")]
    public async Task HttpSchedulerRaisesTheHostsRefusalAsAnInProcessCallWould(string refused, string parameterName, string message)
    {
        Api api = await StartApi(StoreKind.InMemory);
        RequestCounter counter = new();
        await using HttpScheduler client = new(api.SchedulerName, api.Factory.CreateDefaultClient(counter));

        Func<Task> act = refused switch
        {
            "reversed" => () => client.Backfill(triggerKey, hour.AddHours(-1), hour.AddHours(-2)).AsTask(),
            "future" => () => client.Backfill(triggerKey, hour.AddHours(-6), hour.AddHours(2)).AsTask(),
            "too many" => () => client.Backfill(triggerKey, hour.AddHours(-6), hour, new BackfillOptions { MaxSlots = 2 }).AsTask(),
            _ => () => client.Backfill(triggerKey, hour.AddHours(-6), hour, new BackfillOptions { Spacing = TimeSpan.FromMinutes(-1) }).AsTask()
        };

        ArgumentException refusal = (await act.Should().ThrowExactlyAsync<ArgumentException>().WithMessage(message)).Which;
        refusal.ParamName.Should().Be(parameterName, "the refusal is about the same argument it is about in process");
        counter.Requests.Should().ContainSingle("the host decides, in the one request");
        (await api.Scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("backfill:reports"))).Should().BeEmpty();
    }

    [Test]
    public async Task HttpSchedulerRaisesAMissingTriggerAsAnObjectThatDoesNotExist()
    {
        Api api = await StartApi(StoreKind.InMemory);
        await using HttpScheduler client = new(api.SchedulerName, api.Client);

        Func<Task> act = () => client.Backfill(new TriggerKey("ghost", "reports"), hour.AddHours(-2), hour).AsTask();

        await act.Should().ThrowAsync<ObjectDoesNotExistException>().WithMessage("Trigger 'reports.ghost' does not exist*",
            "the host's 404 is the in-process call's missing trigger");
    }

    /// <summary>
    /// A host older than 4.3 has no <c>backfill</c> route and answers it with a bare <c>404</c>. It is
    /// backfilled through the routes it has, as any scheduler is.
    /// </summary>
    [Test]
    public async Task AHostWithoutTheRouteIsBackfilledThroughTheRoutesItHas()
    {
        Api api = await StartApi(StoreKind.InMemory);
        WithoutBackfillRoute transport = new(new HttpWireTransport(api.Client));
        await using HttpScheduler client = new(api.SchedulerName, transport, jsonSerializerOptions: null, serializerRegistry: null);

        BackfillResult result = await client.Backfill(triggerKey, hour.AddHours(-3), hour);

        result.Scheduled.Should().Be(3);
        transport.Routes.Should().StartWith(SchedulerRoutes.BackfillTrigger.Name)
            .And.Contain(SchedulerRoutes.ScheduleJob.Name, "the slots are stored one schedule request each");
        (await api.Scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("backfill:reports"))).Should().HaveCount(3);
    }

    [TestCase("reversed", "The range must end after it starts*")]
    [TestCase("future", "*after the scheduler's current time*")]
    [TestCase("too many", "The range holds 6 slots of trigger 'reports.hourly', more than BackfillOptions.MaxSlots (2) allows*")]
    [TestCase("negative spacing", "BackfillOptions.Spacing must not be negative*")]
    public async Task ARefusalIsA400ProblemAndSchedulesNothing(string refused, string detail)
    {
        Api api = await StartApi(StoreKind.InMemory);
        object request = refused switch
        {
            "reversed" => new { from = hour.AddHours(-1), to = hour.AddHours(-2) },
            "future" => new { from = hour.AddHours(-6), to = hour.AddHours(2) },
            "too many" => new { from = hour.AddHours(-6), to = hour, maxSlots = 2 },
            _ => (object) new { from = hour.AddHours(-6), to = hour, spacing = "-00:01:00" }
        };

        using HttpResponseMessage response = await api.Post(request);
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        using JsonDocument problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("detail").GetString().Should().Match(detail, "the refusal carries the extension's own words");
        problem.RootElement.GetProperty(HttpApiConstants.ProblemDetailsExceptionType).GetString().Should().Be(HttpApiConstants.RequestRefusedExceptionType,
            "the request is what was wrong, and the API names such a refusal one way whichever layer made it");
        (await api.Scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("backfill:reports"))).Should().BeEmpty(
            "a refusal is decided before anything is written");
        logs.WithEventId(AuditEventId).Should().BeEmpty("nothing was changed, so nothing is audited");
    }

    [Test]
    public async Task ABodyWithoutARangeIsRefusedBeforeTheSchedulerIsAsked()
    {
        Api api = await StartApi(StoreKind.InMemory);

        using HttpResponseMessage response = await api.Post(new { maxSlots = 5 });
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, body);
        body.Should().Contain("Missing from").And.Contain("Missing to");
    }

    [Test]
    public async Task AMissingTriggerIsA404()
    {
        Api api = await StartApi(StoreKind.InMemory);

        using HttpResponseMessage response = await api.Post(new { from = hour.AddHours(-2), to = hour }, "reports/ghost");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, "the trigger is in the path, as it is on every single-trigger read");
    }

    [Test]
    public async Task ASuccessfulBackfillIsAuditedUnderTheRoutesName()
    {
        Api api = await StartApi(StoreKind.InMemory);

        using HttpResponseMessage response = await api.Post(new { from = hour.AddHours(-2), to = hour });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        logs.WithEventId(AuditEventId).Should().ContainSingle("a backfill changes the schedule, once per request however many slots it stored")
            .Which.Properties.Should().Contain(new KeyValuePair<string, string?>("Operation", "BackfillTrigger"));
    }

    [Test]
    public async Task AReadOnlyApiRefusesTheRoute()
    {
        Api api = await StartApi(StoreKind.InMemory, readOnly: true);

        using HttpResponseMessage response = await api.Post(new { from = hour.AddHours(-2), to = hour });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, "a backfill schedules triggers, which is what a read-only API refuses");
        (await api.Scheduler.GetTriggerKeys(GroupMatcher<TriggerKey>.GroupEquals("backfill:reports"))).Should().BeEmpty();
    }

    private async Task<Api> StartApi(StoreKind store, bool readOnly = false)
    {
        TestContentRoot.Apply();

        WebApplicationFactory<Program> root = new();
        factories.Add(root);

        // A name of its own per test, because the SQLite file is kept per name for the whole fixture.
        string schedulerName = $"backfill-{Guid.NewGuid():N}";
        WebApplicationFactory<Program> application = root.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging => logging.AddProvider(logs));
            builder.ConfigureServices(services =>
            {
                services.AddQuartzHttpApi(options => options.ReadOnly = readOnly);
                services.AddQuartz(schedulerName, quartz =>
                {
                    if (store == StoreKind.Sqlite)
                    {
                        stores.Configure(quartz, schedulerName);
                    }
                    else
                    {
                        quartz.UseInMemoryStore();
                    }
                });
            });
        });
        factories.Add(application);

        IScheduler scheduler = await application.Services.GetRequiredKeyedService<ISchedulerFactory>(schedulerName).GetScheduler();
        schedulers.Add(scheduler);

        await scheduler.ScheduleJob(
            JobBuilder.Create<DummyJob>().WithIdentity(jobKey).Build(),
            TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(jobKey)
                .StartAt(hour.AddDays(-2))
                .WithCronSchedule("0 0 * * * ?", cron => cron.InTimeZone(TimeZoneInfo.Utc))
                .Build());

        logs.Clear();
        return new Api(schedulerName, scheduler, application.CreateClient(), application);
    }

    private sealed record Api(string SchedulerName, IScheduler Scheduler, HttpClient Client, WebApplicationFactory<Program> Factory)
    {
        public Task<HttpResponseMessage> Post(object body, string trigger = "reports/hourly")
        {
            StringContent content = new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            return Client.PostAsync($"schedulers/{SchedulerName}/triggers/{trigger}/backfill", content);
        }
    }

    /// <summary>
    /// Every request a client sends, as method and path, in the order it sent them.
    /// </summary>
    private sealed class RequestCounter : DelegatingHandler
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> requests = new();

        public List<string> Requests => [.. requests];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Enqueue($"{request.Method} {request.RequestUri!.AbsolutePath}");
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// A host as 4.2 is to a 4.3 client: every route but <c>backfill</c>, which answers a <c>404</c> with no
    /// problem details, as ASP.NET Core does for a path nothing is mapped at.
    /// </summary>
    private sealed class WithoutBackfillRoute : IWireTransport
    {
        private readonly IWireTransport inner;
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> routes = new();

        public WithoutBackfillRoute(IWireTransport inner)
        {
            this.inner = inner;
        }

        public List<string> Routes => [.. routes];

        public ValueTask<WireResponse> Send(WireRequest request, CancellationToken cancellationToken = default)
        {
            routes.Enqueue(request.Route.Name);
            return request.Route == SchedulerRoutes.BackfillTrigger
                ? ValueTask.FromResult(new WireResponse(HttpStatusCode.NotFound, []))
                : inner.Send(request, cancellationToken);
        }
    }
}
