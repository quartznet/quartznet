using System.Collections.Specialized;

using FakeItEasy;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Core;
using Quartz.Diagnostics;
using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Configuration;

[NonParallelizable]
public class QuartzSchedulerBuilderTest
{
    private static readonly TaskCompletionSource<bool> fired = new();

    public class SignallingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            fired.TrySetResult(true);
            return default;
        }
    }

    [Test]
    public async Task BuildsAWorkingSchedulerWithoutAnApplicationContainer()
    {
        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create(q => q
            .ConfigureScheduler(options => options.InstanceName = "standalone-builds")
            .UseDefaultThreadPool(maxConcurrency: 2)
            .UseInMemoryStore());

        IScheduler scheduler = await builder.BuildScheduler();

        try
        {
            scheduler.SchedulerName.Should().Be("standalone-builds");
            scheduler.Status.Should().Be(SchedulerStatus.Created);

            await scheduler.Start();

            var job = JobBuilder.Create<SignallingJob>().WithIdentity("job").Build();
            var trigger = TriggerBuilder.Create().WithIdentity("trigger").StartNow().Build();
            await scheduler.ScheduleJob(job, trigger);

            var completed = await Task.WhenAny(fired.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            completed.Should().BeSameAs(fired.Task, "the scheduled job should have fired");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// The callback is handed an <see cref="IQuartzBuilder"/>, so a chain written for
    /// <c>AddQuartz(q =&gt; …)</c> is the same chain here and reaches <c>BuildScheduler</c> in one
    /// expression. This compiling at all is the assertion.
    /// </summary>
    [Test]
    public async Task ConfigurationMembersChainIntoTheTerminalMethods()
    {
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(options => options.InstanceName = "standalone-chains")
                .UseDefaultThreadPool(maxConcurrency: 2)
                .UseInMemoryStore()
                .UseTimeProvider(TimeProvider.System))
            .BuildScheduler();

        try
        {
            scheduler.SchedulerName.Should().Be("standalone-chains");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// The standalone builder reads an <see cref="IConfiguration" /> section the same way
    /// <c>AddQuartz(configuration)</c> does — both halves of it, the typed binder and the flat-key
    /// adapter — so a console application needs no flattening step of its own.
    /// </summary>
    [Test]
    public async Task AConfigurationSectionConfiguresTheStandaloneSchedulerToo()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Scheduler:InstanceName"] = "named-by-configuration",
                ["ThreadPool:MaxConcurrency"] = "4",
                // No options type of its own, so only the flat-key adapter reads it.
                ["JobStore:Type"] = "Quartz.Impl.RAMJobStore, Quartz",
            })
            .Build();

        IScheduler scheduler = await QuartzSchedulerBuilder.Create()
            .UseConfiguration(configuration)
            .BuildScheduler();

        try
        {
            scheduler.SchedulerName.Should().Be("named-by-configuration");

            SchedulerMetadata metadata = await scheduler.GetMetadata();
            metadata.ThreadPoolSize.Should().Be(4);
            metadata.JobStoreTypeName.Should().Contain(nameof(RAMJobStore),
                "the flat-key half of the section has to be read as well as the typed half");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// Flat properties and code-first configuration are two spellings of one configuration model, so
    /// they land on the same options — and what the code says wins, so a properties file cannot quietly
    /// override a decision the application made.
    /// </summary>
    [Test]
    public async Task PropertiesAndCodeConfigureTheSameSchedulerWithCodeWinning()
    {
        NameValueCollection properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "named-by-properties",
            ["quartz.threadPool.threadCount"] = "6",
        };

        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder
            .Create(q => q.ConfigureScheduler(options => options.InstanceName = "named-by-code"))
            .UseProperties(properties);

        IScheduler scheduler = await builder.BuildScheduler();

        try
        {
            scheduler.SchedulerName.Should().Be("named-by-code",
                "the two describe the same option, and the one written in code is the one that wins");

            SchedulerMetadata metadata = await scheduler.GetMetadata();
            metadata.ThreadPoolSize.Should().Be(6,
                "a setting the code said nothing about still has to come from the properties");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public void ContainerValidationPassesForTheDefaultConfiguration()
    {
        // Build() validates on build, so a missing or mis-scoped registration fails here rather than
        // at first use.
        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create(q => q
            .ConfigureScheduler(options => options.InstanceName = "validated")
            .UseInMemoryStore());

        var act = () => builder.Build();

        act.Should().NotThrow();
    }

    [Test]
    public void OptionsFlowThroughToTheResolvedComponents()
    {
        var services = new ServiceCollection();
        services.AddQuartzScheduler();
        services.Configure<ThreadPoolOptions>(options => options.MaxConcurrency = 17);
        services.Configure<QuartzSchedulerOptions>(options =>
        {
            options.InstanceName = "configured";
            options.MaxBatchSize = 9;
        });

        using var provider = services.BuildServiceProvider();

        var threadPool = provider.GetRequiredService<IThreadPool>();
        var resources = provider.GetRequiredService<QuartzSchedulerResources>();

        threadPool.PoolSize.Should().Be(17);
        resources.Name.Should().Be("configured");
        resources.MaxBatchSize.Should().Be(9);
    }

    [Test]
    public void NamedSchedulersResolveTheirOwnPartsFromKeyedRegistrations()
    {
        var services = new ServiceCollection();
        services.AddQuartzScheduler();
        services.AddQuartzScheduler("reporting");
        services.AddQuartzScheduler("ingest");

        services.Configure<ThreadPoolOptions>(options => options.MaxConcurrency = 1);
        services.Configure<ThreadPoolOptions>("reporting", options => options.MaxConcurrency = 5);
        services.Configure<ThreadPoolOptions>("ingest", options => options.MaxConcurrency = 11);

        using var provider = services.BuildServiceProvider();

        var defaultPool = provider.GetRequiredService<IThreadPool>();
        var reportingPool = provider.GetRequiredKeyedService<IThreadPool>("reporting");
        var ingestPool = provider.GetRequiredKeyedService<IThreadPool>("ingest");

        defaultPool.PoolSize.Should().Be(1);
        reportingPool.PoolSize.Should().Be(5);
        ingestPool.PoolSize.Should().Be(11);

        reportingPool.Should().NotBeSameAs(defaultPool);
        ingestPool.Should().NotBeSameAs(reportingPool);
    }

    [Test]
    public void NamedSchedulersGetSeparateJobStoresAndResources()
    {
        var services = new ServiceCollection();
        services.AddQuartzScheduler();
        services.AddQuartzScheduler("reporting");

        using var provider = services.BuildServiceProvider();

        var defaultStore = provider.GetRequiredService<IJobStore>();
        var reportingStore = provider.GetRequiredKeyedService<IJobStore>("reporting");
        var reportingResources = provider.GetRequiredKeyedService<QuartzSchedulerResources>("reporting");

        reportingStore.Should().NotBeSameAs(defaultStore, "each scheduler must own its job store, otherwise they share trigger state");
        reportingResources.Name.Should().Be("reporting", "the service key doubles as the scheduler's instance name");

        reportingResources.JobStore.Should().BeOfType<TracingJobStore>(
            "the store a scheduler is built with is wrapped for tracing, once and outermost, so that "
            + "every store emits the same spans rather than only the ADO one");

        JobStores.Unwrap(reportingResources.JobStore).Should().BeSameAs(reportingStore,
            "resources must be assembled from the same scheduler's keyed parts — the tracing layer is a "
            + "wrapper over that registration and never a second one");
    }

    [Test]
    public void ApplicationRegistrationsWinOverTheDefaults()
    {
        var services = new ServiceCollection();
        var custom = TestJobStores.Ram();
        services.AddSingleton<IJobStore>(custom);
        services.AddQuartzScheduler();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IJobStore>().Should().BeSameAs(custom, "TryAdd registrations must not displace what the application registered");
    }

    /// <summary>
    /// What a scheduler <em>carries</em> — jobs, triggers, calendars — is added by extension methods
    /// over <see cref="IQuartzBuilder" />, and the callback is handed exactly that, so they are
    /// reachable here without the standalone builder declaring a single one of them. This compiling is
    /// half the assertion; the scheduler it produces is the other half.
    /// </summary>
    [Test]
    public async Task TheContentMembersAreReachableThroughTheCallback()
    {
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(options => options.InstanceName = "standalone-chained")
                .UseInMemoryStore()
                .UseSimpleTypeLoader()
                .AddJob<SignallingJob>(job => job.WithIdentity("chained-job").StoreDurably())
                .AddTrigger(trigger => trigger.WithIdentity("chained-trigger").ForJob("chained-job").StartAt(DateTimeOffset.UtcNow.AddHours(1)))
                .AddCalendar<HolidayCalendar>("chained-calendar"))
            .BuildScheduler();

        try
        {
            (await scheduler.Exists(new JobKey("chained-job"))).Should().BeTrue();
            (await scheduler.Exists(new TriggerKey("chained-trigger"))).Should().BeTrue();
            (await scheduler.GetCalendar("chained-calendar")).Should().NotBeNull();
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    /// <summary>
    /// A calendar with a dependency could not be registered at all: both generic overloads demand
    /// <c>new()</c> and construct the calendar themselves. The factory overload is handed the
    /// scheduler-scoped provider, so what it resolves is this scheduler's parts.
    /// </summary>
    [Test]
    public async Task ACalendarCanBeBuiltFromTheContainer()
    {
        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q =>
            {
                q.ConfigureScheduler(options => options.InstanceName = "standalone-calendar-factory");
                q.Services.AddSingleton(new ExcludedDays(new MonthDay(12, 25)));
                q.AddCalendar("from-container", serviceProvider =>
                {
                    AnnualCalendar calendar = new AnnualCalendar { TimeZone = TimeZoneInfo.Utc };
                    foreach (MonthDay day in serviceProvider.GetRequiredService<ExcludedDays>().Days)
                    {
                        calendar.AddExcludedDay(day);
                    }

                    return calendar;
                });
            })
            .BuildScheduler();

        try
        {
            ICalendar calendar = await scheduler.GetCalendar("from-container");

            calendar.Should().BeOfType<AnnualCalendar>()
                .Which.IsDayExcluded(new MonthDay(12, 25)).Should().BeTrue(
                    "the factory's dependency decided what the calendar excludes, which is the whole point of the overload");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    /// <summary>
    /// A dependency a calendar cannot construct for itself, standing in for the holiday list or clock a
    /// real one would be given.
    /// </summary>
    private sealed class ExcludedDays
    {
        public ExcludedDays(params MonthDay[] days) => Days = days;

        public MonthDay[] Days { get; }
    }

    /// <summary>
    /// What <c>ConfigureAllQuartzSchedulers</c> said reaches a standalone-built scheduler too. It is the
    /// pass <c>AddQuartzExecutionHistory()</c> and <c>AddQuartzSchedulerEvents()</c> install their plugin
    /// through, and before 4.2.0 <see cref="QuartzSchedulerBuilder.Build"/> skipped it: a standalone
    /// scheduler with the history turned on registered the store, recorded nothing and said nothing.
    /// </summary>
    [Test]
    public async Task ContainerWideConfigurationReachesTheStandaloneScheduler()
    {
        (IExecutionHistoryStore store, Task<ExecutionHistoryEntry> recorded) = RecordingHistoryStore();

        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q =>
            {
                q.ConfigureScheduler(options => options.InstanceName = "standalone-configure-all")
                    .UseDefaultThreadPool(maxConcurrency: 2)
                    .UseInMemoryStore();

                // Registered first, so the in-memory default the recorder would otherwise get is never
                // added and every execution lands here.
                q.Services.AddSingleton(store);
                q.Services.AddQuartzExecutionHistory();
            })
            .BuildScheduler();

        await AssertAnExecutionIsRecorded(scheduler, recorded);
    }

    /// <summary>
    /// The same recorder, asked for through the legacy key. The bridge registers it while the
    /// property-derived options are being built in a collection of their own, so this is the route that
    /// needs that collection to share the builder's registry rather than grow one of its own.
    /// </summary>
    [Test]
    public async Task ContainerWideConfigurationFromTheLegacyKeyReachesTheStandaloneScheduler()
    {
        (IExecutionHistoryStore store, Task<ExecutionHistoryEntry> recorded) = RecordingHistoryStore();

        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q =>
            {
                q.ConfigureScheduler(options => options.InstanceName = "standalone-configure-all-key")
                    .UseDefaultThreadPool(maxConcurrency: 2)
                    .UseInMemoryStore();
                q.Services.AddSingleton(store);
            })
            .UseProperties(new NameValueCollection { ["quartz.jobStore.executionHistory"] = "true" })
            .BuildScheduler();

        await AssertAnExecutionIsRecorded(scheduler, recorded);
    }

    private static async Task AssertAnExecutionIsRecorded(IScheduler scheduler, Task<ExecutionHistoryEntry> recorded)
    {
        try
        {
            await scheduler.Start();
            await scheduler.ScheduleJob(
                JobBuilder.Create<SignallingJob>().WithIdentity("recorded").Build(),
                TriggerBuilder.Create().WithIdentity("recorded").StartNow().Build());

            Task completed = await Task.WhenAny(recorded, Task.Delay(TimeSpan.FromSeconds(20)));
            completed.Should().BeSameAs(recorded,
                "the recorder ConfigureAllQuartzSchedulers installs has to reach a scheduler the standalone builder built");
            (await recorded).JobName.Should().Be("recorded");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }
    }

    /// <summary>
    /// A history store that says when the first execution reaches it, which is the whole question here.
    /// </summary>
    private static (IExecutionHistoryStore Store, Task<ExecutionHistoryEntry> Recorded) RecordingHistoryStore()
    {
        TaskCompletionSource<ExecutionHistoryEntry> recorded = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IExecutionHistoryStore store = A.Fake<IExecutionHistoryStore>();
        A.CallTo(() => store.AddExecution(A<ExecutionHistoryEntry>._, A<CancellationToken>._))
            .Invokes(call => recorded.TrySetResult(call.GetArgument<ExecutionHistoryEntry>(0)!));
        return (store, recorded.Task);
    }
}
