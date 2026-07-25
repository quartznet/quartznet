using Microsoft.Extensions.DependencyInjection;

using Quartz.Core;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Named schedulers used to be second-class: a container holds one registration per service type, so a
/// named scheduler could not own its job store or thread pool and had to be assembled by filtering
/// registrations by name after the fact. With the scheduler name as the service key they are ordinary.
/// </summary>
[NonParallelizable]
public class NamedSchedulerContainerTest
{
    [Test]
    public void EachNamedSchedulerOwnsItsThreadPool()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting", q => q.UseDefaultThreadPool(maxConcurrency: 3));
        services.AddQuartz("ingest", q => q.UseDefaultThreadPool(maxConcurrency: 9));

        using var provider = services.BuildServiceProvider();

        Assert.Multiple(() =>
        {
            Assert.That(provider.GetRequiredKeyedService<IThreadPool>("reporting").PoolSize, Is.EqualTo(3));
            Assert.That(provider.GetRequiredKeyedService<IThreadPool>("ingest").PoolSize, Is.EqualTo(9));
        });
    }

    [Test]
    public void NamedSchedulersDoNotShareAJobStore()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting", q => q.UseInMemoryStore());
        services.AddQuartz("ingest", q => q.UseInMemoryStore());

        using var provider = services.BuildServiceProvider();

        var reporting = provider.GetRequiredKeyedService<IJobStore>("reporting");
        var ingest = provider.GetRequiredKeyedService<IJobStore>("ingest");

        Assert.Multiple(() =>
        {
            Assert.That(reporting, Is.InstanceOf<RAMJobStore>());
            Assert.That(ingest, Is.Not.SameAs(reporting), "sharing a job store would mean sharing trigger state");
        });
    }

    [Test]
    public void ANamedSchedulerTakesItsNameFromItsRegistration()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting");

        using var provider = services.BuildServiceProvider();

        Assert.That(provider.GetRequiredKeyedService<QuartzSchedulerResources>("reporting").Name,
            Is.EqualTo("reporting"));
    }

    [Test]
    public async Task NamedAndDefaultSchedulersCoexist()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.SchedulerName = "the-default";
            q.UseInMemoryStore();
        });
        services.AddQuartz("reporting", q => q.UseInMemoryStore());

        using var provider = services.BuildServiceProvider();

        var defaultScheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        var reportingScheduler = await provider.GetRequiredKeyedService<ISchedulerFactory>("reporting").GetScheduler();

        try
        {
            Assert.Multiple(() =>
            {
                Assert.That(defaultScheduler.SchedulerName, Is.EqualTo("the-default"));
                Assert.That(reportingScheduler.SchedulerName, Is.EqualTo("reporting"));
                Assert.That(reportingScheduler, Is.Not.SameAs(defaultScheduler));
            });
        }
        finally
        {
            await defaultScheduler.Shutdown();
            await reportingScheduler.Shutdown();
        }
    }

    [Test]
    public async Task ANamedSchedulerGetsItsOwnJobsAndTriggers()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting", q =>
        {
            q.UseInMemoryStore();
            q.AddJob<NoOpJob>(job => job.WithIdentity("reporting-job"));
            q.AddTrigger(trigger => trigger.ForJob("reporting-job").WithIdentity("reporting-trigger").StartAt(DateTimeOffset.UtcNow.AddHours(1)));
        });
        services.AddQuartz("ingest", q => q.UseInMemoryStore());

        using var provider = services.BuildServiceProvider();

        var reporting = await provider.GetRequiredKeyedService<ISchedulerFactory>("reporting").GetScheduler();
        var ingest = await provider.GetRequiredKeyedService<ISchedulerFactory>("ingest").GetScheduler();

        try
        {
            Assert.Multiple(async () =>
            {
                Assert.That(await reporting.CheckExists(new JobKey("reporting-job")), Is.True);
                Assert.That(await ingest.CheckExists(new JobKey("reporting-job")), Is.False,
                    "a named scheduler's jobs must not leak into another scheduler");
            });
        }
        finally
        {
            await reporting.Shutdown();
            await ingest.Shutdown();
        }
    }

    public class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context) => default;
    }
}
