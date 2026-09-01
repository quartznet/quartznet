using Microsoft.Extensions.Configuration;

using Quartz.Configuration;

namespace Quartz.Tests.Unit.Configuration;

public class QuartzConfigurationHelperTests
{
    [Test]
    public void SimpleOneLevel_ConvertsCorrectly()
    {
        var config = BuildConfig(new Dictionary<string, string> { { "Scheduler:InstanceId", "node-1" } });
        var result = QuartzConfigurationHelper.ToNameValueCollection(config);
        result["quartz.scheduler.instanceId"].Should().Be("node-1");
    }

    [Test]
    public void MultiLevel_ConvertsCorrectly()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Scheduler:InstanceId", "node-1" },
            { "ThreadPool:MaxConcurrency", "10" },
            { "JobStore:Type", "Quartz.Impl.RAMJobStore, Quartz" },
        });

        var result = QuartzConfigurationHelper.ToNameValueCollection(config);
        result["quartz.scheduler.instanceId"].Should().Be("node-1");
        result["quartz.jobStore.type"].Should().Be("Quartz.Impl.RAMJobStore, Quartz");
        result["quartz.threadPool.maxConcurrency"].Should().BeNull(
            "the section binds onto ThreadPoolOptions.MaxConcurrency, and a value flattened here as well "
            + "would be read a second time by the property bridge");
    }

    [Test]
    public void AKeyATypedOptionOwnsIsNotFlattenedTwice()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "ThreadPool:MaxConcurrency", "10" },
            { "ThreadPool:ThreadCount", "8" },
            { "ThreadPool:Type", "Quartz.Impl.DefaultThreadPool, Quartz" },
            { "ThreadPool:Marker", "configured" },
        });

        var result = QuartzConfigurationHelper.ToNameValueCollection(config);

        result["quartz.threadPool.maxConcurrency"].Should().BeNull();
        result["quartz.threadPool.threadCount"].Should().Be("8",
            "the legacy spelling is no property of the options type, so the bridge is its only reader");
        result["quartz.threadPool.type"].Should().Be("Quartz.Impl.DefaultThreadPool, Quartz",
            "the type key selects an implementation, which only the bridge can do");
        result["quartz.threadPool.marker"].Should().Be("configured",
            "a third-party pool's own settings are applied from the flat keys and have no typed home");
    }

    [Test]
    public void TheSchedulerKeysATypedOptionOwnsAreNotFlattenedEither()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Scheduler:InstanceName", "reporting" },
            { "Scheduler:MaxBatchSize", "9" },
            { "Scheduler:Context:environment", "staging" },
            { "Scheduler:InstanceId", "node-1" },
            { "Scheduler:IdleWaitTime", "00:00:07" },
            { "Context:key:legacy", "blue" },
        });

        var result = QuartzConfigurationHelper.ToNameValueCollection(config);

        result["quartz.scheduler.instanceName"].Should().BeNull(
            "the bridge reads that key back onto the very property the section binder just set");
        result["quartz.scheduler.maxBatchSize"].Should().BeNull(
            "the bridge's spelling is batchTriggerAcquisitionMaxCount, so this one is read by nobody — "
            + "and Validate rejects it by name, so it also made ToProperties() unusable");
        result["quartz.scheduler.context.environment"].Should().BeNull(
            "the bridge's spelling is quartz.context.key.*, so the whole subtree is read by nobody");

        result["quartz.scheduler.instanceId"].Should().Be("node-1",
            "AUTO and SYS_PROP select a generator rather than setting the id, which only the bridge does");
        result["quartz.scheduler.idleWaitTime"].Should().Be("00:00:07",
            "the value may be the legacy count of milliseconds, which the binder would read as days");
        result["quartz.context.key.legacy"].Should().Be("blue",
            "nothing typed binds Quartz:Context, so the bridge is the only reader it has");
    }

    [Test]
    public void AFlatKeyForATypedOptionIsPassedThroughUnchanged()
    {
        var config = BuildConfig(new Dictionary<string, string> { { "quartz.threadPool.maxConcurrency", "10" } });

        QuartzConfigurationHelper.ToNameValueCollection(config)["quartz.threadPool.maxConcurrency"]
            .Should().Be("10",
                "only the spelling this synthesizes is left out; a key written flat has no typed binding "
                + "to be read by, so dropping it would leave it read by nobody");
    }

    [Test]
    public void NamedSections_ConvertCorrectly()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "DataSource:default:Provider", "SqlServer" },
            { "Plugin:jobHistory:Type", "Quartz.Plugins.History.LoggingJobHistoryPlugin, Quartz.Plugins" },
        });

        var result = QuartzConfigurationHelper.ToNameValueCollection(config);
        result["quartz.dataSource.default.provider"].Should().Be("SqlServer");
        result["quartz.plugin.jobHistory.type"].Should().Be("Quartz.Plugins.History.LoggingJobHistoryPlugin, Quartz.Plugins");
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
            { "Scheduler:InstanceId", "node-1" },
            { "Schedule:Jobs:0:Name", "myJob" },
        });

        var result = QuartzConfigurationHelper.ToNameValueCollection(config);
        result["quartz.scheduler.instanceId"].Should().Be("node-1");
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
