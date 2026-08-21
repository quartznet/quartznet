#nullable enable

using System.Collections.Specialized;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Quartz.Configuration;
using Quartz.Core;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// A scheduler's clock is its own, and where two things say what it should be, the more specific one
/// wins.
/// </summary>
public sealed class SchedulerTimeProviderTest
{
    [Test]
    public void UseTimeProvider_AppliesToTheSchedulerItWasCalledOn()
    {
        var reportingClock = new FakeTimeProvider();

        var services = new ServiceCollection();
        services.AddQuartz(q => q.UseInMemoryStore());
        services.AddQuartz("reporting", q => q.UseTimeProvider(reportingClock));
        services.AddQuartz("billing", q => q.UseInMemoryStore());

        using var provider = services.BuildServiceProvider();

        Clock(provider, "reporting").Should().BeSameAs(reportingClock);
        Clock(provider, "billing").Should().BeSameAs(TimeProvider.System,
            "one scheduler's clock is not every scheduler's clock");
        Clock(provider).Should().BeSameAs(TimeProvider.System);
    }

    [Test]
    public void UseTimeProvider_OnTheDefaultSchedulerReplacesTheContainersClock()
    {
        var clock = new FakeTimeProvider();

        var services = new ServiceCollection();
        services.AddQuartz(q => q.UseTimeProvider(clock));

        using var provider = services.BuildServiceProvider();

        Clock(provider).Should().BeSameAs(clock);
        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(clock);
    }

    [Test]
    public void ASchedulerWithNoClockOfItsOwn_UsesTheContainers()
    {
        var containerClock = new FakeTimeProvider();

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(containerClock);
        services.AddQuartz("reporting", q => q.UseInMemoryStore());

        using var provider = services.BuildServiceProvider();

        Clock(provider, "reporting").Should().BeSameAs(containerClock,
            "a named scheduler inherits an application-wide clock it was never told about");
    }

    [Test]
    public void ALegacyKeyDoesNotBeatUseTimeProvider()
    {
        var clock = new FakeTimeProvider();
        var properties = new NameValueCollection
        {
            ["quartz.timeProvider.type"] = typeof(TestTimeProvider).AssemblyQualifiedName,
        };

        var services = new ServiceCollection();
        services.AddQuartz(properties, q => q.UseTimeProvider(clock));

        using var provider = services.BuildServiceProvider();

        Clock(provider).Should().BeSameAs(clock,
            "configuration written in code beats a type named by a string, whichever is applied first");
    }

    [Test]
    public void ALegacyKeyIsReadWhenNothingInCodeSaysOtherwise()
    {
        var properties = new NameValueCollection
        {
            ["quartz.timeProvider.type"] = typeof(TestTimeProvider).AssemblyQualifiedName,
        };

        var services = new ServiceCollection();
        services.AddQuartz("reporting", new NameValueCollection());
        services.AddQuartz(properties);

        using var provider = services.BuildServiceProvider();

        Clock(provider).Should().BeOfType<TestTimeProvider>(
            "a key that names a clock must not lose to Quartz's own fallback, whichever scheduler registered it first");
    }

    [Test]
    public void ATriggerIsBuiltWithItsSchedulersClock()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 21, 10, 0, 0, TimeSpan.Zero));

        var services = new ServiceCollection();
        services.AddQuartz("reporting", q =>
        {
            q.UseTimeProvider(clock);
            q.AddTrigger<IJob>(t => t.ForJob("job").WithIdentity("trigger"));
        });

        using var provider = services.BuildServiceProvider();

        provider.ScheduledTriggers("reporting").Should().ContainSingle()
            .Which.StartTimeUtc.Should().Be(clock.GetUtcNow(),
                "a trigger that was given no start time starts now, and now is what its scheduler's clock says");
    }

    private static TimeProvider Clock(IServiceProvider provider, string? schedulerName = null)
    {
        var resources = schedulerName is null
            ? provider.GetRequiredService<QuartzSchedulerResources>()
            : provider.GetRequiredKeyedService<QuartzSchedulerResources>(schedulerName);

        return resources.TimeProvider;
    }

    public sealed class TestTimeProvider : TimeProvider;
}
