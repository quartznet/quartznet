using System;
using System.Collections.Generic;
using System.Linq;

using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NUnit.Framework;

using Quartz.Simpl;

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

    [Test]
    public void AddQuartz_SchedulingSectionInJson_PopulatesSchedulingOptions()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Scheduling:OverWriteExistingData", "false" },
            { "Scheduling:IgnoreDuplicates", "true" },
            { "Scheduling:ScheduleTriggerRelativeToReplacedTrigger", "true" },
            { "Schedule:Jobs:0:Name", "testJob" },
            { "Schedule:Jobs:0:JobType", "Quartz.Job.NativeJob, Quartz.Jobs" },
            { "Schedule:Jobs:0:Durable", "true" },
            { "Schedule:Triggers:0:Name", "testTrigger" },
            { "Schedule:Triggers:0:JobName", "testJob" },
            { "Schedule:Triggers:0:Cron:Expression", "0 0 12 * * ?" }
        });

        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.Scheduling.OverWriteExistingData.Should().BeFalse();
        options.Scheduling.IgnoreDuplicates.Should().BeTrue();
        options.Scheduling.ScheduleTriggerRelativeToReplacedTrigger.Should().BeTrue();
        options.JobDetails.Should().HaveCount(1);
    }

    [Test]
    public void AddQuartz_SchedulingSectionOnly_WithoutScheduleSection_WorksFine()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Scheduler:InstanceName", "SchedulingOnly" },
            { "Scheduling:OverWriteExistingData", "false" },
            { "Scheduling:IgnoreDuplicates", "true" },
        });

        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.Scheduling.OverWriteExistingData.Should().BeFalse();
        options.Scheduling.IgnoreDuplicates.Should().BeTrue();
        options.JobDetails.Should().BeEmpty();
    }

    [Test]
    public void AddQuartz_NamedScheduler_WithSchedulingSection_PopulatesOptions()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Scheduling:OverWriteExistingData", "false" },
            { "Scheduling:IgnoreDuplicates", "true" },
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

        options.Scheduling.OverWriteExistingData.Should().BeFalse();
        options.Scheduling.IgnoreDuplicates.Should().BeTrue();
        options.JobDetails.Should().HaveCount(1);
    }

    [Test]
    public void AddQuartz_NamedScheduler_WithCustomTypeLoader_UsesConfiguredLoader()
    {
        // Use an alias that only AliasTypeLoadHelper can resolve
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Schedule:Jobs:0:Name", "customLoaderJob" },
            { "Schedule:Jobs:0:JobType", "MyApp.AliasedJob" },
            { "Schedule:Jobs:0:Durable", "true" },
            { "Schedule:Triggers:0:Name", "customLoaderTrigger" },
            { "Schedule:Triggers:0:JobName", "customLoaderJob" },
            { "Schedule:Triggers:0:Cron:Expression", "0 0 * * * ?" }
        });

        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz("CustomLoader", config, c =>
        {
            c.UseTypeLoader<AliasTypeLoadHelper>();
        });

        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsSnapshot<QuartzOptions> optionsSnapshot = provider.GetRequiredService<IOptionsSnapshot<QuartzOptions>>();
        QuartzOptions options = optionsSnapshot.Get("CustomLoader");

        // The custom type loader resolved "MyApp.AliasedJob" → NativeJob
        options.JobDetails.Should().HaveCount(1);
        options.JobDetails[0].Key.Name.Should().Be("customLoaderJob");
        options.JobDetails[0].JobType.Should().Be(typeof(Quartz.Job.NativeJob));
    }

    [Test]
    public void AddQuartz_NamedScheduler_WithoutCustomTypeLoader_CannotResolveAlias()
    {
        // Without the custom loader, "MyApp.AliasedJob" cannot be resolved
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Schedule:Jobs:0:Name", "failJob" },
            { "Schedule:Jobs:0:JobType", "MyApp.AliasedJob" },
            { "Schedule:Jobs:0:Durable", "true" },
            { "Schedule:Triggers:0:Name", "failTrigger" },
            { "Schedule:Triggers:0:JobName", "failJob" },
            { "Schedule:Triggers:0:Cron:Expression", "0 0 * * * ?" }
        });

        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz("NoCustomLoader", config);

        ServiceProvider provider = services.BuildServiceProvider();
        IOptionsSnapshot<QuartzOptions> optionsSnapshot = provider.GetRequiredService<IOptionsSnapshot<QuartzOptions>>();

        Action act = () => optionsSnapshot.Get("NoCustomLoader").ToString();
        act.Should().Throw<TypeLoadException>()
            .WithMessage("*MyApp.AliasedJob*");
    }

    [Test]
    public void AddQuartz_WithExecutionGroupInJson_PopulatesOptions()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Schedule:Jobs:0:Name", "groupJob" },
            { "Schedule:Jobs:0:JobType", "Quartz.Job.NativeJob, Quartz.Jobs" },
            { "Schedule:Jobs:0:Durable", "true" },
            { "Schedule:Triggers:0:Name", "groupTrigger" },
            { "Schedule:Triggers:0:JobName", "groupJob" },
            { "Schedule:Triggers:0:ExecutionGroup", "batch" },
            { "Schedule:Triggers:0:Cron:Expression", "0 0 * * * ?" }
        });

        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.Triggers.Should().HaveCount(1);
        Quartz.Impl.Triggers.AbstractTrigger trigger = (Quartz.Impl.Triggers.AbstractTrigger) options.Triggers[0];
        trigger.ExecutionGroup.Should().Be("batch");
    }

    [Test]
    public void AddQuartz_WithPreferredNodeAndExecutionGroup_PopulatesOptions()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Schedule:Jobs:0:Name", "affinityJob" },
            { "Schedule:Jobs:0:JobType", "Quartz.Job.NativeJob, Quartz.Jobs" },
            { "Schedule:Jobs:0:Durable", "true" },
            { "Schedule:Triggers:0:Name", "affinityTrigger" },
            { "Schedule:Triggers:0:JobName", "affinityJob" },
            { "Schedule:Triggers:0:PreferredNode", "node-1" },
            { "Schedule:Triggers:0:ExecutionGroup", "batch" },
            { "Schedule:Triggers:0:Cron:Expression", "0 0 * * * ?" }
        });

        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.Triggers.Should().HaveCount(1);
        Quartz.Impl.Triggers.AbstractTrigger trigger = (Quartz.Impl.Triggers.AbstractTrigger) options.Triggers[0];
        trigger.PreferredNode.Should().Be("node-1");
        trigger.ExecutionGroup.Should().Be("batch");
    }

    [Test]
    public void AddQuartz_WithAutoPin_PopulatesOptions()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Schedule:Jobs:0:Name", "autoPinJob" },
            { "Schedule:Jobs:0:JobType", "Quartz.Job.NativeJob, Quartz.Jobs" },
            { "Schedule:Jobs:0:Durable", "true" },
            { "Schedule:Triggers:0:Name", "autoPinTrigger" },
            { "Schedule:Triggers:0:JobName", "autoPinJob" },
            { "Schedule:Triggers:0:PreferredNode", "*" },
            { "Schedule:Triggers:0:Cron:Expression", "0 0 * * * ?" }
        });

        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        ServiceProvider provider = services.BuildServiceProvider();
        QuartzOptions options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.Triggers.Should().HaveCount(1);
        Quartz.Impl.Triggers.AbstractTrigger trigger = (Quartz.Impl.Triggers.AbstractTrigger) options.Triggers[0];
        trigger.PreferredNode.Should().Be("*");
    }


    private static IConfiguration BuildConfig(Dictionary<string, string> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    /// <summary>
    /// A type load helper that maps the alias "MyApp.AliasedJob" to <c>Quartz.Job.NativeJob</c>,
    /// proving that the custom loader (not the default) was used to resolve the job type.
    /// </summary>
    private sealed class AliasTypeLoadHelper : SimpleTypeLoadHelper
    {
        public override Type LoadType(string name)
        {
            if (name == "MyApp.AliasedJob")
            {
                return base.LoadType("Quartz.Job.NativeJob, Quartz.Jobs");
            }
            return base.LoadType(name);
        }
    }
}
