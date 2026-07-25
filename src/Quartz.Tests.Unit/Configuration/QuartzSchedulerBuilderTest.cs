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
        public ValueTask Execute(IJobExecutionContext context)
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
            Assert.Multiple(() =>
            {
                Assert.That(scheduler.SchedulerName, Is.EqualTo("standalone-builds"));
                Assert.That(scheduler.IsStarted, Is.False);
            });

            await scheduler.Start();

            var job = JobBuilder.Create<SignallingJob>().WithIdentity("job").Build();
            var trigger = TriggerBuilder.Create().WithIdentity("trigger").StartNow().Build();
            await scheduler.ScheduleJob(job, trigger);

            var completed = await Task.WhenAny(fired.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            Assert.That(completed, Is.SameAs(fired.Task), "the scheduled job should have fired");
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
        Assert.DoesNotThrow(() => QuartzSchedulerBuilder.Create()
            .ConfigureScheduler(options => options.InstanceName = "validated")
            .UseInMemoryStore()
            .Build());
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

        Assert.Multiple(() =>
        {
            Assert.That(threadPool.PoolSize, Is.EqualTo(17));
            Assert.That(resources.Name, Is.EqualTo("configured"));
            Assert.That(resources.MaxBatchSize, Is.EqualTo(9));
            Assert.That(resources.ThreadName, Is.EqualTo("configured_QuartzSchedulerThread"));
        });
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

        Assert.Multiple(() =>
        {
            Assert.That(defaultPool.PoolSize, Is.EqualTo(1));
            Assert.That(reportingPool.PoolSize, Is.EqualTo(5));
            Assert.That(ingestPool.PoolSize, Is.EqualTo(11));

            Assert.That(reportingPool, Is.Not.SameAs(defaultPool));
            Assert.That(ingestPool, Is.Not.SameAs(reportingPool));
        });
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

        Assert.Multiple(() =>
        {
            Assert.That(reportingStore, Is.Not.SameAs(defaultStore),
                "each scheduler must own its job store, otherwise they share trigger state");
            Assert.That(reportingResources.Name, Is.EqualTo("reporting"),
                "the service key doubles as the scheduler's instance name");
            Assert.That(reportingResources.JobStore, Is.SameAs(reportingStore),
                "resources must be assembled from the same scheduler's keyed parts");
        });
    }

    [Test]
    public void ApplicationRegistrationsWinOverTheDefaults()
    {
        var services = new ServiceCollection();
        var custom = new RAMJobStore();
        services.AddSingleton<IJobStore>(custom);
        services.AddQuartzScheduler();

        using var provider = services.BuildServiceProvider();

        Assert.That(provider.GetRequiredService<IJobStore>(), Is.SameAs(custom),
            "TryAdd registrations must not displace what the application registered");
    }
}
