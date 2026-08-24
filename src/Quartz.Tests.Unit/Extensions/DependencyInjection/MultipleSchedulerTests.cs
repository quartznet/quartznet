using System.Collections.Specialized;


using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Extensibility;
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
            q.AddTrigger<IJob>(t => t.ForJob("jobA", "group1").WithIdentity("triggerA").StartNow());
        });

        services.AddQuartz("Scheduler2", q =>
        {
            q.AddJob<TestJobB>(j => j.WithIdentity("jobB", "group2"));
            q.AddTrigger<IJob>(t => t.ForJob("jobB", "group2").WithIdentity("triggerB").StartNow());
        });

        using var provider = services.BuildServiceProvider();
        var schedulerOptions = provider.GetRequiredService<IOptionsMonitor<QuartzSchedulerOptions>>();

        provider.ScheduledJobs("Scheduler1").Should().ContainSingle().Which.Key.Name.Should().Be("jobA");
        provider.ScheduledTriggers("Scheduler1").Should().ContainSingle().Which.Key.Name.Should().Be("triggerA");
        schedulerOptions.Get("Scheduler1").InstanceName.Should().Be("Scheduler1");

        provider.ScheduledJobs("Scheduler2").Should().ContainSingle().Which.Key.Name.Should().Be("jobB");
        provider.ScheduledTriggers("Scheduler2").Should().ContainSingle().Which.Key.Name.Should().Be("triggerB");
        schedulerOptions.Get("Scheduler2").InstanceName.Should().Be("Scheduler2");
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
            q.AddTrigger<IJob>(t => t.ForJob("namedJob").WithIdentity("namedTrigger").StartNow());
        });

        using var provider = services.BuildServiceProvider();

        provider.ScheduledJobs().Should().BeEmpty();
        provider.ScheduledTriggers().Should().BeEmpty();
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

        // Each scheduler's registrations are held under its own service key, so one scheduler's listeners
        // are not even visible to another.
        provider.GetKeyedServices<JobListenerRegistration>("Scheduler1").Should().ContainSingle()
            .Which.ListenerType.Should().Be(typeof(TestJobListenerA));
        provider.GetKeyedServices<JobListenerRegistration>("Scheduler2").Should().ContainSingle()
            .Which.ListenerType.Should().Be(typeof(TestJobListenerB));
        provider.GetServices<JobListenerRegistration>().Should().BeEmpty(
            "no default scheduler was registered, so nothing belongs to the unkeyed set");

        provider.GetKeyedServices<TriggerListenerRegistration>("Scheduler1").Should().ContainSingle()
            .Which.ListenerType.Should().Be(typeof(TestTriggerListenerA));
        provider.GetKeyedServices<TriggerListenerRegistration>("Scheduler2").Should().BeEmpty();

        provider.GetKeyedServices<SchedulerListenerRegistration>("Scheduler1").Should().ContainSingle()
            .Which.ListenerType.Should().Be(typeof(TestSchedulerListenerA));
        provider.GetKeyedServices<SchedulerListenerRegistration>("Scheduler2").Should().BeEmpty();
    }

    [Test]
    public void NamedSchedulerCalendars_ShouldBeIsolated()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz("Scheduler1", q =>
        {
            q.AddCalendar("cal1", new Quartz.Impl.Calendar.BaseCalendar(), new AddCalendarOptions { Replace = true });
        });

        services.AddQuartz("Scheduler2", q =>
        {
            q.AddCalendar("cal2", new Quartz.Impl.Calendar.BaseCalendar(), new AddCalendarOptions { UpdateTriggers = true });
        });

        using var provider = services.BuildServiceProvider();

        provider.GetKeyedServices<CalendarConfiguration>("Scheduler1").Should().ContainSingle()
            .Which.Name.Should().Be("cal1");
        provider.GetKeyedServices<CalendarConfiguration>("Scheduler2").Should().ContainSingle()
            .Which.Name.Should().Be("cal2");
        provider.GetServices<CalendarConfiguration>().Should().BeEmpty(
            "no default scheduler was registered, so nothing belongs to the unkeyed set");
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
            q.AddTrigger<IJob>(t => t.ForJob("defaultJob").WithIdentity("defaultTrigger").StartNow());
        });

        using var provider = services.BuildServiceProvider();

        provider.ScheduledJobs().Should().ContainSingle().Which.Key.Name.Should().Be("defaultJob");
        provider.ScheduledTriggers().Should().HaveCount(1);

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
            q.AddTrigger<IJob>(t => t.ForJob("defaultJob").WithIdentity("defaultTrigger").StartNow());
        });

        services.AddQuartz("Named1", q =>
        {
            q.AddJob<TestJobB>(j => j.WithIdentity("namedJob"));
            q.AddTrigger<IJob>(t => t.ForJob("namedJob").WithIdentity("namedTrigger").StartNow());
        });

        using var provider = services.BuildServiceProvider();

        provider.ScheduledJobs().Should().ContainSingle().Which.Key.Name.Should().Be("defaultJob");
        provider.ScheduledJobs("Named1").Should().ContainSingle().Which.Key.Name.Should().Be("namedJob");

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
                    .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(10)).RepeatForever()),
                job => job.WithIdentity("scheduledJob"));
        });

        using var provider = services.BuildServiceProvider();

        provider.ScheduledJobs("Named").Should().ContainSingle().Which.Key.Name.Should().Be("scheduledJob");
        provider.ScheduledTriggers("Named").Should().ContainSingle().Which.Key.Name.Should().Be("scheduledTrigger");

        provider.ScheduledJobs().Should().BeEmpty();
    }

    [Test]
    public async Task NamedSchedulerContent_ShouldBeScheduledOnThatSchedulerOnly()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz(q => q.ConfigureScheduler(options => options.InstanceName = "ContentDefault"));
        services.AddQuartz("ContentNamed", q => q.ScheduleJob<TestJobA>(
            trigger => trigger.WithIdentity("namedOnlyTrigger").StartNow(),
            job => job.WithIdentity("namedOnlyJob")));

        await using var provider = services.BuildServiceProvider();

        var named = await provider.GetRequiredKeyedService<ISchedulerFactory>("ContentNamed").GetScheduler();
        var unnamed = await provider.GetRequiredService<ISchedulerFactory>().GetScheduler();

        try
        {
            (await named.Exists(new JobKey("namedOnlyJob"))).Should().BeTrue();
            (await named.Exists(new TriggerKey("namedOnlyTrigger"))).Should().BeTrue();

            (await unnamed.Exists(new JobKey("namedOnlyJob"))).Should().BeFalse(
                "a named scheduler's content is registered under its own key, so no other scheduler runs it");
            (await unnamed.Exists(new TriggerKey("namedOnlyTrigger"))).Should().BeFalse();
        }
        finally
        {
            await named.Shutdown();
            await unnamed.Shutdown();
        }
    }

    [Test]
    public void SchedulerName_ConfiguredOnNamedScheduler_IsOverriddenByItsRegistration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz("MyName", q => q.ConfigureScheduler(options => options.InstanceName = "Other"));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<QuartzSchedulerOptions>>().Get("MyName");

        options.InstanceName.Should().Be("MyName",
            "the registration name is also the service key, so the instance name cannot drift from it");
    }

    [Test]
    public void AddQuartzHostedService_WithOnlyNamedSchedulers_ShouldRegisterTheOneHostedService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz("Named1", q => { });
        services.AddQuartzHostedService();

        var hostedServices = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
        hostedServices.Should().ContainSingle()
            .Which.ImplementationType.Should().Be(typeof(QuartzHostedService),
                "one hosted service starts every scheduler in the container, named or not");
    }

    [Test]
    public async Task AddQuartzHostedService_WithMixed_ShouldStartEveryScheduler()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddSingleton<IHostApplicationLifetime>(new TestApplicationLifetime());

        // Registered before the schedulers, which used to leave the default one unstarted
        services.AddQuartzHostedService(options => options.AwaitApplicationStarted = false);
        services.AddQuartz(q => q.ConfigureScheduler(o => o.InstanceName = "DefaultOne"));
        services.AddQuartz("Named1", q => { });

        services.Where(d => d.ServiceType == typeof(IHostedService)).Should().ContainSingle();

        using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().OfType<QuartzHostedService>().Single();

        await hostedService.StartAsync(CancellationToken.None);
        try
        {
            (await provider.GetRequiredService<ISchedulerFactory>().GetScheduler()).IsStarted.Should().BeTrue();
            (await provider.GetRequiredKeyedService<ISchedulerFactory>("Named1").GetScheduler()).IsStarted.Should().BeTrue();
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public void AddQuartzHostedService_NamedAndDerived_ShouldRegisterOneService()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz("Named1", q => { });
        services.AddQuartzHostedService("Named1", options => options.WaitForJobsToComplete = true);
        services.AddQuartzHostedService<DerivedHostedService>();

        services.Where(d => d.ServiceType == typeof(IHostedService)).Should().ContainSingle()
            .Which.ImplementationType.Should().Be(typeof(DerivedHostedService),
                "two hosted services would each start every scheduler");
    }

    private sealed class DerivedHostedService : QuartzHostedService
    {
        public DerivedHostedService(
            IHostApplicationLifetime applicationLifetime,
            IServiceProvider serviceProvider,
            IOptionsMonitor<QuartzHostedServiceOptions> options)
            : base(applicationLifetime, serviceProvider, options)
        {
        }
    }

    [Test]
    public async Task AddQuartzHostedService_WithPerSchedulerOptions_ShouldReadTheSchedulersOwn()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddSingleton<IHostApplicationLifetime>(new TestApplicationLifetime());
        services.AddQuartz("Started", q => { });
        services.AddQuartz("Delayed", q => { });
        services.AddQuartzHostedService(options => options.AwaitApplicationStarted = false);
        services.AddQuartzHostedService("Delayed", options => options.StartDelay = TimeSpan.FromMinutes(10));

        using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().OfType<QuartzHostedService>().Single();

        await hostedService.StartAsync(CancellationToken.None);
        try
        {
            var started = await provider.GetRequiredKeyedService<ISchedulerFactory>("Started").GetScheduler();
            var delayed = await provider.GetRequiredKeyedService<ISchedulerFactory>("Delayed").GetScheduler();

            started.IsStarted.Should().BeTrue();
            delayed.IsStarted.Should().BeFalse("its own options asked for a ten minute start delay");
        }
        finally
        {
            await hostedService.StopAsync(CancellationToken.None);
        }
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
        var optionsMonitor = provider.GetRequiredService<IOptionsMonitor<QuartzSchedulerOptions>>();

        optionsMonitor.Get("MyScheduler").InstanceName.Should().Be("MyScheduler");
    }

    [Test]
    public async Task AddQuartzHostedService_WithoutAnyAddQuartz_ShouldSayThereIsNothingToStart()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddSingleton<IHostApplicationLifetime>(new TestApplicationLifetime());
        services.AddQuartzHostedService();

        using var provider = services.BuildServiceProvider();
        var hostedService = provider.GetServices<IHostedService>().OfType<QuartzHostedService>().Single();

        var act = async () => await hostedService.StartAsync(CancellationToken.None);
        await act.Should().ThrowAsync<SchedulerConfigException>().WithMessage("*AddQuartz*");
    }

    [Test]
    public void AddQuartz_WithNullProperties_ShouldThrow()
    {
        var services = new ServiceCollection();

        var act = () => services.AddQuartz("Test", (NameValueCollection) null!, q => { });
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void ConfigureAllQuartzSchedulers_ShouldReachSchedulersRegisteredAfterIt()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.ConfigureAllQuartzSchedulers(q => q.AddJobListener<TestJobListenerA>());
        services.AddQuartz();
        services.AddQuartz("Named1", q => { });

        using var provider = services.BuildServiceProvider();

        provider.GetServices<JobListenerRegistration>().Should().ContainSingle()
            .Which.ListenerType.Should().Be(typeof(TestJobListenerA),
                "a package that configures every scheduler cannot know whether the application "
                + "registers its schedulers before or after it");
        provider.GetKeyedServices<JobListenerRegistration>("Named1").Should().ContainSingle()
            .Which.ListenerType.Should().Be(typeof(TestJobListenerA));
    }

    [Test]
    public void ConfigureAllQuartzSchedulers_ShouldReachSchedulersRegisteredBeforeIt()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz();
        services.AddQuartz("Named1", q => { });
        services.ConfigureAllQuartzSchedulers(q => q.AddJobListener<TestJobListenerA>());

        using var provider = services.BuildServiceProvider();

        provider.GetServices<JobListenerRegistration>().Should().ContainSingle()
            .Which.ListenerType.Should().Be(typeof(TestJobListenerA),
                "a scheduler whose AddQuartz call has already returned has nothing else to carry this to it");
        provider.GetKeyedServices<JobListenerRegistration>("Named1").Should().ContainSingle()
            .Which.ListenerType.Should().Be(typeof(TestJobListenerA));
    }

    [Test]
    public void ConfigureAllQuartzSchedulers_ShouldReachEverySchedulerWhicheverSideItIsCalledFrom()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz("Before", q => { });
        services.ConfigureAllQuartzSchedulers(q => q.AddSchedulerListener<TestSchedulerListenerA>());
        services.AddQuartz("After", q => { });

        using var provider = services.BuildServiceProvider();

        provider.GetKeyedServices<SchedulerListenerRegistration>("Before").Should().ContainSingle()
            .Which.ListenerType.Should().Be(typeof(TestSchedulerListenerA));
        provider.GetKeyedServices<SchedulerListenerRegistration>("After").Should().ContainSingle()
            .Which.ListenerType.Should().Be(typeof(TestSchedulerListenerA),
                "the order of the calls is exactly what this seam exists to stop mattering");
    }

    [Test]
    public void ConfigureAllQuartzSchedulers_ShouldNotApplyTwiceWhenTheDefaultSchedulerIsAddedAgain()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.AddQuartz();
        services.ConfigureAllQuartzSchedulers(q => q.AddJobListener<TestJobListenerA>());

        // Registering the default scheduler is additive: this contributes more configuration to the
        // scheduler that already exists rather than registering a second one.
        services.AddQuartz(q => { });

        using var provider = services.BuildServiceProvider();

        provider.GetServices<JobListenerRegistration>().Should().ContainSingle(
            "a listener registration is not a TryAdd, so a delegate that reached this scheduler twice "
            + "would attach the listener twice");
    }

    [Test]
    public void ConfigureAllQuartzSchedulers_WithNoSchedulerRegistered_ShouldNotThrow()
    {
        var services = new ServiceCollection();

        var act = () => services.ConfigureAllQuartzSchedulers(q => q.AddJobListener<TestJobListenerA>());

        act.Should().NotThrow("configuring every scheduler when there are none applies to none");

        using var provider = services.BuildServiceProvider();
        provider.GetServices<JobListenerRegistration>().Should().BeEmpty();
    }

    [Test]
    public void ConfigureAllQuartzSchedulers_ShouldSetAnOptionOverTheSchedulersOwn()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        services.ConfigureAllQuartzSchedulers(q => q.UseDefaultThreadPool(3));
        services.AddQuartz("Named1", q => q.UseDefaultThreadPool(7));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptionsMonitor<ThreadPoolOptions>>().Get("Named1");

        options.MaxConcurrency.Should().Be(3,
            "container-wide configuration runs after the scheduler's own and options are last-wins, "
            + "which is what ConfigureAll<TOptions> does over an earlier named Configure");
    }

    [Test]
    public async Task ConfigureAllQuartzSchedulers_ShouldLoseToAComponentTheSchedulerChoseItself()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();

        var containerWide = new RecordingJobFactory();
        var schedulersOwn = new RecordingJobFactory();

        services.ConfigureAllQuartzSchedulers(q => q.UseJobFactory(containerWide));
        services.AddQuartz("Named1", q => q.UseJobFactory(schedulersOwn));
        services.AddQuartz("Named2", q => { });

        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredKeyedService<IJobFactory>("Named1").Should().BeSameAs(schedulersOwn,
            "registration is first-wins, so a component a scheduler chose for itself is not replaced "
            + "by the container-wide default");
        provider.GetRequiredKeyedService<IJobFactory>("Named2").Should().BeSameAs(containerWide,
            "a scheduler that chose nothing gets what the container said for everyone");
    }

    #region Test helpers

    private sealed class RecordingJobFactory : IJobFactory
    {
        public ValueTask<JobScope> CreateJob(TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("this factory exists to be resolved, not to build jobs");
        }

        public ValueTask ReturnJob(JobScope scope, CancellationToken cancellationToken = default) => default;
    }

    private sealed class TestJobA : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    private sealed class TestJobB : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
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
        public ValueTask SchedulerError(string message, SchedulerException exception, CancellationToken cancellationToken = default) => default;
        public ValueTask SchedulerInStandbyMode(CancellationToken cancellationToken = default) => default;
        public ValueTask SchedulerStarted(CancellationToken cancellationToken = default) => default;
        public ValueTask SchedulerStarting(CancellationToken cancellationToken = default) => default;
        public ValueTask SchedulerShutdown(CancellationToken cancellationToken = default) => default;
        public ValueTask SchedulerShuttingDown(CancellationToken cancellationToken = default) => default;
        public ValueTask SchedulingDataCleared(CancellationToken cancellationToken = default) => default;
        public ValueTask JobInterrupted(JobKey jobKey, CancellationToken cancellationToken = default) => default;
    }

    #endregion

    /// <summary>
    /// An application lifetime that never starts or stops, for testing the hosted service without a host.
    /// </summary>
    private sealed class TestApplicationLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource started = new();
        private readonly CancellationTokenSource stopping = new();
        private readonly CancellationTokenSource stopped = new();

        public CancellationToken ApplicationStarted => started.Token;

        public CancellationToken ApplicationStopping => stopping.Token;

        public CancellationToken ApplicationStopped => stopped.Token;

        public void StopApplication() => stopping.Cancel();
    }
}
