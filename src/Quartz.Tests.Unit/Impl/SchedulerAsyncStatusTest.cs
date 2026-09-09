#nullable enable

using FakeItEasy;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl;

/// <summary>
/// <see cref="IScheduler.GetStatus" /> and <see cref="IScheduler.GetSchedulerInstanceId" /> answer what
/// <see cref="IScheduler.Status" /> and <see cref="IScheduler.SchedulerInstanceId" /> answer.
/// </summary>
/// <remarks>
/// The pair exists for the one implementation that cannot answer a property without blocking a thread —
/// a proxy for a scheduler in another process. Everywhere else they have to be indistinguishable from
/// the properties, or code that moved to them to stop blocking would be reading a different scheduler.
/// </remarks>
[NonParallelizable]
public sealed class SchedulerAsyncStatusTest
{
    /// <summary>
    /// The scheduler a factory builds in this process, across the lifecycle transitions the status is
    /// there to report.
    /// </summary>
    [Test]
    public async Task ASchedulerInThisProcessAnswersTheSameEitherWay()
    {
        await using ServiceProvider provider = Container(services => services.AddQuartz(q => q.UseInMemoryStore()));
        IScheduler scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        scheduler.Should().BeOfType<StdScheduler>(
            "this is the implementation every in-process scheduler is reached through");

        (await scheduler.GetSchedulerInstanceId()).Should().Be(scheduler.SchedulerInstanceId,
            "the instance id is one string, and which member asked for it cannot change it");

        (await scheduler.GetStatus()).Should().Be(scheduler.Status).And.Be(SchedulerStatus.Created);

        await scheduler.Start();
        (await scheduler.GetStatus()).Should().Be(scheduler.Status).And.Be(SchedulerStatus.Running);

        await scheduler.Standby();
        (await scheduler.GetStatus()).Should().Be(scheduler.Status).And.Be(SchedulerStatus.Standby);

        await scheduler.Shutdown();
        (await scheduler.GetStatus()).Should().Be(scheduler.Status).And.Be(SchedulerStatus.Shutdown,
            "a scheduler that has stopped still reports where it got to");
    }

    /// <summary>
    /// The handle a container hands out, which builds its scheduler on first use.
    /// </summary>
    [Test]
    public async Task TheHandleAContainerInjectsAnswersForTheSchedulerItBuilds()
    {
        await using ServiceProvider provider = Container(services => services.AddQuartz("acme", q => q.UseInMemoryStore()));
        IScheduler handle = provider.GetRequiredKeyedService<IScheduler>("acme");

        handle.Should().BeOfType<DeferredScheduler>();

        (await handle.GetStatus()).Should().Be(SchedulerStatus.Created,
            "the handle builds the scheduler it points at rather than answering for one of its own");
        (await handle.GetSchedulerInstanceId()).Should().Be(handle.SchedulerInstanceId);

        await handle.Start();

        (await handle.GetStatus()).Should().Be(handle.Status).And.Be(SchedulerStatus.Running);

        await handle.Shutdown();
    }

    /// <summary>
    /// A decorator hands both on, rather than letting the interface's default body answer from the
    /// property on the decorator itself.
    /// </summary>
    /// <remarks>
    /// The fake answers a different value from each member, which no real scheduler does: it is the only
    /// way to tell "forwarded the call" from "ran the default, which read the forwarded property".
    /// </remarks>
    [Test]
    public async Task ADecoratorHandsBothOnRatherThanDecomposingThem()
    {
        IScheduler inner = A.Fake<IScheduler>();
        A.CallTo(() => inner.Status).Returns(SchedulerStatus.Standby);
        A.CallTo(() => inner.GetStatus(A<CancellationToken>._)).Returns(SchedulerStatus.Running);
        A.CallTo(() => inner.SchedulerInstanceId).Returns("read-from-the-property");
        A.CallTo(() => inner.GetSchedulerInstanceId(A<CancellationToken>._)).Returns("forwarded");

        DelegatingScheduler scheduler = new(inner);

        (await scheduler.GetStatus()).Should().Be(SchedulerStatus.Running,
            "the decorator asks the scheduler behind it the question it was asked, and an inner "
            + "implementation that answers this member specially is the reason the member exists");
        (await scheduler.GetSchedulerInstanceId()).Should().Be("forwarded");
    }

    /// <summary>
    /// The default bodies, on a scheduler written outside this repository that declares neither.
    /// </summary>
    [Test]
    public async Task ASchedulerThatDeclaresNeitherAnswersFromItsProperties()
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.Status).Returns(SchedulerStatus.ShuttingDown);
        A.CallTo(() => scheduler.SchedulerInstanceId).Returns("NON_CLUSTERED");
        A.CallTo(() => scheduler.GetStatus(A<CancellationToken>._)).CallsBaseMethod();
        A.CallTo(() => scheduler.GetSchedulerInstanceId(A<CancellationToken>._)).CallsBaseMethod();

        (await scheduler.GetStatus()).Should().Be(SchedulerStatus.ShuttingDown,
            "the default implementation is what lets an implementation outside this repository compile "
            + "unchanged, so it has to report what that implementation already reports");
        (await scheduler.GetSchedulerInstanceId()).Should().Be("NON_CLUSTERED");
    }

    private static ServiceProvider Container(Action<IServiceCollection> register)
    {
        ServiceCollection services = new();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        register(services);
        return services.BuildServiceProvider();
    }
}
