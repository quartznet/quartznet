using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Extensions.DependencyInjection;

/// <summary>
/// <see cref="IScheduler"/> is an ordinary service: unkeyed for the default scheduler, keyed by name for
/// the others.
/// </summary>
public sealed class SchedulerResolutionTest
{
    [Test]
    public async Task DefaultScheduler_IsResolvableUnkeyed()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UseInMemoryStore());

        await using var provider = services.BuildServiceProvider();

        var scheduler = provider.GetRequiredService<IScheduler>();
        scheduler.SchedulerName.Should().Be("QuartzScheduler");

        await scheduler.Start();

        var built = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        built.IsStarted.Should().BeTrue("the injected handle drives the scheduler the factory produces");

        await scheduler.Shutdown();
    }

    [Test]
    public async Task NamedSchedulers_AreResolvableByKey()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting", q => q.UseInMemoryStore());
        services.AddQuartz("billing", q => q.UseInMemoryStore());

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<IScheduler>("reporting").SchedulerName.Should().Be("reporting");
        provider.GetRequiredKeyedService<IScheduler>("billing").SchedulerName.Should().Be("billing");

        provider.GetService<IScheduler>().Should().BeNull(
            "no default scheduler was registered, so nothing belongs to the unkeyed registration");
    }

    [Test]
    public async Task DefaultAndNamedSchedulers_ResolveToDifferentSchedulers()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UseInMemoryStore());
        services.AddQuartz("reporting", q => q.UseInMemoryStore());

        await using var provider = services.BuildServiceProvider();

        var standard = provider.GetRequiredService<IScheduler>();
        var reporting = provider.GetRequiredKeyedService<IScheduler>("reporting");

        standard.SchedulerName.Should().Be("QuartzScheduler");
        reporting.SchedulerName.Should().Be("reporting");

        await reporting.Start();

        var repository = provider.GetRequiredService<ISchedulerRepository>();
        repository.LookupAll().Should().ContainSingle("only the scheduler that was used has been built")
            .Which.SchedulerName.Should().Be("reporting");

        await reporting.Shutdown();
    }

    /// <summary>
    /// A scheduler whose parts all initialize synchronously is built on the spot, so its synchronous
    /// members answer without anything having been awaited first.
    /// </summary>
    /// <remarks>
    /// This is the common case rather than a guarantee: it holds for the in-memory store, and for a
    /// database-backed one it holds only if nothing about opening the store actually goes asynchronous.
    /// The contract stays "await something first, or resolve after the host has started" — which under
    /// the hosted service is always true — because a caller cannot tell which case it is in.
    /// </remarks>
    [Test]
    public async Task SynchronousMembers_AnswerWhenTheSchedulerCanBeBuiltWithoutWaiting()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UseInMemoryStore());

        await using var provider = services.BuildServiceProvider();

        var scheduler = provider.GetRequiredService<IScheduler>();

        scheduler.IsStarted.Should().BeFalse();
        scheduler.IsShutdown.Should().BeFalse();
        scheduler.SchedulerInstanceId.Should().Be("NON_CLUSTERED");
        scheduler.ListenerManager.Should().NotBeNull();
    }

    [Test]
    public void SchedulerName_IsAnsweredWithoutBuildingTheScheduler()
    {
        var factory = A.Fake<ISchedulerFactory>();
        var scheduler = new DeferredScheduler(factory, OptionsFor(new QuartzSchedulerOptions()), new SchedulerKey("reporting"));

        scheduler.SchedulerName.Should().Be("reporting");

        A.CallTo(() => factory.GetScheduler(A<CancellationToken>._)).MustNotHaveHappened();
    }

    [Test]
    public void SynchronousMembers_ThrowWhileTheSchedulerIsStillBeingBuilt()
    {
        var creation = new TaskCompletionSource<IScheduler>();
        var factory = A.Fake<ISchedulerFactory>();
        A.CallTo(() => factory.GetScheduler(A<CancellationToken>._)).Returns(new ValueTask<IScheduler>(creation.Task));

        var scheduler = new DeferredScheduler(factory, OptionsFor(new QuartzSchedulerOptions()), new SchedulerKey("reporting"));

        var read = () => scheduler.IsStarted;
        read.Should().Throw<InvalidOperationException>()
            .WithMessage("*reporting*has not been started*",
                "a property cannot wait for a scheduler that is still being built");

        var built = A.Fake<IScheduler>();
        A.CallTo(() => built.IsStarted).Returns(true);
        creation.SetResult(built);

        scheduler.IsStarted.Should().BeTrue("the same handle answers once the scheduler exists");

        // The build was started once and kept, rather than started afresh by every reader.
        A.CallTo(() => factory.GetScheduler(A<CancellationToken>._)).MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task DefaultSchedulerName_ComesFromItsOptions()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.ConfigureScheduler(options => options.InstanceName = "configured"));

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IScheduler>().SchedulerName.Should().Be("configured");
    }

    private static IOptionsMonitor<QuartzSchedulerOptions> OptionsFor(QuartzSchedulerOptions options)
    {
        var monitor = A.Fake<IOptionsMonitor<QuartzSchedulerOptions>>();
        A.CallTo(() => monitor.Get(A<string>._)).Returns(options);
        return monitor;
    }
}
