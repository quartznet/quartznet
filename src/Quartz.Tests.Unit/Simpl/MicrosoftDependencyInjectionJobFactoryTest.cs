using AwesomeAssertions.Execution;

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Impl.Triggers;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Simpl;

[NonParallelizable]
public class MicrosoftDependencyInjectionJobFactoryTest
{
    [Test]
    [Ignore("WIP")]
    public async Task DisposedServiceProviderShouldThrowSchedulerException()
    {
        var factory = new MicrosoftDependencyInjectionJobFactory(new TestServiceProvider());
        await factory.CreateJob(TestUtil.NewMinimalTriggerFiredBundle(), null!);
    }

    [Test]
    public async Task ShouldHandOutTheJobItselfRatherThanAWrapper()
    {
        // The factory used to hide the job inside an IJobWrapper so it had somewhere to keep the
        // DI scope. The scope now travels as JobScope.State, so what the scheduler — and therefore
        // every listener and IJobExecutionContext.JobInstance — sees is the user's own type.
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTransient<ScopedJob>();
        serviceCollection.AddScoped<ScopedDependency>();
        await using var serviceProvider = serviceCollection.BuildServiceProvider(validateScopes: true);

        var factory = new MicrosoftDependencyInjectionJobFactory(serviceProvider);
        var scope = await factory.CreateJob(NewBundleFor<ScopedJob>(), NewScheduler());

        try
        {
            scope.Job.Should().BeOfType<ScopedJob>();
            scope.State.Should().NotBeNull("the DI scope has to survive until the job is returned");
        }
        finally
        {
            await factory.ReturnJob(scope);
        }
    }

    [Test]
    public async Task ShouldDisposeScopedDependenciesWhenJobIsReturned()
    {
        ScopedDependency.Disposed = false;

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTransient<ScopedJob>();
        serviceCollection.AddScoped<ScopedDependency>();
        await using var serviceProvider = serviceCollection.BuildServiceProvider(validateScopes: true);

        var factory = new MicrosoftDependencyInjectionJobFactory(serviceProvider);

        var scope = await factory.CreateJob(NewBundleFor<ScopedJob>(), NewScheduler());
        ScopedDependency.Disposed.Should().BeFalse("the scope is still open while the job runs");

        await factory.ReturnJob(scope);

        ScopedDependency.Disposed.Should().BeTrue("returning the job has to close the scope it was built in");
    }

    private static TriggerFiredBundle NewBundleFor<T>() where T : IJob
    {
        return TestUtil.CreateMinimalFiredBundleWithTypedJobDetail(
            typeof(T),
            new SimpleTriggerImpl("triggerName", "triggerGroup"));
    }

    private static IScheduler NewScheduler()
    {
        var scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.Context).Returns(new SchedulerContext());
        return scheduler;
    }

    private sealed class ScopedJob : IJob
    {
        public ScopedJob(ScopedDependency dependency)
        {
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    private sealed class ScopedDependency : IDisposable
    {
        public static bool Disposed { get; set; }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    [Test]
    public async Task JobsShouldBeDisposedAfterExecute()
    {
        var schedulerBuilder = QuartzSchedulerBuilder.Create()
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
        scheduler.JobFactory = new MicrosoftDependencyInjectionJobFactory(serviceProvider);
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

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
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