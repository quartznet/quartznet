using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;

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

        // The job key should have its own manual configuration
        Assert.AreEqual("JobName1", job1.Key.Name);
        Assert.AreEqual("JobGroup1", job1.Key.Group);
        Assert.AreEqual("JobDescription1", job1.Description);

        Assert.AreEqual("JobName2", job2.Key.Name);
        Assert.AreEqual("JobGroup2", job2.Key.Group);
        Assert.AreEqual("JobDescription2", job2.Description);

        Assert.AreEqual("JobName3", job3.Key.Name);
        Assert.AreEqual("JobGroup3", job3.Key.Group);
        Assert.AreEqual("JobDescription3", job3.Description);

        Assert.AreEqual("JobName4", job4.Key.Name);
        Assert.AreEqual("JobGroup4", job4.Key.Group);
        Assert.AreEqual("JobDescription4", job4.Description);

        Assert.AreEqual("JobName5", job5.Key.Name);
        Assert.AreEqual("JobGroup5", job5.Key.Group);
        Assert.AreEqual("JobDescription5", job5.Description);

        Assert.AreEqual("JobName6", job6.Key.Name);
        Assert.AreEqual("JobGroup6", job6.Key.Group);
        Assert.AreEqual("JobDescription6", job6.Description);

        Assert.AreEqual("JobName7", job7.Key.Name);
        Assert.AreEqual("JobGroup7", job7.Key.Group);
        Assert.AreEqual("JobDescription7", job7.Description);

        Assert.AreEqual("JobName8", job8.Key.Name);
        Assert.AreEqual("JobGroup8", job8.Key.Group);
        Assert.AreEqual("JobDescription8", job8.Description);
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

        // The trigger key should have its own manual configuration
        Assert.AreEqual("TriggerName1", trigger1.Key.Name);
        Assert.AreEqual("TriggerGroup1", trigger1.Key.Group);

        Assert.AreEqual("TriggerName2", trigger2.Key.Name);
        Assert.AreEqual("TriggerGroup2", trigger2.Key.Group);

        Assert.AreEqual("JobName1", trigger1.JobKey.Name);
        Assert.AreEqual("JobGroup1", trigger1.JobKey.Group);

        Assert.AreEqual("JobName2", trigger2.JobKey.Name);
        Assert.AreEqual("JobGroup2", trigger2.JobKey.Group);
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

        Assert.That(quartzOptions.Triggers, Has.Exactly(2).Items);
        Assert.That(quartzOptions.JobDetails, Has.Exactly(2).Items);

        var trigger1 = quartzOptions.Triggers[0];
        var trigger2 = quartzOptions.Triggers[1];
        var job1 = quartzOptions.JobDetails[0];
        var job2 = quartzOptions.JobDetails[1];

        // The trigger key should have its own manual configuration
        Assert.AreEqual("TriggerName1", trigger1.Key.Name);
        Assert.AreEqual("TriggerGroup1", trigger1.Key.Group);

        Assert.AreEqual("TriggerName2", trigger2.Key.Name);
        Assert.AreEqual("TriggerGroup2", trigger2.Key.Group);

        // The job key should have its own manual configuration
        Assert.AreEqual("JobName1", job1.Key.Name);
        Assert.AreEqual("JobGroup1", job1.Key.Group);

        Assert.AreEqual("JobName2", job2.Key.Name);
        Assert.AreEqual("JobGroup2", job2.Key.Group);

        // Also validate that the trigger knows the correct job key
        Assert.AreEqual(job1.Key.Name, trigger1.JobKey.Name);
        Assert.AreEqual(job1.Key.Group, trigger1.JobKey.Group);

        Assert.AreEqual(job2.Key.Name, trigger2.JobKey.Name);
        Assert.AreEqual(job2.Key.Group, trigger2.JobKey.Group);
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

        Assert.That(quartzOptions.Triggers, Has.Exactly(1).Items);
        Assert.That(quartzOptions.JobDetails, Has.Exactly(1).Items);

        var trigger = quartzOptions.Triggers.Single();
        var job = quartzOptions.JobDetails.Single();

        // The job's key should match the trigger's (auto-generated) key
        Assert.AreEqual(trigger.Key.Name, job.Key.Name);
        Assert.AreEqual(trigger.Key.Group, job.Key.Group);

        // Also validate that the trigger knows the correct job key
        Assert.AreEqual(job.Key.Name, trigger.JobKey.Name);
        Assert.AreEqual(job.Key.Group, trigger.JobKey.Group);
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

        Assert.That(quartzOptions.Triggers, Has.Exactly(1).Items);
        Assert.That(quartzOptions.JobDetails, Has.Exactly(1).Items);

        var trigger = quartzOptions.Triggers.Single();
        var job = quartzOptions.JobDetails.Single();

        // The trigger key should have its own manual configuration
        Assert.AreEqual("TriggerName", trigger.Key.Name);
        Assert.AreEqual("TriggerGroup", trigger.Key.Group);

        // The job's key should match the trigger's (auto-generated) key
        Assert.AreEqual(trigger.Key.Name, job.Key.Name);
        Assert.AreEqual(trigger.Key.Group, job.Key.Group);

        // Also validate that the trigger knows the correct job key
        Assert.AreEqual(job.Key.Name, trigger.JobKey.Name);
        Assert.AreEqual(job.Key.Group, trigger.JobKey.Group);
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

        Assert.AreEqual("TestCalendarName", calendarConfiguration.Name);
        Assert.AreEqual("TestCalendarDescription", calendarConfiguration.Calendar.Description);
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

        Assert.AreEqual("TestCalendarName", calendarConfiguration.Name);
        Assert.AreEqual("TestCalendarDescription", calendarConfiguration.Calendar.Description);
    }

#if NET8_0_OR_GREATER
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
                p.UsePostgres(c => c.UseDataSourceConnectionProvider());
            });
        });

        var provider = services.BuildServiceProvider();

        Assert.That(provider.GetService<IDbProvider>(), Is.TypeOf<DataSourceDbProvider>());

        var quartzOptions = provider.GetRequiredService<IOptions<QuartzOptions>>().Value;

        Assert.That(quartzOptions.ContainsKey($"quartz.dataSource.{SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}.connectionProvider.type"));
        Assert.That(quartzOptions[$"quartz.dataSource.{SchedulerBuilder.AdoProviderOptions.DefaultDataSourceName}.connectionProvider.type"], Is.EqualTo(typeof(DataSourceDbProvider).AssemblyQualifiedNameWithoutVersion()));
    }
#endif

    private sealed class DummyJob : IJob
    {
        public Task Execute(IJobExecutionContext context)
        {
            return Task.CompletedTask;
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