using System;
using System.Threading.Tasks;

using FluentAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using NUnit.Framework;

using Quartz.Simpl;

namespace Quartz.Tests.Unit.Simpl;

public class MicrosoftDependencyInjectionJobFactoryTest
{
    [Test]
    [Ignore("WIP")]
    public void DisposedServiceProviderShouldThrowSchedulerException()
    {
        var factory = new MicrosoftDependencyInjectionJobFactory(new TestServiceProvider(), Options.Create(new QuartzOptions()));
        factory.NewJob(TestUtil.NewMinimalTriggerFiredBundle(), null!);
    }

    [Test]
    public async Task JobsShouldBeDisposedAfterExecute()
    {
        var schedulerBuilder = SchedulerBuilder.Create()
            .Build();

        var jobDetail = JobBuilder.Create<TestJob>()
            .StoreDurably()
            .Build();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTransient<TestJob>();
        serviceCollection.AddTransient<Dependency>();
        var serviceProvider = serviceCollection.BuildServiceProvider(validateScopes: true);

        var scheduler = await schedulerBuilder.GetScheduler();
        scheduler.JobFactory = new MicrosoftDependencyInjectionJobFactory(serviceProvider, Options.Create(new QuartzOptions()));
        await scheduler.Start();

        await scheduler.AddJob(jobDetail, replace: false);
        await scheduler.TriggerJob(jobDetail.Key);

        await Task.Delay(100);

        TestJob.Executed.Should().BeTrue();
        TestJob.Disposed.Should().BeTrue();

        Dependency.Disposed.Should().BeTrue();
    }

    private class TestJob : IJob, IDisposable
    {
        public static bool Executed { get; set; }
        public static bool Disposed { get; set; }

        public TestJob(Dependency dependency)
        {
        }

        public Task Execute(IJobExecutionContext context)
        {
            Executed = true;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class Dependency : IDisposable
    {
        public static bool Disposed { get; set; }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private class TestServiceProvider : IServiceProvider
    {
        public object GetService(Type serviceType)
        {
            return Activator.CreateInstance(serviceType);
        }
    }
}