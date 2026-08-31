using Microsoft.Extensions.Configuration;

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
            { "JobStore:Type", "Quartz.Impl.RAMJobStore, Quartz" },
        });

        var result = QuartzConfigurationHelper.ToNameValueCollection(config);
        result["quartz.scheduler.instanceName"].Should().Be("Test");
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
