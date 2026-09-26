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
    public async Task ExecutionsCanBeNarrowedToTheOccurrencesThatGaveUp()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await history.AddExecution(Failed(now.AddSeconds(1), "nightly", retryAttempt: 0, retryScheduled: true));
        await history.AddExecution(Failed(now.AddSeconds(2), "nightly", retryAttempt: 1, retryScheduled: false));
        await history.AddExecution(Entry(now.AddSeconds(3), "hourly"));

        PagedResultDto<ExecutionHistoryEntryDto> gaveUp = await Read<PagedResultDto<ExecutionHistoryEntryDto>>(
            $"{SchedulerUrl}/history/executions?failedFinally=true");

        gaveUp.Items.Should().ContainSingle(
            "a filter on failure alone would show one occurrence twice over").Which.RetryAttempt.Should().Be(1);

        PagedResultDto<ExecutionHistoryEntryDto> everythingElse = await Read<PagedResultDto<ExecutionHistoryEntryDto>>(
            $"{SchedulerUrl}/history/executions?failedFinally=false");
        everythingElse.Items.Should().HaveCount(2);

        PagedResultDto<ExecutionHistoryEntryDto> everything = await Read<PagedResultDto<ExecutionHistoryEntryDto>>(
            $"{SchedulerUrl}/history/executions");
        everything.Items.Should().HaveCount(3, "a route that was not given the parameter narrows nothing");
    }

    [Test]
    public async Task TheAttemptAndTheRetryFlagAreOnEveryRow()
    {
        await history.AddExecution(Failed(DateTimeOffset.UtcNow, "nightly", retryAttempt: 2, retryScheduled: true));

        ExecutionHistoryEntryDto row = (await Read<PagedResultDto<ExecutionHistoryEntryDto>>(
            $"{SchedulerUrl}/history/executions")).Items.Should().ContainSingle().Subject;

        row.RetryAttempt.Should().Be(2);
        row.RetryScheduled.Should().BeTrue(
            "without it a remote reader cannot tell a failure that is going to be retried from one that is over");
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

        roundTripped.EntryId.Should().NotBeNullOrEmpty(
            "the store names a row that arrives unnamed, and the name is what the single-entry route reads it by");
        roundTripped.Should().Be(Entry(firedAt, "nightly") with { EntryId = roundTripped.EntryId },
            "the DTO carries everything the entry does apart from the scheduler name, which the route said");
    }

    /// <summary>
    /// One row by its key carries the captured log, and the listing it came from does not.
    /// </summary>
    [Test]
    public async Task OneExecutionIsReadWithItsLogAndTheListingLeavesTheLogOut()
    {
        DateTimeOffset firedAt = DateTimeOffset.UtcNow;
        await history.AddExecution(Entry(firedAt, "nightly") with { EntryId = "entry-1", Log = "line one\nline two" });

        PagedResultDto<ExecutionHistoryEntryDto> page = await Read<PagedResultDto<ExecutionHistoryEntryDto>>(
            $"{SchedulerUrl}/history/executions");

        ExecutionHistoryEntryDto listed = page.Items.Should().ContainSingle().Subject;
        listed.EntryId.Should().Be("entry-1");
        listed.Log.Should().BeNull(
            "a page of history must not carry every row's log, however the store behind it keeps them");

        ExecutionHistoryEntryDto single = await Read<ExecutionHistoryEntryDto>($"{SchedulerUrl}/history/executions/entry-1");

        single.Log.Should().Be("line one\nline two", "the single-entry route is the one read that carries the log");
        single.JobName.Should().Be("nightly");
    }

    /// <summary>
    /// A row the store does not have is a <c>404</c>, which the HTTP-backed store reads as no row rather
    /// than as a target that serves no history.
    /// </summary>
    [Test]
    public async Task AnExecutionTheStoreDoesNotHaveIsNotFound()
    {
        HttpResponseMessage response = await client.GetAsync($"{SchedulerUrl}/history/executions/no-such-entry");
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);

        HttpExecutionHistoryStore remote = new(TestData.SchedulerName, client);
        (await remote.GetExecution(TestData.SchedulerName, "no-such-entry")).Should().BeNull(
            "the target answered, and what it said is that there is no such execution");
    }

    /// <summary>
    /// Through the HTTP-backed store, as a dashboard fronting the scheduler reads it.
    /// </summary>
    [Test]
    public async Task TheHttpBackedStoreReadsOneExecutionWithItsLog()
    {
        await history.AddExecution(Entry(DateTimeOffset.UtcNow, "nightly") with { EntryId = "entry-2", Log = "captured" });

        HttpExecutionHistoryStore remote = new(TestData.SchedulerName, client);
        ExecutionHistoryEntry? entry = await remote.GetExecution(TestData.SchedulerName, "entry-2");

        entry.Should().NotBeNull();
        entry!.Log.Should().Be("captured");
        entry.EntryId.Should().Be("entry-2");
        entry.SchedulerName.Should().Be(TestData.SchedulerName, "the route named the scheduler, and the row belongs to it");
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

    /// <summary>One failed attempt at an occurrence, which may or may not have another coming.</summary>
    private static ExecutionHistoryEntry Failed(
        DateTimeOffset firedAt,
        string jobName,
        int retryAttempt,
        bool retryScheduled) => Entry(firedAt, jobName) with
    {
        Succeeded = false,
        ExceptionMessage = "the upstream system is down",
        RetryAttempt = retryAttempt,
        RetryScheduled = retryScheduled
    };

    private static MisfireHistoryEntry Misfire(DateTimeOffset misfiredAt, string triggerName) => new(
        SchedulerName: TestData.SchedulerName,
        SchedulerInstanceId: "node-a",
        TriggerGroup: "DummyTriggerGroup",
        TriggerName: triggerName,
        JobKey: new JobKey("DummyJob", "DummyGroup"),
        MisfiredAtUtc: misfiredAt,
        ScheduledFireTimeUtc: misfiredAt.AddMinutes(-5));
}
