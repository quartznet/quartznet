using System.Collections.Specialized;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Core;
using Quartz.Impl;
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
        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create();
        builder.ConfigureScheduler(options => options.InstanceName = "standalone-builds")
            .UseDefaultThreadPool(maxConcurrency: 2)
            .UseInMemoryStore();

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
    /// The chain keeps its concrete type, so it reaches <c>BuildScheduler</c>. This compiling at all is
    /// the assertion: before the explicit interface implementation, every configuration member returned
    /// <see cref="IQuartzBuilder"/> and a standalone scheduler took two statements.
    /// </summary>
    [Test]
    public async Task ConfigurationMembersChainIntoTheTerminalMethods()
    {
        IScheduler scheduler = await QuartzSchedulerBuilder.Create()
            .ConfigureScheduler(options => options.InstanceName = "standalone-chains")
            .UseDefaultThreadPool(maxConcurrency: 2)
            .UseInMemoryStore()
            .UseTimeProvider(TimeProvider.System)
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

        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create().UseProperties(properties);
        builder.ConfigureScheduler(options => options.InstanceName = "named-by-code");

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
        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create();
        builder.ConfigureScheduler(options => options.InstanceName = "validated")
            .UseInMemoryStore();

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
        reportingResources.JobStore.Should().BeSameAs(reportingStore, "resources must be assembled from the same scheduler's keyed parts");
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
}
