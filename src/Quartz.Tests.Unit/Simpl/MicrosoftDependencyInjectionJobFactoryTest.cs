using System.Collections.Concurrent;

using AwesomeAssertions.Execution;

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Impl;
using Quartz.Impl.Triggers;
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

        await act.Should().ThrowAsync<InvalidOperationException>(
            "ActivatorUtilities throws that when a constructor parameter cannot be resolved");
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
    public async Task ShouldFlowAsyncLocalSetInConfigureScopeThroughCreateJob()
    {
        // ConfigureScope exists to establish ambient context for a job, and an AsyncLocal written
        // there has to survive into Execute (#1528). It does not if the factory is an async method,
        // because the state machine restores the caller's ExecutionContext when its synchronous part
        // returns - which is why CreateJobInstance and CreateJob are deliberately not async.
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTransient<TenantFlowJob>();
        await using var serviceProvider = serviceCollection.BuildServiceProvider(validateScopes: true);

        var factory = new TenantSettingJobFactory(serviceProvider);
        var scope = await factory.CreateJob(NewBundleFor<TenantFlowJob>(), NewScheduler());

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

    private sealed class TenantFlowJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    private static TriggerFiredBundle NewBundleFor<T>() where T : IJob
    {
        return TestUtil.CreateMinimalFiredBundleWithTypedJobDetail(
            typeof(T),
            new SimpleTriggerImpl { Key = new TriggerKey("triggerName", "triggerGroup"), StartTimeUtc = TimeProvider.System.GetUtcNow() });
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
        TestJob.Reset();
        Dependency.Reset();

        const string testValue = "test";

        var jobDetail = JobBuilder.Create<TestJob>()
            .StoreDurably()
            .UsingJobData(nameof(TestJob.Test), testValue)
            .Build();

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddTransient<TestJob>();
        serviceCollection.AddTransient<Dependency>();
        var serviceProvider = serviceCollection.BuildServiceProvider(validateScopes: true);

        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create();
        builder.UseJobFactory(new MicrosoftDependencyInjectionJobFactory(serviceProvider));

        ISchedulerFactory schedulerFactory = builder.Build();
        IScheduler scheduler = await schedulerFactory.GetScheduler();
        await scheduler.Start();

        await scheduler.AddJob(jobDetail);
        await scheduler.TriggerJob(jobDetail.Key);

        // Every observable step signals, so the assertions run once the firing is genuinely over
        // rather than once an arbitrary delay has elapsed.
        await Task.WhenAll(
                TestJob.ExecutedSignal.Task,
                TestJob.DisposedSignal.Task,
                Dependency.DisposedSignal.Task)
            .WaitAsync(TimeSpan.FromSeconds(10));

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

        public static TaskCompletionSource ExecutedSignal { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public static TaskCompletionSource DisposedSignal { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static void Reset()
        {
            Executed = false;
            Disposed = false;
            ExecutedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            DisposedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public string Test { get; set; }

        public TestJob(Dependency dependency)
        {
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Executed = true;
            TestValue = Test;
            ExecutedSignal.TrySetResult();
            return new ValueTask();
        }

        public void Dispose()
        {
            Disposed = true;
            DisposedSignal.TrySetResult();
        }
    }

    private sealed class Dependency : IDisposable
    {
        public static bool Disposed { get; set; }

        public static TaskCompletionSource DisposedSignal { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static void Reset()
        {
            Disposed = false;
            DisposedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void Dispose()
        {
            Disposed = true;
            DisposedSignal.TrySetResult();
        }
    }

    private sealed class TestServiceProvider : IServiceProvider
    {
        public object GetService(Type serviceType)
        {
            return Activator.CreateInstance(serviceType);
        }
    }

    [Test]
    public async Task ShouldDisposeScopeAndJobWhenExecuteThrows()
    {
        // A job that fails is exactly the case where a leaked scope is easiest to miss: the failure
        // takes a different path out of the run shell, and ReturnJob still has to happen.
        FailingJob.Reset();
        FailingJobDependency.Reset();

        ServiceCollection serviceCollection = [];
        serviceCollection.AddTransient<FailingJob>();
        serviceCollection.AddScoped<FailingJobDependency>();
        await using ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider(validateScopes: true);

        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create();
        builder.ConfigureScheduler(options => options.InstanceName = "dijobexecutethrows")
            .UseJobFactory(new MicrosoftDependencyInjectionJobFactory(serviceProvider));

        IScheduler scheduler = await builder.BuildScheduler();

        try
        {
            IJobDetail jobDetail = JobBuilder.Create<FailingJob>()
                .WithIdentity("job", "dijobexecutethrows")
                .StoreDurably()
                .Build();

            await scheduler.Start();
            await scheduler.AddJob(jobDetail);
            await scheduler.TriggerJob(jobDetail.Key);

            await Task.WhenAll(
                    FailingJob.ExecutedSignal.Task,
                    FailingJob.DisposedSignal.Task,
                    FailingJobDependency.DisposedSignal.Task)
                .WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        using (new AssertionScope())
        {
            FailingJob.Executed.Should().BeTrue("the job has to have run for this test to prove anything");
            FailingJob.Disposed.Should().BeTrue(
                "the container-resolved job is registered with the scope, so closing the scope has to dispose it even though it threw");
            FailingJobDependency.Disposed.Should().BeTrue(
                "a job that throws must still have its scope closed, or every failing firing leaks its scoped dependencies");
        }
    }

    [Test]
    public async Task ShouldBuildADistinctJobAndScopePerFiring()
    {
        // Two firings are two scopes: sharing either the job or a scoped dependency between them
        // would let one firing see the other's state.
        PerFiringJob.Reset();

        ServiceCollection serviceCollection = [];
        serviceCollection.AddTransient<PerFiringJob>();
        serviceCollection.AddScoped<PerFiringDependency>();
        await using ServiceProvider serviceProvider = serviceCollection.BuildServiceProvider(validateScopes: true);

        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create();
        builder.ConfigureScheduler(options => options.InstanceName = "diinstanceperfiring")
            .UseJobFactory(new MicrosoftDependencyInjectionJobFactory(serviceProvider));

        IScheduler scheduler = await builder.BuildScheduler();

        try
        {
            IJobDetail jobDetail = JobBuilder.Create<PerFiringJob>()
                .WithIdentity("job", "diinstanceperfiring")
                .StoreDurably()
                .Build();

            await scheduler.Start();
            await scheduler.AddJob(jobDetail);
            await scheduler.TriggerJob(jobDetail.Key);
            await scheduler.TriggerJob(jobDetail.Key);

            await PerFiringJob.BothFired.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        List<PerFiringJob.Firing> firings = PerFiringJob.Firings.ToList();

        using (new AssertionScope())
        {
            firings.Should().HaveCount(2, "both triggerings have to have reached the job");
            firings[0].Job.Should().NotBeSameAs(firings[1].Job,
                "a transient job has to be built afresh for every firing, otherwise two firings share its fields");
            firings[0].Dependency.Should().NotBeSameAs(firings[1].Dependency,
                "each firing gets its own dependency injection scope, so its scoped services are its own");
        }
    }

    private sealed class FailingJob : IJob, IDisposable
    {
        public static bool Executed { get; set; }
        public static bool Disposed { get; set; }

        public static TaskCompletionSource ExecutedSignal { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public static TaskCompletionSource DisposedSignal { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static void Reset()
        {
            Executed = false;
            Disposed = false;
            ExecutedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            DisposedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public FailingJob(FailingJobDependency dependency)
        {
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Executed = true;
            ExecutedSignal.TrySetResult();
            throw new InvalidOperationException("job failed");
        }

        public void Dispose()
        {
            Disposed = true;
            DisposedSignal.TrySetResult();
        }
    }

    private sealed class FailingJobDependency : IDisposable
    {
        public static bool Disposed { get; set; }

        public static TaskCompletionSource DisposedSignal { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static void Reset()
        {
            Disposed = false;
            DisposedSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public void Dispose()
        {
            Disposed = true;
            DisposedSignal.TrySetResult();
        }
    }

    private sealed class PerFiringJob : IJob
    {
        private static int fireCount;

        public static ConcurrentBag<Firing> Firings { get; private set; } = [];
        public static TaskCompletionSource BothFired { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly PerFiringDependency dependency;

        public PerFiringJob(PerFiringDependency dependency)
        {
            this.dependency = dependency;
        }

        public static void Reset()
        {
            Firings = [];
            BothFired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fireCount = 0;
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Firings.Add(new Firing(this, dependency));

            if (Interlocked.Increment(ref fireCount) == 2)
            {
                BothFired.TrySetResult();
            }

            return default;
        }

        internal sealed record Firing(PerFiringJob Job, PerFiringDependency Dependency);
    }

    private sealed class PerFiringDependency
    {
    }
}