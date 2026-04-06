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
            { "Schedule:Triggers:0:Cron:Expression", "0 0 12 * * ?" },
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

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

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.Scheduling.OverWriteExistingData.Should().BeFalse();
        options.Scheduling.IgnoreDuplicates.Should().BeTrue();
        options.JobDetails.Should().BeEmpty();
    }

    [Test]
    public void AddQuartz_NamedScheduler_WithCustomTypeLoader_UsesConfiguredLoader()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Schedule:Jobs:0:Name", "customLoaderJob" },
            { "Schedule:Jobs:0:JobType", "MyApp.AliasedJob" },
            { "Schedule:Jobs:0:Durable", "true" },
            { "Schedule:Triggers:0:Name", "customLoaderTrigger" },
            { "Schedule:Triggers:0:JobName", "customLoaderJob" },
            { "Schedule:Triggers:0:Cron:Expression", "0 0 * * * ?" },
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz("CustomLoader", config, c =>
        {
            c.UseTypeLoader<AliasTypeLoadHelper>();
        });

        var provider = services.BuildServiceProvider();
        var snapshot = provider.GetRequiredService<IOptionsSnapshot<QuartzOptions>>();
        var options = snapshot.Get("CustomLoader");

        options.JobDetails.Should().HaveCount(1);
        options.JobDetails[0].Key.Name.Should().Be("customLoaderJob");
        options.JobDetails[0].JobType.FullName.Should().Contain("Quartz.Job.NativeJob");
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
            { "Schedule:Triggers:0:Cron:Expression", "0 0 * * * ?" },
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.Triggers.Should().HaveCount(1);
        var trigger = (Quartz.Impl.Triggers.AbstractTrigger) options.Triggers[0];
        trigger.ExecutionGroup.Should().Be("batch");
    }

    private static IConfiguration BuildConfig(Dictionary<string, string> values)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>
    /// A type load helper that maps the alias "MyApp.AliasedJob" to NativeJob,
    /// proving the custom loader was used instead of the default.
    /// </summary>
    private sealed class AliasTypeLoadHelper : Quartz.Spi.ITypeLoadHelper
    {
        private readonly Quartz.Simpl.SimpleTypeLoadHelper inner = new();

        public void Initialize()
        {
            inner.Initialize();
        }

        public Type LoadType(string name)
        {
            if (name == "MyApp.AliasedJob")
            {
                return inner.LoadType("Quartz.Job.NativeJob, Quartz.Jobs");
            }
            return inner.LoadType(name);
        }
    }
}
