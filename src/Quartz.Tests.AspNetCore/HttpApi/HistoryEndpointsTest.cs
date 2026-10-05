using System.Text.Json;
using System.Text.Json.Serialization;

using FakeItEasy;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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

        BindAnsweringScheduler(factory);
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
    /// One row by its key carries the run's input, and the listing leaves it out but says which rows had
    /// one too large to keep.
    /// </summary>
    [Test]
    public async Task OneExecutionIsReadWithItsInputAndTheListingLeavesTheInputOut()
    {
        DateTimeOffset firedAt = DateTimeOffset.UtcNow;
        await history.AddExecution(Entry(firedAt, "send-invoice") with { EntryId = "with-input", Input = "{\"invoiceId\":42}" });
        await history.AddExecution(Entry(firedAt.AddSeconds(-1), "send-invoice") with { EntryId = "too-large", InputTooLarge = true });

        PagedResultDto<ExecutionHistoryEntryDto> page = await Read<PagedResultDto<ExecutionHistoryEntryDto>>(
            $"{SchedulerUrl}/history/executions");

        page.Items.Should().OnlyContain(row => row.Input == null,
            "a page of history must not carry every row's input, however the store behind it keeps them");
        page.Items.Single(row => row.EntryId == "too-large").InputTooLarge.Should().BeTrue("the flag is small, so the listing carries it");

        ExecutionHistoryEntryDto single = await Read<ExecutionHistoryEntryDto>($"{SchedulerUrl}/history/executions/with-input");
        single.Input.Should().Be("{\"invoiceId\":42}", "the single-entry route is the read Run again makes for the input");
        single.InputTooLarge.Should().BeFalse();
    }

    /// <summary>
    /// A 4.3 dashboard or HTTP client reads a 4.4 host's rows, input and all: the two members are new, and
    /// a reader that does not know them skips them.
    /// </summary>
    [Test]
    public async Task AClientFrom43ReadsAnExecutionThatCarriesAnInput()
    {
        await history.AddExecution(Entry(DateTimeOffset.UtcNow, "send-invoice") with
        {
            EntryId = "entry-1",
            Log = "line",
            Input = "{\"invoiceId\":42}",
            InputTooLarge = true
        });

        JsonSerializerOptions readerFrom43 = new(JsonSerializerDefaults.Web);

        string single = await client.GetStringAsync($"{SchedulerUrl}/history/executions/entry-1");
        single.Should().Contain("\"input\"", "the control: the body does carry the member a 4.3 reader has never heard of");

        Func<ExecutionAsOf43?> readSingle = () => JsonSerializer.Deserialize<ExecutionAsOf43>(single, readerFrom43);
        readSingle.Should().NotThrow().Which!.Should().Be(new ExecutionAsOf43("send-invoice", "entry-1", "line"));

        string listing = await client.GetStringAsync($"{SchedulerUrl}/history/executions");
        Func<ExecutionPageAsOf43?> readListing = () => JsonSerializer.Deserialize<ExecutionPageAsOf43>(listing, readerFrom43);
        readListing.Should().NotThrow().Which!.Items.Should().ContainSingle().Which.EntryId.Should().Be("entry-1");
    }

    /// <summary>
    /// Run again against a scheduler in another process: the input read off the single-entry route goes
    /// back through the trigger route unchanged, so the job gets the input the failed run had.
    /// </summary>
    [Test]
    public async Task ARecordedInputGoesBackThroughTheTriggerRouteUnchanged()
    {
        const string input = "{\"invoiceId\":42,\"note\":\"café 日本\"}";
        await history.AddExecution(Entry(DateTimeOffset.UtcNow, "send-invoice") with { EntryId = "entry-1", Input = input });

        IScheduler target = factories[0].Services.GetRequiredService<ISchedulerRepository>().Lookup(TestData.SchedulerName)!;
        JobDataMap? received = null;
        A.CallTo(() => target.TriggerJob(A<JobKey>._, A<JobDataMap?>._, A<CancellationToken>._))
            .Invokes((JobKey _, JobDataMap? data, CancellationToken _) => received = data);

        HttpExecutionHistoryStore remoteHistory = new(TestData.SchedulerName, client);
        ExecutionHistoryEntry row = (await remoteHistory.GetExecution(TestData.SchedulerName, "entry-1"))!;

        HttpScheduler remote = new(TestData.SchedulerName, client);
        await remote.TriggerJob(new JobKey(row.JobName, row.JobGroup), new JobDataMap { [SchedulerConstants.JobInput] = row.Input! });

        received.Should().NotBeNull("the trigger route hands the scheduler the map it was sent");
        received!.GetString(SchedulerConstants.JobInput).Should().Be(input,
            "a string survives every path a job's input takes, the wire included, which is why the history keeps it as one");
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

    [Test]
    public async Task ExecutionsCanBeNarrowedToOneJobAWindowAndSomeResults()
    {
        // Inside the shipped store's retention window, which is measured against the wall clock.
        DateTimeOffset start = DateTimeOffset.UtcNow.AddMinutes(-10);
        await history.AddExecution(Run(start, "release-stale", JobRunResult.Succeeded));
        await history.AddExecution(Run(start.AddMinutes(1), "release-stale", JobRunResult.Skipped));
        await history.AddExecution(Run(start.AddMinutes(2), "release-stale-archive", JobRunResult.Skipped));
        await history.AddExecution(Run(start.AddMinutes(3), "release-stale", JobRunResult.Cancelled));
        await history.AddExecution(Run(start.AddMinutes(4), "release-stale", JobRunResult.Failed));

        PagedResultDto<ExecutionHistoryEntryDto> exact = await Read<PagedResultDto<ExecutionHistoryEntryDto>>(
            $"{SchedulerUrl}/history/executions?jobGroup=DummyGroup&jobName=release-stale&results=Skipped,Cancelled");
        exact.Items.Select(item => item.Result).Should().Equal([JobRunResult.Cancelled, JobRunResult.Skipped],
            "one job exactly, where jobContains would also have matched release-stale-archive");

        PagedResultDto<ExecutionHistoryEntryDto> repeated = await Read<PagedResultDto<ExecutionHistoryEntryDto>>(
            $"{SchedulerUrl}/history/executions?jobGroup=DummyGroup&jobName=release-stale&results=skipped&results=CANCELLED");
        repeated.Items.Should().HaveCount(2, "a repeated parameter reads as the comma-separated one does, and a name in any case");

        string from = Uri.EscapeDataString(start.AddMinutes(1).ToString("O"));
        string before = Uri.EscapeDataString(start.AddMinutes(3).ToString("O"));
        PagedResultDto<ExecutionHistoryEntryDto> window = await Read<PagedResultDto<ExecutionHistoryEntryDto>>(
            $"{SchedulerUrl}/history/executions?firedFrom={from}&firedBefore={before}");
        window.Items.Select(item => item.JobName).Should().Equal(["release-stale-archive", "release-stale"],
            "from is inclusive and before is exclusive, so two windows that meet list each execution once");
    }

    [TestCase("jobName=release-stale", "Both jobName and jobGroup*")]
    [TestCase("results=Sideways", "Unknown results value 'Sideways'*Succeeded, Failed, Cancelled, Skipped*")]
    [TestCase("results=9", "Unknown results value '9'*")]
    [TestCase("results=Failed%2C%20Skipped%2CBogus", "Unknown results value 'Bogus'*")]
    public async Task AFilterThatNamesNothingIsRefused(string query, string detail)
    {
        using HttpResponseMessage response = await client.GetAsync($"{SchedulerUrl}/history/executions?{query}");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest,
            "a filter the host cannot read would otherwise be a page that answers a different question");
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("detail").GetString().Should().Match(detail);
    }

    /// <summary>
    /// A 4.4 row carries what the run reported, and its metrics as an object that reads back as the text
    /// the recorder wrote.
    /// </summary>
    [Test]
    public async Task ARunsOutcomeRoundTripsThroughTheWire()
    {
        ExecutionHistoryEntry recorded = Run(DateTimeOffset.UtcNow, "release-stale", JobRunResult.Skipped) with
        {
            Summary = "no stale reservations",
            MetricsJson = """{"scanned":1200,"released":0,"ratio":0.25,"note":"café <b>","ok":true,"nothing":null}""",
            Manual = true,
            FireInstanceId = "node-a-17"
        };
        await history.AddExecution(recorded);

        using (JsonDocument raw = JsonDocument.Parse(await client.GetStringAsync($"{SchedulerUrl}/history/executions")))
        {
            raw.RootElement.GetProperty("items")[0].GetProperty("metrics").ValueKind.Should().Be(JsonValueKind.Object,
                "the metrics travel as the object they are, which a client in any language can read without parsing a string");
        }

        ExecutionHistoryEntryDto row = (await Read<PagedResultDto<ExecutionHistoryEntryDto>>($"{SchedulerUrl}/history/executions"))
            .Items.Should().ContainSingle().Subject;

        row.Result.Should().Be(JobRunResult.Skipped);
        row.AsExecutionHistoryEntry(TestData.SchedulerName).Should().Be(recorded with { EntryId = row.EntryId },
            "the result, the summary, the metrics as the same text, the manual flag and the fire instance id all come back as they were");
    }

    [Test]
    public async Task MetricsThatAreNotAnObjectAreLeftOffTheRowRatherThanFailingThePage()
    {
        await history.AddExecution(Run(DateTimeOffset.UtcNow, "release-stale", JobRunResult.Succeeded) with { MetricsJson = "not json" });

        ExecutionHistoryEntryDto row = (await Read<PagedResultDto<ExecutionHistoryEntryDto>>($"{SchedulerUrl}/history/executions"))
            .Items.Should().ContainSingle().Subject;

        row.Metrics.Should().BeNull("a store of an application's own may keep anything in the column, and one row must not fail the listing");
    }

    [Test]
    public async Task TheMisfireListingLeavesVetoesOutUnlessTheyAreAskedFor()
    {
        await SeedEveryReason();

        PagedResultDto<MisfireHistoryEntryDto> unasked = await Read<PagedResultDto<MisfireHistoryEntryDto>>(
            $"{SchedulerUrl}/history/misfires?includeTotalCount=true");
        unasked.Items.Select(item => item.Reason).Should().Equal([MisfireReason.Overlap, MisfireReason.Missed],
            "a request that names no reason is answered with the reasons every client can read");
        unasked.TotalCount.Should().Be(2, "the count is of what the listing lists, or a pager asks for a page that is not there");

        PagedResultDto<MisfireHistoryEntryDto> vetoes = await Read<PagedResultDto<MisfireHistoryEntryDto>>(
            $"{SchedulerUrl}/history/misfires?reasons=Vetoed");
        vetoes.Items.Should().ContainSingle().Which.Reason.Should().Be(MisfireReason.Vetoed);

        PagedResultDto<MisfireHistoryEntryDto> all = await Read<PagedResultDto<MisfireHistoryEntryDto>>(
            $"{SchedulerUrl}/history/misfires?reasons=Missed,Overlap,Vetoed");
        all.Items.Should().HaveCount(3);

        MisfireCountResponse count = await Read<MisfireCountResponse>(
            $"{SchedulerUrl}/history/misfires/count?since={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"))}");
        count.Count.Should().Be(1, "the count is of misfires, and neither an overlap nor a veto is one");
    }

    /// <summary>
    /// A 4.3 dashboard or HTTP client reads a misfire's reason through an enum converter that has no
    /// <c>Vetoed</c>, so a single vetoed row would fail its whole listing.
    /// </summary>
    [Test]
    public async Task AClientFrom43ReadsTheDefaultListingOfAHostHoldingVetoes()
    {
        await SeedEveryReason();

        JsonSerializerOptions readerFrom43 = new(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter<MisfireReasonAsOf43>() }
        };

        string listing = await client.GetStringAsync($"{SchedulerUrl}/history/misfires");
        Func<MisfirePageAsOf43?> readDefault = () => JsonSerializer.Deserialize<MisfirePageAsOf43>(listing, readerFrom43);
        readDefault.Should().NotThrow("the listing leaves out what a 4.3 client cannot read")
            .Which!.Items.Should().HaveCount(2);

        // The control: the same reader fails the moment a vetoed row reaches it, which is what the default
        // listing is keeping from it.
        string everything = await client.GetStringAsync($"{SchedulerUrl}/history/misfires?reasons=Missed,Overlap,Vetoed");
        Func<MisfirePageAsOf43?> readEverything = () => JsonSerializer.Deserialize<MisfirePageAsOf43>(everything, readerFrom43);
        readEverything.Should().Throw<JsonException>("the reader is shaped as 4.3's is, with no name for a veto");
    }

    [Test]
    public async Task MisfiresCanBeNarrowedToOneJob()
    {
        await history.AddMisfire(Misfire(DateTimeOffset.UtcNow, "at-midnight"));
        await history.AddMisfire(Misfire(DateTimeOffset.UtcNow, "hourly") with { JobKey = new JobKey("other", "DummyGroup") });

        PagedResultDto<MisfireHistoryEntryDto> filtered = await Read<PagedResultDto<MisfireHistoryEntryDto>>(
            $"{SchedulerUrl}/history/misfires?jobGroup=DummyGroup&jobName=DummyJob");

        filtered.Items.Should().ContainSingle().Which.TriggerName.Should().Be("at-midnight");
    }

    [Test]
    public async Task StatusesAreListedByJobAndCanBeNarrowedToTheFailingOnes()
    {
        await SeedStatuses();

        PagedResultDto<JobRunStatusDto> all = await Read<PagedResultDto<JobRunStatusDto>>(
            $"{SchedulerUrl}/history/job-status?includeTotalCount=true");
        all.Items.Select(status => status.Job.Name).Should().Equal(["healthy", "sick"], "statuses are listed by group and then name");
        all.TotalCount.Should().Be(2);

        JobRunStatusDto failing = (await Read<PagedResultDto<JobRunStatusDto>>($"{SchedulerUrl}/history/job-status?failing=true"))
            .Items.Should().ContainSingle().Subject;
        failing.Job.Name.Should().Be("sick");
        failing.ConsecutiveFailures.Should().Be(2);
        failing.LastResult.Should().Be(JobRunResult.Failed);
        failing.LastFailureMessage.Should().Be("the upstream system is down");
        failing.RunCount.Should().Be(3);
    }

    [Test]
    public async Task OneJobsStatusIsReadByItsKey()
    {
        await SeedStatuses();

        JobRunStatusDto status = await Read<JobRunStatusDto>($"{SchedulerUrl}/history/job-status/DummyGroup/healthy");

        status.LastResult.Should().Be(JobRunResult.Skipped);
        status.LastSucceededAtUtc.Should().NotBeNull("a skipped run is a success");

        using HttpResponseMessage missing = await client.GetAsync($"{SchedulerUrl}/history/job-status/DummyGroup/never-ran");
        missing.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound, "no run of the job has been recorded");
        (await missing.Content.ReadAsStringAsync()).Should().Contain("No recorded run of job DummyGroup.never-ran",
            "a 404 with problem details, which a reader tells apart from a host that has no such route");
    }

    [Test]
    public async Task ASetOfJobsHasItsStatusesFetchedInOneRequest()
    {
        await SeedStatuses();

        using StringContent keys = new(
            """{"jobs":[{"name":"sick","group":"DummyGroup"},{"name":"never-ran","group":"DummyGroup"}]}""",
            System.Text.Encoding.UTF8,
            "application/json");
        using HttpResponseMessage response = await client.PostAsync($"{SchedulerUrl}/history/job-status/fetch", keys);

        response.EnsureSuccessStatusCode();
        JobRunStatusDto[] statuses = JsonSerializer.Deserialize<JobRunStatusDto[]>(
            await response.Content.ReadAsStringAsync(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web).ConfigureWireFormat(new SystemTextJsonSerializerRegistry()))!;

        statuses.Should().ContainSingle("a job with no recorded run is left out").Which.Job.Name.Should().Be("sick");
    }

    [Test]
    public async Task AFetchOfMoreKeysThanTheHostTakesIsRefused()
    {
        string keys = string.Join(",", Enumerable.Range(0, 1001).Select(index => $$"""{"name":"job{{index}}","group":"g"}"""));
        using StringContent body = new("{\"jobs\":[" + keys + "]}", System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await client.PostAsync($"{SchedulerUrl}/history/job-status/fetch", body);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest, "one request reads at most a thousand statuses, as a bulk fetch does");
    }

    /// <summary>
    /// Through the HTTP-backed store, as a dashboard fronting the scheduler reads it: the filters reach a host
    /// that reads them, and the statuses come back as the host keeps them.
    /// </summary>
    [Test]
    public async Task TheHttpBackedStoreFiltersAndReadsStatuses()
    {
        await SeedStatuses();

        HttpExecutionHistoryStore remote = new(TestData.SchedulerName, client);

        PagedResult<ExecutionHistoryEntry> failures = await remote.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = TestData.SchedulerName,
            Job = new JobKey("sick", "DummyGroup"),
            Results = [JobRunResult.Failed]
        });
        failures.Items.Should().HaveCount(2).And.OnlyContain(entry => entry.EffectiveResult == JobRunResult.Failed);

        (await remote.GetJobRunStatus(TestData.SchedulerName, new JobKey("sick", "DummyGroup")))!.ConsecutiveFailures.Should().Be(2);
        (await remote.GetJobRunStatus(TestData.SchedulerName, new JobKey("never-ran", "DummyGroup"))).Should().BeNull();

        PagedResult<JobRunStatus> named = await remote.QueryJobRunStatuses(new JobRunStatusQuery
        {
            SchedulerName = TestData.SchedulerName,
            Jobs = [new JobKey("healthy", "DummyGroup"), new JobKey("sick", "DummyGroup")]
        });
        named.Items.Select(status => status.Job.Name).Should().Equal(["healthy", "sick"]);
    }

    /// <summary>
    /// A 4.4 host whose history store keeps rows only answers <c>501</c> naming <see cref="NotSupportedException" />,
    /// which the HTTP-backed store raises again as that.
    /// </summary>
    [Test]
    public async Task AStoreThatKeepsNoStatusIsAnsweredNotImplemented()
    {
        IExecutionHistoryStore rowsOnly = A.Fake<IExecutionHistoryStore>();
        A.CallTo(() => rowsOnly.QueryJobRunStatuses(A<JobRunStatusQuery>._, A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => rowsOnly.GetJobRunStatus(A<string>._, A<JobKey>._, A<CancellationToken>._)).CallsBaseMethod();

        WebApplicationFactory<Program> factory = factories[0].WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddSingleton(rowsOnly)));
        factories.Add(factory);
        using HttpClient rowsOnlyClient = factory.CreateClient();
        BindAnsweringScheduler(factory);

        using HttpResponseMessage response = await rowsOnlyClient.GetAsync($"{SchedulerUrl}/history/job-status");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotImplemented,
            "the server is working; what it serves from keeps no status, which is neither a fault nor a missing route");
        using (JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            body.RootElement.GetProperty(HttpApiConstants.ProblemDetailsExceptionType).GetString().Should().Be(nameof(NotSupportedException));
            body.RootElement.GetProperty("detail").GetString().Should().Contain("keeps no per-job run status");
        }

        HttpExecutionHistoryStore remote = new(TestData.SchedulerName, rowsOnlyClient);
        Func<Task> read = async () => await remote.GetJobRunStatus(TestData.SchedulerName, new JobKey("sick", "DummyGroup"));
        await read.Should().ThrowAsync<NotSupportedException>().WithMessage("*keeps no per-job run status*");
    }

    /// <summary>
    /// The run statistics route counts what the history holds, narrowed as the listing is, in buckets of
    /// the size asked for.
    /// </summary>
    [Test]
    public async Task RunsAreCountedByResultInBucketsOverTheWire()
    {
        DateTimeOffset hour = new(DateTimeOffset.UtcNow.UtcTicks / TimeSpan.TicksPerHour * TimeSpan.TicksPerHour, TimeSpan.Zero);
        await history.AddExecution(Run(hour.AddMinutes(-50), "release-stale", JobRunResult.Succeeded) with { Duration = TimeSpan.FromMilliseconds(100) });
        await history.AddExecution(Run(hour.AddMinutes(-40), "release-stale", JobRunResult.Failed) with { Duration = TimeSpan.FromMilliseconds(300) });
        await history.AddExecution(Run(hour.AddMinutes(-30), "release-stale-archive", JobRunResult.Skipped));
        await history.AddExecution(Run(hour.AddMinutes(-90), "release-stale", JobRunResult.Cancelled));

        ExecutionStatisticsDto statistics = await Read<ExecutionStatisticsDto>(
            $"{SchedulerUrl}/history/statistics?jobGroup=DummyGroup&jobName=release-stale&bucket=01:00:00");

        statistics.BucketSize.Should().Be(TimeSpan.FromHours(1));
        statistics.Truncated.Should().BeFalse();
        statistics.Buckets.Select(bucket => (bucket.StartUtc, bucket.RunCount, bucket.CancelledCount, bucket.SucceededCount, bucket.FailedCount))
            .Should().Equal([(hour.AddHours(-2), 1L, 1L, 0L, 0L), (hour.AddHours(-1), 2L, 0L, 1L, 1L)],
                "one job exactly, oldest bucket first, where jobContains would also have counted release-stale-archive");
        statistics.Buckets[1].P50Duration.Should().Be(TimeSpan.FromMilliseconds(200), "halfway between 100 and 300");
        statistics.Buckets[1].MaxDuration.Should().Be(TimeSpan.FromMilliseconds(300));

        ExecutionStatisticsDto group = await Read<ExecutionStatisticsDto>($"{SchedulerUrl}/history/statistics?jobGroup=DummyGroup&bucket=1.00:00:00");
        group.Buckets.Sum(bucket => bucket.RunCount).Should().Be(4, "a group alone counts the whole group");

        ExecutionStatisticsDto byDefault = await Read<ExecutionStatisticsDto>($"{SchedulerUrl}/history/statistics?results=Skipped");
        byDefault.BucketSize.Should().Be(TimeSpan.FromHours(1), "an hour when the request names no bucket");
        byDefault.Buckets.Should().ContainSingle().Which.SkippedCount.Should().Be(1);
    }

    [TestCase("jobName=release-stale", "jobName needs jobGroup*")]
    [TestCase("bucket=00:00:30", "bucket must be at least 00:01:00*")]
    [TestCase("results=Sideways", "Unknown results value 'Sideways'*")]
    public async Task AStatisticsReadTheHostCannotAnswerIsRefused(string query, string detail)
    {
        using HttpResponseMessage response = await client.GetAsync($"{SchedulerUrl}/history/statistics?{query}");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.BadRequest);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("detail").GetString().Should().Match(detail);
    }

    /// <summary>
    /// The HTTP-backed store reads the host's version, then the statistics, and hands back what the host's
    /// store counted.
    /// </summary>
    [Test]
    public async Task TheHttpBackedStoreCountsThroughTheHost()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await history.AddExecution(Run(now.AddMinutes(-5), "release-stale", JobRunResult.Failed));
        await history.AddExecution(Run(now.AddMinutes(-4), "release-stale", JobRunResult.Succeeded));

        HttpExecutionHistoryStore remote = new(TestData.SchedulerName, client);
        ExecutionStatistics statistics = await remote.QueryExecutionStatistics(new ExecutionStatisticsQuery
        {
            SchedulerName = TestData.SchedulerName,
            Job = new JobKey("release-stale", "DummyGroup"),
            BucketSize = TimeSpan.FromDays(1)
        });

        statistics.Buckets.Sum(bucket => bucket.RunCount).Should().Be(2);
        statistics.Buckets.Sum(bucket => bucket.FailedCount).Should().Be(1);
    }

    /// <summary>
    /// A host that reports 4.3 is never asked for statistics: a 4.3 host has no such route, and the client
    /// says so from the version alone.
    /// </summary>
    [Test]
    public async Task AHostThatReports43IsNeverAskedForStatistics()
    {
        IScheduler older = A.Fake<IScheduler>();
        A.CallTo(() => older.SchedulerName).Returns(TestData.SchedulerName);
        A.CallTo(() => older.GetMetadata(A<CancellationToken>._)).Returns(TestData.Metadata with { Version = "4.3.0.0" });

        ISchedulerRepository repository = factories[0].Services.GetRequiredService<ISchedulerRepository>();
        repository.Remove(TestData.SchedulerName);
        repository.Bind(older);

        RecordingHandler recording = new();
        using HttpClient recordingClient = factories[0].CreateDefaultClient(recording);
        HttpExecutionHistoryStore remote = new(TestData.SchedulerName, recordingClient);

        Func<Task> read = async () => await remote.QueryExecutionStatistics(new ExecutionStatisticsQuery { SchedulerName = TestData.SchedulerName });

        await read.Should().ThrowAsync<NotSupportedException>().WithMessage("*Quartz 4.3.0.0*no run statistics route*");
        recording.Paths.Should().ContainSingle("the version settles it before the statistics route is asked")
            .Which.Should().EndWith($"/{SchedulerUrl}");
    }

    /// <summary>
    /// A store that cannot count answers <c>501</c> naming <see cref="NotSupportedException" />, which the
    /// HTTP-backed store raises again as that.
    /// </summary>
    [Test]
    public async Task AStoreThatCannotCountIsAnsweredNotImplemented()
    {
        IExecutionHistoryStore cannotCount = A.Fake<IExecutionHistoryStore>();
        A.CallTo(() => cannotCount.QueryExecutionStatistics(A<ExecutionStatisticsQuery>._, A<CancellationToken>._))
            .Throws(new NotSupportedException("AcmeHistoryStore cannot count."));

        WebApplicationFactory<Program> factory = factories[0].WithWebHostBuilder(builder => builder.ConfigureTestServices(
            services => services.AddSingleton(cannotCount)));
        factories.Add(factory);
        using HttpClient cannotCountClient = factory.CreateClient();
        BindAnsweringScheduler(factory);

        using HttpResponseMessage response = await cannotCountClient.GetAsync($"{SchedulerUrl}/history/statistics");

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotImplemented);
        using (JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()))
        {
            body.RootElement.GetProperty(HttpApiConstants.ProblemDetailsExceptionType).GetString().Should().Be(nameof(NotSupportedException));
            body.RootElement.GetProperty("detail").GetString().Should().Be("AcmeHistoryStore cannot count.");
        }

        HttpExecutionHistoryStore remote = new(TestData.SchedulerName, cannotCountClient);
        Func<Task> read = async () => await remote.QueryExecutionStatistics(new ExecutionStatisticsQuery { SchedulerName = TestData.SchedulerName });
        await read.Should().ThrowAsync<NotSupportedException>().WithMessage("AcmeHistoryStore cannot count.");
    }

    /// <summary>Records the path of every request it passes on.</summary>
    private sealed class RecordingHandler : DelegatingHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return base.SendAsync(request, cancellationToken);
        }
    }

    /// <summary>
    /// Reads one history route the way the remote client does: the catalogue names the route the URL is
    /// a call of, and the answer comes back through the client's own status mapping.
    /// </summary>
    private async Task<T> Read<T>(string url) where T : class
    {
        JsonSerializerOptions serializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            .ConfigureWireFormat(new SystemTextJsonSerializerRegistry());

        WireRoute route = SchedulerRoutes.Match("GET", url)?.Route
                          ?? throw new InvalidOperationException($"GET {url} is no route of the catalogue");

        WireClient wire = new(new HttpWireTransport(client), serializerOptions);
        return await wire.SendAndRead<T>(new WireRequest(route, url), CancellationToken.None);
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

    /// <summary>One run of a job, with what it achieved.</summary>
    private static ExecutionHistoryEntry Run(DateTimeOffset firedAt, string jobName, JobRunResult result) => Entry(firedAt, jobName) with
    {
        Succeeded = result is JobRunResult.Succeeded or JobRunResult.Skipped,
        ExceptionMessage = result == JobRunResult.Failed ? "the upstream system is down" : null,
        Result = result
    };

    /// <summary>A misfire of each reason, the veto newest.</summary>
    private async Task SeedEveryReason()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await history.AddMisfire(Misfire(now.AddSeconds(-2), "missed"));
        await history.AddMisfire(Misfire(now.AddSeconds(-1), "skipped") with { Reason = MisfireReason.Overlap });
        await history.AddMisfire(Misfire(now, "vetoed") with { Reason = MisfireReason.Vetoed });
    }

    /// <summary>
    /// <c>healthy</c> succeeded and then skipped; <c>sick</c> succeeded and then failed for good twice.
    /// </summary>
    private async Task SeedStatuses()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow.AddMinutes(-10);
        await history.AddExecution(Run(start, "healthy", JobRunResult.Succeeded));
        await history.AddExecution(Run(start.AddMinutes(1), "healthy", JobRunResult.Skipped));
        await history.AddExecution(Run(start, "sick", JobRunResult.Succeeded));
        await history.AddExecution(Run(start.AddMinutes(1), "sick", JobRunResult.Failed));
        await history.AddExecution(Run(start.AddMinutes(2), "sick", JobRunResult.Failed));
    }

    /// <summary>
    /// Binds the host's scheduler, answering its details with this build's version, which is what the
    /// HTTP-backed store reads before it sends a 4.4 filter.
    /// </summary>
    private static void BindAnsweringScheduler(WebApplicationFactory<Program> factory)
    {
        IScheduler fake = A.Fake<IScheduler>();
        A.CallTo(() => fake.SchedulerName).Returns(TestData.SchedulerName);
        A.CallTo(() => fake.GetMetadata(A<CancellationToken>._)).Returns(TestData.Metadata with
        {
            Version = typeof(IScheduler).Assembly.GetName().Version!.ToString()
        });

        ISchedulerRepository repository = factory.Services.GetRequiredService<ISchedulerRepository>();
        foreach (IScheduler bound in repository.LookupAll())
        {
            repository.Remove(bound.SchedulerName);
        }

        repository.Bind(fake);
    }

    /// <summary>The reason as a 4.3 client knew it: no <c>Vetoed</c>.</summary>
    private enum MisfireReasonAsOf43
    {
        Missed = 0,
        Overlap = 1
    }

    /// <summary>A misfire as a 4.3 client reads it, down to the member that matters.</summary>
    private sealed record MisfireRowAsOf43(string TriggerName, MisfireReasonAsOf43 Reason);

    /// <summary>A page of misfires as a 4.3 client reads it.</summary>
    private sealed record MisfirePageAsOf43(MisfireRowAsOf43[] Items, bool HasMore, int? TotalCount);

    /// <summary>An execution as a 4.3 client reads it: no input, no flag.</summary>
    private sealed record ExecutionAsOf43(string JobName, string? EntryId, string? Log);

    /// <summary>A page of executions as a 4.3 client reads it.</summary>
    private sealed record ExecutionPageAsOf43(ExecutionAsOf43[] Items, bool HasMore, int? TotalCount);
}
