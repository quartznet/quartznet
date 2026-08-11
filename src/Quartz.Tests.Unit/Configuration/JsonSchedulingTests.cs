
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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
            { "Schedule:Jobs:0:JobType", "Quartz.Jobs.NativeJob, Quartz.Jobs" },
            { "Schedule:Jobs:0:Durable", "true" },
            { "Schedule:Triggers:0:Name", "testTrigger" },
            { "Schedule:Triggers:0:JobName", "testJob" },
            { "Schedule:Triggers:0:Cron:Expression", "0/30 * * * * ?" },
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value
            .InstanceName.Should().Be("JsonTest");
        provider.ScheduledJobs().Should().HaveCount(1);
        provider.ScheduledTriggers().Should().HaveCount(1);
        provider.ScheduledTriggers()[0].Should().BeAssignableTo<ICronTrigger>();
    }

    [Test]
    public void AddQuartz_WithSimpleTrigger_PopulatesOptions()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Schedule:Jobs:0:Name", "simpleJob" },
            { "Schedule:Jobs:0:JobType", "Quartz.Jobs.NativeJob, Quartz.Jobs" },
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
        var trigger = (ISimpleTrigger) provider.ScheduledTriggers()[0];
        trigger.RepeatCount.Should().Be(-1);
        trigger.RepeatInterval.Should().Be(TimeSpan.FromSeconds(10));
    }

    [TestCase("DoNothing", 2)]
    [TestCase("FireOnceNow", 1)]
    [TestCase("FireAndProceed", 1)]
    [TestCase("IgnoreMisfirePolicy", -1)]
    // A simple trigger's name on a cron trigger. It resolved before the per-family maps existed and
    // still does - to 2, DoNothing - but the resolver now says so instead of leaving it to be
    // discovered in production.
    [TestCase("RescheduleNowWithExistingRepeatCount", 2)]
    public void AddQuartz_CronTriggerMisfireInstruction_IsReadPerFamily(string name, int expected)
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Schedule:Jobs:0:Name", "misfireJob" },
            { "Schedule:Jobs:0:JobType", "Quartz.Job.NativeJob, Quartz.Jobs" },
            { "Schedule:Jobs:0:Durable", "true" },
            { "Schedule:Triggers:0:Name", "misfireTrigger" },
            { "Schedule:Triggers:0:JobName", "misfireJob" },
            { "Schedule:Triggers:0:Cron:Expression", "0/30 * * * * ?" },
            { "Schedule:Triggers:0:Cron:MisfireInstruction", name },
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        var provider = services.BuildServiceProvider();

        provider.ScheduledTriggers()[0].MisfireInstructionCode.Should().Be(expected);
    }

    [Test]
    public void AddQuartz_MisfireInstructionOfAnotherFamilyWithNoCounterpart_IsRejected()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Schedule:Jobs:0:Name", "misfireJob" },
            { "Schedule:Jobs:0:JobType", "Quartz.Job.NativeJob, Quartz.Jobs" },
            { "Schedule:Jobs:0:Durable", "true" },
            { "Schedule:Triggers:0:Name", "misfireTrigger" },
            { "Schedule:Triggers:0:JobName", "misfireJob" },
            { "Schedule:Triggers:0:Cron:Expression", "0/30 * * * * ?" },
            { "Schedule:Triggers:0:Cron:MisfireInstruction", "RescheduleNextWithRemainingCount" },
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        var provider = services.BuildServiceProvider();

        Action act = () => provider.ScheduledTriggers();

        act.Should().Throw<SchedulerConfigException>().WithMessage("*RescheduleNextWithRemainingCount*cron*");
    }

    [Test]
    public void AddQuartzSchedulers_SchedulersSection_RegistersNamedSchedulers()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Schedulers:Primary:ThreadPool:MaxConcurrency", "10" },
            { "Schedulers:Secondary:ThreadPool:MaxConcurrency", "5" },
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartzSchedulers(config);

        var provider = services.BuildServiceProvider();
        var threadPool = provider.GetRequiredService<IOptionsSnapshot<ThreadPoolOptions>>();

        threadPool.Get("Primary").MaxConcurrency.Should().Be(10);
        threadPool.Get("Secondary").MaxConcurrency.Should().Be(5);
    }

    [Test]
    public void AddQuartz_NamedScheduler_WithRootSectionContainingSchedulers_ResolvesSubsection()
    {
        // Reproduces #3106: passing the root "Quartz" section (which holds the scheduler under
        // "Schedulers:{name}") to the named overload should resolve down to that scheduler's section.
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Schedulers:LocalScheduler:ThreadPool:MaxConcurrency", "7" },
            { "Schedulers:LocalScheduler:Schedule:Jobs:0:Name", "rootJob" },
            { "Schedulers:LocalScheduler:Schedule:Jobs:0:JobType", "Quartz.Jobs.NativeJob, Quartz.Jobs" },
            { "Schedulers:LocalScheduler:Schedule:Jobs:0:Durable", "true" },
            { "Schedulers:LocalScheduler:Schedule:Triggers:0:Name", "rootTrigger" },
            { "Schedulers:LocalScheduler:Schedule:Triggers:0:JobName", "rootJob" },
            { "Schedulers:LocalScheduler:Schedule:Triggers:0:Cron:Expression", "0 0 * * * ?" },
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz("LocalScheduler", config);

        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptionsSnapshot<ThreadPoolOptions>>()
            .Get("LocalScheduler").MaxConcurrency.Should().Be(7);
        provider.ScheduledJobs("LocalScheduler").Should().HaveCount(1);
        provider.ScheduledJobs("LocalScheduler")[0].Key.Name.Should().Be("rootJob");
        provider.ScheduledTriggers("LocalScheduler").Should().HaveCount(1);
    }

    [Test]
    public void AddQuartzSchedulers_SchedulersAndDirectConfig_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Scheduler:InstanceName", "Default" },
            { "Schedulers:Named:ThreadPool:MaxConcurrency", "5" },
        });

        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => services.AddQuartzSchedulers(config);
        act.Should().Throw<SchedulerConfigException>().WithMessage("*both*Schedulers*");
    }

    [Test]
    public void AddQuartzSchedulers_SchedulersWithTopLevelSchedule_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Schedulers:Primary:ThreadPool:MaxConcurrency", "5" },
            { "Schedule:Jobs:0:Name", "strayJob" },
            { "Schedule:Jobs:0:JobType", "Quartz.Jobs.NativeJob, Quartz.Jobs" },
        });

        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => services.AddQuartzSchedulers(config);
        act.Should().Throw<SchedulerConfigException>().WithMessage("*top-level*Schedule*");
    }

    [Test]
    public void AddQuartz_WithASchedulersSection_PointsAtAddQuartzSchedulers()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Schedulers:Primary:ThreadPool:MaxConcurrency", "5" },
        });

        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => services.AddQuartz(config);
        act.Should().Throw<SchedulerConfigException>().WithMessage("*AddQuartzSchedulers*",
            "one call registering an unknown number of schedulers depending on the shape of a file is "
            + "two methods wearing one name");
    }

    [Test]
    public void AddQuartzSchedulers_WithoutASchedulersSection_PointsAtAddQuartz()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "ThreadPool:MaxConcurrency", "5" },
        });

        var services = new ServiceCollection();
        services.AddLogging();

        var act = () => services.AddQuartzSchedulers(config);
        act.Should().Throw<SchedulerConfigException>().WithMessage("*AddQuartz(configuration)*");
    }

    [Test]
    public void AddQuartz_WithoutScheduleSection_WorksFine()
    {
        var config = BuildConfig(new Dictionary<string, string> { { "Scheduler:InstanceName", "NoSchedule" } });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<QuartzSchedulerOptions>>().Value
            .InstanceName.Should().Be("NoSchedule");
        provider.ScheduledJobs().Should().BeEmpty();
    }

    [Test]
    public void AddQuartz_SchedulingSectionInJson_PopulatesSchedulingOptions()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Scheduling:OverwriteExistingData", "false" },
            { "Scheduling:IgnoreDuplicates", "true" },
            { "Scheduling:ScheduleTriggerRelativeToReplacedTrigger", "true" },
            { "Schedule:Jobs:0:Name", "testJob" },
            { "Schedule:Jobs:0:JobType", "Quartz.Jobs.NativeJob, Quartz.Jobs" },
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

        options.Scheduling.OverwriteExistingData.Should().BeFalse();
        options.Scheduling.IgnoreDuplicates.Should().BeTrue();
        options.Scheduling.ScheduleTriggerRelativeToReplacedTrigger.Should().BeTrue();
        provider.ScheduledJobs().Should().HaveCount(1);
    }

    [Test]
    public void AddQuartz_SchedulingSectionOnly_WithoutScheduleSection_WorksFine()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Scheduler:InstanceName", "SchedulingOnly" },
            { "Scheduling:OverwriteExistingData", "false" },
            { "Scheduling:IgnoreDuplicates", "true" },
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(config);

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.Scheduling.OverwriteExistingData.Should().BeFalse();
        options.Scheduling.IgnoreDuplicates.Should().BeTrue();
        provider.ScheduledJobs().Should().BeEmpty();
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
        var jobs = provider.ScheduledJobs("CustomLoader");

        jobs.Should().HaveCount(1);
        jobs[0].Key.Name.Should().Be("customLoaderJob");
        jobs[0].JobType.FullName.Should().Contain("Quartz.Jobs.NativeJob");
    }

    [Test]
    public void AddQuartz_WithExecutionGroupInJson_PopulatesOptions()
    {
        var config = BuildConfig(new Dictionary<string, string>
        {
            { "Schedule:Jobs:0:Name", "groupJob" },
            { "Schedule:Jobs:0:JobType", "Quartz.Jobs.NativeJob, Quartz.Jobs" },
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
        var triggers = provider.ScheduledTriggers();

        triggers.Should().HaveCount(1);
        var trigger = (Quartz.Impl.Triggers.TriggerBase) triggers[0];
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
    private sealed class AliasTypeLoadHelper : Quartz.Extensibility.ITypeLoadHelper
    {
        private readonly Quartz.Impl.SimpleTypeLoadHelper inner = new();

        public Type LoadType(string name)
        {
            if (name == "MyApp.AliasedJob")
            {
                return inner.LoadType("Quartz.Jobs.NativeJob, Quartz.Jobs");
            }
            return inner.LoadType(name);
        }
    }
}
