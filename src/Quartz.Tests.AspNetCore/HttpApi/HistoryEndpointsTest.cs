using System.Text.Json;

using FakeItEasy;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Serialization.SystemTextJson;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// The three history routes: what a scheduler has run, what it has missed, and how many misfires it has
/// had lately.
/// </summary>
/// <remarks>
/// The history belongs to the process the scheduler runs in — it is what the scheduler <em>did</em>,
/// which no job store keeps — so these routes are what lets anything outside that process read it. A
/// dashboard fronting a scheduler over HTTP had no history at all before them.
/// <para>
/// A host per test, because the history store is a singleton: a row one test recorded would otherwise be
/// in the page the next one reads.
/// </para>
/// </remarks>
public sealed class HistoryEndpointsTest
{
    private const string SchedulerUrl = "schedulers/" + TestData.SchedulerName;

    private readonly List<WebApplicationFactory<Program>> factories = [];

    private IExecutionHistoryStore history = null!;
    private HttpClient client = null!;

    [SetUp]
    public void SetUp()
    {
        TestContentRoot.Apply();

        WebApplicationFactory<Program> factory = new();
        factories.Add(factory);

        client = factory.CreateClient();

        IScheduler fake = A.Fake<IScheduler>();
        A.CallTo(() => fake.SchedulerName).Returns(TestData.SchedulerName);

        ISchedulerRepository repository = factory.Services.GetRequiredService<ISchedulerRepository>();
        foreach (IScheduler bound in repository.LookupAll())
        {
            repository.Remove(bound.SchedulerName);
        }

        repository.Bind(fake);
        history = factory.Services.GetRequiredService<IExecutionHistoryStore>();
    }

    [TearDown]
    public async Task TearDown()
    {
        client.Dispose();

        foreach (WebApplicationFactory<Program> factory in factories)
        {
            await factory.DisposeAsync();
        }

        factories.Clear();
    }

    /// <summary>
    /// Mapping the API records history, so a worker that maps it answers these routes rather than
    /// answering them empty with nothing to say why.
    /// </summary>
    [Test]
    public void TheApiRecordsHistoryByDefault()
    {
        history.Should().BeOfType<InMemoryExecutionHistoryStore>(
            "AddQuartzHttpApi() installs the shipped store, bounded as the dashboard's has always been");
    }

    [Test]
    public async Task ExecutionsAreListedNewestFirstAndPaged()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        for (int index = 0; index < 3; index++)
        {
            await history.AddExecution(Entry(now.AddSeconds(index), "job" + index));
        }

        PagedResultDto<ExecutionHistoryEntryDto> first = await Read<PagedResultDto<ExecutionHistoryEntryDto>>(
            $"{SchedulerUrl}/history/executions?take=2&includeTotalCount=true");

        first.Items.Select(item => item.JobName).Should().Equal(["job2", "job1"], "a history page reads newest first");
        first.HasMore.Should().BeTrue();
        first.TotalCount.Should().Be(3);

        PagedResultDto<ExecutionHistoryEntryDto> second = await Read<PagedResultDto<ExecutionHistoryEntryDto>>(
            $"{SchedulerUrl}/history/executions?skip=2&take=2");

        second.Items.Select(item => item.JobName).Should().Equal(["job0"]);
        second.HasMore.Should().BeFalse();
    }

    [Test]
    public async Task ExecutionsCanBeNarrowedByNodeJobAndTrigger()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await history.AddExecution(Entry(now, "nightly", node: "node-a", triggerName: "at-midnight"));
        await history.AddExecution(Entry(now, "hourly", node: "node-b", triggerName: "on-the-hour"));

        PagedResultDto<ExecutionHistoryEntryDto> onB = await Read<PagedResultDto<ExecutionHistoryEntryDto>>(
            $"{SchedulerUrl}/history/executions?schedulerInstanceId=node-b");
        onB.Items.Should().ContainSingle().Which.JobName.Should().Be("hourly",
            "a cluster's history is unreadable until it can be narrowed to one machine");

        PagedResultDto<ExecutionHistoryEntryDto> byJob = await Read<PagedResultDto<ExecutionHistoryEntryDto>>(
            $"{SchedulerUrl}/history/executions?jobContains=night");
        byJob.Items.Should().ContainSingle().Which.JobName.Should().Be("nightly");

        PagedResultDto<ExecutionHistoryEntryDto> byTrigger = await Read<PagedResultDto<ExecutionHistoryEntryDto>>(
            $"{SchedulerUrl}/history/executions?triggerContains=on-the-hour");
        byTrigger.Items.Should().ContainSingle().Which.JobName.Should().Be("hourly");
    }

    [Test]
    public async Task MisfiresAreListedAndCounted()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await history.AddMisfire(Misfire(now.AddMinutes(-30), "long-ago"));
        await history.AddMisfire(Misfire(now, "just-now"));

        PagedResultDto<MisfireHistoryEntryDto> listed = await Read<PagedResultDto<MisfireHistoryEntryDto>>(
            $"{SchedulerUrl}/history/misfires?includeTotalCount=true");
        listed.Items.Select(item => item.TriggerName).Should().Equal(["just-now", "long-ago"]);
        listed.Items[0].JobKey!.Name.Should().Be("DummyJob", "a misfire says which job did not run");

        MisfireCountResponse count = await Read<MisfireCountResponse>(
            $"{SchedulerUrl}/history/misfires/count?since={Uri.EscapeDataString(now.AddMinutes(-10).ToString("O"))}");
        count.Count.Should().Be(1,
            "a summary tile asks how bad it is right now, which is a count over a window rather than a page");
    }

    [Test]
    public async Task MisfiresCanBeNarrowedByTrigger()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await history.AddMisfire(Misfire(now, "at-midnight"));
        await history.AddMisfire(Misfire(now, "on-the-hour"));

        PagedResultDto<MisfireHistoryEntryDto> filtered = await Read<PagedResultDto<MisfireHistoryEntryDto>>(
            $"{SchedulerUrl}/history/misfires?triggerContains=midnight");

        filtered.Items.Should().ContainSingle().Which.TriggerName.Should().Be("at-midnight");
    }

    /// <summary>
    /// A row is read back as the entry it was, which is what lets a client hold the same records the
    /// process that recorded them holds.
    /// </summary>
    [Test]
    public async Task AnEntryRoundTripsThroughTheWire()
    {
        DateTimeOffset firedAt = DateTimeOffset.UtcNow;
        await history.AddExecution(Entry(firedAt, "nightly"));

        PagedResultDto<ExecutionHistoryEntryDto> page = await Read<PagedResultDto<ExecutionHistoryEntryDto>>(
            $"{SchedulerUrl}/history/executions");

        ExecutionHistoryEntry roundTripped = page.Items.Single().AsExecutionHistoryEntry(TestData.SchedulerName);

        roundTripped.Should().Be(Entry(firedAt, "nightly"),
            "the DTO carries everything the entry does apart from the scheduler name, which the route said");
    }

    private async Task<T> Read<T>(string url) where T : class
    {
        JsonSerializerOptions serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            .ConfigureWireFormat(new SystemTextJsonSerializerRegistry());

        return await client.Get<T>(url, serializerOptions, CancellationToken.None);
    }

    private static ExecutionHistoryEntry Entry(
        DateTimeOffset firedAt,
        string jobName,
        string node = "node-a",
        string triggerName = "DummyTrigger") => new(
        SchedulerName: TestData.SchedulerName,
        SchedulerInstanceId: node,
        JobGroup: "DummyGroup",
        JobName: jobName,
        TriggerGroup: "DummyTriggerGroup",
        TriggerName: triggerName,
        FiredAtUtc: firedAt,
        Duration: TimeSpan.FromMilliseconds(5),
        Succeeded: true,
        ExceptionMessage: null);

    private static MisfireHistoryEntry Misfire(DateTimeOffset misfiredAt, string triggerName) => new(
        SchedulerName: TestData.SchedulerName,
        SchedulerInstanceId: "node-a",
        TriggerGroup: "DummyTriggerGroup",
        TriggerName: triggerName,
        JobKey: new JobKey("DummyJob", "DummyGroup"),
        MisfiredAtUtc: misfiredAt,
        ScheduledFireTimeUtc: misfiredAt.AddMinutes(-5));
}
