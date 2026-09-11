using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// What <c>AddQuartzExecutionHistory()</c> installs, and what a second call to it does not.
/// </summary>
/// <remarks>
/// Three things call it — an application, <c>AddQuartzHttpApi()</c> and <c>AddQuartzDashboard()</c> —
/// so a process that maps both surfaces would otherwise record every execution two or three times, and
/// a history that counts one run as three is worse than no history.
/// </remarks>
[NonParallelizable]
public sealed class ExecutionHistoryRegistrationTest
{
    [SetUp]
    public void SetUp() => SignallingJob.Reset();

    [Test]
    public async Task AnExecutionIsRecordedOnce()
    {
        await using ServiceProvider provider = Container(services =>
        {
            services.AddQuartz(q => q.UseInMemoryStore());
            services.AddQuartzExecutionHistory();
        });

        List<ExecutionHistoryEntry> recorded = await RunOneJob(provider);

        recorded.Should().ContainSingle("one recorder writes one row for one execution")
            .Which.JobName.Should().Be("recorded");
    }

    [Test]
    public async Task ASecondCallInstallsNoSecondRecorder()
    {
        await using ServiceProvider provider = Container(services =>
        {
            services.AddQuartz(q => q.UseInMemoryStore());
            services.AddQuartzExecutionHistory();
            services.AddQuartzExecutionHistory(options => options.Retention = TimeSpan.FromHours(2));
        });

        List<ExecutionHistoryEntry> recorded = await RunOneJob(provider);

        recorded.Should().ContainSingle(
            "the recorder is installed once however many callers ask for it - two of them would double every row");
    }

    /// <summary>
    /// The order against <c>AddQuartz</c> does not matter, which is what
    /// <c>ConfigureAllQuartzSchedulers</c> promises and what a package calling this cannot control.
    /// </summary>
    [Test]
    public async Task TheHistoryReachesASchedulerRegisteredAfterwards()
    {
        await using ServiceProvider provider = Container(services =>
        {
            services.AddQuartzExecutionHistory();
            services.AddQuartz(q => q.UseInMemoryStore());
        });

        List<ExecutionHistoryEntry> recorded = await RunOneJob(provider);

        recorded.Should().ContainSingle("a package that records history cannot know which line the application writes first");
    }

    /// <summary>
    /// A named scheduler is covered too: the recorder is added to every scheduler in the container.
    /// </summary>
    [Test]
    public async Task ANamedSchedulerIsRecordedUnderItsOwnName()
    {
        await using ServiceProvider provider = Container(services =>
        {
            services.AddQuartz("acme", q => q.UseInMemoryStore());
            services.AddQuartzExecutionHistory();
        });

        IScheduler scheduler = await provider.GetRequiredKeyedService<ISchedulerFactory>("acme").GetScheduler();
        List<ExecutionHistoryEntry> recorded = await RunOneJob(provider, scheduler);

        recorded.Should().ContainSingle()
            .Which.SchedulerName.Should().Be("acme",
                "a plugin is told its own scheduler's name, which is what its rows are keyed by");
    }

    /// <summary>
    /// <see cref="ExecutionHistoryOptions.MaxEntriesPerScheduler" /> of zero is the opt-out, and it is
    /// honoured by the recorder rather than by the store.
    /// </summary>
    [Test]
    public async Task RecordingNothingIsAnOptOutRatherThanAStoreThatKeepsNothing()
    {
        await using ServiceProvider provider = Container(services =>
        {
            services.AddQuartz(q => q.UseInMemoryStore());
            services.AddQuartzExecutionHistory(options => options.MaxEntriesPerScheduler = 0);
        });

        List<ExecutionHistoryEntry> recorded = await RunOneJob(provider);

        recorded.Should().BeEmpty("a process that does not want a history should not pay to build the rows");
    }

    /// <summary>
    /// A store registered before this is the one that is recorded into: the default is a
    /// <c>TryAdd</c>, so an application that keeps history somewhere that survives a restart keeps it.
    /// </summary>
    [Test]
    public async Task AStoreRegisteredByTheApplicationIsTheOneRecordedInto()
    {
        RecordingStore applicationStore = new();

        await using ServiceProvider provider = Container(services =>
        {
            services.AddSingleton<IExecutionHistoryStore>(applicationStore);
            services.AddQuartz(q => q.UseInMemoryStore());
            services.AddQuartzExecutionHistory();
        });

        await RunOneJob(provider);

        applicationStore.Executions.Should().ContainSingle(
            "the shipped store is a TryAdd, so a store the application registered first is the seam");
    }

    private static ServiceProvider Container(Action<IServiceCollection> configure)
    {
        ServiceCollection services = new();
        services.AddLogging();
        configure(services);
        return services.BuildServiceProvider();
    }

    private static async Task<List<ExecutionHistoryEntry>> RunOneJob(ServiceProvider provider, IScheduler scheduler = null)
    {
        scheduler ??= await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await scheduler.Start();
        await scheduler.ScheduleJob(
            JobBuilder.Create<SignallingJob>().WithIdentity("recorded", "DummyGroup").Build(),
            TriggerBuilder.Create().WithIdentity("now", "DummyGroup").StartNow().Build());

        (await SignallingJob.Executed.Task.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue();

        // The row is written by the listener the plugin registered, which runs after the job returns.
        await scheduler.Shutdown(waitForJobsToComplete: true);

        IExecutionHistoryStore store = provider.GetRequiredService<IExecutionHistoryStore>();
        PagedResult<ExecutionHistoryEntry> page = await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = scheduler.SchedulerName
        });

        return page.Items.ToList();
    }

    private sealed class SignallingJob : IJob
    {
        public static TaskCompletionSource<bool> Executed { get; private set; } = new();

        public static void Reset() => Executed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Executed.TrySetResult(true);
            return default;
        }
    }

    /// <summary>
    /// A store of the application's own, which is what the seam is for.
    /// </summary>
    private sealed class RecordingStore : IExecutionHistoryStore
    {
        public List<ExecutionHistoryEntry> Executions { get; } = [];

        public ValueTask AddExecution(ExecutionHistoryEntry entry, CancellationToken cancellationToken = default)
        {
            lock (Executions)
            {
                Executions.Add(entry);
            }

            return default;
        }

        public ValueTask<PagedResult<ExecutionHistoryEntry>> QueryExecutions(ExecutionHistoryQuery query, CancellationToken cancellationToken = default)
        {
            lock (Executions)
            {
                return new ValueTask<PagedResult<ExecutionHistoryEntry>>(
                    new PagedResult<ExecutionHistoryEntry>([.. Executions], HasMore: false, Executions.Count));
            }
        }

        public ValueTask AddMisfire(MisfireHistoryEntry entry, CancellationToken cancellationToken = default) => default;

        public ValueTask<PagedResult<MisfireHistoryEntry>> QueryMisfires(MisfireHistoryQuery query, CancellationToken cancellationToken = default)
        {
            return new ValueTask<PagedResult<MisfireHistoryEntry>>(new PagedResult<MisfireHistoryEntry>([], HasMore: false, 0));
        }

        public ValueTask<int> CountMisfires(string schedulerName, DateTimeOffset since, CancellationToken cancellationToken = default) => new(0);
    }
}
