using FakeItEasy;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// A scheduler's parts are keyed singletons the container owns, so "shut it down and ask for it again"
/// cannot mean "build a fresh one" the way it did in 3.x. It used to hand back the same closed instance
/// with its thread pool and job store re-initialized underneath it, which looks alive and schedules
/// nothing. The factory refuses instead, and says what to do rather than what went wrong.
/// </summary>
[NonParallelizable]
public sealed class ShutDownSchedulerIsNotRestartedTest
{
    [Test]
    public async Task AskingForAShutDownSchedulerAgainThrows()
    {
        await using ServiceProvider provider = BuildContainer("RestartRefusedScheduler");
        ISchedulerFactory factory = provider.GetRequiredService<ISchedulerFactory>();

        IScheduler scheduler = await factory.GetScheduler();
        await scheduler.Start();
        await scheduler.Shutdown(waitForJobsToComplete: false);

        Func<Task> act = async () => await factory.GetScheduler();

        await act.Should().ThrowAsync<SchedulerException>()
            .WithMessage("*RestartRefusedScheduler*has been shut down*Standby()/Start()*",
                "a dead scheduler has to name the scheduler and the way to pause and resume, rather than "
                + "silently handing back an instance that can never run again");
    }

    [Test]
    public async Task ARepositoryStillHoldingAShutDownSchedulerIsNotTrusted()
    {
        // The repository Quartz ships drops a scheduler that has shut down as soon as a read notices, so
        // this path is only reachable through a repository of the application's own — which is a
        // documented registration, every Quartz registration being TryAdd.
        IScheduler dead = A.Fake<IScheduler>();
        A.CallTo(() => dead.SchedulerName).Returns("StillBoundScheduler");
        A.CallTo(() => dead.SchedulerInstanceId).Returns("NON_CLUSTERED");
        A.CallTo(() => dead.IsShutdown).Returns(true);

        ISchedulerRepository repository = A.Fake<ISchedulerRepository>();
        A.CallTo(() => repository.Lookup("StillBoundScheduler", A<string>._)).Returns(dead);

        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddSingleton(repository);
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "StillBoundScheduler");
            q.UseInMemoryStore();
        });

        await using ServiceProvider provider = services.BuildServiceProvider();

        Func<Task> act = async () => await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await act.Should().ThrowAsync<SchedulerException>()
            .WithMessage("*StillBoundScheduler*has been shut down*",
                "a repository entry is checked before it is handed out, so a repository that keeps its dead "
                + "does not turn one into a working scheduler");
    }

    [Test]
    public async Task StandbyAndStartAreStillTheWayToPauseAndResume()
    {
        await using ServiceProvider provider = BuildContainer("StandbyScheduler");
        ISchedulerFactory factory = provider.GetRequiredService<ISchedulerFactory>();

        IScheduler scheduler = await factory.GetScheduler();
        try
        {
            await scheduler.Start();
            await scheduler.Standby();
            scheduler.InStandbyMode.Should().BeTrue();

            await scheduler.Start();
            scheduler.IsStarted.Should().BeTrue("Standby() is reversible, which is what makes refusing a restart tolerable");

            (await factory.GetScheduler()).Should().BeSameAs(scheduler,
                "a live scheduler is still handed back rather than rebuilt");
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    private static ServiceProvider BuildContainer(string instanceName)
    {
        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = instanceName);
            q.UseInMemoryStore();
        });

        return services.BuildServiceProvider();
    }
}
