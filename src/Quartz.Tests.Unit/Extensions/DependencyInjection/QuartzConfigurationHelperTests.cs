using System.Collections.Generic;
using System.Collections.Specialized;

using FluentAssertions;

using Microsoft.Extensions.Configuration;

using NUnit.Framework;

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

    private static IConfiguration BuildConfig(Dictionary<string, string> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
