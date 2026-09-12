using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Tests.AspNetCore.Support;

// The wire's own names, aliased rather than imported: the dashboard's DTOs and the contract's share three
// names, and this fixture is about the pair agreeing rather than about either of them.
using KeyDto = Quartz.HttpApiContract.KeyDto;
using SchedulerEvent = Quartz.HttpApiContract.SchedulerEvent;
using SchedulerEventKind = Quartz.HttpApiContract.SchedulerEventKind;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// Which process the scheduler a test drives runs in.
/// </summary>
/// <remarks>
/// The dashboard's client is one implementation over two arrangements: a scheduler this container built,
/// and a scheduler in another process registered with <c>AddQuartzHttpClient</c>. Running the contract
/// under both is what stops the second one drifting — which is exactly what happened to the HTTP-backed
/// client 4.0 deleted, twice, with nothing exercising it.
/// </remarks>
public enum Carrier
{
    /// <summary>A scheduler this container built.</summary>
    Local,

    /// <summary>A scheduler in another process, reached over the Quartz HTTP API.</summary>
    Http
}

/// <summary>
/// The contract <see cref="IQuartzApiClient" /> states, held against whichever implementation the
/// container answers with — over a local scheduler and over one in another process.
/// </summary>
/// <remarks>
/// <para>
/// The interface is public so that an application can replace it, and its four "return the thing itself"
/// members have non-nullable return types — so what they do when the thing is gone is contract, not an
/// implementation detail of the one client Quartz ships. The dashboard's error boundary renders a
/// <see cref="KeyNotFoundException" /> as the not-found page; a replacement answering <c>null!</c>
/// instead would fault the page with a <see cref="NullReferenceException" /> raised somewhere further in,
/// and nothing said so until this test did.
/// </para>
/// <para>
/// It resolves the client out of a container rather than constructing <c>InProcessQuartzApiClient</c>,
/// because the promise is about whatever <c>AddQuartzDashboard</c> left registered — the registration is
/// <c>TryAdd</c>, so an application that registers its own client first is the one the pages read, and it
/// is the one this contract binds.
/// </para>
/// <para>
/// The <see cref="Carrier.Http" /> fixture is a dashboard with no scheduler of its own: it registers
/// <c>AddQuartzHttpClient</c> against a host running the API, so every answer below travels as JSON and
/// comes back through <c>HttpScheduler</c>. A page cannot tell the two apart, and that is the claim.
/// </para>
/// </remarks>
[TestFixture(Carrier.Local)]
[TestFixture(Carrier.Http)]
public sealed class QuartzApiClientContractTest
{
    private readonly Carrier carrier;

    private WebApplicationFactory<Program>? host;
    private ServiceProvider provider = null!;
    private IServiceScope scope = null!;
    private IScheduler scheduler = null!;
    private IQuartzApiClient client = null!;
    private IExecutionHistoryStore history = null!;

    /// <summary>
    /// The broker of the process the scheduler runs in, which is where its events are published. For the
    /// HTTP carrier that is the host's, and the dashboard reads it through the target's reader.
    /// </summary>
    private SchedulerEventBroker broker = null!;

    public QuartzApiClientContractTest(Carrier carrier)
    {
        this.carrier = carrier;
    }

    [SetUp]
    public async Task SetUp()
    {
        if (carrier == Carrier.Local)
        {
            await SetUpLocal();
        }
        else
        {
            await SetUpHttp();
        }

        // Scoped, because that is the lifetime the pages resolve it with.
        scope = provider.CreateScope();
        client = scope.ServiceProvider.GetRequiredService<IQuartzApiClient>();
    }

    private async Task SetUpLocal()
    {
        string schedulerName = $"api-client-contract-{Guid.NewGuid():N}";

        ServiceCollection services = new();
        services.AddQuartzDashboard();
        services.AddQuartz(quartz => quartz.ConfigureScheduler(options => options.InstanceName = schedulerName));

        provider = services.BuildServiceProvider();

        // Resolving the scheduler is what binds it into the repository the client looks names up in.
        scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        history = provider.GetRequiredService<IExecutionHistoryStore>();
        broker = provider.GetRequiredService<SchedulerEventBroker>();
    }

    private async Task SetUpHttp()
    {
        TestContentRoot.Apply();
        host = new WebApplicationFactory<Program>();

        // The test host maps the API but runs no hosted service, so nothing has built its scheduler yet.
        scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
        history = host.Services.GetRequiredService<IExecutionHistoryStore>();
        broker = host.Services.GetRequiredService<SchedulerEventBroker>();

        // A dashboard with no scheduler of its own: everything it renders is somebody else's process.
        ServiceCollection services = new();
        services.AddQuartzDashboard();
        services.AddQuartzHttpClient(scheduler.SchedulerName, _ => host.CreateClient());

        provider = services.BuildServiceProvider();

        // Resolving it is what binds it into this container's repository, which the hosted service would
        // do in an application that had one.
        provider.GetRequiredKeyedService<IScheduler>(scheduler.SchedulerName).Should().BeOfType<HttpScheduler>();
    }

    [TearDown]
    public async Task TearDown()
    {
        scope?.Dispose();

        if (provider is not null)
        {
            await provider.DisposeAsync();
        }

        if (scheduler is not null)
        {
            await scheduler.Clear();

            if (carrier == Carrier.Local)
            {
                await scheduler.Shutdown(waitForJobsToComplete: false);
            }
        }

        if (host is not null)
        {
            await host.DisposeAsync();
            host = null;
        }
    }

    [Test]
    public async Task AnUnknownSchedulerNameIsReportedAsAMissingKey()
    {
        Func<Task> act = () => client.GetScheduler("no-such-scheduler").AsTask();

        (await act.Should().ThrowAsync<KeyNotFoundException>(
            "GetScheduler returns a non-nullable detail, so a name nothing goes by has no other answer"))
            .Which.Message.Should().Contain("no-such-scheduler", "the page shows the name that was not found");
    }

    [Test]
    public async Task AMissingJobIsReportedAsAMissingKey()
    {
        Func<Task> act = () => client.GetJobDetail(scheduler.SchedulerName, new JobKeyDto("ghosts", "no-such-job")).AsTask();

        (await act.Should().ThrowAsync<KeyNotFoundException>(
            "the scheduler exists and holds no such job, which is the case the non-nullable JobDetailDto cannot express"))
            .Which.Message.Should().Contain("no-such-job");
    }

    [Test]
    public async Task AMissingTriggerIsReportedAsAMissingKey()
    {
        Func<Task> act = () => client.GetTrigger(scheduler.SchedulerName, new TriggerKeyDto("ghosts", "no-such-trigger")).AsTask();

        (await act.Should().ThrowAsync<KeyNotFoundException>())
            .Which.Message.Should().Contain("no-such-trigger");
    }

    [Test]
    public async Task AMissingCalendarIsReportedAsAMissingKey()
    {
        Func<Task> act = () => client.GetCalendar(scheduler.SchedulerName, "no-such-calendar").AsTask();

        (await act.Should().ThrowAsync<KeyNotFoundException>())
            .Which.Message.Should().Contain("no-such-calendar");
    }

    /// <summary>
    /// The other half of the contract: a capability the source does not have is a value rather than an
    /// exception, so the overview can draw "cannot say" differently from "nothing is limited".
    /// </summary>
    [Test]
    public async Task ASchedulerThatLimitsNothingReportsNoLimitsRatherThanRefusing()
    {
        ExecutionLimitsDto limits = await client.GetExecutionLimits(scheduler.SchedulerName);

        limits.Should().NotBeNull("the member never answers null");
        limits.CanReport.Should().BeTrue("a scheduler Quartz ships can always say what its limits are");
        limits.Limits.Should().BeEmpty("nothing was limited, which is a different fact from being unable to report");
    }

    /// <summary>
    /// What the client schedules is what the scheduler holds, whichever process that scheduler is in.
    /// </summary>
    [Test]
    public async Task WhatIsScheduledThroughTheClientIsWhatTheSchedulerHolds()
    {
        JobKey jobKey = new("nightly", "contract");
        TriggerKey triggerKey = new("at-midnight", "contract");

        JobDetailDto job = new(
            Name: jobKey.Name,
            Group: jobKey.Group,
            JobType: typeof(DummyJob).FullName!,
            Description: "scheduled from the dashboard",
            Durable: true,
            RequestsRecovery: false,
            ConcurrentExecutionDisallowed: false,
            PersistJobDataAfterExecution: false,
            JobDataMap: new JobDataMap { ["colour"] = "green" });

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(triggerKey)
            .ForJob(jobKey.Name, jobKey.Group)
            .WithCronSchedule("0 0 0 * * ?")
            .Build();

        await client.ScheduleJob(scheduler.SchedulerName, new ScheduleJobRequest(trigger, job));

        ITrigger? stored = await scheduler.GetTrigger(triggerKey);
        stored.Should().NotBeNull("the scheduler that holds it is the one the client was pointed at");
        stored!.JobKey.Should().Be(jobKey);

        JobDetailDto readBack = await client.GetJobDetail(scheduler.SchedulerName, new JobKeyDto(jobKey.Group, jobKey.Name));
        readBack.Name.Should().Be(jobKey.Name);
    }

    /// <summary>
    /// The history of what a scheduler has run is readable through the client, filters and paging
    /// included — from the process the scheduler runs in.
    /// </summary>
    /// <remarks>
    /// Recorded where the scheduler is, which is the point: for the HTTP carrier that is the host's own
    /// store, reached over the history routes. A dashboard fronting a scheduler over HTTP used to show an
    /// empty History page with nothing to say why.
    /// </remarks>
    [Test]
    public async Task TheHistoryIsReadFromTheProcessTheSchedulerRunsIn()
    {
        DateTimeOffset firedAt = DateTimeOffset.UtcNow;
        await history.AddExecution(Execution(firedAt, "nightly", node: "node-a", triggerName: "at-midnight"));
        await history.AddExecution(Execution(firedAt.AddSeconds(1), "hourly", node: "node-b", triggerName: "on-the-hour"));

        PagedResult<DashboardHistoryEntry> all = await client.QueryExecutions(new DashboardHistoryQuery
        {
            SchedulerName = scheduler.SchedulerName,
            IncludeTotalCount = true
        });
        all.Items.Select(entry => entry.JobName).Should().Equal(["hourly", "nightly"], "a history page reads newest first");
        all.TotalCount.Should().Be(2);

        PagedResult<DashboardHistoryEntry> onNodeA = await client.QueryExecutions(new DashboardHistoryQuery
        {
            SchedulerName = scheduler.SchedulerName,
            SchedulerInstanceId = "node-a"
        });
        onNodeA.Items.Should().ContainSingle().Which.JobName.Should().Be("nightly",
            "a cluster's history is unreadable until it can be narrowed to one machine");

        PagedResult<DashboardHistoryEntry> byJob = await client.QueryExecutions(new DashboardHistoryQuery
        {
            SchedulerName = scheduler.SchedulerName,
            JobFilter = "night"
        });
        byJob.Items.Should().ContainSingle().Which.JobName.Should().Be("nightly");

        PagedResult<DashboardHistoryEntry> byTrigger = await client.QueryExecutions(new DashboardHistoryQuery
        {
            SchedulerName = scheduler.SchedulerName,
            TriggerFilter = "on-the-hour"
        });
        byTrigger.Items.Should().ContainSingle().Which.JobName.Should().Be("hourly");

        PagedResult<DashboardHistoryEntry> firstPage = await client.QueryExecutions(new DashboardHistoryQuery
        {
            SchedulerName = scheduler.SchedulerName,
            Take = 1,
            IncludeTotalCount = true
        });
        firstPage.Items.Should().ContainSingle().Which.JobName.Should().Be("hourly");
        firstPage.HasMore.Should().BeTrue("paging means the same thing over both carriers");
    }

    [Test]
    public async Task TheMisfiresOfTheSchedulerAreCountedThroughTheClient()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        await history.AddMisfire(Misfire(now.AddMinutes(-30), "long-ago"));
        await history.AddMisfire(Misfire(now, "just-now"));

        PagedResult<DashboardMisfireEntry> listed = await client.QueryMisfires(new DashboardMisfireQuery
        {
            SchedulerName = scheduler.SchedulerName
        });
        listed.Items.Select(entry => entry.TriggerName).Should().Equal(["just-now", "long-ago"]);
        listed.Items[0].JobKey!.Name.Should().Be("DummyJob", "a misfire says which job did not run");

        int recent = await client.CountMisfires(scheduler.SchedulerName, now.AddMinutes(-10));
        recent.Should().Be(1, "the overview's tile asks for a count over a window, not for a page");
    }

    /// <summary>
    /// What a scheduler does is watchable from this container, from the process the scheduler runs in.
    /// </summary>
    /// <remarks>
    /// The Live Logs page resolves its stream exactly this way, so running it over both carriers is what
    /// stops the remote one drifting: for the HTTP carrier the event is raised on the host, written as a
    /// frame by the event route and read back by the target's reader, and the page cannot tell that from an
    /// event published into this process's broker.
    /// </remarks>
    [Test]
    public async Task TheEventsAreWatchedFromTheProcessTheSchedulerRunsIn()
    {
        ISchedulerEventSource? source = SchedulerEventSources.For(provider, scheduler.SchedulerName);
        source.Should().NotBeNull("a dashboard reads the events of whatever scheduler it renders");

        using CancellationTokenSource subscription = new();
        await using IAsyncEnumerator<SchedulerEvent> events = source!
            .Subscribe(scheduler.SchedulerName, subscription.Token)
            .GetAsyncEnumerator(CancellationToken.None);

        Task<bool> reading = events.MoveNextAsync().AsTask();

        // A stream carries what happens after a subscription is made, so nothing is raised until the
        // scheduler's own process has one.
        using CancellationTokenSource waiting = new(TimeSpan.FromSeconds(30));
        while (!broker.HasSubscribers(scheduler.SchedulerName))
        {
            await Task.Delay(10, waiting.Token);
        }

        await scheduler.AddJob(
            JobBuilder.Create<DummyJob>().WithIdentity("watched", "contract").StoreDurably().Build(),
            new AddJobOptions { Replace = true });
        (await scheduler.PauseJob(new JobKey("watched", "contract"))).Should().BeTrue();

        (await reading.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue("the scheduler raised an event");

        SchedulerEvent watched = events.Current;
        watched.Kind.Should().Be(SchedulerEventKind.JobPaused);
        watched.JobKey.Should().Be(new KeyDto("watched", "contract"));
        watched.SchedulerName.Should().Be(scheduler.SchedulerName);
        watched.SchedulerInstanceId.Should().Be(scheduler.SchedulerInstanceId,
            "the node is the one that raised it, which for a remote target is somebody else's process");

        await subscription.CancelAsync();
    }

    /// <summary>
    /// A scheduler in another process is listed as one, and the listing says which node answered.
    /// </summary>
    [Test]
    public async Task TheListingSaysWhereTheSchedulerIs()
    {
        List<SchedulerHeaderDto> schedulers = await client.GetSchedulers();

        SchedulerHeaderDto header = schedulers.Should().ContainSingle(x => x.SchedulerName == scheduler.SchedulerName).Subject;

        if (carrier == Carrier.Http)
        {
            header.Origin.Should().Be(SchedulerOrigin.Remote,
                "nothing in this process runs it, and every page about it is about somebody else's process");
        }
        else
        {
            header.Origin.Should().Be(SchedulerOrigin.Container);
        }

        header.SchedulerInstanceId.Should().Be(scheduler.SchedulerInstanceId,
            "the listing carries the node, asked for once and asynchronously rather than read off a blocking property");
        header.Status.Should().NotBeNull();
    }

    /// <summary>
    /// More rows than one page holds are read a page at a time, and asking for all of them against a
    /// server that caps pages is refused rather than silently truncated.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The truncation guard is <c>HttpScheduler.WholeAnswer</c>: <c>take=all</c> travels as a request for
    /// everything, and a server with <c>QuartzHttpApiOptions.MaxPageSize</c> set answers it with that
    /// many rows and a <c>hasMore</c>. Handing those back as the whole store is the one answer worse than
    /// no answer, so the client refuses — which is why every dashboard listing loops on <c>HasMore</c>
    /// with a page size of its own.
    /// </para>
    /// <para>
    /// The cap has to be at or above the page size the caller asks with: a <em>number</em> above it is a
    /// <c>400</c>, so a server capped below <see cref="PagedQuery.DefaultTake" /> refuses the dashboard's
    /// own page rather than truncating it.
    /// </para>
    /// </remarks>
    [Test]
    public async Task MoreRowsThanOnePageHoldsAreReadAPageAtATime()
    {
        if (carrier != Carrier.Http)
        {
            Assert.Ignore("the cap is the HTTP API's, and a local client has no wire to be capped on");
        }

        const int Jobs = 1001;
        for (int index = 0; index < Jobs; index++)
        {
            await scheduler.AddJob(
                JobBuilder.Create<DummyJob>()
                    .WithIdentity("job" + index.ToString("D4", System.Globalization.CultureInfo.InvariantCulture), "bulk")
                    .StoreDurably()
                    .Build(),
                new AddJobOptions { Replace = true });
        }

        List<JobKeyDto> collected = [];
        DashboardJobQuery query = new() { GroupContains = "bulk", Take = PagedQuery.DefaultTake };
        while (true)
        {
            PagedResult<JobKeyDto> page = await client.QueryJobs(scheduler.SchedulerName, query);
            collected.AddRange(page.Items);

            if (!page.HasMore || page.Items.Count == 0)
            {
                break;
            }

            query = query with { Skip = query.Skip + page.Items.Count };
        }

        collected.Should().HaveCount(Jobs, "a page at a time is how a listing bigger than one page is read");

        Func<Task> askForEverything = () => client.QueryJobs(
            scheduler.SchedulerName,
            new DashboardJobQuery { GroupContains = "bulk", Take = PagedQuery.All }).AsTask();

        await askForEverything.Should().ThrowAsync<HttpClientException>(
            "the server caps what 'everything' means, and a truncated page handed back as the whole store is worse than an error");
    }

    private ExecutionHistoryEntry Execution(
        DateTimeOffset firedAt,
        string jobName,
        string node = "node-a",
        string triggerName = "DummyTrigger") => new(
        SchedulerName: scheduler.SchedulerName,
        SchedulerInstanceId: node,
        JobGroup: "contract",
        JobName: jobName,
        TriggerGroup: "contract",
        TriggerName: triggerName,
        FiredAtUtc: firedAt,
        Duration: TimeSpan.FromMilliseconds(5),
        Succeeded: true,
        ExceptionMessage: null);

    private MisfireHistoryEntry Misfire(DateTimeOffset misfiredAt, string triggerName) => new(
        SchedulerName: scheduler.SchedulerName,
        SchedulerInstanceId: "node-a",
        TriggerGroup: "contract",
        TriggerName: triggerName,
        JobKey: new JobKey("DummyJob", "contract"),
        MisfiredAtUtc: misfiredAt,
        ScheduledFireTimeUtc: misfiredAt.AddMinutes(-5));
}
