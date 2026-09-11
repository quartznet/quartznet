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
