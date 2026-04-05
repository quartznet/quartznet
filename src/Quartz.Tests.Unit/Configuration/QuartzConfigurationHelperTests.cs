using System.Collections.Specialized;

using FluentAssertions;

using Microsoft.Extensions.Configuration;

using NUnit.Framework;

using Quartz.Configuration;

namespace Quartz.Tests.Unit.Configuration;

public class QuartzConfigurationHelperTests
{
    [Test]
    public void SimpleOneLevel_ConvertsCorrectly()
    {
        var config = BuildConfig(new Dictionary<string, string> { { "Scheduler:InstanceName", "My Scheduler" } });
        var result = QuartzConfigurationHelper.ToNameValueCollection(config);
        result["quartz.scheduler.instanceName"].Should().Be("My Scheduler");
    }

    [Test]
    public void MultiLevel_ConvertsCorrectly()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Scheduler:InstanceName", "Test" },
            { "ThreadPool:MaxConcurrency", "10" },
            { "JobStore:Type", "Quartz.Simpl.RAMJobStore, Quartz" },
        });

        var result = QuartzConfigurationHelper.ToNameValueCollection(config);
        result["quartz.scheduler.instanceName"].Should().Be("Test");
        result["quartz.threadPool.maxConcurrency"].Should().Be("10");
        result["quartz.jobStore.type"].Should().Be("Quartz.Simpl.RAMJobStore, Quartz");
    }

    [Test]
    public void NamedSections_ConvertCorrectly()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "DataSource:default:Provider", "SqlServer" },
            { "Plugin:jobHistory:Type", "Quartz.Plugin.History.LoggingJobHistoryPlugin, Quartz.Plugins" },
        });

        var result = QuartzConfigurationHelper.ToNameValueCollection(config);
        result["quartz.dataSource.default.provider"].Should().Be("SqlServer");
        result["quartz.plugin.jobHistory.type"].Should().Be("Quartz.Plugin.History.LoggingJobHistoryPlugin, Quartz.Plugins");
    }

    [Test]
    public void FlatKeys_PassThroughUnchanged()
    {
        var config = BuildConfig(new Dictionary<string, string> { { "quartz.scheduler.instanceName", "Flat" } });
        var result = QuartzConfigurationHelper.ToNameValueCollection(config);
        result["quartz.scheduler.instanceName"].Should().Be("Flat");
    }

    [Test]
    public void ScheduleSection_IsSkipped()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Scheduler:InstanceName", "Test" },
            { "Schedule:Jobs:0:Name", "myJob" },
        });

        var result = QuartzConfigurationHelper.ToNameValueCollection(config);
        result["quartz.scheduler.instanceName"].Should().Be("Test");
        result.Count.Should().Be(1);
    }

    [Test]
    public void EmptySection_ProducesEmptyCollection()
    {
        var config = BuildConfig(new Dictionary<string, string>());
        QuartzConfigurationHelper.ToNameValueCollection(config).Count.Should().Be(0);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
