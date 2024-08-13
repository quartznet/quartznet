using FluentAssertions;
using FluentAssertions.Execution;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

        const string testValue = "test";

        var jobDetail = JobBuilder.Create<TestJob>()
            .StoreDurably()
            .UsingJobData(nameof(TestJob.Test), testValue)
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
        using (new AssertionScope())
        {
            TestJob.Executed.Should().BeTrue();
            TestJob.Disposed.Should().BeTrue();
            TestJob.TestValue.Should().Be(testValue);

            Dependency.Disposed.Should().BeTrue();
        }
    }

    private class TestJob : IJob, IDisposable
    {
        public static bool Executed { get; set; }
        public static bool Disposed { get; set; }
        public static string TestValue { get; set; }

        public string Test { get; set; }

        public TestJob(Dependency dependency)
        {
        }

        public ValueTask Execute(IJobExecutionContext context)
        {
            Executed = true;
            TestValue = Test;
            return new ValueTask();
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