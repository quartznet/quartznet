using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NUnit.Framework;

namespace Quartz.Tests.Unit.Configuration;

public class JsonSchedulingTests
{
    [Test]
    public void AddQuartz_WithCronTriggerInJson_PopulatesOptions()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Scheduler:InstanceName", "JsonTest" },
            { "Schedule:Jobs:0:Name", "testJob" },
            { "Schedule:Jobs:0:JobType", "Quartz.Job.NativeJob, Quartz.Jobs" },
            { "Schedule:Jobs:0:Durable", "true" },
            { "Schedule:Triggers:0:Name", "testTrigger" },
            { "Schedule:Triggers:0:JobName", "testJob" },
            { "Schedule:Triggers:0:Cron:Expression", "0/30 * * * * ?" },
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options["quartz.scheduler.instanceName"].Should().Be("JsonTest");
        options.JobDetails.Should().HaveCount(1);
        options.Triggers.Should().HaveCount(1);
        options.Triggers[0].Should().BeAssignableTo<ICronTrigger>();
    }

    [Test]
    public void AddQuartz_WithSimpleTrigger_PopulatesOptions()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Schedule:Jobs:0:Name", "simpleJob" },
            { "Schedule:Jobs:0:JobType", "Quartz.Job.NativeJob, Quartz.Jobs" },
            { "Schedule:Jobs:0:Durable", "true" },
            { "Schedule:Triggers:0:Name", "simpleTrigger" },
            { "Schedule:Triggers:0:JobName", "simpleJob" },
            { "Schedule:Triggers:0:Simple:RepeatCount", "-1" },
            { "Schedule:Triggers:0:Simple:Interval", "00:00:10" },
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;
        var trigger = (ISimpleTrigger) options.Triggers[0];
        trigger.RepeatCount.Should().Be(-1);
        trigger.RepeatInterval.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Test]
    public void AddQuartz_SchedulersSection_RegistersNamedSchedulers()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Schedulers:Primary:ThreadPool:MaxConcurrency", "10" },
            { "Schedulers:Secondary:ThreadPool:MaxConcurrency", "5" },
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        var provider = services.BuildServiceProvider();
        var snapshot = provider.GetRequiredService<IOptionsSnapshot<QuartzOptions>>();

        snapshot.Get("Primary")["quartz.threadPool.maxConcurrency"].Should().Be("10");
        snapshot.Get("Secondary")["quartz.threadPool.maxConcurrency"].Should().Be("5");
    }

    [Test]
    public void AddQuartz_SchedulersAndDirectConfig_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Scheduler:InstanceName", "Default" },
            { "Schedulers:Named:ThreadPool:MaxConcurrency", "5" },
        });

        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => services.AddQuartz(config);
        act.Should().Throw<SchedulerConfigException>().WithMessage("*both*Schedulers*");
    }

    [Test]
    public void AddQuartz_WithoutScheduleSection_WorksFine()
    {
        var config = BuildConfig(new Dictionary<string, string> { { "Scheduler:InstanceName", "NoSchedule" } });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;
        options["quartz.scheduler.instanceName"].Should().Be("NoSchedule");
        options.JobDetails.Should().BeEmpty();
    }

    private static IConfiguration BuildConfig(Dictionary<string, string> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
