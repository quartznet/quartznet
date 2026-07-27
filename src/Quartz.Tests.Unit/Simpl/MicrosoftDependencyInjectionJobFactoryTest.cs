using AwesomeAssertions.Execution;

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Impl.Triggers;
using Quartz.Impl;
using Quartz.Extensibility;

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

    [Test]
    public async Task ShouldDisposeTheScopeEvenWhenDisposingTheJobThrows()
    {
        ScopedDependency.Disposed = false;

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddScoped<ScopedDependency>();
        await using var serviceProvider = serviceCollection.BuildServiceProvider(validateScopes: true);

        // Not registered, so Quartz activates it and owns its disposal.
        var factory = new MicrosoftDependencyInjectionJobFactory(serviceProvider);
        var scope = await factory.CreateJob(NewBundleFor<ThrowsOnDisposeJob>(), NewScheduler());

        var act = async () => await factory.ReturnJob(scope);

        await act.Should().ThrowAsync<InvalidOperationException>("the job's failure must not be swallowed");
        ScopedDependency.Disposed.Should().BeTrue(
            "the scope has to be closed even when the job throws on the way out, or every such firing leaks it");
    }

    [Test]
    public async Task ShouldDisposeTheScopeWhenBuildingTheJobThrows()
    {
        ScopedDependency.Disposed = false;

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddScoped<ScopedDependency>();
        await using var serviceProvider = serviceCollection.BuildServiceProvider(validateScopes: true);

        var factory = new MicrosoftDependencyInjectionJobFactory(serviceProvider);

        // Depends on a service that is not registered, so activation fails after the scope is open.
        var act = async () => await factory.CreateJob(NewBundleFor<NeedsMissingDependencyJob>(), NewScheduler());

        await act.Should().ThrowAsync<Exception>();
        ScopedDependency.Disposed.Should().BeTrue(
            "ReturnJob is not called when CreateJob throws, so the scope has to be closed on the way out");
    }

    private sealed class ThrowsOnDisposeJob : IJob, IDisposable
    {
        public ThrowsOnDisposeJob(ScopedDependency dependency)
        {
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

        public void Dispose() => throw new InvalidOperationException("disposal failed");
    }

    private sealed class NeedsMissingDependencyJob : IJob
    {
        public NeedsMissingDependencyJob(ScopedDependency dependency, IDisposable notRegistered)
        {
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    [Test]
    public async Task ShouldFlowAsyncLocalSetInConfigureScopeThroughToTheJob()
    {
        // ConfigureScope exists to establish ambient context for a job, and an AsyncLocal written
        // there has to survive into Execute (#1528). It does not if the factory is an async method,
        // because the state machine restores the caller's ExecutionContext when its synchronous part
        // returns - which is why CreateJobInstance and CreateJob are deliberately not async.
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTransient<TenantCapturingJob>();
        await using var serviceProvider = serviceCollection.BuildServiceProvider(validateScopes: true);

        var factory = new TenantSettingJobFactory(serviceProvider);
        var scope = await factory.CreateJob(NewBundleFor<TenantCapturingJob>(), NewScheduler());

        try
        {
            TenantSettingJobFactory.Tenant.Value.Should().Be(
                "tenant-from-configure-scope",
                "the value ConfigureScope set must still be on the caller's execution context");
        }
        finally
        {
            await factory.ReturnJob(scope);
            TenantSettingJobFactory.Tenant.Value = null;
        }
    }

    private sealed class TenantSettingJobFactory : MicrosoftDependencyInjectionJobFactory
    {
        public static readonly AsyncLocal<string> Tenant = new();

        public TenantSettingJobFactory(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        protected override void ConfigureScope(IServiceScope scope, TriggerFiredBundle bundle, IScheduler scheduler)
        {
            Tenant.Value = "tenant-from-configure-scope";
        }
    }

    private sealed class TenantCapturingJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
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

    private sealed class TestJob : IJob, IDisposable
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

    private sealed class TestServiceProvider : IServiceProvider
    {
        public object GetService(Type serviceType)
        {
            return Activator.CreateInstance(serviceType);
        }
    }
}