using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;


using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Listener;
using Quartz.Logging;
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
        SchedulerBuilder builder = SchedulerBuilder.Create();

        builder.UsePlugin<TestPluginWithDependency>("testPlugin");

        builder.Properties["quartz.plugin.testPlugin.type"].Should().Be(typeof(TestPluginWithDependency).AssemblyQualifiedNameWithoutVersion());
    }

    [Test]
    public void UsePluginUnderAddQuartzShouldRegisterPluginAndSetProperty()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddSingleton<ITestPluginDependency, TestPluginDependency>();
        services.AddQuartz(q => q.UsePlugin<TestPluginWithDependency>("testPlugin"));

        using ServiceProvider provider = services.BuildServiceProvider();

        TestPluginWithDependency plugin = provider.GetService<TestPluginWithDependency>();
        plugin.Should().NotBeNull();
        plugin.Dependency.Should().NotBeNull("plugin should be constructed by the container with constructor injection");

        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;
        options["quartz.plugin.testPlugin.type"].Should().Be(typeof(TestPluginWithDependency).AssemblyQualifiedNameWithoutVersion());
    }

    [Test]
    public void TryRegisterSingletonOnSchedulerBuilderShouldReturnFalse()
    {
        SchedulerBuilder builder = SchedulerBuilder.Create();

        bool registered = builder.TryRegisterSingleton<ITestPluginDependency, TestPluginDependency>();

        registered.Should().BeFalse();
    }

    [Test]
    public void TryRegisterSingletonUnderAddQuartzShouldRegisterService()
    {
        ServiceCollection services = new ServiceCollection();
        bool registered = false;
        services.AddQuartz(q =>
        {
            registered = q.TryRegisterSingleton<ITestPluginDependency, TestPluginDependency>();
        });

        registered.Should().BeTrue();

        using ServiceProvider provider = services.BuildServiceProvider();
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
        SchedulerBuilder builder = SchedulerBuilder.Create();

        Action act = () => builder.UsePlugin<TestPluginWithDependency>(name);

        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void BuiltInPluginExtensionUnderAddQuartzShouldStillRegisterPluginAndSetProperty()
    {
        ServiceCollection services = new ServiceCollection();
        services.AddQuartz(q => q.UseJobAutoInterrupt());

        using ServiceProvider provider = services.BuildServiceProvider();

        provider.GetService<JobInterruptMonitorPlugin>().Should().NotBeNull();

        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;
        options["quartz.plugin.jobAutoInterrupt.type"].Should().Be(typeof(JobInterruptMonitorPlugin).AssemblyQualifiedNameWithoutVersion());
    }

    [Test]
    public async Task UsePluginInDeferredConfigurationShouldConstructPluginWithInjectedDependencies()
    {
        try
        {
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ITestPluginDependency, TestPluginDependency>();

            services.AddQuartz((q, sp) =>
            {
                q.SchedulerName = "DeferredUsePluginTest";
                q.UseInMemoryStore();
                q.UsePlugin<TestPluginWithDependency>("testPlugin");
            });

            await using ServiceProvider provider = services.BuildServiceProvider();
            ISchedulerFactory factory = provider.GetRequiredService<ISchedulerFactory>();
            IScheduler scheduler = await factory.GetScheduler();

            scheduler.Context.TryGetValue(TestPluginWithDependency.ContextKey, out object pluginInstance).Should().BeTrue();
            TestPluginWithDependency plugin = (TestPluginWithDependency) pluginInstance;
            plugin.Dependency.Should().NotBeNull("plugin should be constructed with constructor injection even in deferred configuration");

            await scheduler.Shutdown();
        }
        finally
        {
            LogProvider.SetCurrentLogProvider(null);
        }
    }

    [Test]
    public async Task TryRegisterSingletonInDeferredConfigurationShouldMakeCompanionServiceAvailable()
    {
        try
        {
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();

            services.AddQuartz((q, sp) =>
            {
                q.SchedulerName = "DeferredCompanionServiceTest";
                q.UseInMemoryStore();
                q.TryRegisterSingleton<ITestPluginDependency, TestPluginDependency>();
                q.UsePlugin<TestPluginWithDependency>("testPlugin");
            });

            await using ServiceProvider provider = services.BuildServiceProvider();
            ISchedulerFactory factory = provider.GetRequiredService<ISchedulerFactory>();
            IScheduler scheduler = await factory.GetScheduler();

            scheduler.Context.TryGetValue(TestPluginWithDependency.ContextKey, out object pluginInstance).Should().BeTrue();
            TestPluginWithDependency plugin = (TestPluginWithDependency) pluginInstance;
            plugin.Dependency.Should().NotBeNull("companion service registered during deferred configuration should be available for constructor injection");

            await scheduler.Shutdown();
        }
        finally
        {
            LogProvider.SetCurrentLogProvider(null);
        }
    }

    [Test]
    public void DeferredRegistryShouldResolveSameSingletonInstanceForRepeatedLookups()
    {
        DeferredSingletonRegistry registry = new DeferredSingletonRegistry();
        registry.Register(typeof(ITestPluginDependency), typeof(TestPluginDependency));
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();

        object first = registry.Resolve(typeof(ITestPluginDependency), provider);
        object second = registry.Resolve(typeof(ITestPluginDependency), provider);

        first.Should().BeOfType<TestPluginDependency>();
        second.Should().BeSameAs(first, "repeated lookups of a registered service type should observe the same singleton");
    }

    [Test]
    public void DeferredRegistryShouldNotResolveUnregisteredServiceType()
    {
        DeferredSingletonRegistry registry = new DeferredSingletonRegistry();
        registry.Register(typeof(ITestPluginDependency), typeof(TestPluginDependency));
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();

        registry.Resolve(typeof(TestListenerWithDependency), provider).Should().BeNull("only registered service types resolve from the registry");
        registry.IsRegistered(typeof(ITestPluginDependency)).Should().BeTrue();
        registry.IsRegistered(typeof(TestListenerWithDependency)).Should().BeFalse();
    }

    [Test]
    public void DeferredRegistryWrapServiceProviderShouldReturnSameProviderWhenEmpty()
    {
        DeferredSingletonRegistry registry = new DeferredSingletonRegistry();
        using ServiceProvider provider = new ServiceCollection().BuildServiceProvider();

        registry.WrapServiceProvider(provider).Should().BeSameAs(provider, "an empty registry should not hide the container's service provider capabilities");
    }

    [Test]
    public async Task DeferredListenerShouldReceiveCompanionServiceFromDeferredRegistration()
    {
        try
        {
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();

            services.AddQuartz((q, sp) =>
            {
                q.SchedulerName = "DeferredListenerCompanionTest";
                q.UseInMemoryStore();
                q.TryRegisterSingleton<ITestPluginDependency, TestPluginDependency>();
                q.AddSchedulerListener<TestListenerWithDependency>();
            });

            await using ServiceProvider provider = services.BuildServiceProvider();
            ISchedulerFactory factory = provider.GetRequiredService<ISchedulerFactory>();
            IScheduler scheduler = await factory.GetScheduler();

            TestListenerWithDependency listener = scheduler.ListenerManager.GetSchedulerListeners().OfType<TestListenerWithDependency>().Single();
            listener.Dependency.Should().NotBeNull("companion service registered during deferred configuration should be available for listener constructor injection");

            await scheduler.Shutdown();
        }
        finally
        {
            LogProvider.SetCurrentLogProvider(null);
        }
    }

    [Test]
    public async Task BuiltInPluginExtensionInDeferredConfigurationShouldInitializePlugin()
    {
        try
        {
            ServiceCollection services = new ServiceCollection();
            services.AddLogging();

            services.AddQuartz((q, sp) =>
            {
                q.SchedulerName = "DeferredBuiltInPluginTest";
                q.UseInMemoryStore();
                q.UseJobAutoInterrupt();
            });

            await using ServiceProvider provider = services.BuildServiceProvider();
            ISchedulerFactory factory = provider.GetRequiredService<ISchedulerFactory>();
            IScheduler scheduler = await factory.GetScheduler();

            scheduler.ListenerManager.GetTriggerListeners().Should().Contain(l => l is JobInterruptMonitorPlugin);

            await scheduler.Shutdown();
        }
        finally
        {
            LogProvider.SetCurrentLogProvider(null);
        }
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

    public Task Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        scheduler.Context.Put(ContextKey, this);
        return Task.CompletedTask;
    }

    public Task Start(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task Shutdown(CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
