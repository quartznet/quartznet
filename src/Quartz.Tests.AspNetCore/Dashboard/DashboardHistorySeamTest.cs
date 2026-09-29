using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// How the dashboard's 4.0 history seam and the one Quartz now keeps history behind are joined.
/// </summary>
/// <remarks>
/// Both are public and both keep working, which means the pair has to be joined in whichever direction
/// the application's own registrations call for — an application that registered an
/// <see cref="IDashboardHistoryStore" /> of its own keeps it and has everything answered out of it, and
/// one that registered nothing gets the dashboard's type as a view of Quartz's store.
/// </remarks>
public sealed class DashboardHistorySeamTest
{
    [Test]
    public void WithNoStoreOfItsOwnTheDashboardsSeamIsAViewOfQuartzsHistory()
    {
        ServiceCollection services = new();
        services.AddQuartzDashboard();
        services.AddQuartz();

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IExecutionHistoryStore>().Should().BeOfType<InMemoryExecutionHistoryStore>(
            "the shipped store is what the recorder writes to and what the HTTP API serves");
        provider.GetRequiredService<IDashboardHistoryStore>().Should().BeOfType<DashboardHistoryStoreOverExecutionHistory>(
            "the documented 4.0 type still resolves, over the one store there is");
    }

    [Test]
    public async Task AStoreTheApplicationRegisteredAnswersBothSeams()
    {
        IDashboardHistoryStore applicationStore = TestData.Dashboard.HistoryStore();

        ServiceCollection services = new();
        services.AddSingleton(applicationStore);
        services.AddQuartzDashboard();
        services.AddQuartz();

        await using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDashboardHistoryStore>().Should().BeSameAs(applicationStore,
            "the 4.0 recipe is to register one before AddQuartzDashboard, and that still wins");

        IExecutionHistoryStore quartzStore = provider.GetRequiredService<IExecutionHistoryStore>();
        quartzStore.Should().BeOfType<ExecutionHistoryStoreOverDashboardStore>(
            "what the recorder writes and what the HTTP API serves has to be the application's store, or half the "
            + "surfaces would read a history nothing writes to");

        await quartzStore.AddExecution(Execution("acme"));

        PagedResult<DashboardHistoryEntry> page = await applicationStore.QueryExecutions(
            new DashboardHistoryQuery { SchedulerName = "acme" });
        page.Items.Should().ContainSingle().Which.JobName.Should().Be("nightly");
    }

    /// <summary>
    /// The dashboard's 4.0 seam keeps rows and nothing else, so Quartz's seam over it says it has no
    /// per-job status rather than making one up.
    /// </summary>
    [Test]
    public async Task AStoreTheApplicationRegisteredHasNoRunStatus()
    {
        IExecutionHistoryStore quartzStore = new ExecutionHistoryStoreOverDashboardStore(TestData.Dashboard.HistoryStore());

        Func<Task> query = async () => await quartzStore.QueryJobRunStatuses(new JobRunStatusQuery { SchedulerName = "acme" });
        Func<Task> single = async () => await quartzStore.GetJobRunStatus("acme", new JobKey("nightly", "reports"));

        await query.Should().ThrowAsync<NotSupportedException>().WithMessage("*IDashboardHistoryStore*no per-job run status*");
        await single.Should().ThrowAsync<NotSupportedException>().WithMessage("*IDashboardHistoryStore*",
            "the adapter declares the single read itself rather than leaving it to the interface's default");
    }

    /// <summary>
    /// What a 4.4 row adds, and what a 4.4 query asks, survive the adapters in both directions: through
    /// the dashboard's seam and back into Quartz's.
    /// </summary>
    [Test]
    public async Task TheRunsOutcomeAndThe44FiltersSurviveBothAdapters()
    {
        ExecutionHistoryStoreOverDashboardStore quartzStore = new(TestData.Dashboard.HistoryStore());
        DateTimeOffset firedAt = DateTimeOffset.UtcNow.AddMinutes(-1);

        ExecutionHistoryEntry reported = Execution("acme") with
        {
            FiredAtUtc = firedAt,
            EntryId = "entry-1",
            Result = JobRunResult.Skipped,
            Summary = "no stale reservations",
            MetricsJson = """{"scanned":1200}""",
            Manual = true,
            FireInstanceId = "node-a-17"
        };
        await quartzStore.AddExecution(reported);
        await quartzStore.AddExecution(Execution("acme") with { FiredAtUtc = firedAt, JobName = "other", EntryId = "entry-2" });
        await quartzStore.AddExecution(Execution("acme") with { FiredAtUtc = firedAt, EntryId = "entry-3", Result = JobRunResult.Succeeded });

        PagedResult<ExecutionHistoryEntry> page = await quartzStore.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = "acme",
            Job = new JobKey("nightly", "DummyGroup"),
            Results = [JobRunResult.Skipped],
            FiredFrom = firedAt,
            FiredBefore = firedAt.AddSeconds(1)
        });

        page.Items.Should().ContainSingle("the job and the results reached the store, or the other two rows would be here")
            .Which.Should().Be(reported, "the result, summary, metrics, manual flag and fire instance id are carried both ways");

        (await quartzStore.QueryExecutions(new ExecutionHistoryQuery { SchedulerName = "acme", FiredBefore = firedAt }))
            .Items.Should().BeEmpty("the window reached the store");
    }

    [Test]
    public async Task TheMisfireJobAndReasonsSurviveBothAdapters()
    {
        ExecutionHistoryStoreOverDashboardStore quartzStore = new(TestData.Dashboard.HistoryStore());
        MisfireHistoryEntry missed = new("acme", "node-a", "DummyTriggerGroup", "missed", new JobKey("nightly", "DummyGroup"), DateTimeOffset.UtcNow, null);

        await quartzStore.AddMisfire(missed);
        await quartzStore.AddMisfire(missed with { TriggerName = "vetoed", Reason = MisfireReason.Vetoed });
        await quartzStore.AddMisfire(missed with { TriggerName = "elsewhere", JobKey = new JobKey("other", "DummyGroup") });

        (await quartzStore.QueryMisfires(new MisfireHistoryQuery { SchedulerName = "acme", Reasons = [MisfireReason.Vetoed] }))
            .Items.Should().ContainSingle().Which.TriggerName.Should().Be("vetoed");
        (await quartzStore.QueryMisfires(new MisfireHistoryQuery { SchedulerName = "acme", Job = new JobKey("other", "DummyGroup") }))
            .Items.Should().ContainSingle().Which.TriggerName.Should().Be("elsewhere");
    }

    /// <summary>
    /// The dashboard reads a job's status from the same store it reads the history from, so an
    /// application's 4.0 store answers the pages' status reads the way it answers Quartz's: it keeps none.
    /// </summary>
    [Test]
    public async Task TheDashboardReadsNoRunStatusFromAStoreTheApplicationRegistered()
    {
        ServiceCollection services = new();
        services.AddSingleton(TestData.Dashboard.HistoryStore());
        services.AddQuartzDashboard();
        services.AddQuartz();

        await using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        IQuartzApiClient client = Client(scope.ServiceProvider);

        Func<Task> one = async () => await client.GetJobRunStatus("QuartzScheduler", new JobKeyDto("reports", "nightly"));
        Func<Task> many = async () => await client.GetJobRunStatuses("QuartzScheduler", [new JobKeyDto("reports", "nightly")]);

        await one.Should().ThrowAsync<NotSupportedException>().WithMessage("*IDashboardHistoryStore*",
            "the pages leave the panel out on this, rather than saying the job never ran");
        await many.Should().ThrowAsync<NotSupportedException>().WithMessage("*IDashboardHistoryStore*");
    }

    [Test]
    public async Task TheDashboardReadsRunStatusesFromTheHistoryQuartzKeeps()
    {
        ServiceCollection services = new();
        services.AddQuartzDashboard();
        services.AddQuartz();

        await using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        IQuartzApiClient client = Client(scope.ServiceProvider);

        await provider.GetRequiredService<IExecutionHistoryStore>().AddExecution(Execution("QuartzScheduler") with
        {
            JobGroup = "reports",
            Succeeded = false,
            ExceptionMessage = "boom",
            Result = JobRunResult.Failed
        });

        (await client.GetJobRunStatus("QuartzScheduler", new JobKeyDto("reports", "nightly")))!.ConsecutiveFailures.Should().Be(1);
        (await client.GetJobRunStatus("QuartzScheduler", new JobKeyDto("reports", "never-ran"))).Should().BeNull();
        (await client.GetJobRunStatuses("QuartzScheduler", [new JobKeyDto("reports", "nightly"), new JobKeyDto("reports", "never-ran")]))
            .Should().ContainSingle("a job with no recorded run is absent").Which.Job.Name.Should().Be("nightly");
        (await client.GetJobRunStatuses("QuartzScheduler", [])).Should().BeEmpty("no keys name no job");
    }

    /// <summary>
    /// A store registered against Quartz's own seam is what the application said it wanted, and is not
    /// replaced by anything the dashboard does.
    /// </summary>
    [Test]
    public void AStoreRegisteredAgainstQuartzsSeamIsLeftAlone()
    {
        ServiceCollection services = new();
        services.AddSingleton<IExecutionHistoryStore>(new NoOpExecutionHistoryStore());
        services.AddSingleton(TestData.Dashboard.HistoryStore());
        services.AddQuartzDashboard();
        services.AddQuartz();

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetRequiredService<IExecutionHistoryStore>().Should().BeOfType<NoOpExecutionHistoryStore>(
            "only Quartz's own default is replaced to reach a dashboard store — a store the application registered is not");
    }

    /// <summary>
    /// The dashboard's two history settings stay the knobs while the dashboard is registered.
    /// </summary>
    [Test]
    public void TheDashboardsBoundsAreTheOnesTheHistoryIsKeptUnder()
    {
        ServiceCollection services = new();
        services.AddQuartzDashboard(options =>
        {
            options.HistoryRetention = TimeSpan.FromHours(3);
            options.HistoryMaxEntriesPerScheduler = 17;
        });
        services.AddQuartz();

        using ServiceProvider provider = services.BuildServiceProvider();

        ExecutionHistoryOptions history = provider.GetRequiredService<IOptions<ExecutionHistoryOptions>>().Value;

        history.Retention.Should().Be(TimeSpan.FromHours(3));
        history.MaxEntriesPerScheduler.Should().Be(17,
            "a deployment that configured the dashboard's settings gets the history it asked for wherever it is read from");
    }

    /// <summary>
    /// A scheduler in another process has its history read from that process.
    /// </summary>
    /// <remarks>
    /// The keyed store <c>AddQuartzHttpClient</c> registers is what says so. Reading this process's
    /// store for it would show an empty history page for a scheduler that has been running jobs all day.
    /// </remarks>
    [Test]
    public async Task ARemoteSchedulersHistoryIsReadFromItsOwnProcess()
    {
        RecordingExecutionHistoryStore remote = new();

        ServiceCollection services = new();
        services.AddQuartzDashboard();
        services.AddQuartz();
        services.AddKeyedSingleton<IExecutionHistoryStore>("far-away", remote);

        await using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        IQuartzApiClient client = Client(scope.ServiceProvider);

        await client.QueryExecutions(new DashboardHistoryQuery { SchedulerName = "far-away" });
        remote.Reads.Should().Be(1, "the scheduler's history is kept where the scheduler runs");

        await client.QueryExecutions(new DashboardHistoryQuery { SchedulerName = "QuartzScheduler" });
        remote.Reads.Should().Be(1, "a local scheduler's history is this process's");
    }

    /// <summary>
    /// A target that serves no history says so, and the refusal reaches the page.
    /// </summary>
    [Test]
    public async Task ATargetThatServesNoHistorySaysSo()
    {
        ServiceCollection services = new();
        services.AddQuartzDashboard();
        services.AddQuartz();
        services.AddKeyedSingleton<IExecutionHistoryStore>("far-away", new NoOpExecutionHistoryStore());

        await using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        IQuartzApiClient client = Client(scope.ServiceProvider);

        Func<Task> act = async () => await client.CountMisfires("far-away", DateTimeOffset.UtcNow.AddHours(-1));

        await act.Should().ThrowAsync<NotSupportedException>(
            "the pages turn this into 'the target serves no history' rather than into a zero nobody said");
    }

    private static IQuartzApiClient Client(IServiceProvider provider) => provider.GetRequiredService<IQuartzApiClient>();

    private static ExecutionHistoryEntry Execution(string schedulerName) => new(
        schedulerName,
        "node-a",
        "DummyGroup",
        "nightly",
        "DummyTriggerGroup",
        "at-midnight",
        DateTimeOffset.UtcNow,
        TimeSpan.FromMilliseconds(5),
        Succeeded: true,
        ExceptionMessage: null);

    /// <summary>
    /// A store that counts the reads it was asked for, standing in for the one registered against a
    /// scheduler in another process.
    /// </summary>
    private sealed class RecordingExecutionHistoryStore : IExecutionHistoryStore
    {
        public int Reads { get; private set; }

        public ValueTask AddExecution(ExecutionHistoryEntry entry, CancellationToken cancellationToken = default) => default;

        public ValueTask<PagedResult<ExecutionHistoryEntry>> QueryExecutions(ExecutionHistoryQuery query, CancellationToken cancellationToken = default)
        {
            Reads++;
            return new ValueTask<PagedResult<ExecutionHistoryEntry>>(new PagedResult<ExecutionHistoryEntry>([], HasMore: false, 0));
        }

        public ValueTask AddMisfire(MisfireHistoryEntry entry, CancellationToken cancellationToken = default) => default;

        public ValueTask<PagedResult<MisfireHistoryEntry>> QueryMisfires(MisfireHistoryQuery query, CancellationToken cancellationToken = default)
        {
            Reads++;
            return new ValueTask<PagedResult<MisfireHistoryEntry>>(new PagedResult<MisfireHistoryEntry>([], HasMore: false, 0));
        }

        public ValueTask<int> CountMisfires(string schedulerName, DateTimeOffset since, CancellationToken cancellationToken = default)
        {
            Reads++;
            return new ValueTask<int>(0);
        }
    }

    /// <summary>
    /// A store that serves no history at all, which is what a target older than the history routes
    /// looks like from here.
    /// </summary>
    private sealed class NoOpExecutionHistoryStore : IExecutionHistoryStore
    {
        public ValueTask AddExecution(ExecutionHistoryEntry entry, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<PagedResult<ExecutionHistoryEntry>> QueryExecutions(ExecutionHistoryQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask AddMisfire(MisfireHistoryEntry entry, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<PagedResult<MisfireHistoryEntry>> QueryMisfires(MisfireHistoryQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public ValueTask<int> CountMisfires(string schedulerName, DateTimeOffset since, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
