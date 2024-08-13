using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Impl.AdoJobStore.Common;
using Quartz.Util;

namespace Quartz.Tests.Unit.Extensions.DependencyInjection;

public class ServiceCollectionExtensionsTests
{
    [Test]
    public void ScheduleJob_WithJobIdentity_ShouldHonorIt()
    {
        var services = new ServiceCollection();

        // Go through AddQuartz(), because the IServiceCollectionQuartzConfigurator interface refuses mocking or implementation, due to an internal default-implemented property
        services.AddQuartz(quartz => quartz.ScheduleJob<DummyJob>(
            trigger => trigger.WithIdentity("TriggerName", "TriggerGroup"),
            job => job.WithIdentity("JobName", "JobGroup")));

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

            // The job key should have its own manual configuration
            Assert.That(job.Key.Name, Is.EqualTo("JobName"));
            Assert.That(job.Key.Group, Is.EqualTo("JobGroup"));

            // Also validate that the trigger knows the correct job key
            Assert.That(trigger.JobKey.Name, Is.EqualTo(job.Key.Name));
            Assert.That(trigger.JobKey.Group, Is.EqualTo(job.Key.Group));
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
}