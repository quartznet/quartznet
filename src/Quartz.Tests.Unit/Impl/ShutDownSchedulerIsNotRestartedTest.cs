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
    public async Task AShutDownSchedulerStillBoundInTheRepositoryThrowsToo()
    {
        // A scheduler removes itself from its repository as it shuts down, so the entry is normally gone
        // by the time anyone asks again. Between 'closed' being set and that removal it is not, and a
        // scheduler bound by hand into another container's repository never leaves at all.
        await using ServiceProvider provider = BuildContainer("StillBoundScheduler");

        IScheduler dead = A.Fake<IScheduler>();
        A.CallTo(() => dead.SchedulerName).Returns("StillBoundScheduler");
        A.CallTo(() => dead.SchedulerInstanceId).Returns("NON_CLUSTERED");
        A.CallTo(() => dead.IsShutdown).Returns(true);

        provider.GetRequiredService<ISchedulerRepository>().Bind(dead);

        Func<Task> act = async () => await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        await act.Should().ThrowAsync<SchedulerException>()
            .WithMessage("*StillBoundScheduler*has been shut down*",
                "the repository entry is checked before the scheduler is built, so both paths refuse alike");
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
