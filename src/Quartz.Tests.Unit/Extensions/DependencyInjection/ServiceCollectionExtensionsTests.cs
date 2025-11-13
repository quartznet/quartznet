using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quartz.Configuration;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Util;

namespace Quartz.Tests.Unit.Extensions.DependencyInjection;

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

        // Go through AddQuartz(), because the IServiceCollectionQuartzConfigurator interface refuses mocking or implementation, due to an internal default-implemented property
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
                new JobKey("JobName3", "JobGroup3"),
                job =>
                {
                    job.WithDescription("JobDescription3");
                });

            quartz.AddJob<DummyJob>(
                null,
                (serviceProvider, job) =>
                {
                    IConfiguration configuration = serviceProvider.GetRequiredService<IConfiguration>();

                    job.WithIdentity(configuration.GetSection("job4:Name").Get<string>(), configuration.GetSection("job4:Group").Get<string>());
                    job.WithDescription(configuration.GetSection("job4:Description").Get<string>());
                });

            quartz.AddJob(
                typeof(DummyJob),
                new JobKey("JobName5", "JobGroup5"),
                job =>
                {
                    job.WithDescription("JobDescription5");
                });

            quartz.AddJob(
                typeof(DummyJob),
                null,
                job =>
                {
                    job.WithIdentity("JobName6", "JobGroup6");
                    job.WithDescription("JobDescription6");
                });

            quartz.AddJob(
                typeof(DummyJob),
                new JobKey("JobName7", "JobGroup7"),
                (serviceProvider, job) =>
                {
                    IConfiguration configuration = serviceProvider.GetRequiredService<IConfiguration>();

                    job.WithDescription(configuration.GetSection("job7:Description").Get<string>());
                });

            quartz.AddJob(
                typeof(DummyJob),
                null,
                (serviceProvider, job) =>
                {
                    IConfiguration configuration = serviceProvider.GetRequiredService<IConfiguration>();

                    job.WithIdentity(configuration.GetSection("job8:Name").Get<string>(), configuration.GetSection("job8:Group").Get<string>());
                    job.WithDescription(configuration.GetSection("job8:Description").Get<string>());
                });
        });

        using var serviceProvider = services.BuildServiceProvider();

        var quartzOptions = serviceProvider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        Assert.That(quartzOptions.JobDetails, Has.Exactly(8).Items);

        var job1 = quartzOptions.JobDetails[0];
        var job2 = quartzOptions.JobDetails[1];
        var job3 = quartzOptions.JobDetails[2];
        var job4 = quartzOptions.JobDetails[3];
        var job5 = quartzOptions.JobDetails[4];
        var job6 = quartzOptions.JobDetails[5];
        var job7 = quartzOptions.JobDetails[6];
        var job8 = quartzOptions.JobDetails[7];

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

        // Go through AddQuartz(), because the IServiceCollectionQuartzConfigurator interface refuses mocking or implementation, due to an internal default-implemented property
        services.AddQuartz(quartz =>
        {
            quartz.AddTrigger(
                trigger =>
                {
                    trigger.ForJob("JobName1", "JobGroup1");
                    trigger.WithIdentity("TriggerName1", "TriggerGroup1");
                });

            quartz.AddTrigger(
                (serviceProvider, trigger) =>
                {
                    IConfiguration configuration = serviceProvider.GetRequiredService<IConfiguration>();

                    trigger.ForJob("JobName2", "JobGroup2");
                    trigger.WithIdentity(configuration.GetSection("trigger2:Name").Get<string>(), configuration.GetSection("trigger2:Group").Get<string>());
                });
        });

        using var serviceProvider = services.BuildServiceProvider();

        var quartzOptions = serviceProvider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        Assert.That(quartzOptions.Triggers, Has.Exactly(2).Items);

        var trigger1 = quartzOptions.Triggers[0];
        var trigger2 = quartzOptions.Triggers[1];

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

        // Go through AddQuartz(), because the IServiceCollectionQuartzConfigurator interface refuses mocking or implementation, due to an internal default-implemented property
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

        var quartzOptions = serviceProvider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(quartzOptions.Triggers, Has.Exactly(2).Items);
            Assert.That(quartzOptions.JobDetails, Has.Exactly(2).Items);
        });

        var trigger1 = quartzOptions.Triggers[0];
        var trigger2 = quartzOptions.Triggers[1];
        var job1 = quartzOptions.JobDetails[0];
        var job2 = quartzOptions.JobDetails[1];

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

        // Go through AddQuartz(), because the IServiceCollectionQuartzConfigurator interface refuses mocking or implementation, due to an internal default-implemented property
        services.AddQuartz(quartz => quartz.ScheduleJob<DummyJob>(
            trigger => { }));

        using var serviceProvider = services.BuildServiceProvider();

        var quartzOptions = serviceProvider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(quartzOptions.Triggers, Has.Exactly(1).Items);
            Assert.That(quartzOptions.JobDetails, Has.Exactly(1).Items);
        });

        var trigger = quartzOptions.Triggers.Single();
        var job = quartzOptions.JobDetails.Single();

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

        // Go through AddQuartz(), because the IServiceCollectionQuartzConfigurator interface refuses mocking or implementation, due to an internal default-implemented property
        services.AddQuartz(quartz => quartz.ScheduleJob<DummyJob>(
            trigger => trigger.WithIdentity("TriggerName", "TriggerGroup")));

        using var serviceProvider = services.BuildServiceProvider();

        var quartzOptions = serviceProvider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(quartzOptions.Triggers, Has.Exactly(1).Items);
            Assert.That(quartzOptions.JobDetails, Has.Exactly(1).Items);
        });

        var trigger = quartzOptions.Triggers.Single();
        var job = quartzOptions.JobDetails.Single();

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

        // Go through AddQuartz(), because the IServiceCollectionQuartzConfigurator interface refuses mocking or implementation, due to an internal default-implemented property
        services.AddQuartz(quartz =>
        {
            quartz.AddCalendar<DummyCalendar>(
                "TestCalendarName",
                true,
                true,
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

        // Go through AddQuartz(), because the IServiceCollectionQuartzConfigurator interface refuses mocking or implementation, due to an internal default-implemented property
        services.AddQuartz(quartz =>
        {
            quartz.AddCalendar<DummyCalendar>(
                "TestCalendarName",
                true,
                true,
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
    public void ConfiguredDbDataSource_ShouldBeUsed()
    {
        var services = new ServiceCollection();

        services.AddNpgsqlDataSource("Host=myserver;Username=mylogin;Password=mypass;Database=mydatabase");
        services.AddQuartz(quartz =>
        {
            quartz.AddDataSourceProvider();

            quartz.UsePersistentStore(p =>
            {
                p.UsePostgres("default", c => c.UseDataSourceConnectionProvider());
            });
        });

        var provider = services.BuildServiceProvider();

        Assert.That(provider.GetService<IDbProvider>(), Is.TypeOf<DataSourceDbProvider>());

        var quartzOptions = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(quartzOptions.ContainsKey("quartz.dataSource.default.connectionProvider.type"));
            Assert.That(quartzOptions["quartz.dataSource.default.connectionProvider.type"], Is.EqualTo(typeof(DataSourceDbProvider).AssemblyQualifiedNameWithoutVersion()));
        });
    }

    private sealed class DummyJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context)
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