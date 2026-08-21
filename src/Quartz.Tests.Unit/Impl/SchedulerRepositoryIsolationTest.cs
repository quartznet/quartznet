using System.Collections.Specialized;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore.Common;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// The scheduler repository and the connection manager belong to the container that built a scheduler.
/// Neither has a process-wide instance any more (#3178), so two sets of schedulers in one process are
/// independent of each other. That is observable, and these tests are what pin it down.
/// </summary>
[NonParallelizable]
public sealed class SchedulerRepositoryIsolationTest
{
    [Test]
    public async Task TwoContainers_EachOwnTheirSchedulerRepository()
    {
        await using var first = BuildContainer("FirstScheduler");
        await using var second = BuildContainer("SecondScheduler");

        var firstScheduler = await first.GetRequiredService<ISchedulerFactory>().GetScheduler();
        var secondScheduler = await second.GetRequiredService<ISchedulerFactory>().GetScheduler();

        try
        {
            var firstRepository = first.GetRequiredService<ISchedulerRepository>();
            var secondRepository = second.GetRequiredService<ISchedulerRepository>();

            firstRepository.Should().NotBeSameAs(secondRepository);

            firstRepository.LookupAll().Should().ContainSingle().Which.Should().BeSameAs(firstScheduler);
            secondRepository.LookupAll().Should().ContainSingle().Which.Should().BeSameAs(secondScheduler);

            firstRepository.Lookup("SecondScheduler").Should().BeNull(
                "a container's repository holds only the schedulers that container built");
            secondRepository.Lookup("FirstScheduler").Should().BeNull();
        }
        finally
        {
            await firstScheduler.Shutdown();
            await secondScheduler.Shutdown();
        }
    }

    [Test]
    public async Task PropertiesBuiltSchedulerAndAddQuartz_DoNotShareARepository()
    {
        await using var container = BuildContainer("ContainerScheduler");
        var containerFactory = container.GetRequiredService<ISchedulerFactory>();

        var properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "PropertiesScheduler",
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
        };

        using StandaloneSchedulerFactory propertiesFactory = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();

        var containerScheduler = await containerFactory.GetScheduler();
        var propertiesScheduler = await propertiesFactory.GetScheduler();

        try
        {
            (await containerFactory.GetAllSchedulers()).Should().ContainSingle()
                .Which.Should().BeSameAs(containerScheduler);

            (await propertiesFactory.GetAllSchedulers()).Should().ContainSingle()
                .Which.Should().BeSameAs(propertiesScheduler,
                    "a standalone builder reports the schedulers of its own container, not every scheduler in the process");

            (await containerFactory.LookupScheduler("PropertiesScheduler")).Should().BeNull(
                "a scheduler built by a standalone builder is no longer reachable from an AddQuartz container");
            (await propertiesFactory.LookupScheduler("ContainerScheduler")).Should().BeNull(
                "and the other way round");
        }
        finally
        {
            await containerScheduler.Shutdown();
            await propertiesScheduler.Shutdown();
        }
    }

    [Test]
    public async Task TwoPropertiesBuiltSchedulers_DoNotShareARepository()
    {
        using StandaloneSchedulerFactory firstFactory = QuartzSchedulerBuilder.Create().UseProperties(PropertiesFor("FirstPropertiesScheduler")).Build();
        using StandaloneSchedulerFactory secondFactory = QuartzSchedulerBuilder.Create().UseProperties(PropertiesFor("SecondPropertiesScheduler")).Build();

        var firstScheduler = await firstFactory.GetScheduler();
        var secondScheduler = await secondFactory.GetScheduler();

        try
        {
            (await firstFactory.GetAllSchedulers()).Should().ContainSingle()
                .Which.Should().BeSameAs(firstScheduler);
            (await secondFactory.GetAllSchedulers()).Should().ContainSingle()
                .Which.Should().BeSameAs(secondScheduler);
        }
        finally
        {
            await firstScheduler.Shutdown();
            await secondScheduler.Shutdown();
        }
    }

    [Test]
    public async Task ASchedulerBoundIntoAnotherRepository_DoesNotOutliveItsShutdown()
    {
        await using ServiceProvider container = BuildContainer("VisitingScheduler");
        IScheduler scheduler = await container.GetRequiredService<ISchedulerFactory>().GetScheduler();

        // Binding by hand is how a scheduler is made visible where it was not built — a dashboard's
        // container, or a standalone scheduler shown beside the container's own.
        SchedulerRepository visited = new();
        visited.Bind(scheduler);
        visited.LookupAll().Should().ContainSingle().Which.Should().BeSameAs(scheduler);

        await scheduler.Shutdown(waitForJobsToComplete: false);

        visited.LookupAll().Should().BeEmpty(
            "a scheduler unbinds itself from its own container's repository and from no other, so any "
            + "repository it was bound into has to notice the shutdown by itself");
        visited.Lookup("VisitingScheduler").Should().BeNull();

        container.GetRequiredService<ISchedulerRepository>().LookupAll().Should().BeEmpty(
            "the scheduler's own repository drops it too, as it always did");
    }

    [Test]
    public void TwoContainers_EachOwnTheirConnectionManager()
    {
        using var first = BuildContainer("FirstConnectionScheduler");
        using var second = BuildContainer("SecondConnectionScheduler");

        var firstManager = first.GetRequiredService<IDbConnectionManager>();
        var secondManager = second.GetRequiredService<IDbConnectionManager>();

        firstManager.Should().NotBeSameAs(secondManager);

        firstManager.AddDbProvider("shared-data-source-name", new StubDbProvider());

        var act = () => secondManager.GetDbProvider("shared-data-source-name");
        act.Should().Throw<ArgumentException>(
            "a data source registered in one container must not leak into another");
    }

    private static ServiceProvider BuildContainer(string instanceName)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddQuartz(q => q.ConfigureScheduler(options => options.InstanceName = instanceName));
        return services.BuildServiceProvider();
    }

    private static NameValueCollection PropertiesFor(string instanceName)
    {
        return new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = instanceName,
            ["quartz.serializer.type"] = TestConstants.DefaultSerializerType,
        };
    }
}
