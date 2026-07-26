using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Core;
using Quartz.Simpl;
using Quartz.Spi;

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
        var scheduler = await QuartzSchedulerBuilder.Create()
            .ConfigureScheduler(options => options.InstanceName = "standalone-builds")
            .UseDefaultThreadPool(maxConcurrency: 2)
            .UseInMemoryStore()
            .BuildScheduler();

        try
        {
            scheduler.SchedulerName.Should().Be("standalone-builds");
            scheduler.IsStarted.Should().BeFalse();

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

    [Test]
    public void ContainerValidationPassesForTheDefaultConfiguration()
    {
        // Build() validates on build, so a missing or mis-scoped registration fails here rather than
        // at first use.
        var act = () => QuartzSchedulerBuilder.Create()
            .ConfigureScheduler(options => options.InstanceName = "validated")
            .UseInMemoryStore()
            .Build();

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
        resources.ThreadName.Should().Be("configured_QuartzSchedulerThread");
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
