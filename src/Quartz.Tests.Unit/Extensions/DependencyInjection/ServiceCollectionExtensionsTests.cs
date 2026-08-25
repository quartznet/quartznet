using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quartz.Configuration;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Util;

namespace Quartz.Tests.Unit.Extensions.DependencyInjection;

[NonParallelizable]
public class ServiceCollectionExtensionsTests
{
    [Test]
    public void AddJob_WithJobIdentityAndDescription_ShouldHonorIt()
    {
        var configurationBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "job2:Name", "JobName2" },
                { "job2:Group", "JobGroup2" },
                { "job2:Description", "JobDescription2" },

                { "job4:Name", "JobName4" },
                { "job4:Group", "JobGroup4" },
                { "job4:Description", "JobDescription4" },

                { "job7:Description", "JobDescription7" },

                { "job8:Name", "JobName8" },
                { "job8:Group", "JobGroup8" },
                { "job8:Description", "JobDescription8" }
            });

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configurationBuilder.Build());

        // Go through AddQuartz(), because the IQuartzBuilder interface refuses mocking or implementation, due to an internal default-implemented property
        services.AddQuartz(quartz =>
        {
            quartz.AddJob<DummyJob>(
                job =>
                {
                    job.WithIdentity("JobName1", "JobGroup1");
                    job.WithDescription("JobDescription1");
                });

            quartz.AddJob<DummyJob>(
                (serviceProvider, job) =>
                {
                    IConfiguration configuration = serviceProvider.GetRequiredService<IConfiguration>();

                    job.WithIdentity(configuration.GetSection("job2:Name").Get<string>(), configuration.GetSection("job2:Group").Get<string>());
                    job.WithDescription(configuration.GetSection("job2:Description").Get<string>());
                });

            quartz.AddJob<DummyJob>(
                job =>
                {
                    job.WithIdentity(new JobKey("JobName3", "JobGroup3"));
                    job.WithDescription("JobDescription3");
                });

            quartz.AddJob<DummyJob>(
                (serviceProvider, job) =>
                {
                    IConfiguration configuration = serviceProvider.GetRequiredService<IConfiguration>();

                    job.WithIdentity(configuration.GetSection("job4:Name").Get<string>(), configuration.GetSection("job4:Group").Get<string>());
                    job.WithDescription(configuration.GetSection("job4:Description").Get<string>());
                });

            quartz.AddJob(
                typeof(DummyJob),
                job =>
                {
                    job.WithIdentity(new JobKey("JobName5", "JobGroup5"));
                    job.WithDescription("JobDescription5");
                });

            quartz.AddJob(
                typeof(DummyJob),
                job =>
                {
                    job.WithIdentity("JobName6", "JobGroup6");
                    job.WithDescription("JobDescription6");
                });

            quartz.AddJob(
                typeof(DummyJob),
                (serviceProvider, job) =>
                {
                    IConfiguration configuration = serviceProvider.GetRequiredService<IConfiguration>();

                    job.WithIdentity(new JobKey("JobName7", "JobGroup7"));
                    job.WithDescription(configuration.GetSection("job7:Description").Get<string>());
                });

            quartz.AddJob(
                typeof(DummyJob),
                (serviceProvider, job) =>
                {
                    IConfiguration configuration = serviceProvider.GetRequiredService<IConfiguration>();

                    job.WithIdentity(configuration.GetSection("job8:Name").Get<string>(), configuration.GetSection("job8:Group").Get<string>());
                    job.WithDescription(configuration.GetSection("job8:Description").Get<string>());
                });
        });

        using var serviceProvider = services.BuildServiceProvider();

        var jobs = serviceProvider.ScheduledJobs();

        jobs.Should().HaveCount(8);

        var job1 = jobs[0];
        var job2 = jobs[1];
        var job3 = jobs[2];
        var job4 = jobs[3];
        var job5 = jobs[4];
        var job6 = jobs[5];
        var job7 = jobs[6];
        var job8 = jobs[7];

        Assert.Multiple(() =>
        {
            // The job key should have its own manual configuration
            Assert.That(job1.Key.Name, Is.EqualTo("JobName1"));
            Assert.That(job1.Key.Group, Is.EqualTo("JobGroup1"));
            Assert.That(job1.Description, Is.EqualTo("JobDescription1"));

            Assert.That(job2.Key.Name, Is.EqualTo("JobName2"));
            Assert.That(job2.Key.Group, Is.EqualTo("JobGroup2"));
            Assert.That(job2.Description, Is.EqualTo("JobDescription2"));

            Assert.That(job3.Key.Name, Is.EqualTo("JobName3"));
            Assert.That(job3.Key.Group, Is.EqualTo("JobGroup3"));
            Assert.That(job3.Description, Is.EqualTo("JobDescription3"));

            Assert.That(job4.Key.Name, Is.EqualTo("JobName4"));
            Assert.That(job4.Key.Group, Is.EqualTo("JobGroup4"));
            Assert.That(job4.Description, Is.EqualTo("JobDescription4"));

            Assert.That(job5.Key.Name, Is.EqualTo("JobName5"));
            Assert.That(job5.Key.Group, Is.EqualTo("JobGroup5"));
            Assert.That(job5.Description, Is.EqualTo("JobDescription5"));

            Assert.That(job6.Key.Name, Is.EqualTo("JobName6"));
            Assert.That(job6.Key.Group, Is.EqualTo("JobGroup6"));
            Assert.That(job6.Description, Is.EqualTo("JobDescription6"));

            Assert.That(job7.Key.Name, Is.EqualTo("JobName7"));
            Assert.That(job7.Key.Group, Is.EqualTo("JobGroup7"));
            Assert.That(job7.Description, Is.EqualTo("JobDescription7"));

            Assert.That(job8.Key.Name, Is.EqualTo("JobName8"));
            Assert.That(job8.Key.Group, Is.EqualTo("JobGroup8"));
            Assert.That(job8.Description, Is.EqualTo("JobDescription8"));
        });
    }

    [Test]
    public void AddTrigger_WithJobIdentity_ShouldHonorIt()
    {
        var configurationBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "trigger2:Name", "TriggerName2" },
                { "trigger2:Group", "TriggerGroup2" }
            });

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configurationBuilder.Build());

        // Go through AddQuartz(), because the IQuartzBuilder interface refuses mocking or implementation, due to an internal default-implemented property
        services.AddQuartz(quartz =>
        {
            quartz.AddTrigger<IJob>(
                trigger =>
                {
                    trigger.ForJob("JobName1", "JobGroup1");
                    trigger.WithIdentity("TriggerName1", "TriggerGroup1");
                });

            quartz.AddTrigger<IJob>(
                (serviceProvider, trigger) =>
                {
                    IConfiguration configuration = serviceProvider.GetRequiredService<IConfiguration>();

                    trigger.ForJob("JobName2", "JobGroup2");
                    trigger.WithIdentity(configuration.GetSection("trigger2:Name").Get<string>(), configuration.GetSection("trigger2:Group").Get<string>());
                });
        });

        using var serviceProvider = services.BuildServiceProvider();

        var triggers = serviceProvider.ScheduledTriggers();

        triggers.Should().HaveCount(2);

        var trigger1 = triggers[0];
        var trigger2 = triggers[1];

        Assert.Multiple(() =>
        {
            // The trigger key should have its own manual configuration
            Assert.That(trigger1.Key.Name, Is.EqualTo("TriggerName1"));
            Assert.That(trigger1.Key.Group, Is.EqualTo("TriggerGroup1"));

            Assert.That(trigger2.Key.Name, Is.EqualTo("TriggerName2"));
            Assert.That(trigger2.Key.Group, Is.EqualTo("TriggerGroup2"));

            Assert.That(trigger1.JobKey.Name, Is.EqualTo("JobName1"));
            Assert.That(trigger1.JobKey.Group, Is.EqualTo("JobGroup1"));

            Assert.That(trigger2.JobKey.Name, Is.EqualTo("JobName2"));
            Assert.That(trigger2.JobKey.Group, Is.EqualTo("JobGroup2"));
        });
    }

    [Test]
    public void ScheduleJob_WithJobIdentity_ShouldHonorIt()
    {
        var configurationBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "trigger2:Name", "TriggerName2" },
                { "trigger2:Group", "TriggerGroup2" },

                { "job2:Name", "JobName2" },
                { "job2:Group", "JobGroup2" }
            });

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configurationBuilder.Build());

        // Go through AddQuartz(), because the IQuartzBuilder interface refuses mocking or implementation, due to an internal default-implemented property
        services.AddQuartz(quartz =>
        {
            quartz.ScheduleJob<DummyJob>(
                trigger =>
                {
                    trigger.WithIdentity("TriggerName1", "TriggerGroup1");
                },
                job =>
                {
                    job.WithIdentity("JobName1", "JobGroup1");
                });

            quartz.ScheduleJob<DummyJob>(
                (serviceProvider, trigger) =>
                {
                    IConfiguration configuration = serviceProvider.GetRequiredService<IConfiguration>();

                    trigger.WithIdentity(configuration.GetSection("trigger2:Name").Get<string>(), configuration.GetSection("trigger2:Group").Get<string>());
                },
                (serviceProvider, job) =>
                {
                    IConfiguration configuration = serviceProvider.GetRequiredService<IConfiguration>();

                    job.WithIdentity(configuration.GetSection("job2:Name").Get<string>(), configuration.GetSection("job2:Group").Get<string>());
                });
        });

        using var serviceProvider = services.BuildServiceProvider();

        var triggers = serviceProvider.ScheduledTriggers();
        var jobs = serviceProvider.ScheduledJobs();

        triggers.Should().HaveCount(2);
        jobs.Should().HaveCount(2);

        var trigger1 = triggers[0];
        var trigger2 = triggers[1];
        var job1 = jobs[0];
        var job2 = jobs[1];

        Assert.Multiple(() =>
        {
            // The trigger key should have its own manual configuration
            Assert.That(trigger1.Key.Name, Is.EqualTo("TriggerName1"));
            Assert.That(trigger1.Key.Group, Is.EqualTo("TriggerGroup1"));

            Assert.That(trigger2.Key.Name, Is.EqualTo("TriggerName2"));
            Assert.That(trigger2.Key.Group, Is.EqualTo("TriggerGroup2"));

            // The job key should have its own manual configuration
            Assert.That(job1.Key.Name, Is.EqualTo("JobName1"));
            Assert.That(job1.Key.Group, Is.EqualTo("JobGroup1"));

            Assert.That(job2.Key.Name, Is.EqualTo("JobName2"));
            Assert.That(job2.Key.Group, Is.EqualTo("JobGroup2"));

            // Also validate that the trigger knows the correct job key
            Assert.That(trigger1.JobKey.Name, Is.EqualTo(job1.Key.Name));
            Assert.That(trigger1.JobKey.Group, Is.EqualTo(job1.Key.Group));

            Assert.That(trigger2.JobKey.Name, Is.EqualTo(job2.Key.Name));
            Assert.That(trigger2.JobKey.Group, Is.EqualTo(job2.Key.Group));
        });
    }

    [Test]
    public void ScheduleJob_WithoutJobIdentityWithoutTriggerIdentity_ShouldCopyFromTriggerIdentity()
    {
        var services = new ServiceCollection();

        // Go through AddQuartz(), because the IQuartzBuilder interface refuses mocking or implementation, due to an internal default-implemented property
        services.AddQuartz(quartz => quartz.ScheduleJob<DummyJob>(
            trigger => { }));

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.ScheduledTriggers().Should().HaveCount(1);
        serviceProvider.ScheduledJobs().Should().HaveCount(1);

        var trigger = serviceProvider.ScheduledTriggers().Single();
        var job = serviceProvider.ScheduledJobs().Single();

        Assert.Multiple(() =>
        {
            // The job's key should match the trigger's (auto-generated) key
            Assert.That(job.Key.Name, Is.EqualTo(trigger.Key.Name));
            Assert.That(job.Key.Group, Is.EqualTo(trigger.Key.Group));

            // Also validate that the trigger knows the correct job key
            Assert.That(trigger.JobKey.Name, Is.EqualTo(job.Key.Name));
            Assert.That(trigger.JobKey.Group, Is.EqualTo(job.Key.Group));
        });
    }

    [Test]
    public void ScheduleJob_WithoutJobIdentityWithTriggerIdentity_ShouldCopyFromTriggerIdentity()
    {
        var services = new ServiceCollection();

        // Go through AddQuartz(), because the IQuartzBuilder interface refuses mocking or implementation, due to an internal default-implemented property
        services.AddQuartz(quartz => quartz.ScheduleJob<DummyJob>(
            trigger => trigger.WithIdentity("TriggerName", "TriggerGroup")));

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.ScheduledTriggers().Should().HaveCount(1);
        serviceProvider.ScheduledJobs().Should().HaveCount(1);

        var trigger = serviceProvider.ScheduledTriggers().Single();
        var job = serviceProvider.ScheduledJobs().Single();

        Assert.Multiple(() =>
        {
            // The trigger key should have its own manual configuration
            Assert.That(trigger.Key.Name, Is.EqualTo("TriggerName"));
            Assert.That(trigger.Key.Group, Is.EqualTo("TriggerGroup"));

            // The job's key should match the trigger's (auto-generated) key
            Assert.That(job.Key.Name, Is.EqualTo(trigger.Key.Name));
            Assert.That(job.Key.Group, Is.EqualTo(trigger.Key.Group));

            // Also validate that the trigger knows the correct job key
            Assert.That(trigger.JobKey.Name, Is.EqualTo(job.Key.Name));
            Assert.That(trigger.JobKey.Group, Is.EqualTo(job.Key.Group));
        });
    }

    [Test]
    public void AddCalendar_WithoutServiceProvider_ShouldHonorIt()
    {
        var configurationBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "calendar2:Description", "CalendarDescription2" }
            });

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configurationBuilder.Build());

        // Go through AddQuartz(), because the IQuartzBuilder interface refuses mocking or implementation, due to an internal default-implemented property
        services.AddQuartz(quartz =>
        {
            quartz.AddCalendar<DummyCalendar>(
                "TestCalendarName",
                new AddCalendarOptions { Replace = true, UpdateTriggers = true },
                calendar =>
                {
                    calendar.Description = "TestCalendarDescription";
                });
        });

        using var serviceProvider = services.BuildServiceProvider();

        var calendarConfiguration = serviceProvider.GetRequiredService<CalendarConfiguration>();

        Assert.Multiple(() =>
        {
            Assert.That(calendarConfiguration.Name, Is.EqualTo("TestCalendarName"));
            Assert.That(calendarConfiguration.Calendar.Description, Is.EqualTo("TestCalendarDescription"));
        });
    }

    [Test]
    public void AddCalendar_WithServiceProvider_ShouldHonorIt()
    {
        var configurationBuilder = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string>
            {
                { "calendar:Description", "TestCalendarDescription" }
            });

        var services = new ServiceCollection();

        services.AddSingleton<IConfiguration>(configurationBuilder.Build());

        // Go through AddQuartz(), because the IQuartzBuilder interface refuses mocking or implementation, due to an internal default-implemented property
        services.AddQuartz(quartz =>
        {
            quartz.AddCalendar<DummyCalendar>(
                "TestCalendarName",
                new AddCalendarOptions { Replace = true, UpdateTriggers = true },
                (serviceProvider, calendar) =>
                {
                    IConfiguration configuration = serviceProvider.GetRequiredService<IConfiguration>();

                    calendar.Description = configuration.GetSection("calendar:Description").Get<string>();
                });
        });

        using var serviceProvider = services.BuildServiceProvider();

        var calendarConfiguration = serviceProvider.GetRequiredService<CalendarConfiguration>();

        Assert.Multiple(() =>
        {
            Assert.That(calendarConfiguration.Name, Is.EqualTo("TestCalendarName"));
            Assert.That(calendarConfiguration.Calendar.Description, Is.EqualTo("TestCalendarDescription"));
        });
    }

    [Test]
    public void AddJob_WithEitherConfigurator_ShouldNotBeAmbiguous()
    {
        // Regression test for #2795: these calls must compile without CS0121 ambiguity
        var services = new ServiceCollection();

        services.AddQuartz(quartz =>
        {
            quartz.AddJob<DummyJob>(job => job.WithIdentity("test1", "group1"));

            quartz.AddJob<DummyJob>((_, job) => job.WithIdentity("test2", "group1"));

            quartz.AddJob(typeof(DummyJob), job => job.WithIdentity("test3", "group1"));

            quartz.AddJob(typeof(DummyJob), (_, job) => job.WithIdentity("test4", "group1"));
        });

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.ScheduledJobs().Should().HaveCount(4);
    }

    [Test]
    public void AddTrigger_WithExplicitDelegate_ShouldNotBeAmbiguous()
    {
        // Regression test for #2795: AddTrigger had the same ambiguity as AddJob
        var services = new ServiceCollection();

        services.AddQuartz(quartz =>
        {
            quartz.AddJob<DummyJob>(job => job.WithIdentity("job1", "group1"));

            // Explicit Action<ITriggerConfigurator<IJob>> — must not be ambiguous
            quartz.AddTrigger<IJob>(t => t
                .ForJob(new JobKey("job1", "group1"))
                .WithSimpleSchedule(s => s.WithRepeatCount(0)));

            // Explicit Action<IServiceProvider, ITriggerConfigurator<IJob>> — must not be ambiguous
            quartz.AddTrigger<IJob>((sp, t) => t
                .ForJob(new JobKey("job1", "group1"))
                .WithSimpleSchedule(s => s.WithRepeatCount(0)));
        });

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.ScheduledTriggers().Should().HaveCount(2);
    }

    /// <summary>
    /// A trigger for a job added elsewhere names it by key, so it has nothing to do with a job type;
    /// naming one is only what lets the trigger's job data name that job's properties.
    /// </summary>
    [Test]
    public void AddTrigger_WithoutAJobType_RegistersTheTrigger()
    {
        var services = new ServiceCollection();

        services.AddQuartz(quartz =>
        {
            quartz.AddJob<DummyJob>(job => job.WithIdentity("job1", "group1"));

            quartz.AddTrigger(t => t
                .WithIdentity("trigger1", "group1")
                .ForJob(new JobKey("job1", "group1"))
                .WithSimpleSchedule(s => s.WithRepeatCount(0)));

            quartz.AddTrigger((serviceProvider, t) => t
                .WithIdentity("trigger2", "group1")
                .ForJob(new JobKey("job1", "group1"))
                .WithSimpleSchedule(s => s.WithRepeatCount(0)));
        });

        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.ScheduledTriggers().Select(x => x.Key.Name).Should().BeEquivalentTo(
            ["trigger1", "trigger2"],
            "both shapes register a trigger, and neither is ambiguous with AddTrigger<TJob>");
    }

    [Test]
    public void ConfiguredDbDataSource_ShouldBeUsed()
    {
        var services = new ServiceCollection();

        services.AddNpgsqlDataSource("Host=myserver;Username=mylogin;Password=mypass;Database=mydatabase");
        services.AddQuartz(quartz =>
        {
            quartz.UsePersistentStore(p =>
            {
                p.UsePostgres(c =>
                {
                    c.Provider = "Npgsql";
                    c.UseRegisteredDataSource = true;
                });
            });
        });

        var provider = services.BuildServiceProvider();

        // The connection provider is a registration now rather than a type name in a property bag,
        // so the container itself is the thing to assert on.
        provider.GetService<IDbProvider>().Should().BeOfType<DataSourceDbProvider>(
            "asking for the container's data source must win over the provider the database method implies");

        provider.GetRequiredService<IOptionsMonitor<DataSourceOptions>>().Get("quartz")
            .UseRegisteredDataSource.Should().BeTrue();
    }

    [Test]
    public async Task LookupScheduler_ByName_ShouldReturnSchedulerWithoutRequiringDefaultSchedulerCall()
    {
        // This tests the fix for the issue where the by-name lookup returned null
        // unless GetScheduler() was called first
        const string schedulerName = "TestScheduler";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = schedulerName);
            q.UseInMemoryStore();
        });

        await using var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ISchedulerFactory>();

        // Call GetScheduler with the name directly, without calling GetScheduler() first
        var scheduler = await factory.LookupScheduler(schedulerName);

        // Should not be null
        Assert.That(scheduler, Is.Not.Null);
        Assert.That(scheduler.SchedulerName, Is.EqualTo(schedulerName));

        await scheduler.Shutdown();
    }

    [Test]
    public async Task LookupScheduler_ByName_AfterDefaultCall_ShouldReturnSameScheduler()
    {
        // This tests that both methods return the same scheduler instance
        const string schedulerName = "TestScheduler2";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = schedulerName);
            q.UseInMemoryStore();
        });

        using var serviceProvider = services.BuildServiceProvider();
        var factory = serviceProvider.GetRequiredService<ISchedulerFactory>();

        // Call both methods
        var defaultScheduler = await factory.GetScheduler();
        var namedScheduler = await factory.LookupScheduler(schedulerName);

        // Should return the same instance
        Assert.That(namedScheduler, Is.Not.Null);
        Assert.That(namedScheduler, Is.SameAs(defaultScheduler));
        Assert.That(namedScheduler.SchedulerName, Is.EqualTo(schedulerName));

        await defaultScheduler.Shutdown();
    }

    [Test]
    public void ScheduleJob_WithoutAJobIdentity_ShouldGiveTheJobTheTriggersKey()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.ScheduleJob<DummyJob>(
            t => t.WithIdentity("derived", "derivedGroup").StartNow()));

        using var provider = services.BuildServiceProvider();

        provider.ScheduledJobs().Should().ContainSingle()
            .Which.Key.Should().Be(new JobKey("derived", "derivedGroup"));
        provider.ScheduledTriggers().Should().ContainSingle()
            .Which.JobKey.Should().Be(new JobKey("derived", "derivedGroup"),
                "the trigger has to point at the job it just named");
    }

    [Test]
    public void ScheduleJob_WithoutAnyIdentity_ShouldStillAgreeOnAGeneratedKey()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.ScheduleJob<DummyJob>(t => t.StartNow()));

        using var provider = services.BuildServiceProvider();

        var job = provider.ScheduledJobs().Should().ContainSingle().Subject;
        var trigger = provider.ScheduledTriggers().Should().ContainSingle().Subject;

        trigger.JobKey.Should().Be(job.Key);
        job.Key.Name.Should().Be(trigger.Key.Name);
        job.Key.Group.Should().Be(trigger.Key.Group);
    }

    [Test]
    public void ScheduleJob_WithAJobIdentity_ShouldKeepItAndPointTheTriggerAtIt()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.ScheduleJob<DummyJob>(
            t => t.WithIdentity("theTrigger").StartNow(),
            j => j.WithIdentity("theJob", "jobs")));

        using var provider = services.BuildServiceProvider();

        provider.ScheduledJobs().Should().ContainSingle()
            .Which.Key.Should().Be(new JobKey("theJob", "jobs"));
        provider.ScheduledTriggers().Should().ContainSingle()
            .Which.JobKey.Should().Be(new JobKey("theJob", "jobs"));
    }

    [Test]
    public void ScheduleJob_WithATriggerPointedElsewhere_ShouldRefuse()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.ScheduleJob<DummyJob>(
            t => t.WithIdentity("theTrigger").ForJob("someOtherJob").StartNow(),
            j => j.WithIdentity("theJob", "jobs")));

        using var provider = services.BuildServiceProvider();

        var act = () => provider.ScheduledJobs();
        act.Should().Throw<InvalidOperationException>().WithMessage("*doesn't refer to job being scheduled*");
    }

    [Test]
    public void AddJob_ShouldRegisterTheJobTypeScoped()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.AddJob<DummyJob>(j => j.WithIdentity("registered")));

        ServiceDescriptor descriptor = services.Single(d => d.ServiceType == typeof(DummyJob));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped,
            "the job factory opens a scope per fire and resolves the job from it, so the job lives as "
            + "long as that scope and can take scoped dependencies");
    }

    [Test]
    public void AddJob_WithAnUnresolvableDependency_ShouldFailValidation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(q => q.AddJob<JobNeedingSomethingUnregistered>(j => j.WithIdentity("unresolvable")));

        // Before the job type was registered, the container had never heard of it: validation passed and
        // the failure arrived at fire time instead, as a trigger stuck in the error state.
        var act = () => services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        act.Should().Throw<AggregateException>()
            .WithMessage("*JobNeedingSomethingUnregistered*");
    }

    [Test]
    public void ScheduleJob_WithAnUnresolvableDependency_ShouldFailValidation()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddQuartz(q => q.ScheduleJob<JobNeedingSomethingUnregistered>(
            t => t.WithIdentity("unresolvable").StartNow()));

        var act = () => services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        act.Should().Throw<AggregateException>()
            .WithMessage("*JobNeedingSomethingUnregistered*");
    }

    [Test]
    public void AddJob_ShouldKeepAnExistingRegistration()
    {
        var services = new ServiceCollection();
        var instance = new DummyJob();
        services.AddSingleton(instance);

        services.AddQuartz(q =>
        {
            q.AddJob<DummyJob>(j => j.WithIdentity("first"));

            // Registering the same job twice has to stay harmless as well
            q.AddJob<DummyJob>(j => j.WithIdentity("second"));
        });

        services.Where(d => d.ServiceType == typeof(DummyJob)).Should().ContainSingle()
            .Which.ImplementationInstance.Should().BeSameAs(instance,
                "a registration the application made itself, with its own lifetime, wins");
    }

    [Test]
    public void AddJob_WithATypeTheContainerCannotBuild_ShouldNotRegisterIt()
    {
        var services = new ServiceCollection();
        services.AddQuartz(q => q.AddJob(typeof(IJob), j => j.WithIdentity("abstract")));

        services.Should().NotContain(d => d.ServiceType == typeof(IJob),
            "an interface is not something the container could construct, and registering it would turn a "
            + "job the factory can still activate into a startup failure");
    }

    private sealed class JobNeedingSomethingUnregistered : IJob
    {
        public JobNeedingSomethingUnregistered(IUnregisteredDependency dependency)
        {
            Dependency = dependency;
        }

        public IUnregisteredDependency Dependency { get; }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    public interface IUnregisteredDependency;

    private sealed class DummyJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }

    private sealed class DummyCalendar : ICalendar
    {
        public string Description { get; set; }

        public ICalendar CalendarBase { get; set; }

        public ICalendar Clone() => this;

        public DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc) => timeUtc;

        public bool IsTimeIncluded(DateTimeOffset timeUtc) => true;
    }
}
