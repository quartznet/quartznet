using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Tests.Unit.Extensions.DependencyInjection;

public class QuartzConfigurationHelperTests
{
    [Test]
    public void SimpleOneLevel_ConvertsCorrectly()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string>
        {
            { "Scheduler:InstanceName", "My Scheduler" }
        });

        NameValueCollection result = QuartzConfigurationHelper.ToNameValueCollection(config);

        result["quartz.scheduler.instanceName"].Should().Be("My Scheduler");
    }

    [Test]
    public void MultiLevel_ConvertsCorrectly()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string>
        {
            { "Scheduler:InstanceName", "Test" },
            { "Scheduler:InstanceId", "AUTO" },
            { "ThreadPool:MaxConcurrency", "10" },
            { "JobStore:Type", "Quartz.Simpl.RAMJobStore, Quartz" },
            { "JobStore:DataSource", "default" }
        });

        NameValueCollection result = QuartzConfigurationHelper.ToNameValueCollection(config);

        result["quartz.scheduler.instanceName"].Should().Be("Test");
        result["quartz.scheduler.instanceId"].Should().Be("AUTO");
        result["quartz.threadPool.maxConcurrency"].Should().Be("10");
        result["quartz.jobStore.type"].Should().Be("Quartz.Simpl.RAMJobStore, Quartz");
        result["quartz.jobStore.dataSource"].Should().Be("default");
    }

    [Test]
    public void NamedSections_ConvertCorrectly()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string>
        {
            { "DataSource:default:Provider", "SqlServer" },
            { "DataSource:default:ConnectionString", "Server=localhost" },
            { "Plugin:jobHistory:Type", "Quartz.Plugin.History.LoggingJobHistoryPlugin, Quartz.Plugins" }
        });

        NameValueCollection result = QuartzConfigurationHelper.ToNameValueCollection(config);

        result["quartz.dataSource.default.provider"].Should().Be("SqlServer");
        result["quartz.dataSource.default.connectionString"].Should().Be("Server=localhost");
        result["quartz.plugin.jobHistory.type"].Should().Be("Quartz.Plugin.History.LoggingJobHistoryPlugin, Quartz.Plugins");
    }

    [Test]
    public void FlatKeys_PassThroughUnchanged()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string>
        {
            { "quartz.scheduler.instanceName", "Flat Key Scheduler" },
            { "quartz.threadPool.maxConcurrency", "5" }
        });

        NameValueCollection result = QuartzConfigurationHelper.ToNameValueCollection(config);

        result["quartz.scheduler.instanceName"].Should().Be("Flat Key Scheduler");
        result["quartz.threadPool.maxConcurrency"].Should().Be("5");
    }

    [Test]
    public void MixedFlatAndHierarchical_BothWork()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string>
        {
            { "quartz.scheduler.instanceId", "AUTO" },
            { "ThreadPool:MaxConcurrency", "10" }
        });

        NameValueCollection result = QuartzConfigurationHelper.ToNameValueCollection(config);

        result["quartz.scheduler.instanceId"].Should().Be("AUTO");
        result["quartz.threadPool.maxConcurrency"].Should().Be("10");
    }

    [Test]
    public void PascalCaseToCamelCase_AllSegments()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string>
        {
            { "Scheduler:BatchTriggerAcquisitionMaxCount", "5" }
        });

        NameValueCollection result = QuartzConfigurationHelper.ToNameValueCollection(config);

        result["quartz.scheduler.batchTriggerAcquisitionMaxCount"].Should().Be("5");
    }

    [Test]
    public void ScheduleSection_IsSkipped()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string>
        {
            { "Scheduler:InstanceName", "Test" },
            { "Schedule:Jobs:0:Name", "myJob" },
            { "Schedule:Triggers:0:Name", "myTrigger" }
        });

        NameValueCollection result = QuartzConfigurationHelper.ToNameValueCollection(config);

        result["quartz.scheduler.instanceName"].Should().Be("Test");
        result.Count.Should().Be(1);
    }

    [Test]
    public void EmptySection_ProducesEmptyCollection()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string>());

        NameValueCollection result = QuartzConfigurationHelper.ToNameValueCollection(config);

        result.Count.Should().Be(0);
    }

    [Test]
    public void NumericValues_BecomeStrings()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string>
        {
            { "ThreadPool:MaxConcurrency", "10" },
            { "Scheduler:IdleWaitTime", "30000" }
        });

        NameValueCollection result = QuartzConfigurationHelper.ToNameValueCollection(config);

        result["quartz.threadPool.maxConcurrency"].Should().Be("10");
        result["quartz.scheduler.idleWaitTime"].Should().Be("30000");
    }

    [Test]
    public void AlreadyLowerCase_NotChanged()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string>
        {
            { "serializer:type", "stj" }
        });

        NameValueCollection result = QuartzConfigurationHelper.ToNameValueCollection(config);

        result["quartz.serializer.type"].Should().Be("stj");
    }

    [Test]
    public void ANameKeepsItsCaseWhileAPropertyIsCamelCased()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string>
        {
            { "ExecutionLimit:Reports", "2" },
            { "ExecutionLimit:tenant:acme", "3" },
            { "Context:Key:ApiKey", "secret" },
            { "DataSource:MyDS:ConnectionString", "Server=nowhere" },
            { "Plugin:JobHistory:Type", "Quartz.Plugin.History.LoggingJobHistoryPlugin, Quartz.Plugins" },
            { "JobListener:Audit:Type", "MyApp.AuditListener, MyApp" },
            { "triggerlistener:Audit:Type", "MyApp.AuditListener, MyApp" },
            { "Scheduler:IdleWaitTime", "30000" },
        });

        // AllKeys, because the collection's own indexer ignores case and would hide the difference.
        QuartzConfigurationHelper.ToNameValueCollection(config).AllKeys.Should().BeEquivalentTo(
            new[]
            {
                "quartz.executionLimit.Reports",
                "quartz.executionLimit.tenant:acme",
                "quartz.context.key.ApiKey",
                "quartz.dataSource.MyDS.connectionString",
                "quartz.plugin.jobHistory.type",
                "quartz.jobListener.Audit.type",
                "quartz.triggerListener.Audit.type",
                "quartz.scheduler.idleWaitTime",
            },
            "a name is looked up ordinally, while a property is matched without regard to case; a plugin's name stays "
            + "camel-cased because the XML plugin persists jobs named after it; and the prefix is "
            + "spelled as StdSchedulerFactory spells it, because it matches the prefix ordinally");
    }

    [Test]
    public async Task AnExecutionGroupFromConfigurationKeepsItsName()
    {
        IScheduler scheduler = await SchedulerFrom(new Dictionary<string, string>
        {
            { "ExecutionLimit:Reports", "2" },
            { "ExecutionLimit:tenant:acme", "3" },
        });
        try
        {
            ExecutionLimits limits = await scheduler.GetExecutionLimits();

            limits.Should().ContainKey("Reports",
                    "groups are matched ordinally, so a limit on 'reports' leaves the group 'Reports' uncapped")
                .WhoseValue.Should().Be(2);
            limits.Should().ContainKey("tenant:acme",
                    "':' is the configuration path separator, so it is the separator the group was written with")
                .WhoseValue.Should().Be(3);
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task AContextKeyFromConfigurationKeepsItsCase()
    {
        IScheduler scheduler = await SchedulerFrom(new Dictionary<string, string>
        {
            { "Context:Key:ApiKey", "secret" },
        });
        try
        {
            scheduler.Context.GetString("ApiKey").Should().Be("secret",
                "a job reads the context by the key the configuration wrote, and the context compares keys ordinally");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task ADataSourceFromConfigurationIsKnownByTheNameItWasWrittenWith()
    {
        string name = "Reports" + Guid.NewGuid().ToString("N");
        IScheduler scheduler = await SchedulerFrom(new Dictionary<string, string>
        {
            { $"DataSource:{name}:ConnectionProvider:Type", typeof(NamedDbProvider).AssemblyQualifiedName },
        });
        try
        {
            DBConnectionManager.Instance.GetDbProvider(name).Should().BeOfType<NamedDbProvider>(
                "quartz.jobStore.dataSource names the data source as written, and the job store looks it up ordinally");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    [Test]
    public async Task AListenerKeepsItsNameAndAPluginKeepsItsCamelCasedName()
    {
        IScheduler scheduler = await SchedulerFrom(new Dictionary<string, string>
        {
            { "JobListener:Audit:Type", typeof(NamedJobListener).AssemblyQualifiedName },
            { "Plugin:Recorder:Type", typeof(NamedPlugin).AssemblyQualifiedName },
        });
        try
        {
            scheduler.ListenerManager.GetJobListener("Audit").Should().BeOfType<NamedJobListener>(
                "the listener manager finds a listener by its name, ordinally");
            scheduler.Context.GetString(NamedPlugin.ContextKey).Should().Be("recorder",
                "the XML plugin builds its persisted file-scan job keys from this name, so it stays as before");
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    /// <summary>
    /// The control for the tests above, which passes with or without them: a property segment is still
    /// camel-cased, and still reaches its reader.
    /// </summary>
    [Test]
    public async Task APropertySegmentIsStillCamelCasedAndStillRead()
    {
        IScheduler scheduler = await SchedulerFrom(new Dictionary<string, string>
        {
            { "ThreadPool:MaxConcurrency", "3" },
        });
        try
        {
            SchedulerMetaData metaData = await scheduler.GetMetaData();
            metaData.ThreadPoolSize.Should().Be(3);
        }
        finally
        {
            await scheduler.Shutdown();
        }
    }

    private static Task<IScheduler> SchedulerFrom(Dictionary<string, string> values)
    {
        values["Scheduler:InstanceName"] = "names-as-written-" + Guid.NewGuid().ToString("N");
        StdSchedulerFactory factory = new(QuartzConfigurationHelper.ToNameValueCollection(BuildConfig(values)));
        return factory.GetScheduler();
    }

    private static IConfiguration BuildConfig(Dictionary<string, string> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    public sealed class NamedDbProvider : IDbProvider
    {
        public void Initialize()
        {
        }

        public DbCommand CreateCommand() => throw new NotSupportedException();

        public DbConnection CreateConnection() => throw new NotSupportedException();

        public string ConnectionString { get; set; } = "";

        public DbMetadata Metadata { get; } = new();

        public void Shutdown()
        {
        }
    }

    public sealed class NamedJobListener : IJobListener
    {
        public string Name { get; set; } = "";

        public Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    public sealed class NamedPlugin : ISchedulerPlugin
    {
        public const string ContextKey = "NamedPlugin.Name";

        public Task Initialize(string pluginName, IScheduler scheduler, CancellationToken cancellationToken = default)
        {
            scheduler.Context.Put(ContextKey, pluginName);
            return Task.CompletedTask;
        }

        public Task Start(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task Shutdown(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
