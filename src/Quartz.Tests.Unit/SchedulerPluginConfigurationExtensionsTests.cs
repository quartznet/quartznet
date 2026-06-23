using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Listener;
using Quartz.Plugin.Interrupt;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Tests.Unit;

[NonParallelizable]
public sealed class SchedulerPluginConfigurationExtensionsTests
{
    [Test]
    public void UsePluginOnSchedulerBuilderShouldSetPluginTypeProperty()
    {
        var builder = SchedulerBuilder.Create();

        builder.UsePlugin<TestPluginWithDependency>("testPlugin");

        builder.Properties["quartz.plugin.testPlugin.type"].Should().Be(typeof(TestPluginWithDependency).AssemblyQualifiedNameWithoutVersion());
    }

    [Test]
    public void UsePluginUnderAddQuartzShouldRegisterPluginAndSetProperty()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITestPluginDependency, TestPluginDependency>();
        services.AddQuartz(q => q.UsePlugin<TestPluginWithDependency>("testPlugin"));

        using var provider = services.BuildServiceProvider();

        var plugin = provider.GetService<TestPluginWithDependency>();
        plugin.Should().NotBeNull();
        plugin.Dependency.Should().NotBeNull("plugin should be constructed by the container with constructor injection");

        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;
        options["quartz.plugin.testPlugin.type"].Should().Be(typeof(TestPluginWithDependency).AssemblyQualifiedNameWithoutVersion());
    }

    [Test]
    public void TryRegisterSingletonOnSchedulerBuilderShouldReturnFalse()
    {
        var builder = SchedulerBuilder.Create();

        var registered = builder.TryRegisterSingleton<ITestPluginDependency, TestPluginDependency>();

        registered.Should().BeFalse();
    }

    [Test]
    public void TryRegisterSingletonUnderAddQuartzShouldRegisterService()
    {
        var services = new ServiceCollection();
        var registered = false;
        services.AddQuartz(q =>
        {
            registered = q.TryRegisterSingleton<ITestPluginDependency, TestPluginDependency>();
        });

        registered.Should().BeTrue();

        using var provider = services.BuildServiceProvider();
        provider.GetService<ITestPluginDependency>().Should().BeOfType<TestPluginDependency>();
    }

    [Test]
    public void UsePluginShouldRequireConfigurer()
    {
        IPropertyConfigurationRoot configurer = null;

        Action act = () => configurer.UsePlugin<TestPluginWithDependency>("testPlugin");

        act.Should().Throw<ArgumentNullException>();
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("my.plugin")]
    public void UsePluginShouldValidatePluginName(string name)
    {
        var builder = SchedulerBuilder.Create();

        Action act = () => builder.UsePlugin<TestPluginWithDependency>(name);

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void BuiltInPluginExtensionUnderAddQuartzShouldStillRegisterPluginAndSetProperty()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UseJobAutoInterrupt());

        using var provider = services.BuildServiceProvider();

        provider.GetService<JobInterruptMonitorPlugin>().Should().NotBeNull();

        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;
        options["quartz.plugin.jobAutoInterrupt.type"].Should().Be(typeof(JobInterruptMonitorPlugin).AssemblyQualifiedNameWithoutVersion());
    }

    [Test]
    public async Task UsePluginInDeferredConfigurationShouldConstructPluginWithInjectedDependencies()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITestPluginDependency, TestPluginDependency>();

        services.AddQuartz((q, sp) =>
        {
            q.SchedulerName = "DeferredUsePluginTest";
            q.UseInMemoryStore();
            q.UsePlugin<TestPluginWithDependency>("testPlugin");
        });

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ISchedulerFactory>();
        var scheduler = await factory.GetScheduler();

        scheduler.Context.TryGetValue(TestPluginWithDependency.ContextKey, out var pluginInstance).Should().BeTrue();
        var plugin = (TestPluginWithDependency) pluginInstance;
        plugin.Dependency.Should().NotBeNull("plugin should be constructed with constructor injection even in deferred configuration");

        await scheduler.Shutdown();
    }

    [Test]
    public async Task TryRegisterSingletonInDeferredConfigurationShouldMakeCompanionServiceAvailable()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddQuartz((q, sp) =>
        {
            q.SchedulerName = "DeferredCompanionServiceTest";
            q.UseInMemoryStore();
            q.TryRegisterSingleton<ITestPluginDependency, TestPluginDependency>();
            q.UsePlugin<TestPluginWithDependency>("testPlugin");
        });

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ISchedulerFactory>();
        var scheduler = await factory.GetScheduler();

        scheduler.Context.TryGetValue(TestPluginWithDependency.ContextKey, out var pluginInstance).Should().BeTrue();
        var plugin = (TestPluginWithDependency) pluginInstance;
        plugin.Dependency.Should().NotBeNull("companion service registered during deferred configuration should be available for constructor injection");

        await scheduler.Shutdown();
    }

    [Test]
    public void DeferredRegistryShouldResolveSameSingletonInstanceForRepeatedLookups()
    {
        var registry = new DeferredSingletonRegistry();
        registry.Register(typeof(ITestPluginDependency), typeof(TestPluginDependency));
        using var provider = new ServiceCollection().BuildServiceProvider();

        var first = registry.Resolve(typeof(ITestPluginDependency), provider);
        var second = registry.Resolve(typeof(ITestPluginDependency), provider);

        first.Should().BeOfType<TestPluginDependency>();
        second.Should().BeSameAs(first, "repeated lookups of a registered service type should observe the same singleton");
    }

    [Test]
    public void DeferredRegistryShouldNotResolveUnregisteredServiceType()
    {
        var registry = new DeferredSingletonRegistry();
        registry.Register(typeof(ITestPluginDependency), typeof(TestPluginDependency));
        using var provider = new ServiceCollection().BuildServiceProvider();

        registry.Resolve(typeof(TestListenerWithDependency), provider).Should().BeNull("only registered service types resolve from the registry");
        registry.IsRegistered(typeof(ITestPluginDependency)).Should().BeTrue();
        registry.IsRegistered(typeof(TestListenerWithDependency)).Should().BeFalse();
    }

    [Test]
    public async Task DeferredListenerShouldReceiveCompanionServiceFromDeferredRegistration()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddQuartz((q, sp) =>
        {
            q.SchedulerName = "DeferredListenerCompanionTest";
            q.UseInMemoryStore();
            q.TryRegisterSingleton<ITestPluginDependency, TestPluginDependency>();
            q.AddSchedulerListener<TestListenerWithDependency>();
        });

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ISchedulerFactory>();
        var scheduler = await factory.GetScheduler();

        var listener = scheduler.ListenerManager.GetSchedulerListeners().OfType<TestListenerWithDependency>().Single();
        listener.Dependency.Should().NotBeNull("companion service registered during deferred configuration should be available for listener constructor injection");

        await scheduler.Shutdown();
    }

    [Test]
    public async Task BuiltInPluginExtensionInDeferredConfigurationShouldInitializePlugin()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddQuartz((q, sp) =>
        {
            q.SchedulerName = "DeferredBuiltInPluginTest";
            q.UseInMemoryStore();
            q.UseJobAutoInterrupt();
        });

        await using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<ISchedulerFactory>();
        var scheduler = await factory.GetScheduler();

        scheduler.ListenerManager.GetTriggerListeners().Should().Contain(l => l is JobInterruptMonitorPlugin);

        await scheduler.Shutdown();
    }
}

public interface ITestPluginDependency
{
}

public sealed class TestPluginDependency : ITestPluginDependency
{
}

public sealed class TestListenerWithDependency : SchedulerListenerSupport
{
    public TestListenerWithDependency(ITestPluginDependency dependency)
    {
        Dependency = dependency;
    }

    public ITestPluginDependency Dependency { get; }
}

public sealed class TestPluginWithDependency : ISchedulerPlugin
{
    public const string ContextKey = "TestPluginWithDependency";

    public TestPluginWithDependency(ITestPluginDependency dependency)
    {
        Dependency = dependency;
    }

    public ITestPluginDependency Dependency { get; }

    public ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        scheduler.Context[ContextKey] = this;
        return ValueTask.CompletedTask;
    }

    public ValueTask Start(CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }

    public ValueTask Shutdown(CancellationToken cancellationToken = default)
    {
        return ValueTask.CompletedTask;
    }
}
