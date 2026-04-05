using System;
using System.Collections.Generic;
using System.Linq;

using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NUnit.Framework;

namespace Quartz.Tests.Unit.Extensions.DependencyInjection;

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
            { "Schedule:Triggers:0:Cron:Expression", "0/30 * * * * ?" }
        });

        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options["quartz.scheduler.instanceName"].Should().Be("JsonTest");
        options.JobDetails.Should().HaveCount(1);
        options.JobDetails[0].Key.Name.Should().Be("testJob");
        options.Triggers.Should().HaveCount(1);
        options.Triggers[0].Key.Name.Should().Be("testTrigger");
        options.Triggers[0].Should().BeAssignableTo<ICronTrigger>();
    }

    [Test]
    public void AddQuartz_WithSimpleTriggerInJson_PopulatesOptions()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Schedule:Jobs:0:Name", "simpleJob" },
            { "Schedule:Jobs:0:JobType", "Quartz.Job.NativeJob, Quartz.Jobs" },
            { "Schedule:Jobs:0:Durable", "true" },
            { "Schedule:Triggers:0:Name", "simpleTrigger" },
            { "Schedule:Triggers:0:JobName", "simpleJob" },
            { "Schedule:Triggers:0:Simple:RepeatCount", "-1" },
            { "Schedule:Triggers:0:Simple:Interval", "00:00:10" }
        });

        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.JobDetails.Should().HaveCount(1);
        options.Triggers.Should().HaveCount(1);
        ISimpleTrigger simpleTrigger = (ISimpleTrigger) options.Triggers[0];
        simpleTrigger.RepeatCount.Should().Be(-1);
        simpleTrigger.RepeatInterval.Should().Be(TimeSpan.FromSeconds(10));
    }

    [Test]
    public void AddQuartz_WithJobDataMap_PopulatesDataMap()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Schedule:Jobs:0:Name", "dataJob" },
            { "Schedule:Jobs:0:JobType", "Quartz.Job.NativeJob, Quartz.Jobs" },
            { "Schedule:Jobs:0:Durable", "true" },
            { "Schedule:Jobs:0:JobDataMap:key1", "value1" },
            { "Schedule:Jobs:0:JobDataMap:key2", "value2" },
            { "Schedule:Triggers:0:Name", "dataTrigger" },
            { "Schedule:Triggers:0:JobName", "dataJob" },
            { "Schedule:Triggers:0:Cron:Expression", "0 0 12 * * ?" }
        });

        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.JobDetails[0].JobDataMap["key1"].Should().Be("value1");
        options.JobDetails[0].JobDataMap["key2"].Should().Be("value2");
    }

    [Test]
    public void AddQuartz_MissingTriggerScheduleType_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Schedule:Triggers:0:Name", "badTrigger" },
            { "Schedule:Triggers:0:JobName", "someJob" }
        });

        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        ServiceProvider provider = services.BuildServiceProvider();

        Action act = () => provider.GetRequiredService<IOptions<QuartzOptions>>().Value.ToString();
        act.Should().Throw<SchedulerConfigException>()
            .WithMessage("*must specify exactly one schedule type*");
    }

    [Test]
    public void AddQuartz_WithoutScheduleSection_WorksFine()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Scheduler:InstanceName", "NoSchedule" }
        });

        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options["quartz.scheduler.instanceName"].Should().Be("NoSchedule");
        options.JobDetails.Should().BeEmpty();
    }

    [Test]
    public void AddQuartz_NamedScheduler_WithJson()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "ThreadPool:MaxConcurrency", "5" },
            { "Schedule:Jobs:0:Name", "namedJob" },
            { "Schedule:Jobs:0:JobType", "Quartz.Job.NativeJob, Quartz.Jobs" },
            { "Schedule:Jobs:0:Durable", "true" },
            { "Schedule:Triggers:0:Name", "namedTrigger" },
            { "Schedule:Triggers:0:JobName", "namedJob" },
            { "Schedule:Triggers:0:Cron:Expression", "0 0 * * * ?" }
        });

        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz("TestScheduler", config);

        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsSnapshot<QuartzOptions> optionsSnapshot = provider.GetRequiredService<IOptionsSnapshot<QuartzOptions>>();
        QuartzOptions options = optionsSnapshot.Get("TestScheduler");

        options["quartz.threadPool.maxConcurrency"].Should().Be("5");
        options.JobDetails.Should().HaveCount(1);
        options.Triggers.Should().HaveCount(1);
    }

    [Test]
    public void AddQuartz_SchedulersSection_RegistersNamedSchedulers()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Schedulers:Primary:ThreadPool:MaxConcurrency", "10" },
            { "Schedulers:Primary:Schedule:Jobs:0:Name", "primaryJob" },
            { "Schedulers:Primary:Schedule:Jobs:0:JobType", "Quartz.Job.NativeJob, Quartz.Jobs" },
            { "Schedulers:Primary:Schedule:Jobs:0:Durable", "true" },
            { "Schedulers:Primary:Schedule:Triggers:0:Name", "primaryTrigger" },
            { "Schedulers:Primary:Schedule:Triggers:0:JobName", "primaryJob" },
            { "Schedulers:Primary:Schedule:Triggers:0:Cron:Expression", "0/10 * * * * ?" },
            { "Schedulers:Secondary:ThreadPool:MaxConcurrency", "5" },
        });

        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsSnapshot<QuartzOptions> optionsSnapshot = provider.GetRequiredService<IOptionsSnapshot<QuartzOptions>>();

        QuartzOptions primary = optionsSnapshot.Get("Primary");
        primary["quartz.threadPool.maxConcurrency"].Should().Be("10");
        primary.JobDetails.Should().HaveCount(1);
        primary.Triggers.Should().HaveCount(1);

        QuartzOptions secondary = optionsSnapshot.Get("Secondary");
        secondary["quartz.threadPool.maxConcurrency"].Should().Be("5");
    }

    [Test]
    public void AddQuartz_SchedulersAndDirectConfig_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Scheduler:InstanceName", "Default" },
            { "Schedulers:Named:ThreadPool:MaxConcurrency", "5" },
        });

        ServiceCollection services = new ServiceCollection();
        services.AddLogging();

        Action act = () => services.AddQuartz(config);
        act.Should().Throw<SchedulerConfigException>()
            .WithMessage("*both*Schedulers*");
    }

    [Test]
    public void AddQuartz_SchedulersOnly_NoError()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Schedulers:OnlyScheduler:ThreadPool:MaxConcurrency", "3" },
        });

        ServiceCollection services = new ServiceCollection();
        services.AddLogging();

        // Should not throw
        services.AddQuartz(config);

        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsSnapshot<QuartzOptions> optionsSnapshot = provider.GetRequiredService<IOptionsSnapshot<QuartzOptions>>();
        QuartzOptions options = optionsSnapshot.Get("OnlyScheduler");
        options["quartz.threadPool.maxConcurrency"].Should().Be("3");
    }

    private static IConfiguration BuildConfig(Dictionary<string, string> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
