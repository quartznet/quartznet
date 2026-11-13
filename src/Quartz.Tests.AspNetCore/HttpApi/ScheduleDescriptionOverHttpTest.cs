using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// The trigger listing carries each row's schedule in words and the expression it is written in (#4243): on the
/// wire, through an <see cref="HttpScheduler" /> in the same one request, through decorators over one, and not at
/// all from a host older than 4.8.
/// </summary>
[NonParallelizable]
public sealed class ScheduleDescriptionOverHttpTest
{
    private static readonly JobKey jobKey = new("export", "reports");

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
    public async Task TheListingCarriesEachTriggersScheduleInWordsAndItsExpression()
    {
        Api api = await StartApi();

        using HttpResponseMessage response = await api.Client.GetAsync($"schedulers/{api.SchedulerName}/triggers");
        string body = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue(body);
        using JsonDocument listing = JsonDocument.Parse(body);
        List<(string? Name, string? Description, string? Expression)> rows = listing.RootElement.GetProperty("items").EnumerateArray()
            .Select(item => (
                item.GetProperty("name").GetString(),
                item.GetProperty("scheduleDescription").GetString(),
                item.GetProperty("scheduleExpression").GetString()))
            .ToList();

        rows.Should().Equal(
            ("weekday-mornings", "Every weekday at 07:00 (America/Chicago), excluding calendar 'holidays'", "0 0 7 ? * MON-FRI"),
            ("every-30-seconds", "Every 30 seconds", null));
    }

    [Test]
    public async Task HttpSchedulerReadsTheDescriptionsInTheOneListingRequest()
    {
        Api api = await StartApi();
        RequestCounter counter = new();
        await using HttpScheduler client = new(api.SchedulerName, api.Factory.CreateDefaultClient(counter));

        PagedResult<DescribedTriggerHeader> page = await DescribedTriggerListing.Query(client, new TriggerQuery());

        page.Items.Select(x => x.ScheduleDescription).Should().Equal(
            "Every weekday at 07:00 (America/Chicago), excluding calendar 'holidays'",
            "Every 30 seconds");
        counter.Requests.Should().ContainSingle("the host described the page it listed, so nothing is read back to describe it here")
            .Which.Should().StartWith($"GET /schedulers/{api.SchedulerName}/triggers");
    }

    [Test]
    public async Task DecoratorsOverHttpSchedulerSendTheOneListingRequestToo()
    {
        Api api = await StartApi();
        RequestCounter counter = new();
        await using HttpScheduler client = new(api.SchedulerName, api.Factory.CreateDefaultClient(counter));
        IScheduler decorated = new DelegatingScheduler(new DelegatingScheduler(client));

        PagedResult<DescribedTriggerHeader> page = await DescribedTriggerListing.Query(decorated, new TriggerQuery());

        page.Items.Select(x => x.ScheduleExpression).Should().Equal("0 0 7 ? * MON-FRI", null);
        counter.Requests.Should().ContainSingle("each decorator forwards the listing to the scheduler it ends in");
    }

    [Test]
    public async Task ADecoratorOverASchedulerOfThisProcessDescribesThePageItself()
    {
        Api api = await StartApi();
        IScheduler decorated = new DelegatingScheduler(schedulers[^1]);

        PagedResult<DescribedTriggerHeader> page = await DescribedTriggerListing.Query(decorated, new TriggerQuery());

        page.Items.Select(x => x.ScheduleDescription).Should().Equal(
            "Every weekday at 07:00 (America/Chicago), excluding calendar 'holidays'",
            "Every 30 seconds");
    }

    /// <summary>
    /// A host older than 4.8 lists the same rows with neither member. They come back undescribed, from the one
    /// request, and the dashboard shows a dash for them.
    /// </summary>
    [Test]
    public async Task AHostOlderThan48SendsNoDescriptionsAndTheRowsComeBackWithout()
    {
        Api api = await StartApi();
        WithoutScheduleMembers transport = new(new HttpWireTransport(api.Client));
        await using HttpScheduler client = new(api.SchedulerName, transport, jsonSerializerOptions: null, serializerRegistry: null);

        PagedResult<DescribedTriggerHeader> page = await DescribedTriggerListing.Query(client, new TriggerQuery());

        page.Items.Select(x => x.Header.Key.Name).Should().Equal("weekday-mornings", "every-30-seconds");
        page.Items.Should().AllSatisfy(x =>
        {
            x.ScheduleDescription.Should().BeNull();
            x.ScheduleExpression.Should().BeNull();
        });
        transport.Routes.Should().Equal([SchedulerRoutes.QueryTriggers.Name], "an older host's rows are not read again to describe them");
    }

    private async Task<Api> StartApi()
    {
        TestContentRoot.Apply();

        WebApplicationFactory<Program> root = new();
        factories.Add(root);

        string schedulerName = $"schedule-descriptions-{Guid.NewGuid():N}";
        WebApplicationFactory<Program> application = root.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.AddQuartzHttpApi();
            services.AddQuartz(schedulerName, quartz => quartz.UseInMemoryStore());
        }));
        factories.Add(application);

        IScheduler scheduler = await application.Services.GetRequiredKeyedService<ISchedulerFactory>(schedulerName).GetScheduler();
        schedulers.Add(scheduler);

        await scheduler.AddCalendar("holidays", new Quartz.Impl.Calendar.HolidayCalendar());
        await scheduler.AddJob(JobBuilder.Create<DummyJob>().WithIdentity(jobKey).StoreDurably().Build());
        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity("weekday-mornings", "a")
            .ForJob(jobKey)
            .WithCalendarName("holidays")
            .WithCronSchedule("0 0 7 ? * MON-FRI", cron => cron.InTimeZone(TimeZones.FindById("America/Chicago")))
            .Build());
        await scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity("every-30-seconds", "b")
            .ForJob(jobKey)
            .StartAt(new DateTimeOffset(2030, 10, 5, 0, 0, 0, TimeSpan.Zero))
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(30)).RepeatForever())
            .Build());

        return new Api(schedulerName, application.CreateClient(), application);
    }

    private sealed record Api(string SchedulerName, HttpClient Client, WebApplicationFactory<Program> Factory);

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
    /// A host as 4.7 is to a 4.8 client: its trigger listing has no <c>scheduleDescription</c> and no
    /// <c>scheduleExpression</c>.
    /// </summary>
    private sealed class WithoutScheduleMembers : IWireTransport
    {
        private readonly IWireTransport inner;
        private readonly ConcurrentQueue<string> routes = new();

        public WithoutScheduleMembers(IWireTransport inner)
        {
            this.inner = inner;
        }

        public List<string> Routes => [.. routes];

        public async ValueTask<WireResponse> Send(WireRequest request, CancellationToken cancellationToken = default)
        {
            routes.Enqueue(request.Route.Name);
            WireResponse response = await inner.Send(request, cancellationToken);
            if (request.Route != SchedulerRoutes.QueryTriggers)
            {
                return response;
            }

            JsonNode listing = JsonNode.Parse(response.Body)!;
            foreach (JsonNode? item in listing["items"]!.AsArray())
            {
                JsonObject row = item!.AsObject();
                row.Remove("scheduleDescription").Should().BeTrue("a 4.8 host sends the member, so there is one to take away");
                row.Remove("scheduleExpression");
            }

            return response with { Body = JsonSerializer.SerializeToUtf8Bytes(listing) };
        }
    }
}
