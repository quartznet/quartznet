using Microsoft.Extensions.DependencyInjection;

using Quartz.Core;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// Constructing a scheduler must not start its thread. The container is what constructs it, so anything
/// that merely resolves the graph — a diagnostic, a ValidateOnBuild pass, a test asserting on
/// registrations — would otherwise spin one up as a side effect.
/// </summary>
[NonParallelizable]
public class SchedulerConstructionTest
{
    [Test]
    public void ResolvingTheSchedulerDoesNotStartItsThread()
    {
        var services = new ServiceCollection();
        services.AddQuartz();

        using var provider = services.BuildServiceProvider();

        var scheduler = provider.GetRequiredService<QuartzScheduler>();

        scheduler.schedThread.Running.Should().BeFalse("resolving a scheduler must not start a thread");
    }

    [Test]
    public void ValidatingTheContainerDoesNotStartASchedulerThread()
    {
        var services = new ServiceCollection();
        services.AddQuartz();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        provider.GetRequiredService<QuartzScheduler>().schedThread.Running.Should().BeFalse();
    }

    [Test]
    public async Task StartingTheSchedulerStartsItsThread()
    {
        var services = new ServiceCollection();
        services.AddQuartz();

        using var provider = services.BuildServiceProvider();

        var scheduler = provider.GetRequiredService<QuartzScheduler>();
        await scheduler.Start();

        try
        {
            scheduler.schedThread.Running.Should().BeTrue();
            scheduler.schedThread.Paused.Should().BeFalse();
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: false);
        }
    }

    [Test]
    public async Task ShuttingDownASchedulerThatWasNeverStartedDoesNotThrow()
    {
        var services = new ServiceCollection();
        services.AddQuartz();

        using var provider = services.BuildServiceProvider();

        var scheduler = provider.GetRequiredService<QuartzScheduler>();

        // Halt and Shutdown used to dereference state that only Start created.
        await scheduler.Shutdown(waitForJobsToComplete: false);

        scheduler.Status.Should().Be(SchedulerStatus.Shutdown);
    }
}
