using Microsoft.Extensions.DependencyInjection;

using Quartz.Core;
using Quartz.Impl;
using Quartz.Extensibility;

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

        provider.GetRequiredKeyedService<IThreadPool>("reporting").PoolSize.Should().Be(3);
        provider.GetRequiredKeyedService<IThreadPool>("ingest").PoolSize.Should().Be(9);
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

        reporting.Should().BeOfType<RAMJobStore>();
        ingest.Should().NotBeSameAs(reporting, "sharing a job store would mean sharing trigger state");
    }

    [Test]
    public void ANamedSchedulerTakesItsNameFromItsRegistration()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting");

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<QuartzSchedulerResources>("reporting").Name.Should().Be("reporting");
    }

    [Test]
    public async Task NamedAndDefaultSchedulersCoexist()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "the-default");
            q.UseInMemoryStore();
        });
        services.AddQuartz("reporting", q => q.UseInMemoryStore());

        using var provider = services.BuildServiceProvider();

        var defaultScheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        var reportingScheduler = await provider.GetRequiredKeyedService<ISchedulerFactory>("reporting").GetScheduler();

        try
        {
            defaultScheduler.SchedulerName.Should().Be("the-default");
            reportingScheduler.SchedulerName.Should().Be("reporting");
            reportingScheduler.Should().NotBeSameAs(defaultScheduler);
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
            q.AddTrigger<IJob>(trigger => trigger.ForJob("reporting-job").WithIdentity("reporting-trigger").StartAt(DateTimeOffset.UtcNow.AddHours(1)));
        });
        services.AddQuartz("ingest", q => q.UseInMemoryStore());

        using var provider = services.BuildServiceProvider();

        var reporting = await provider.GetRequiredKeyedService<ISchedulerFactory>("reporting").GetScheduler();
        var ingest = await provider.GetRequiredKeyedService<ISchedulerFactory>("ingest").GetScheduler();

        try
        {
            (await reporting.Exists(new JobKey("reporting-job"))).Should().BeTrue();
            (await ingest.Exists(new JobKey("reporting-job"))).Should()
                .BeFalse("a named scheduler's jobs must not leak into another scheduler");
        }
        finally
        {
            await reporting.Shutdown();
            await ingest.Shutdown();
        }
    }

    [Test]
    public async Task TheDefaultSchedulerCannotTakeANamedSchedulersName()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.ConfigureScheduler(options => options.InstanceName = "tenant-1"));
        services.AddQuartz("tenant-1", q => q.UseInMemoryStore());

        using var provider = services.BuildServiceProvider();

        var act = async () => await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await act.Should().ThrowAsync<SchedulerConfigException>()
            .WithMessage("*AddQuartz() configured InstanceName 'tenant-1'*AddQuartz(\"tenant-1\", ...) is also registered*",
                "the collision has to name both registrations, because it used to surface as a duplicate-name "
                + "ArgumentException from the repository during host start, naming neither");
    }

    [Test]
    public async Task TheDefaultSchedulersNameCollidesIgnoringCase()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.ConfigureScheduler(options => options.InstanceName = "TENANT-1"));
        services.AddQuartz("tenant-1", q => q.UseInMemoryStore());

        using var provider = services.BuildServiceProvider();

        var act = async () => await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await act.Should().ThrowAsync<SchedulerConfigException>()
            .WithMessage("*AddQuartz(\"tenant-1\", ...)*",
                "the repository indexes names ignoring case, so the check has to as well — and it reports the "
                + "name as the registration spelled it");
    }

    [Test]
    public void TwoNamedSchedulersCollideIgnoringCaseWhereTheyAreRegistered()
    {
        var services = new ServiceCollection();
        services.AddQuartz("tenant-1", q => q.UseInMemoryStore());

        var act = () => services.AddQuartz("TENANT-1", q => q.UseInMemoryStore());

        act.Should().Throw<ArgumentException>().WithMessage("*already been registered*",
            "two named schedulers whose names differ only by case would collide in the repository too");
    }

    [Test]
    public async Task ANamedSchedulerIsNotAccusedOfCollidingWithItself()
    {
        var services = new ServiceCollection();
        services.AddQuartz("tenant-1", q => q.UseInMemoryStore());
        services.AddQuartz("tenant-2", q => q.UseInMemoryStore());

        using var provider = services.BuildServiceProvider();

        var first = await provider.GetRequiredKeyedService<ISchedulerFactory>("tenant-1").GetScheduler();
        var second = await provider.GetRequiredKeyedService<ISchedulerFactory>("tenant-2").GetScheduler();

        try
        {
            first.SchedulerName.Should().Be("tenant-1",
                "a named scheduler's instance name is its registration name, so it always matches the registry");
            second.SchedulerName.Should().Be("tenant-2");
        }
        finally
        {
            await first.Shutdown();
            await second.Shutdown();
        }
    }

    public class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
