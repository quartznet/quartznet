#nullable enable

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// The seams that let an application bring a component of its own: a job store, an instance id
/// generator, and matchers for a listener.
/// </summary>
public sealed class BuilderSeamsTest
{
    [Test]
    public void UseJobStore_BuildsTheStoreFromTheContainer()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UseJobStore<CountingJobStore>());

        using var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<IJobStore>();
        store.Should().BeOfType<CountingJobStore>()
            .Which.Signaler.Should().NotBeNull("the store is given its own scheduler's collaborators");
    }

    [Test]
    public void UseJobStore_WithOptions_HandsTheStoreItsSchedulersOptions()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting", q =>
            q.UseJobStore<ConfigurableJobStore, ConfigurableJobStoreOptions>(options => options.Label = "reporting-store"));
        services.AddQuartz(q => q.UseInMemoryStore());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<IJobStore>("reporting").Should().BeOfType<ConfigurableJobStore>()
            .Which.Label.Should().Be("reporting-store",
                "the options were declared as that scheduler's, so IOptions<T> resolves its named instance");
    }

    [Test]
    public void UseJobStore_WithAFactory_RunsTheFactoryWithTheSchedulersProvider()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting", q => q.UseJobStore(provider =>
            new DelegatingJobStore(ActivatorUtilities.CreateInstance<RAMJobStore>(provider))));

        using var container = services.BuildServiceProvider();

        container.GetRequiredKeyedService<IJobStore>("reporting").Should().BeOfType<DelegatingJobStore>();
    }

    [Test]
    public void AJobStoreChosenInCodeBeatsOneNamedByAKey()
    {
        var properties = new System.Collections.Specialized.NameValueCollection
        {
            ["quartz.jobStore.type"] = typeof(CountingJobStore).AssemblyQualifiedName,
        };

        var services = new ServiceCollection();
        services.AddQuartz(properties, q => q.UseInMemoryStore());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IJobStore>().Should().BeOfType<RAMJobStore>(
            "registration is first-wins and the callback runs before the property-derived registrations");
    }

    [Test]
    public void UseInstanceIdGenerator_ChoosesTheGeneratorAndSaysTheIdIsGenerated()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UseInstanceIdGenerator<HostNameInstanceIdGenerator>());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IInstanceIdGenerator>().Should().BeOfType<HostNameInstanceIdGenerator>();
        provider.GetRequiredService<IOptionsMonitor<QuartzSchedulerOptions>>().Get(Options.DefaultName)
            .GenerateInstanceId.Should().BeTrue(
                "a generator that was chosen and never called would be configuration that says nothing");
    }

    [Test]
    public void UseInstanceIdGenerator_WithAnInstance_IsTheGeneratorTheSchedulerUses()
    {
        var generator = new HostNameInstanceIdGenerator();

        var services = new ServiceCollection();
        services.AddQuartz("reporting", q => q.UseInstanceIdGenerator(generator));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<IInstanceIdGenerator>("reporting").Should().BeSameAs(generator);
    }

    [Test]
    public void ListenerMatchers_AreTakenAsACollection()
    {
        IReadOnlyCollection<IMatcher<JobKey>> matchers = [GroupMatcher<JobKey>.GroupEquals("reports")];

        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            // Both call shapes: the collection a caller already holds, and the loose arguments params has
            // always accepted.
            q.AddJobListener<CountingJobListener>(matchers);
            q.AddTriggerListener<CountingTriggerListener>(
                GroupMatcher<TriggerKey>.GroupEquals("reports"),
                GroupMatcher<TriggerKey>.GroupEquals("audits"));
        });

        using var provider = services.BuildServiceProvider();

        provider.GetServices<JobListenerRegistration>().Should().ContainSingle()
            .Which.Matchers.Should().ContainSingle();
        provider.GetServices<TriggerListenerRegistration>().Should().ContainSingle()
            .Which.Matchers.Should().HaveCount(2);
    }

    private sealed class CountingJobListener : IJobListener
    {
        public string Name => nameof(CountingJobListener);
    }

    private sealed class CountingTriggerListener : ITriggerListener
    {
        public string Name => nameof(CountingTriggerListener);
    }

    public sealed class ConfigurableJobStoreOptions
    {
        public string? Label { get; set; }
    }

    /// <summary>
    /// A store of an application's own, which is what the seam exists for.
    /// </summary>
    public class CountingJobStore : DelegatingJobStore
    {
        public CountingJobStore(ILoggerFactory loggerFactory, ISchedulerSignaler signaler, TimeProvider timeProvider)
            : base(new RAMJobStore(loggerFactory, signaler, timeProvider))
        {
            Signaler = signaler;
        }

        public ISchedulerSignaler Signaler { get; }
    }

    public sealed class ConfigurableJobStore : CountingJobStore
    {
        public ConfigurableJobStore(
            ILoggerFactory loggerFactory,
            ISchedulerSignaler signaler,
            TimeProvider timeProvider,
            IOptions<ConfigurableJobStoreOptions> options)
            : base(loggerFactory, signaler, timeProvider)
        {
            Label = options.Value.Label;
        }

        public string? Label { get; }
    }
}
