using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using NUnit.Framework;

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

        // Default options should have no jobs from the named scheduler
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
        var flatJobListeners = provider.GetServices<IJobListener>().ToList();
        flatJobListeners.Should().BeEmpty("named scheduler listeners should not pollute the global DI pool");

        var flatTriggerListeners = provider.GetServices<ITriggerListener>().ToList();
        flatTriggerListeners.Should().BeEmpty();

        var flatSchedulerListeners = provider.GetServices<ISchedulerListener>().ToList();
        flatSchedulerListeners.Should().BeEmpty();

        // But their configurations should be tagged correctly
        var jobListenerConfigs = provider.GetServices<JobListenerConfiguration>().ToList();
        jobListenerConfigs.Should().HaveCount(2);
        jobListenerConfigs.Should().Contain(c => c.OptionsName == "Scheduler1" && c.ListenerType == typeof(TestJobListenerA));
        jobListenerConfigs.Should().Contain(c => c.OptionsName == "Scheduler2" && c.ListenerType == typeof(TestJobListenerB));

        var triggerListenerConfigs = provider.GetServices<TriggerListenerConfiguration>().ToList();
        triggerListenerConfigs.Should().HaveCount(1);
        triggerListenerConfigs[0].OptionsName.Should().Be("Scheduler1");

        var schedulerListenerConfigs = provider.GetServices<SchedulerListenerConfiguration>().ToList();
        schedulerListenerConfigs.Should().HaveCount(1);
        schedulerListenerConfigs[0].OptionsName.Should().Be("Scheduler1");
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

        // ISchedulerFactory should be registered for default scheduler
        var factory = provider.GetService<ISchedulerFactory>();
        factory.Should().NotBeNull();
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

        var defaultOptions = optionsMonitor.Get(Options.DefaultName);
        defaultOptions.JobDetails.Should().HaveCount(1);
        defaultOptions.JobDetails[0].Key.Name.Should().Be("defaultJob");

        var namedOptions = optionsMonitor.Get("Named1");
        namedOptions.JobDetails.Should().HaveCount(1);
        namedOptions.JobDetails[0].Key.Name.Should().Be("namedJob");

        // Default ISchedulerFactory should be registered
        provider.GetService<ISchedulerFactory>().Should().NotBeNull();

        // Registry should only contain the named scheduler
        var registry = provider.GetRequiredService<SchedulerNameRegistry>();
        registry.Names.Should().HaveCount(1);
        registry.Names[0].Should().Be("Named1");
    }

    [Test]
    public void OnlyNamedSchedulers_ShouldNotRegisterDefaultFactory()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz("Named1", q => { });

        using var provider = services.BuildServiceProvider();

        // No default ISchedulerFactory when only named schedulers are used
        provider.GetService<ISchedulerFactory>().Should().BeNull();
    }

    [Test]
    public void AddQuartz_WithEmptyName_ShouldThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());

        var act = () => services.AddQuartz("", q => { });
        act.Should().Throw<ArgumentException>();

        var act2 = () => services.AddQuartz("  ", q => { });
        act2.Should().Throw<ArgumentException>();
    }

    [Test]
    public void AddQuartzHostedService_WithOnlyNamedSchedulers_ShouldNotRegisterDefaultHostedService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz("Named1", q => { });
        services.AddQuartzHostedService();

        // Should have NamedSchedulerHostedService but not QuartzHostedService
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

        services.AddQuartz(q => { }); // default
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

        var properties = new System.Collections.Specialized.NameValueCollection
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
    public void DefaultSchedulerListeners_ShouldNotLeakToNamed()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz(q =>
        {
            q.AddJobListener<TestJobListenerA>();
        });

        services.AddQuartz("Named", q => { });

        using var provider = services.BuildServiceProvider();

        // Default scheduler uses flat IJobListener registrations
        var flatJobListeners = provider.GetServices<IJobListener>().ToList();
        flatJobListeners.Should().HaveCount(1);
        flatJobListeners[0].Should().BeOfType<TestJobListenerA>();

        // Default listener configs have empty OptionsName
        var configs = provider.GetServices<JobListenerConfiguration>().ToList();
        configs.Where(c => c.OptionsName.Length == 0).Should().HaveCount(1);
        configs.Where(c => c.OptionsName == "Named").Should().BeEmpty();
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

        // Default options should be empty
        var defaultOptions = optionsMonitor.Get(Options.DefaultName);
        defaultOptions.JobDetails.Should().BeEmpty();
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
    public void AddQuartzHostedService_WithoutAnyAddQuartz_ShouldNotThrow()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        // No AddQuartz calls at all
        services.AddQuartzHostedService();

        // Should only have NamedSchedulerHostedService (which will no-op)
        var hostedServices = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
        hostedServices.Should().HaveCount(1);
        hostedServices[0].ImplementationType.Should().Be(typeof(NamedSchedulerHostedService));
    }

    [Test]
    public void AddQuartz_WithNullProperties_ShouldThrow()
    {
        var services = new ServiceCollection();

        Action act = () => services.AddQuartz("Test", (System.Collections.Specialized.NameValueCollection) null, q => { });
        act.Should().Throw<ArgumentNullException>();
    }

    #region Test helpers

    private sealed class TestJobA : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }

    private sealed class TestJobB : IJob
    {
        public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
    }

    private sealed class TestJobListenerA : IJobListener
    {
        public string Name => nameof(TestJobListenerA);
        public Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestJobListenerB : IJobListener
    {
        public string Name => nameof(TestJobListenerB);
        public Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestTriggerListenerA : ITriggerListener
    {
        public string Name => nameof(TestTriggerListenerA);
        public Task TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task TriggerMisfired(ITrigger trigger, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TriggerComplete(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TestSchedulerListenerA : ISchedulerListener
    {
        public Task JobScheduled(ITrigger trigger, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task JobUnscheduled(TriggerKey triggerKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TriggerFinalized(ITrigger trigger, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TriggerPaused(TriggerKey triggerKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TriggersPaused(string triggerGroup, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TriggerResumed(TriggerKey triggerKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task TriggersResumed(string triggerGroup, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task JobAdded(IJobDetail jobDetail, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task JobDeleted(JobKey jobKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task JobPaused(JobKey jobKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task JobsPaused(string jobGroup, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task JobResumed(JobKey jobKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task JobsResumed(string jobGroup, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SchedulerError(string msg, SchedulerException cause, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SchedulerInStandbyMode(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SchedulerStarted(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SchedulerStarting(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SchedulerShutdown(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SchedulerShuttingdown(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SchedulingDataCleared(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task JobInterrupted(JobKey jobKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    #endregion
}
