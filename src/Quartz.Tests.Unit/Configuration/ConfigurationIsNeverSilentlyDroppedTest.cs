#nullable enable

using System.Collections.Specialized;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Core;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Simpl;
using Quartz.Spi;

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

        provider.GetRequiredService<IJobStore>().Should().BeOfType<JobStoreTX>(
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
            ["Plugin:Xml:Type"] = "Quartz.Plugin.Xml.XMLSchedulingDataProcessorPlugin, Quartz.Plugins",
            ["Plugin:Xml:FileNames"] = "~/quartz_jobs.xml",
        }));

        using var provider = services.BuildServiceProvider();
        var properties = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

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
                options.Clustered = true;
                options.UseDbLocks = true;
            });
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
            store.UseLockHandler<StdRowLockSemaphore>();
            RegisterStubProvider(store.Services, q.SchedulerName);
        }));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<ISemaphore>("reporting").Should().BeOfType<StdRowLockSemaphore>();
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

    private sealed class RecordingJobListener : IJobListener
    {
        public string Name { get; set; } = "";

        public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

        public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default) => default;
    }

    private sealed class CountingObjectSerializer : IObjectSerializer
    {
        public void Initialize()
        {
        }

        public byte[] Serialize<T>(T obj) where T : class => [];

        public T? DeSerialize<T>(byte[] data) where T : class => null;
    }
}
