#nullable enable

using System.Collections.Specialized;
using System.Text.Json;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Core;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Impl.Triggers;
using Quartz.Serialization.SystemTextJson;
using Quartz.Serialization.SystemTextJson.Triggers;
using Quartz.Impl;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Configuration;

/// <summary>
/// Guards the one failure mode this configuration model can have that nothing else catches:
/// configuration that is accepted without complaint and then quietly discarded.
/// </summary>
/// <remarks>
/// <para>
/// Registration is first-wins, so the order in which defaults, legacy property keys and the caller's own
/// configuration reach the service collection decides which of them survives. Getting that order wrong
/// does not fail a build or throw at startup; it produces a scheduler configured as if the application
/// had said nothing. Every test here is a case where that has already happened once.
/// </para>
/// <para>
/// Marked non-parallelizable because building a persistent store publishes its provider to the
/// process-wide connection manager.
/// </para>
/// </remarks>
[NonParallelizable]
public class ConfigurationIsNeverSilentlyDroppedTest
{
    /// <summary>
    /// Configures a persistent store that never reaches a database, so what is under test is which
    /// registration won rather than whether a driver is installed.
    /// </summary>
    private static void UseStubbedPersistentStore(IQuartzBuilder q)
    {
        q.UsePersistentStore(store =>
        {
            store.Configure(options => options.DataSource = "test");
            RegisterStubProvider(store.Services, q.SchedulerName);
        });
    }

    private static void RegisterStubProvider(IServiceCollection services, string schedulerName)
    {
        if (schedulerName.Length == 0)
        {
            services.AddSingleton<IDbProvider>(new StubDbProvider());
        }
        else
        {
            services.AddKeyedSingleton<IDbProvider>(schedulerName, new StubDbProvider());
        }
    }

    private static IConfiguration Section(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(x => "Quartz:" + x.Key, x => x.Value))
            .Build()
            .GetSection("Quartz");
    }

    [Test]
    public void AJobStoreChosenInCodeBeatsALegacyTypeKey()
    {
        var services = new ServiceCollection();
        services.AddQuartz(
            new NameValueCollection { ["quartz.jobStore.type"] = typeof(RAMJobStore).AssemblyQualifiedName },
            UseStubbedPersistentStore);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IJobStore>().Should().BeOfType<LocalTransactionJobStore>(
            "a leftover 3.x quartz.jobStore.type must not turn a configured persistent store back into an "
            + "in-memory one, which would lose every job on restart without a word");
    }

    [Test]
    public void AThreadPoolChosenInCodeBeatsALegacyTypeKey()
    {
        var services = new ServiceCollection();
        services.AddQuartz(
            new NameValueCollection { ["quartz.threadPool.type"] = typeof(DedicatedThreadPool).AssemblyQualifiedName },
            q => q.UseDefaultThreadPool(maxConcurrency: 4));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IThreadPool>().Should().BeOfType<DefaultThreadPool>();
    }

    [Test]
    public void TheHierarchicalJobStoreSectionReachesAPersistentStore()
    {
        var services = new ServiceCollection();
        services.AddQuartz(
            Section(new Dictionary<string, string?>
            {
                ["JobStore:TablePrefix"] = "QRTZ2_",
                ["JobStore:UseProperties"] = "true",
            }),
            UseStubbedPersistentStore);

        using var provider = services.BuildServiceProvider();
        var store = (JobStoreSupport) provider.GetRequiredService<IJobStore>();

        store.TablePrefix.Should().Be("QRTZ2_", "querying QRTZ_TRIGGERS when the tables are QRTZ2_ is a runtime failure, not a fallback");
    }

    [Test]
    public void SectionsWithoutTypedOptionsAreStillRead()
    {
        var services = new ServiceCollection();
        services.AddQuartz(Section(new Dictionary<string, string?>
        {
            ["Plugin:Xml:Type"] = "Quartz.Plugins.Xml.XmlSchedulingDataProcessorPlugin, Quartz.Plugins",
            ["Plugin:Xml:FileNames"] = "~/quartz_jobs.xml",
        }));

        using var provider = services.BuildServiceProvider();
        var properties = provider.GetRequiredService<IOptions<QuartzOptions>>().Value.Properties;

        properties["quartz.plugin.xml.type"].Should().NotBeNull(
            "plugins have no typed options yet, so dropping the section leaves nothing to read them");
        properties["quartz.plugin.xml.fileNames"].Should().Be("~/quartz_jobs.xml");
    }

    [Test]
    public void ANamedSchedulersStoreReadsItsOwnOptions()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting", q =>
        {
            q.UsePersistentStore(store =>
            {
                store.Configure(options =>
                {
                    options.DataSource = "test";
                    options.TablePrefix = "REPORT_";
                });
                RegisterStubProvider(store.Services, q.SchedulerName);
            });
        });

        using var provider = services.BuildServiceProvider();
        var store = (JobStoreSupport) provider.GetRequiredKeyedService<IJobStore>("reporting");

        store.TablePrefix.Should().Be("REPORT_", "IOptions<T> resolves the unnamed options, so a named scheduler has to be given its own");
        store.InstanceName.Should().Be("reporting");
    }

    [Test]
    public void AStoreWithNoChosenLockHandlerIsLeftToChooseItsOwn()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.Configure(options =>
            {
                options.DataSource = "test";
                options.UseDbLocks = true;
            });
            store.UseClustering();
            RegisterStubProvider(store.Services, q.SchedulerName);
        }));

        using var provider = services.BuildServiceProvider();
        var store = (JobStoreSupport) provider.GetRequiredService<IJobStore>();

        store.LockHandler.Should().BeNull(
            "the choice between database row locks and an in-process monitor is made during Initialize, "
            + "once the delegate is known — injecting a default here would make a clustered scheduler "
            + "lock only against itself");
    }

    [Test]
    public void AnExplicitLockHandlerIsInjected()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.UsePersistentStore(store =>
        {
            store.Configure(options => options.DataSource = "test");
            store.UseLockHandler<SimpleSemaphore>();
            RegisterStubProvider(store.Services, q.SchedulerName);
        }));

        using var provider = services.BuildServiceProvider();
        var store = (JobStoreSupport) provider.GetRequiredService<IJobStore>();

        store.LockHandler.Should().BeOfType<SimpleSemaphore>();
    }

    [Test]
    public void ANamedSchedulersLockHandlerResolvesItsOwnDatabaseProvider()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting", q => q.UsePersistentStore(store =>
        {
            store.Configure(options => options.DataSource = "test");
            // Takes an IDbProvider, which for a named scheduler exists only under that scheduler's key.
            store.UseLockHandler<SelectForUpdateSemaphore>();
            RegisterStubProvider(store.Services, q.SchedulerName);
        }));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<ISemaphore>("reporting").Should().BeOfType<SelectForUpdateSemaphore>();
    }

    [Test]
    public void EachNamedSchedulerKeepsItsOwnSerializer()
    {
        var services = new ServiceCollection();
        services.AddQuartz("a", q => q.UsePersistentStore(store =>
        {
            store.Configure(options => options.DataSource = "test");
            RegisterStubProvider(store.Services, q.SchedulerName);
        }));
        services.AddQuartz("b", q => q.UsePersistentStore(store =>
        {
            store.Configure(options => options.DataSource = "test");
            store.UseSerializer<CountingObjectSerializer>();
            RegisterStubProvider(store.Services, q.SchedulerName);
        }));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<IObjectSerializer>("a").Should().BeOfType<SystemTextJsonObjectSerializer>();
        provider.GetRequiredKeyedService<IObjectSerializer>("b").Should().BeOfType<CountingObjectSerializer>(
            "reading a database written with one serializer using another fails at the first trigger fire");
    }

    [Test]
    public void EachNamedSchedulerKeepsItsOwnCustomTriggerSerializers()
    {
        var services = new ServiceCollection();
        services.AddQuartz("a", q => q.UsePersistentStore(store =>
        {
            store.Configure(options => options.DataSource = "test");
            store.UseSystemTextJsonSerializer(json => json.AddTriggerSerializer<TriggerKnownToA>(new TriggerKnownToASerializer()));
            RegisterStubProvider(store.Services, q.SchedulerName);
        }));
        services.AddQuartz("b", q => q.UsePersistentStore(store =>
        {
            store.Configure(options => options.DataSource = "test");
            store.UseSystemTextJsonSerializer(json => json.AddTriggerSerializer<TriggerKnownToB>(new TriggerKnownToBSerializer()));
            RegisterStubProvider(store.Services, q.SchedulerName);
        }));

        using var provider = services.BuildServiceProvider();

        var a = provider.GetRequiredKeyedService<IObjectSerializer>("a");
        var b = provider.GetRequiredKeyedService<IObjectSerializer>("b");

        a.Serialize<ITrigger>(NewTrigger<TriggerKnownToA>()).Should().NotBeEmpty();
        b.Serialize<ITrigger>(NewTrigger<TriggerKnownToB>()).Should().NotBeEmpty();

        // While the maps lived in statics both schedulers saw both registrations, so whichever scheduler
        // was configured last decided what every scheduler in the process could write.
        a.Invoking(x => x.Serialize<ITrigger>(NewTrigger<TriggerKnownToB>())).Should().Throw<JsonSerializationException>(
            "a custom trigger serializer registered for one scheduler must not leak into another");
        b.Invoking(x => x.Serialize<ITrigger>(NewTrigger<TriggerKnownToA>())).Should().Throw<JsonSerializationException>();

        // The built-ins are still there in both, which is what makes registering a custom serializer an
        // addition rather than a replacement.
        a.Serialize<ITrigger>(NewTrigger<SimpleTriggerImpl>()).Should().NotBeEmpty();
        b.Serialize<ITrigger>(NewTrigger<SimpleTriggerImpl>()).Should().NotBeEmpty();
    }

    [Test]
    public void AContainerRegisteredSerializerRegistryReachesTheDefaultSerializer()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new SystemTextJsonSerializerRegistry()
            .AddTriggerSerializer<TriggerKnownToA>(new TriggerKnownToASerializer()));
        services.AddQuartz(UseStubbedPersistentStore);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IObjectSerializer>()
            .Serialize<ITrigger>(NewTrigger<TriggerKnownToA>()).Should().NotBeEmpty(
                "nothing called UseSystemTextJsonSerializer, so the container's registry is the only place "
                + "the default serializer can learn a custom trigger type from");
    }

    [Test]
    public void AKeyedSerializerRegistryReachesOnlyItsOwnScheduler()
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton("a", new SystemTextJsonSerializerRegistry()
            .AddTriggerSerializer<TriggerKnownToA>(new TriggerKnownToASerializer()));
        services.AddQuartz("a", UseStubbedPersistentStore);
        services.AddQuartz("b", UseStubbedPersistentStore);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<IObjectSerializer>("a")
            .Serialize<ITrigger>(NewTrigger<TriggerKnownToA>()).Should().NotBeEmpty();

        provider.GetRequiredKeyedService<IObjectSerializer>("b")
            .Invoking(x => x.Serialize<ITrigger>(NewTrigger<TriggerKnownToA>())).Should().Throw<JsonSerializationException>(
                "a registry registered under one scheduler's key belongs to that scheduler, and the others "
                + "keep reading the container's");
    }

    [Test]
    public void AContainerRegisteredSerializerRegistryReachesAPropertyConfiguredSerializer()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new SystemTextJsonSerializerRegistry()
            .AddTriggerSerializer<TriggerKnownToA>(new TriggerKnownToASerializer()));
        services.AddQuartz(
            new NameValueCollection { ["quartz.serializer.type"] = "stj" },
            UseStubbedPersistentStore);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IObjectSerializer>()
            .Serialize<ITrigger>(NewTrigger<TriggerKnownToA>()).Should().NotBeEmpty(
                "a serializer named by quartz.serializer.type is built without any callback to register "
                + "custom serializers through, so it has to be handed the container's registry");
    }

    /// <summary>
    /// Reads one scope's limit off a scheduler's snapshot, asserting the scope is configured at all.
    /// </summary>
    private static int? LimitFor(ExecutionLimits? limits, ExecutionGroupScope scope)
    {
        limits.Should().NotBeNull();
        limits!.TryGetLimit(scope, out int? limit).Should().BeTrue($"execution scope '{scope}' should be configured");
        return limit;
    }

    [Test]
    public async Task ExecutionLimitsSetInCodeAreApplied()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = "limits-in-code");
            q.UseExecutionLimits(limits => limits.ForGroup("heavy", 2));
        });

        using var provider = services.BuildServiceProvider();
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            provider.GetRequiredService<QuartzScheduler>().GetExecutionLimits()
                .Should().NotBeNull("a group limit that is registered and never read lets the group saturate every worker");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task ExecutionLimitsSpelledAsPropertiesAreApplied()
    {
        var services = new ServiceCollection();
        services.AddQuartz(new NameValueCollection
        {
            ["quartz.executionLimit.heavy"] = "2"
        });

        using var provider = services.BuildServiceProvider();
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            LimitFor(provider.GetRequiredService<QuartzScheduler>().GetExecutionLimits(), ExecutionGroupScope.Named("heavy"))
                .Should().Be(2, "quartz.executionLimit.* keys must reach the scheduler like limits set in code");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task ExecutionLimitsSetInCodeBeatTheSameLimitsSpelledAsProperties()
    {
        var services = new ServiceCollection();
        services.AddQuartz(
            new NameValueCollection { ["quartz.executionLimit.heavy"] = "2" },
            q => q.UseExecutionLimits(limits => limits.ForGroup("heavy", 9)));

        using var provider = services.BuildServiceProvider();
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            LimitFor(provider.GetRequiredService<QuartzScheduler>().GetExecutionLimits(), ExecutionGroupScope.Named("heavy"))
                .Should().Be(9, "code beats strings, as everywhere else");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    /// <summary>
    /// A key supplied after <c>AddQuartz</c> has run is not in the collection <c>AddQuartz</c> was handed,
    /// so registration-time parsing alone cannot see it. This is the documented route for a key the typed
    /// options do not cover, and it silently stopped applying.
    /// </summary>
    [Test]
    public async Task ExecutionLimitsSetThroughQuartzOptionsAfterAddQuartzAreApplied()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.ConfigureScheduler(options => options.InstanceName = "deferred-limits"));
        services.Configure<QuartzOptions>(options => options.Properties["quartz.executionLimit.heavy"] = "2");

        using var provider = services.BuildServiceProvider();
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            LimitFor(provider.GetRequiredService<QuartzScheduler>().GetExecutionLimits(), ExecutionGroupScope.Named("heavy"))
                .Should().Be(2, "a group left uncapped saturates the whole thread pool");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task EachNamedSchedulerKeepsItsOwnExecutionLimits()
    {
        var services = new ServiceCollection();
        services.AddQuartz("reporting", new NameValueCollection { ["quartz.executionLimit.heavy"] = "2" });
        services.AddQuartz("ingest", q => q.UseExecutionLimits(limits => limits.ForGroup("heavy", 7)));

        using var provider = services.BuildServiceProvider();
        var reporting = await provider.GetRequiredKeyedService<ISchedulerFactory>("reporting").GetScheduler();
        var ingest = await provider.GetRequiredKeyedService<ISchedulerFactory>("ingest").GetScheduler();
        try
        {
            LimitFor(await reporting.GetExecutionLimits(), ExecutionGroupScope.Named("heavy")).Should().Be(2);
            LimitFor(await ingest.GetExecutionLimits(), ExecutionGroupScope.Named("heavy")).Should().Be(7);
        }
        finally
        {
            await reporting.Shutdown();
            await ingest.Shutdown();
        }
    }

    [Test]
    public async Task LegacyListenerPropertiesStillAttachTheirListener()
    {
        var services = new ServiceCollection();
        services.AddQuartz(new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "legacy-listeners",
            ["quartz.jobListener.audit.type"] = typeof(RecordingJobListener).AssemblyQualifiedName,
        });

        using var provider = services.BuildServiceProvider();
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            var listeners = scheduler.ListenerManager.GetJobListeners();

            listeners.Should().ContainSingle(x => x is RecordingJobListener);
            listeners.Single(x => x is RecordingJobListener).Name.Should().Be("audit",
                "a listener is known to the listener manager by name, and configuration only gives it the one it was declared under");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public void AHierarchicalJobStoreTypeStillSelectsTheStore()
    {
        var services = new ServiceCollection();
        services.AddQuartz(Section(new Dictionary<string, string?>
        {
            ["JobStore:Type"] = typeof(LocalTransactionJobStore).AssemblyQualifiedName,
            ["JobStore:DataSource"] = "test",
            ["JobStore:TablePrefix"] = "QRTZ2_",
        }), q => RegisterStubProvider(q.Services, q.SchedulerName));

        using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IJobStore>();

        store.Should().BeOfType<LocalTransactionJobStore>(
            "Type has no property on the options type to bind to, so excluding the section from "
            + "flattening would leave nobody reading it and fall back to RAMJobStore");
        ((JobStoreSupport) store).TablePrefix.Should().Be("QRTZ2_");
    }

    [Test]
    public void AHierarchicalAutoInstanceIdIsTranslatedNotStored()
    {
        var services = new ServiceCollection();
        services.AddQuartz(Section(new Dictionary<string, string?>
        {
            ["Scheduler:InstanceId"] = "AUTO",
        }));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value;

        options.GenerateInstanceId.Should().BeTrue(
            "storing the sentinel literally makes every clustered node claim the same instance id");
    }

    [Test]
    public void AConfiguredSerializerBeatsTheBuiltInFallback()
    {
        var services = new ServiceCollection();
        services.AddQuartz(
            new NameValueCollection { ["quartz.serializer.type"] = typeof(CountingObjectSerializer).AssemblyQualifiedName },
            UseStubbedPersistentStore);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IObjectSerializer>().Should().BeOfType<CountingObjectSerializer>(
            "reading a database written by one serializer with another fails at the first trigger fire");
    }

    [Test]
    public void ADriverDelegateKeyAppliesWhenTheStoreIsChosenInCode()
    {
        var services = new ServiceCollection();
        services.AddQuartz(
            new NameValueCollection { ["quartz.jobStore.driverDelegateType"] = typeof(SqlServerDelegate).AssemblyQualifiedName },
            UseStubbedPersistentStore);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IDriverDelegate>().Should().BeOfType<SqlServerDelegate>(
            "moving store selection into code must not silently drop the delegate named in configuration");
    }

    [Test]
    public void UseClusteringKeepsIntervalsThatCameFromConfiguration()
    {
        var services = new ServiceCollection();
        services.AddQuartz(
            new NameValueCollection { ["quartz.jobStore.clusterCheckinInterval"] = "20000" },
            q => q.UsePersistentStore(store =>
            {
                store.Configure(options => options.DataSource = "test");
                store.UseClustering();
                RegisterStubProvider(store.Services, q.SchedulerName);
            }));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ClusteringOptions>>().Value;

        options.Enabled.Should().BeTrue();
        options.CheckinInterval.Should().Be(TimeSpan.FromSeconds(20),
            "UseClustering() with no arguments states no interval, so it must not overwrite one");
    }

    [Test]
    public void TheLegacyThreadPoolNameIsStillUnderstood()
    {
        var services = new ServiceCollection();
        services.AddQuartz(new NameValueCollection
        {
            ["quartz.threadPool.type"] = "Quartz.Simpl.SimpleThreadPool, Quartz",
        });

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IThreadPool>().Should().BeOfType<DefaultThreadPool>(
            "the type was renamed, and failing to start is a worse answer than using its replacement");
    }

    [Test]
    public void JobStoreKeysWithoutATypedOptionAreStillMapped()
    {
        var services = new ServiceCollection();
        services.AddQuartz(new NameValueCollection
        {
            ["quartz.jobStore.dataSource"] = "test",
            ["quartz.jobStore.misfireHandlerFrequency"] = "5000",
            ["quartz.jobStore.maxTransientRetries"] = "10",
            ["quartz.jobStore.makeThreadsDaemons"] = "true",
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<AdoJobStoreOptions>>().Get(Options.DefaultName);

        options.MisfireHandlerFrequency.Should().Be(TimeSpan.FromSeconds(5));
        options.MaxTransientRetries.Should().Be(10);
        options.MakeThreadsDaemons.Should().BeTrue();
    }

    [Test]
    public async Task PluginSettingsInConfigurationFindAPluginAddedInCode()
    {
        var services = new ServiceCollection();
        services.AddQuartz(
            new NameValueCollection
            {
                ["quartz.scheduler.instanceName"] = "plugin-settings",
                ["quartz.plugin.recorder.someSetting"] = "configured",
            },
            q => q.AddPlugin(_ => new RecordingPlugin(), "recorder"));

        using var provider = services.BuildServiceProvider();
        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            var plugin = provider.GetServices<ISchedulerPlugin>().OfType<RecordingPlugin>().Single();

            plugin.SomeSetting.Should().Be("configured");
            plugin.Name.Should().Be("recorder",
                "some plugins derive persisted job keys from their name, so it has to be the name it was added under");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public void AHierarchicalDurationInMillisecondsIsNotReadAsDays()
    {
        var services = new ServiceCollection();
        services.AddQuartz(Section(new Dictionary<string, string?>
        {
            // The spelling a 3.x appsettings.json used, once the flat keys grew a hierarchical form.
            ["Scheduler:IdleWaitTime"] = "30000",
        }));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value.IdleWaitTime
            .Should().Be(TimeSpan.FromSeconds(30),
                "TimeSpan reads a bare integer as days, which would leave the scheduler idle for eighty years");
    }

    [Test]
    public void ABothWaysDurationIsStillReadAsATimeSpan()
    {
        var services = new ServiceCollection();
        services.AddQuartz(Section(new Dictionary<string, string?>
        {
            ["Scheduler:IdleWaitTime"] = "00:00:45",
        }));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value.IdleWaitTime
            .Should().Be(TimeSpan.FromSeconds(45));
    }

    [Test]
    public void HierarchicalClusteringStillImpliesDatabaseLocks()
    {
        var services = new ServiceCollection();
        services.AddQuartz(
            Section(new Dictionary<string, string?>
            {
                ["JobStore:Clustered"] = "true",
                ["JobStore:DataSource"] = "test",
            }),
            UseStubbedPersistentStore);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<ClusteringOptions>>().Value.Enabled.Should().BeTrue();
        provider.GetRequiredService<IOptions<AdoJobStoreOptions>>().Value.UseDbLocks.Should().BeTrue(
            "clustering has never worked without database locking, so saying one must not fail validation for the other");
    }

    /// <summary>
    /// Clustering has typed options of its own now, so it also has a section of its own. The older
    /// spelling above still works, and both have to arrive at the same place.
    /// </summary>
    [Test]
    public void ClusteringSaidInItsOwnSectionIsReadTheSameWay()
    {
        var services = new ServiceCollection();
        services.AddQuartz(
            Section(new Dictionary<string, string?>
            {
                ["JobStore:Clustering:Enabled"] = "true",
                ["JobStore:Clustering:CheckinInterval"] = "00:00:20",
                ["JobStore:DataSource"] = "test",
            }),
            UseStubbedPersistentStore);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ClusteringOptions>>().Value;

        options.Enabled.Should().BeTrue();
        options.CheckinInterval.Should().Be(TimeSpan.FromSeconds(20),
            "the sub-section binds onto the typed options, so its intervals are not silently defaulted");

        provider.GetRequiredService<IOptions<AdoJobStoreOptions>>().Value.UseDbLocks.Should().BeTrue(
            "the implication clustering has always carried must hold for both spellings of the key");
    }

    [Test]
    public void AKnobInATypedSectionWithNoTypedOptionIsStillReadable()
    {
        var services = new ServiceCollection();
        services.AddQuartz(Section(new Dictionary<string, string?>
        {
            ["JobStore:Marker"] = "configured",
            ["ThreadPool:Marker"] = "configured",
        }));

        using var provider = services.BuildServiceProvider();
        var properties = provider.GetRequiredService<IOptions<QuartzOptions>>().Value.Properties;

        properties["quartz.jobStore.marker"].Should().Be("configured",
            "a third-party store has no options type, so its settings are only ever read as flat keys");
        properties["quartz.threadPool.marker"].Should().Be("configured");
    }

    [Test]
    public void ALockHandlerKeepsTheSettingsBesideItsTypeKey()
    {
        var services = new ServiceCollection();
        services.AddQuartz(
            new NameValueCollection
            {
                ["quartz.jobStore.lockHandler.type"] = typeof(MarkedSemaphore).AssemblyQualifiedName,
                ["quartz.jobStore.lockHandler.marker"] = "configured",
            },
            UseStubbedPersistentStore);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ISemaphore>().Should().BeOfType<MarkedSemaphore>()
            .Which.Marker.Should().Be("configured");
    }

    [Test]
    public void TwoPluginEntriesOfOneTypeStayTwoPlugins()
    {
        var services = new ServiceCollection();
        services.AddQuartz(new NameValueCollection
        {
            ["quartz.plugin.dev.type"] = typeof(RecordingPlugin).AssemblyQualifiedName,
            ["quartz.plugin.dev.someSetting"] = "dev",
            ["quartz.plugin.tenantA.type"] = typeof(RecordingPlugin).AssemblyQualifiedName,
            ["quartz.plugin.tenantA.someSetting"] = "tenantA",
        });

        using var provider = services.BuildServiceProvider();
        var properties = provider.GetRequiredService<IOptions<QuartzOptions>>().Value.ToNameValueCollection();

        var plugins = Quartz.Configuration.SchedulerPluginFactory.Create(provider, [], properties, "");

        plugins.Select(x => ((RecordingPlugin) x.Plugin).SomeSetting)
            .Should().BeEquivalentTo(["dev", "tenantA"],
                "one plugin per configured name is what the properties format has always meant");
    }

    [Test]
    public async Task ASchedulerNameSetOnTheOptionsReachesTheScheduler()
    {
        var services = new ServiceCollection();
        services.AddQuartz();
        services.Configure<QuartzOptions>(
            options => options.Properties["quartz.scheduler.instanceName"] = "NamedThroughOptions");

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value.InstanceName
            .Should().Be("NamedThroughOptions");

        var scheduler = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();
        try
        {
            scheduler.SchedulerName.Should().Be("NamedThroughOptions",
                "a name accepted without complaint and then not used is the failure mode this suite exists for");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public void EveryFlatKeyReachesTheReadersIncludingTheEmptyOnes()
    {
        var options = new QuartzOptions();
        options.Properties["quartz.plugin.dev.type"] = null;
        options.Properties["quartz.plugin.dev.blank"] = "   ";
        options.Properties["quartz.plugin.dev.someSetting"] = "dev";

        var properties = options.ToNameValueCollection();

        properties.AllKeys.Should().BeEquivalentTo(
            ["quartz.plugin.dev.type", "quartz.plugin.dev.blank", "quartz.plugin.dev.someSetting"],
            "whether an empty value means 'not configured' is the reader's decision, and it makes it — "
            + "dropping the key here instead would make a key set to an empty string indistinguishable "
            + "from one that was never given");

        properties["quartz.plugin.dev.type"].Should().BeNull();
        properties["quartz.plugin.dev.blank"].Should().Be("   ");
    }

    private sealed class MarkedSemaphore : ISemaphore
    {
        public string Marker { get; set; } = "";

        public bool RequiresConnection => false;

        public ValueTask<bool> ObtainLock(Guid requestorId, ConnectionAndTransactionHolder? conn, SchedulerLock lockKind, CancellationToken cancellationToken = default)
            => new(true);

        public ValueTask ReleaseLock(Guid requestorId, SchedulerLock lockKind, CancellationToken cancellationToken = default) => default;
    }

    private sealed class RecordingPlugin : ISchedulerPlugin
    {
        public string SomeSetting { get; set; } = "";

        public string Name { get; private set; } = "";

        public ValueTask Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default)
        {
            Name = pluginName;
            return default;
        }

        public ValueTask Start(CancellationToken cancellationToken = default) => default;

        public ValueTask Shutdown(CancellationToken cancellationToken = default) => default;
    }

    private sealed class RecordingJobListener : IJobListener
    {
        public string Name { get; set; } = "";

        public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

        public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default) => default;
    }

    private static TTrigger NewTrigger<TTrigger>() where TTrigger : SimpleTriggerImpl, new()
    {
        return new TTrigger
        {
            Key = new TriggerKey("trigger", "group"),
            JobKey = new JobKey("job", "group"),
            StartTimeUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        };
    }

    private sealed class TriggerKnownToA : SimpleTriggerImpl;

    private sealed class TriggerKnownToB : SimpleTriggerImpl;

    private sealed class TriggerKnownToASerializer : TriggerSerializer<TriggerKnownToA>
    {
        public override string TriggerTypeName => "KnownToA";

        public override IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, JsonSerializerOptions options)
            => SimpleScheduleBuilder.Create();

        protected override void SerializeFields(Utf8JsonWriter writer, TriggerKnownToA trigger, JsonSerializerOptions options)
        {
        }

        protected override void DeserializeFields(TriggerKnownToA trigger, JsonElement jsonElement, JsonSerializerOptions options)
        {
        }
    }

    private sealed class TriggerKnownToBSerializer : TriggerSerializer<TriggerKnownToB>
    {
        public override string TriggerTypeName => "KnownToB";

        public override IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, JsonSerializerOptions options)
            => SimpleScheduleBuilder.Create();

        protected override void SerializeFields(Utf8JsonWriter writer, TriggerKnownToB trigger, JsonSerializerOptions options)
        {
        }

        protected override void DeserializeFields(TriggerKnownToB trigger, JsonElement jsonElement, JsonSerializerOptions options)
        {
        }
    }

    private sealed class CountingObjectSerializer : IObjectSerializer
    {
        public byte[] Serialize<T>(T obj) where T : class => [];

        public T? Deserialize<T>(byte[] data) where T : class => null;
    }
}
