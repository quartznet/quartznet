using System.Collections.Specialized;

using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Extensions.DependencyInjection;

[NonParallelizable]
public sealed class MultipleSchedulerTests
{
    [Test]
    public void NamedSchedulers_ShouldHaveIsolatedOptions()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz("Scheduler1", q =>
        {
            q.AddJob<TestJobA>(j => j.WithIdentity("jobA", "group1"));
            q.AddTrigger(t => t.ForJob("jobA", "group1").WithIdentity("triggerA").StartNow());
        });

        services.AddQuartz("Scheduler2", q =>
        {
            q.AddJob<TestJobB>(j => j.WithIdentity("jobB", "group2"));
            q.AddTrigger(t => t.ForJob("jobB", "group2").WithIdentity("triggerB").StartNow());
        });

        using var provider = services.BuildServiceProvider();
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<QuartzOptions>>();

        var options1 = optionsMonitor.Get("Scheduler1");
        var options2 = optionsMonitor.Get("Scheduler2");

        options1.JobDetails.Should().HaveCount(1);
        options1.JobDetails[0].Key.Name.Should().Be("jobA");
        options1.Triggers.Should().HaveCount(1);
        options1.Triggers[0].Key.Name.Should().Be("triggerA");
        options1[StdSchedulerFactory.PropertySchedulerInstanceName].Should().Be("Scheduler1");

        options2.JobDetails.Should().HaveCount(1);
        options2.JobDetails[0].Key.Name.Should().Be("jobB");
        options2.Triggers.Should().HaveCount(1);
        options2.Triggers[0].Key.Name.Should().Be("triggerB");
        options2[StdSchedulerFactory.PropertySchedulerInstanceName].Should().Be("Scheduler2");
    }

    [Test]
    public void NamedSchedulerJobs_ShouldNotLeakToDefaultOptions()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz("Named", q =>
        {
            q.AddJob<TestJobA>(j => j.WithIdentity("namedJob"));
            q.AddTrigger(t => t.ForJob("namedJob").WithIdentity("namedTrigger").StartNow());
        });

        using var provider = services.BuildServiceProvider();
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<QuartzOptions>>();

        var defaultOptions = optionsMonitor.Get(Options.DefaultName);
        defaultOptions.JobDetails.Should().BeEmpty();
        defaultOptions.Triggers.Should().BeEmpty();
    }

    [Test]
    public void SchedulerNameRegistry_ShouldTrackAllNamedSchedulers()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz("Alpha", q => { });
        services.AddQuartz("Beta", q => { });

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<SchedulerNameRegistry>();

        registry.Names.Should().HaveCount(2);
        registry.Names.Should().Contain("Alpha");
        registry.Names.Should().Contain("Beta");
    }

    [Test]
    public void NamedSchedulerListeners_ShouldBeIsolated()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz("Scheduler1", q =>
        {
            q.AddJobListener<TestJobListenerA>();
            q.AddTriggerListener<TestTriggerListenerA>();
            q.AddSchedulerListener<TestSchedulerListenerA>();
        });

        services.AddQuartz("Scheduler2", q =>
        {
            q.AddJobListener<TestJobListenerB>();
        });

        using var provider = services.BuildServiceProvider();

        // Named scheduler listeners should NOT be registered as flat IJobListener/ITriggerListener/ISchedulerListener
        provider.GetServices<IJobListener>().Should().BeEmpty("named scheduler listeners should not pollute the global DI pool");
        provider.GetServices<ITriggerListener>().Should().BeEmpty();
        provider.GetServices<ISchedulerListener>().Should().BeEmpty();

        // But their configurations should be tagged correctly
        var jobListenerConfigs = provider.GetServices<JobListenerConfiguration>().ToList();
        jobListenerConfigs.Should().HaveCount(2);
        jobListenerConfigs.Should().Contain(c => c.OptionsName == "Scheduler1" && c.ListenerType == typeof(TestJobListenerA));
        jobListenerConfigs.Should().Contain(c => c.OptionsName == "Scheduler2" && c.ListenerType == typeof(TestJobListenerB));

        provider.GetServices<TriggerListenerConfiguration>().Should().ContainSingle(c => c.OptionsName == "Scheduler1");
        provider.GetServices<SchedulerListenerConfiguration>().Should().ContainSingle(c => c.OptionsName == "Scheduler1");
    }

    [Test]
    public void NamedSchedulerCalendars_ShouldBeIsolated()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz("Scheduler1", q =>
        {
            q.AddCalendar("cal1", new Quartz.Impl.Calendar.BaseCalendar(), replace: true, updateTriggers: false);
        });

        services.AddQuartz("Scheduler2", q =>
        {
            q.AddCalendar("cal2", new Quartz.Impl.Calendar.BaseCalendar(), replace: false, updateTriggers: true);
        });

        using var provider = services.BuildServiceProvider();
        var calConfigs = provider.GetServices<CalendarConfiguration>().ToList();

        calConfigs.Should().HaveCount(2);
        calConfigs.Should().Contain(c => c.OptionsName == "Scheduler1" && c.Name == "cal1");
        calConfigs.Should().Contain(c => c.OptionsName == "Scheduler2" && c.Name == "cal2");
    }

    [Test]
    public void DefaultAddQuartz_ShouldContinueWorkingUnchanged()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz(q =>
        {
            q.AddJob<TestJobA>(j => j.WithIdentity("defaultJob"));
            q.AddTrigger(t => t.ForJob("defaultJob").WithIdentity("defaultTrigger").StartNow());
        });

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        options.JobDetails.Should().HaveCount(1);
        options.JobDetails[0].Key.Name.Should().Be("defaultJob");
        options.Triggers.Should().HaveCount(1);

        provider.GetService<ISchedulerFactory>().Should().NotBeNull();
    }

    [Test]
    public void MixedDefaultAndNamed_ShouldCoexist()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz(q =>
        {
            q.AddJob<TestJobA>(j => j.WithIdentity("defaultJob"));
            q.AddTrigger(t => t.ForJob("defaultJob").WithIdentity("defaultTrigger").StartNow());
        });

        services.AddQuartz("Named1", q =>
        {
            q.AddJob<TestJobB>(j => j.WithIdentity("namedJob"));
            q.AddTrigger(t => t.ForJob("namedJob").WithIdentity("namedTrigger").StartNow());
        });

        using var provider = services.BuildServiceProvider();
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<QuartzOptions>>();

        optionsMonitor.Get(Options.DefaultName).JobDetails.Should().HaveCount(1);
        optionsMonitor.Get(Options.DefaultName).JobDetails[0].Key.Name.Should().Be("defaultJob");

        optionsMonitor.Get("Named1").JobDetails.Should().HaveCount(1);
        optionsMonitor.Get("Named1").JobDetails[0].Key.Name.Should().Be("namedJob");

        provider.GetService<ISchedulerFactory>().Should().NotBeNull();

        var registry = provider.GetRequiredService<SchedulerNameRegistry>();
        registry.Names.Should().ContainSingle().Which.Should().Be("Named1");
    }

    [Test]
    public void OnlyNamedSchedulers_ShouldNotRegisterDefaultFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz("Named1", q => { });

        using var provider = services.BuildServiceProvider();
        provider.GetService<ISchedulerFactory>().Should().BeNull();
    }

    [Test]
    public void AddQuartz_WithEmptyName_ShouldThrow()
    {
        var services = new ServiceCollection();

        var act = () => services.AddQuartz("", q => { });
        act.Should().Throw<ArgumentException>();

        var act2 = () => services.AddQuartz("  ", q => { });
        act2.Should().Throw<ArgumentException>();
    }

    [Test]
    public void AddQuartz_WithDuplicateName_ShouldThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz("Duplicate", q => { });

        Action act = () => services.AddQuartz("Duplicate", q => { });
        act.Should().Throw<ArgumentException>().WithMessage("*already been registered*");
    }

    [Test]
    public void ScheduleJob_WithNamedScheduler_ShouldWork()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz("Named", q =>
        {
            q.ScheduleJob<TestJobA>(
                trigger => trigger
                    .WithIdentity("scheduledTrigger")
                    .StartNow()
                    .WithSimpleSchedule(x => x.WithIntervalInSeconds(10).RepeatForever()),
                job => job.WithIdentity("scheduledJob"));
        });

        using var provider = services.BuildServiceProvider();
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<QuartzOptions>>();
        var namedOptions = optionsMonitor.Get("Named");

        namedOptions.JobDetails.Should().HaveCount(1);
        namedOptions.JobDetails[0].Key.Name.Should().Be("scheduledJob");
        namedOptions.Triggers.Should().HaveCount(1);
        namedOptions.Triggers[0].Key.Name.Should().Be("scheduledTrigger");

        optionsMonitor.Get(Options.DefaultName).JobDetails.Should().BeEmpty();
    }

    [Test]
    public void SchedulerName_SetterOnNamedConfigurator_ShouldThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        Action act = () => services.AddQuartz("MyName", q =>
        {
            q.SchedulerName = "Other";
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("*cannot be changed*");
    }

    [Test]
    public void AddQuartzHostedService_WithOnlyNamedSchedulers_ShouldNotRegisterDefaultHostedService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz("Named1", q => { });
        services.AddQuartzHostedService();

        var hostedServices = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
        hostedServices.Should().Contain(d => d.ImplementationType == typeof(NamedSchedulerHostedService));
        hostedServices.Should().NotContain(d => d.ImplementationType == typeof(QuartzHostedService));
    }

    [Test]
    public void AddQuartzHostedService_WithMixed_ShouldRegisterBothHostedServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz(q => { });
        services.AddQuartz("Named1", q => { });
        services.AddQuartzHostedService();

        var hostedServices = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
        hostedServices.Should().Contain(d => d.ImplementationType == typeof(QuartzHostedService));
        hostedServices.Should().Contain(d => d.ImplementationType == typeof(NamedSchedulerHostedService));
    }

    [Test]
    public void NamedScheduler_WithProperties_ShouldForceSchedulerName()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        var properties = new NameValueCollection
        {
            { "quartz.scheduler.instanceName", "WillBeOverridden" }
        };

        services.AddQuartz("MyScheduler", properties, q => { });

        using var provider = services.BuildServiceProvider();
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<QuartzOptions>>();
        var options = optionsMonitor.Get("MyScheduler");

        options[StdSchedulerFactory.PropertySchedulerInstanceName].Should().Be("MyScheduler");
    }

    [Test]
    public void AddQuartzHostedService_WithoutAnyAddQuartz_ShouldNotThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartzHostedService();

        var hostedServices = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
        hostedServices.Should().HaveCount(1);
        hostedServices[0].ImplementationType.Should().Be(typeof(NamedSchedulerHostedService));
    }

    [Test]
    public void AddQuartz_WithNullProperties_ShouldThrow()
    {
        var services = new ServiceCollection();

        var act = () => services.AddQuartz("Test", (NameValueCollection) null!, q => { });
        act.Should().Throw<ArgumentNullException>();
    }

    #region Test helpers

    private sealed class TestJobA : IJob
    {
        public ValueTask Execute(IJobExecutionContext context) => default;
    }

    private sealed class TestJobB : IJob
    {
        public ValueTask Execute(IJobExecutionContext context) => default;
    }

    private sealed class TestJobListenerA : IJobListener
    {
        public string Name => nameof(TestJobListenerA);
        public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
        public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException, CancellationToken cancellationToken = default) => default;
    }

    private sealed class TestJobListenerB : IJobListener
    {
        public string Name => nameof(TestJobListenerB);
        public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
        public ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
        public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException, CancellationToken cancellationToken = default) => default;
    }

    private sealed class TestTriggerListenerA : ITriggerListener
    {
        public string Name => nameof(TestTriggerListenerA);
        public ValueTask TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
        public ValueTask<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default) => new(false);
        public ValueTask TriggerMisfired(ITrigger trigger, CancellationToken cancellationToken = default) => default;
        public ValueTask TriggerComplete(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default) => default;
    }

    private sealed class TestSchedulerListenerA : ISchedulerListener
    {
        public ValueTask JobScheduled(ITrigger trigger, CancellationToken cancellationToken = default) => default;
        public ValueTask JobUnscheduled(TriggerKey triggerKey, CancellationToken cancellationToken = default) => default;
        public ValueTask TriggerFinalized(ITrigger trigger, CancellationToken cancellationToken = default) => default;
        public ValueTask TriggerPaused(TriggerKey triggerKey, CancellationToken cancellationToken = default) => default;
        public ValueTask TriggersPaused(string triggerGroup, CancellationToken cancellationToken = default) => default;
        public ValueTask TriggerResumed(TriggerKey triggerKey, CancellationToken cancellationToken = default) => default;
        public ValueTask TriggersResumed(string triggerGroup, CancellationToken cancellationToken = default) => default;
        public ValueTask JobAdded(IJobDetail jobDetail, CancellationToken cancellationToken = default) => default;
        public ValueTask JobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => default;
        public ValueTask JobPaused(JobKey jobKey, CancellationToken cancellationToken = default) => default;
        public ValueTask JobsPaused(string jobGroup, CancellationToken cancellationToken = default) => default;
        public ValueTask JobResumed(JobKey jobKey, CancellationToken cancellationToken = default) => default;
        public ValueTask JobsResumed(string jobGroup, CancellationToken cancellationToken = default) => default;
        public ValueTask SchedulerError(string msg, SchedulerException cause, CancellationToken cancellationToken = default) => default;
        public ValueTask SchedulerInStandbyMode(CancellationToken cancellationToken = default) => default;
        public ValueTask SchedulerStarted(CancellationToken cancellationToken = default) => default;
        public ValueTask SchedulerStarting(CancellationToken cancellationToken = default) => default;
        public ValueTask SchedulerShutdown(CancellationToken cancellationToken = default) => default;
        public ValueTask SchedulerShuttingdown(CancellationToken cancellationToken = default) => default;
        public ValueTask SchedulingDataCleared(CancellationToken cancellationToken = default) => default;
        public ValueTask JobInterrupted(JobKey jobKey, CancellationToken cancellationToken = default) => default;
    }

    #endregion
}
