using System.Collections.Specialized;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Impl.AdoJobStore;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Configuration;

public class QuartzPropertyBridgeTest
{
    private static ServiceProvider Bridge(NameValueCollection properties, string schedulerName = null)
    {
        var services = new ServiceCollection();
        QuartzPropertyBridge.Apply(services, properties, schedulerName);
        return services.BuildServiceProvider();
    }

    private static T Options<T>(ServiceProvider provider, string name = null) where T : class
    {
        return provider.GetRequiredService<IOptionsMonitor<T>>()
            .Get(name ?? Microsoft.Extensions.Options.Options.DefaultName);
    }

    [Test]
    public void SchedulerKeysMapOntoSchedulerOptions()
    {
        using var provider = Bridge(new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "legacy",
            ["quartz.scheduler.instanceId"] = "node-7",
            ["quartz.scheduler.threadName"] = "custom-thread",
            ["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = "12",
            ["quartz.scheduler.interruptJobsOnShutdown"] = "true",
            ["quartz.scheduler.makeSchedulerThreadDaemon"] = "true",
        });

        var options = Options<QuartzSchedulerOptions>(provider);

        Assert.Multiple(() =>
        {
            Assert.That(options.InstanceName, Is.EqualTo("legacy"));
            Assert.That(options.InstanceId, Is.EqualTo("node-7"));
            Assert.That(options.ThreadName, Is.EqualTo("custom-thread"));
            Assert.That(options.MaxBatchSize, Is.EqualTo(12));
            Assert.That(options.InterruptJobsOnShutdown, Is.True);
            Assert.That(options.MakeSchedulerThreadDaemon, Is.True);
        });
    }

    [Test]
    public void LegacyDurationsAreMillisecondsAndBecomeTimeSpans()
    {
        using var provider = Bridge(new NameValueCollection
        {
            ["quartz.scheduler.idleWaitTime"] = "45000",
            ["quartz.scheduler.batchTriggerAcquisitionFireAheadTimeWindow"] = "2500",
        });

        var options = Options<QuartzSchedulerOptions>(provider);

        Assert.Multiple(() =>
        {
            Assert.That(options.IdleWaitTime, Is.EqualTo(TimeSpan.FromSeconds(45)));
            Assert.That(options.BatchTriggerAcquisitionFireAheadTimeWindow, Is.EqualTo(TimeSpan.FromMilliseconds(2500)));
        });
    }

    [Test]
    public void FlatAndHierarchicalSpellingsProduceTheSameOptions()
    {
        using var flat = Bridge(new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "same",
            ["quartz.scheduler.idleWaitTime"] = "45000",
            ["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = "12",
            ["quartz.threadPool.maxConcurrency"] = "25",
        });

        var services = new ServiceCollection();
        services.BindQuartzOptions(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                ["Quartz:Scheduler:InstanceName"] = "same",
                ["Quartz:Scheduler:IdleWaitTime"] = "00:00:45",
                ["Quartz:Scheduler:MaxBatchSize"] = "12",
                ["Quartz:ThreadPool:MaxConcurrency"] = "25",
            })
            .Build()
            .GetSection("Quartz"));
        using var hierarchical = services.BuildServiceProvider();

        var fromFlat = Options<QuartzSchedulerOptions>(flat);
        var fromHierarchical = Options<QuartzSchedulerOptions>(hierarchical);

        Assert.Multiple(() =>
        {
            Assert.That(fromFlat.InstanceName, Is.EqualTo(fromHierarchical.InstanceName));
            Assert.That(fromFlat.IdleWaitTime, Is.EqualTo(fromHierarchical.IdleWaitTime));
            Assert.That(fromFlat.MaxBatchSize, Is.EqualTo(fromHierarchical.MaxBatchSize));
            Assert.That(
                Options<ThreadPoolOptions>(flat).MaxConcurrency,
                Is.EqualTo(Options<ThreadPoolOptions>(hierarchical).MaxConcurrency));
        });
    }

    [Test]
    public void ThreadCountIsAcceptedAsAnAliasForMaxConcurrency()
    {
        using var provider = Bridge(new NameValueCollection
        {
            ["quartz.threadPool.threadCount"] = "8",
        });

        Assert.That(Options<ThreadPoolOptions>(provider).MaxConcurrency, Is.EqualTo(8));
    }

    [Test]
    public void SchedulerContextKeysBecomeContextEntries()
    {
        using var provider = Bridge(new NameValueCollection
        {
            ["quartz.context.key.environment"] = "staging",
            ["quartz.context.key.region"] = "eu-north",
        });

        var options = Options<QuartzSchedulerOptions>(provider);

        Assert.Multiple(() =>
        {
            Assert.That(options.Context["environment"], Is.EqualTo("staging"));
            Assert.That(options.Context["region"], Is.EqualTo("eu-north"));
        });
    }

    [Test]
    public void AutoInstanceIdRequestsGeneration()
    {
        using var provider = Bridge(new NameValueCollection
        {
            ["quartz.scheduler.instanceId"] = "AUTO",
        });

        Assert.That(Options<QuartzSchedulerOptions>(provider).GenerateInstanceId, Is.True);
    }

    [Test]
    public void SystemPropertyInstanceIdSelectsItsGenerator()
    {
        using var provider = Bridge(new NameValueCollection
        {
            ["quartz.scheduler.instanceId"] = "SYS_PROP",
        });

        Assert.Multiple(() =>
        {
            Assert.That(Options<QuartzSchedulerOptions>(provider).GenerateInstanceId, Is.True);
            Assert.That(provider.GetRequiredService<IInstanceIdGenerator>(),
                Is.InstanceOf<SystemPropertyInstanceIdGenerator>());
        });
    }

    [Test]
    public void PersistentJobStoreKeysMapOntoAdoOptions()
    {
        using var provider = Bridge(new NameValueCollection
        {
            ["quartz.jobStore.type"] = typeof(JobStoreTX).AssemblyQualifiedName,
            ["quartz.jobStore.dataSource"] = "primary",
            ["quartz.jobStore.tablePrefix"] = "QRTZ2_",
            ["quartz.jobStore.useProperties"] = "true",
            ["quartz.jobStore.clustered"] = "true",
            ["quartz.jobStore.clusterCheckinInterval"] = "10000",
            ["quartz.jobStore.misfireThreshold"] = "90000",
        });

        var options = Options<AdoJobStoreOptions>(provider);

        Assert.Multiple(() =>
        {
            Assert.That(options.DataSource, Is.EqualTo("primary"));
            Assert.That(options.TablePrefix, Is.EqualTo("QRTZ2_"));
            Assert.That(options.UseProperties, Is.True);
            Assert.That(options.Clustered, Is.True);
            Assert.That(options.UseDbLocks, Is.True, "clustering has always implied database locking");
            Assert.That(options.ClusterCheckinInterval, Is.EqualTo(TimeSpan.FromSeconds(10)));
            Assert.That(options.MisfireThreshold, Is.EqualTo(TimeSpan.FromSeconds(90)));
        });
    }

    [Test]
    public void InMemoryJobStoreKeepsItsOwnMisfireThreshold()
    {
        using var provider = Bridge(new NameValueCollection
        {
            ["quartz.jobStore.misfireThreshold"] = "30000",
        });

        Assert.That(Options<InMemoryJobStoreOptions>(provider).MisfireThreshold, Is.EqualTo(TimeSpan.FromSeconds(30)));
    }

    [Test]
    public void DataSourceKeysBecomeNamedOptions()
    {
        using var provider = Bridge(new NameValueCollection
        {
            ["quartz.dataSource.primary.provider"] = "SqlServer",
            ["quartz.dataSource.primary.connectionString"] = "Server=a",
            ["quartz.dataSource.reporting.provider"] = "Npgsql",
            ["quartz.dataSource.reporting.connectionStringName"] = "reportingDb",
        });

        var monitor = provider.GetRequiredService<IOptionsMonitor<DataSourceOptions>>();

        Assert.Multiple(() =>
        {
            Assert.That(monitor.Get("primary").Provider, Is.EqualTo("SqlServer"));
            Assert.That(monitor.Get("primary").ConnectionString, Is.EqualTo("Server=a"));
            Assert.That(monitor.Get("reporting").Provider, Is.EqualTo("Npgsql"));
            Assert.That(monitor.Get("reporting").ConnectionStringName, Is.EqualTo("reportingDb"));
        });
    }

    [Test]
    public void SerializerAliasesResolveToTheirTypes()
    {
        using var provider = Bridge(new NameValueCollection
        {
            ["quartz.serializer.type"] = "stj",
        });

        Assert.That(provider.GetRequiredService<IObjectSerializer>(),
            Is.InstanceOf<SystemTextJsonObjectSerializer>());
    }

    [Test]
    public void BinarySerializerIsRejectedWithAnActionableMessage()
    {
        var exception = Assert.Throws<SchedulerException>(() => Bridge(new NameValueCollection
        {
            ["quartz.serializer.type"] = "binary",
        }));

        Assert.That(exception!.Message, Does.Contain("Binary serialization is not supported"));
    }

    [Test]
    public void ConfiguredTypesBecomeRegistrations()
    {
        using var provider = Bridge(new NameValueCollection
        {
            ["quartz.threadPool.type"] = typeof(DedicatedThreadPool).AssemblyQualifiedName,
        });

        Assert.That(provider.GetRequiredService<IThreadPool>(), Is.InstanceOf<DedicatedThreadPool>());
    }

    [Test]
    public void NamedSchedulerKeysLandOnThatSchedulersOptionsOnly()
    {
        var services = new ServiceCollection();
        services.AddQuartzScheduler();
        QuartzPropertyBridge.Apply(services, new NameValueCollection
        {
            ["quartz.threadPool.maxConcurrency"] = "6",
        }, "reporting");

        using var provider = services.BuildServiceProvider();

        Assert.Multiple(() =>
        {
            Assert.That(Options<ThreadPoolOptions>(provider, "reporting").MaxConcurrency, Is.EqualTo(6));
            Assert.That(Options<ThreadPoolOptions>(provider).MaxConcurrency,
                Is.EqualTo(ThreadPoolOptions.DefaultMaxConcurrency),
                "the default scheduler must not inherit a named scheduler's properties");
        });
    }

    [Test]
    public void NamedSchedulerNameCannotBeOverriddenByProperties()
    {
        var services = new ServiceCollection();
        QuartzPropertyBridge.Apply(services, new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "something-else",
        }, "reporting");

        using var provider = services.BuildServiceProvider();

        Assert.That(Options<QuartzSchedulerOptions>(provider, "reporting").InstanceName, Is.EqualTo("reporting"));
    }
}
