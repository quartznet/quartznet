using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace Quartz.Tests.Unit.Extensions.DependencyInjection
{
    [TestFixture()]
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

            Assert.That(quartzOptions.Triggers, Has.Exactly(1).Items);
            Assert.That(quartzOptions.JobDetails, Has.Exactly(1).Items);

            var trigger = quartzOptions.Triggers.Single();
            var job = quartzOptions.JobDetails.Single();

            // The trigger key should have its own manual configuration
            Assert.AreEqual("TriggerName", trigger.Key.Name);
            Assert.AreEqual("TriggerGroup", trigger.Key.Group);

            // The job key should have its own manual configuration
            Assert.AreEqual("JobName", job.Key.Name);
            Assert.AreEqual("JobGroup", job.Key.Group);

            // Also validate that the trigger knows the correct job key
            Assert.AreEqual(job.Key.Name, trigger.JobKey.Name);
            Assert.AreEqual(job.Key.Group, trigger.JobKey.Group);
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

        private sealed class DummyJob : IJob
        {
            public Task Execute(IJobExecutionContext context)
            {
                return Task.CompletedTask;
            }
        }
    }
}
