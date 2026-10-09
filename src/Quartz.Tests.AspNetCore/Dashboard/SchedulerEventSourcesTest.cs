using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// Which source a key's events are read from, on the one case where reading the key and resolving it
/// could disagree.
/// </summary>
public sealed class SchedulerEventSourcesTest
{
    /// <summary>
    /// A scheduler of this process named <c>x/y</c> beside a target <c>x</c> fronting a scheduler <c>y</c>:
    /// the key <c>x/y</c> resolves to the local scheduler, so its events are this process's.
    /// </summary>
    [Test]
    public void ALocalSchedulerNamedLikeAKeyKeepsItsOwnEvents()
    {
        IScheduler local = A.Fake<IScheduler>();
        A.CallTo(() => local.SchedulerName).Returns("x/y");
        A.CallTo(() => local.SchedulerInstanceId).Returns("node-1");

        SchedulerRepository repository = new();
        repository.Bind(local);

        FakeSchedulerEventSource broker = new();
        FakeSchedulerEventSource targetsOwn = new();

        SchedulerTargets targets = new();
        targets.Add(new SchedulerTarget { Name = "x", Origin = SchedulerOrigin.Remote, Events = (_, _) => targetsOwn });

        ServiceCollection services = new();
        services.AddSingleton<ISchedulerRepository>(repository);
        services.AddSingleton(targets);
        services.AddSingleton<ISchedulerEventSource>(broker);
        using ServiceProvider provider = services.BuildServiceProvider();

        SchedulerEventSources.For(provider, "x/y").Should().BeSameAs(broker,
            "the key resolves to the scheduler of this process, and showing target x's events under it would attribute another scheduler's firings to it");
        SchedulerEventSources.For(provider, "x/other").Should().BeSameAs(targetsOwn,
            "a key no local scheduler is named like is the target's scheduler");
    }
}
