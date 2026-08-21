using System.Collections.Specialized;
using System.Data;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Configuration;

public class QuartzPropertyBridgeTest
{
    private static ServiceProvider Bridge(NameValueCollection properties, string schedulerName = null)
    {
        var services = new ServiceCollection();
        QuartzPropertyBridge.Apply(services, properties, schedulerName);
        services.AddQuartzScheduler(schedulerName);
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
            ["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = "8",
            ["quartz.scheduler.interruptJobsOnShutdown"] = "true",
        });

        var options = Options<QuartzSchedulerOptions>(provider);

        options.InstanceName.Should().Be("legacy");
        options.InstanceId.Should().Be("node-7");
        options.MaxBatchSize.Should().Be(8);
        options.ShutdownJobInterruption.Should().Be(ShutdownJobInterruption.WhenNotWaitingForJobs,
            "the legacy key only ever meant a shutdown that does not wait for jobs");
    }

    [Test]
    public void TheTwoInterruptOnShutdownKeysFoldOntoOneSetting()
    {
        (string, string, ShutdownJobInterruption)[] cases =
        [
            (null, null, ShutdownJobInterruption.Never),
            ("false", "false", ShutdownJobInterruption.Never),
            ("true", null, ShutdownJobInterruption.WhenNotWaitingForJobs),
            (null, "true", ShutdownJobInterruption.WhenWaitingForJobs),
            ("true", "true", ShutdownJobInterruption.Always),
            ("false", "true", ShutdownJobInterruption.WhenWaitingForJobs),
            ("true", "false", ShutdownJobInterruption.WhenNotWaitingForJobs)
        ];

        foreach ((string onShutdown, string withWait, ShutdownJobInterruption expected) in cases)
        {
            var properties = new NameValueCollection();
            if (onShutdown is not null)
            {
                properties["quartz.scheduler.interruptJobsOnShutdown"] = onShutdown;
            }

            if (withWait is not null)
            {
                properties["quartz.scheduler.interruptJobsOnShutdownWithWait"] = withWait;
            }

            using var provider = Bridge(properties);

            Options<QuartzSchedulerOptions>(provider).ShutdownJobInterruption.Should().Be(expected,
                $"interruptJobsOnShutdown={onShutdown ?? "unset"}, withWait={withWait ?? "unset"}");
        }
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

        options.IdleWaitTime.Should().Be(TimeSpan.FromSeconds(45));
        options.BatchTriggerAcquisitionFireAheadTimeWindow.Should().Be(TimeSpan.FromMilliseconds(2500));
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

        fromFlat.InstanceName.Should().Be(fromHierarchical.InstanceName);
        fromFlat.IdleWaitTime.Should().Be(fromHierarchical.IdleWaitTime);
        fromFlat.MaxBatchSize.Should().Be(fromHierarchical.MaxBatchSize);
        Options<ThreadPoolOptions>(flat).MaxConcurrency.Should().Be(Options<ThreadPoolOptions>(hierarchical).MaxConcurrency);
    }

    [Test]
    public void ThreadCountIsAcceptedAsAnAliasForMaxConcurrency()
    {
        using var provider = Bridge(new NameValueCollection
        {
            ["quartz.threadPool.threadCount"] = "8",
        });

        Options<ThreadPoolOptions>(provider).MaxConcurrency.Should().Be(8);
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

        options.Context["environment"].Should().Be("staging");
        options.Context["region"].Should().Be("eu-north");
    }

    [Test]
    public void AutoInstanceIdRequestsGeneration()
    {
        using var provider = Bridge(new NameValueCollection
        {
            ["quartz.scheduler.instanceId"] = "AUTO",
        });

        Options<QuartzSchedulerOptions>(provider).GenerateInstanceId.Should().BeTrue();
    }

    [Test]
    public void SystemPropertyInstanceIdSelectsItsGenerator()
    {
        using var provider = Bridge(new NameValueCollection
        {
            ["quartz.scheduler.instanceId"] = "SYS_PROP",
        });

        Options<QuartzSchedulerOptions>(provider).GenerateInstanceId.Should().BeTrue();
        provider.GetRequiredService<IInstanceIdGenerator>().Should().BeOfType<SystemPropertyInstanceIdGenerator>();
    }

    [Test]
    public void PersistentJobStoreKeysMapOntoAdoOptions()
    {
        using var provider = Bridge(new NameValueCollection
        {
            ["quartz.jobStore.type"] = typeof(LocalTransactionJobStore).AssemblyQualifiedName,
            ["quartz.jobStore.dataSource"] = "primary",
            ["quartz.jobStore.tablePrefix"] = "QRTZ2_",
            ["quartz.jobStore.useProperties"] = "true",
            ["quartz.jobStore.clustered"] = "true",
            ["quartz.jobStore.clusterCheckinInterval"] = "10000",
            ["quartz.jobStore.misfireThreshold"] = "90000",
        });

        var options = Options<AdoJobStoreOptions>(provider);

        options.DataSource.Should().Be("primary");
        options.TablePrefix.Should().Be("QRTZ2_");
        options.StoreJobDataAsStrings.Should().BeTrue(
            "quartz.jobStore.useProperties is unchanged; only the option it sets was renamed");
        options.UseDbLocks.Should().BeTrue("clustering has always implied database locking");
        options.MisfireThreshold.Should().Be(TimeSpan.FromSeconds(90));

        var clustering = Options<ClusteringOptions>(provider);

        clustering.Enabled.Should().BeTrue();
        clustering.CheckinInterval.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Test]
    public void TheSerializableIsolationFlagBecomesTheIsolationLevel()
    {
        using var provider = Bridge(new NameValueCollection
        {
            ["quartz.jobStore.dataSource"] = "primary",
            ["quartz.jobStore.txIsolationLevelSerializable"] = "true",
        });

        Options<AdoJobStoreOptions>(provider).TransactionIsolationLevel.Should().Be(IsolationLevel.Serializable);
    }

    [Test]
    public void TheSerializableIsolationFlagSetToFalseStillSaysNothing()
    {
        using var provider = Bridge(new NameValueCollection
        {
            ["quartz.jobStore.dataSource"] = "primary",
            ["quartz.jobStore.txIsolationLevelSerializable"] = "false",
        });

        Options<AdoJobStoreOptions>(provider).TransactionIsolationLevel.Should().BeNull(
            "the flag's 'false' was the absence of a choice, and SQLite's defaulting reads that absence — "
            + "translating it to an explicit ReadCommitted would stop SQLite getting serializable");
    }

    [Test]
    public void InMemoryJobStoreKeepsItsOwnMisfireThreshold()
    {
        using var provider = Bridge(new NameValueCollection
        {
            ["quartz.jobStore.misfireThreshold"] = "30000",
        });

        Options<InMemoryJobStoreOptions>(provider).MisfireThreshold.Should().Be(TimeSpan.FromSeconds(30));
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

        monitor.Get("primary").Provider.Should().Be("SqlServer");
        monitor.Get("primary").ConnectionString.Should().Be("Server=a");
        monitor.Get("reporting").Provider.Should().Be("Npgsql");
        monitor.Get("reporting").ConnectionStringName.Should().Be("reportingDb");
    }

    [Test]
    public void SerializerAliasesResolveToTheirTypes()
    {
        using var provider = Bridge(new NameValueCollection
        {
            ["quartz.serializer.type"] = "stj",
        });

        provider.GetRequiredService<IObjectSerializer>().Should().BeOfType<SystemTextJsonObjectSerializer>();
    }

    [Test]
    public void BinarySerializerIsRejectedWithAnActionableMessage()
    {
        var act = () => Bridge(new NameValueCollection
        {
            ["quartz.serializer.type"] = "binary",
        });

        act.Should().Throw<SchedulerException>()
            .WithMessage("*Binary serialization is not supported*");
    }

    [Test]
    public void ConfiguredTypesBecomeRegistrations()
    {
        using var provider = Bridge(new NameValueCollection
        {
            ["quartz.threadPool.type"] = typeof(DedicatedThreadPool).AssemblyQualifiedName,
        });

        provider.GetRequiredService<IThreadPool>().Should().BeOfType<DedicatedThreadPool>();
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
        services.AddQuartzScheduler("reporting");

        using var provider = services.BuildServiceProvider();

        Options<ThreadPoolOptions>(provider, "reporting").MaxConcurrency.Should().Be(6);
        Options<ThreadPoolOptions>(provider).MaxConcurrency.Should().Be(ThreadPoolOptions.DefaultMaxConcurrency, "the default scheduler must not inherit a named scheduler's properties");
    }

    [Test]
    public void NamedSchedulerNameCannotBeOverriddenByProperties()
    {
        var services = new ServiceCollection();
        QuartzPropertyBridge.Apply(services, new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = "something-else",
        }, "reporting");
        services.AddQuartzScheduler("reporting");

        using var provider = services.BuildServiceProvider();

        Options<QuartzSchedulerOptions>(provider, "reporting").InstanceName.Should().Be("reporting");
    }
}
