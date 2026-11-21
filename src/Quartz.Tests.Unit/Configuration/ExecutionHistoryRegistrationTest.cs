using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Tests.Unit.Plugin.History;

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

    /// <summary>
    /// The recorder writes what the scheduler observed about the firing, which is more than whether it
    /// threw: a job under a retry policy writes a row per attempt, and only the attempt number and the
    /// retry flag tell them apart.
    /// </summary>
    [Test]
    public async Task EachAttemptAtAnOccurrenceIsRecordedAsTheAttemptItWas()
    {
        FailingJob.Reset();

        await using ServiceProvider provider = Container(services =>
        {
            services.AddQuartz(q => q.UseInMemoryStore());
            services.AddQuartzExecutionHistory();
        });

        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        await scheduler.ScheduleJob(
            JobBuilder.Create<FailingJob>().WithIdentity("flaky", "DummyGroup").Build(),
            TriggerBuilder.Create()
                .WithIdentity("now", "DummyGroup")
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(0))
                .StartNow()
                .WithRetryPolicy(RetryPolicy.Fixed(1, TimeSpan.FromMilliseconds(200)))
                .Build());

        (await FailingJob.Retried.Task.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue(
            "the job fails, is retried once, and fails again - which is one occurrence and two rows");

        await scheduler.Shutdown(waitForJobsToComplete: true);

        IExecutionHistoryStore store = provider.GetRequiredService<IExecutionHistoryStore>();
        List<ExecutionHistoryEntry> rows = (await store.QueryExecutions(new ExecutionHistoryQuery
        {
            SchedulerName = scheduler.SchedulerName
        })).Items.OrderBy(x => x.RetryAttempt).ToList();

        rows.Should().HaveCount(2);
        rows.Select(x => x.Succeeded).Should().AllBeEquivalentTo(false);
        rows.Select(x => x.RetryAttempt).Should().Equal([0, 1],
            "the row records the attempt the job made, not the one the trigger is about to make");
        rows.Select(x => x.RetryScheduled).Should().Equal([true, false],
            "the first failure was answered with another attempt and the second was not, which is the "
            + "difference a history page has to show and a FailedFinally filter selects on");
    }

    /// <summary>
    /// The recorder resolves its store while the scheduler is built and outside any scope, so a scoped store
    /// under scope validation fails the build, saying what to change. On 4.7 it failed every event the
    /// history recorded while the scheduler ran on.
    /// </summary>
    [TestCase(false)]
    [TestCase(true)]
    public async Task AScopedHistoryStoreFailsTheSchedulerBuildSayingToRegisterItAsASingleton(bool onlyAsyncDisposable)
    {
        ServiceCollection services = new();
        services.AddLogging();
        if (onlyAsyncDisposable)
        {
            // A scope holding it refuses to be disposed synchronously, which the diagnosis has to survive.
            services.AddScoped<IExecutionHistoryStore, AsyncDisposableStore>();
        }
        else
        {
            services.AddScoped<IExecutionHistoryStore, RecordingStore>();
        }

        services.AddQuartz(q => q.UseInMemoryStore());
        services.AddQuartzExecutionHistory();
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        Func<Task> build = async () => await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        (await build.Should().ThrowAsync<SchedulerConfigException>("the failure to start is all an operator has to go on"))
            .WithMessage("IExecutionHistoryStore, or a service it depends on, is registered as a scoped service*Register the history store and what it depends on as singletons.")
            .WithInnerException<InvalidOperationException>();
    }

    /// <summary>
    /// A history switched off builds no store, so one that cannot be built does not stop the scheduler
    /// starting, as on 4.7: the recorder resolves the store only once the history is on.
    /// </summary>
    [Test]
    public async Task AHistorySwitchedOffBuildsNoStore()
    {
        int built = 0;
        await using ServiceProvider provider = Container(services =>
        {
            services.AddSingleton<IExecutionHistoryStore>(_ =>
            {
                Interlocked.Increment(ref built);
                throw new SchedulerConfigException("the history store cannot be built");
            });
            services.AddQuartz(q => q.UseInMemoryStore());
            services.AddQuartzExecutionHistory(options => options.MaxEntriesPerScheduler = 0);
        });

        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await scheduler.Start();
        await scheduler.ScheduleJob(
            JobBuilder.Create<SignallingJob>().WithIdentity("unrecorded", "DummyGroup").Build(),
            TriggerBuilder.Create().WithIdentity("now", "DummyGroup").StartNow().Build());
        (await SignallingJob.Executed.Task.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue();
        await scheduler.Shutdown(waitForJobsToComplete: true);

        built.Should().Be(0, "MaxEntriesPerScheduler of 0 records nothing, so nothing asks for a store to record into");
    }

    /// <summary>
    /// A build that fails on the history store reports it once. The shutdown that follows flushes only a
    /// store the recorder holds, so it does not ask for the store again and report the same failure as
    /// one of its own.
    /// </summary>
    [Test]
    public async Task AFailedBuildAsksForTheHistoryStoreOnce()
    {
        int attempts = 0;
        RecordingLoggerProvider log = new();
        await using ServiceProvider provider = Container(services =>
        {
            services.AddLogging(logging => logging.AddProvider(log));
            services.AddSingleton<IExecutionHistoryStore>(_ =>
            {
                Interlocked.Increment(ref attempts);
                throw new SchedulerConfigException("the history store cannot be built");
            });
            services.AddQuartz(q => q.UseInMemoryStore());
            services.AddQuartzExecutionHistory();
        });

        Func<Task> build = async () => await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await build.Should().ThrowAsync<SchedulerConfigException>().WithMessage("the history store cannot be built");
        attempts.Should().Be(1, "the failure is the build's, and the shutdown after it asks for no store");
        log.Entries.Should().NotContain(entry => entry.EventId.Id == 4006,
            "a second failure in the shutdown after the first would be reported under 'another exception while shutting down'");
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
    /// A job that always throws, so its trigger's retry policy is spent rather than satisfied.
    /// </summary>
    private sealed class FailingJob : IJob
    {
        private static int firings;

        public static TaskCompletionSource<bool> Retried { get; private set; } = new();

        public static void Reset()
        {
            firings = 0;
            Retried = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref firings) >= 2)
            {
                Retried.TrySetResult(true);
            }

            throw new InvalidOperationException("the upstream system is down");
        }
    }

    /// <summary>
    /// A store that is only <see cref="IAsyncDisposable" />, which a container cannot dispose synchronously.
    /// </summary>
    private sealed class AsyncDisposableStore : IExecutionHistoryStore, IAsyncDisposable
    {
        public ValueTask AddExecution(ExecutionHistoryEntry entry, CancellationToken cancellationToken = default) => default;

        public ValueTask<PagedResult<ExecutionHistoryEntry>> QueryExecutions(ExecutionHistoryQuery query, CancellationToken cancellationToken = default)
            => new(new PagedResult<ExecutionHistoryEntry>([], HasMore: false, 0));

        public ValueTask AddMisfire(MisfireHistoryEntry entry, CancellationToken cancellationToken = default) => default;

        public ValueTask<PagedResult<MisfireHistoryEntry>> QueryMisfires(MisfireHistoryQuery query, CancellationToken cancellationToken = default)
            => new(new PagedResult<MisfireHistoryEntry>([], HasMore: false, 0));

        public ValueTask<int> CountMisfires(string schedulerName, DateTimeOffset since, CancellationToken cancellationToken = default) => new(0);

        public ValueTask DisposeAsync() => default;
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
