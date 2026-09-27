#nullable enable

using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// A job that takes nothing from the container is built without a dependency injection scope (#3866),
/// and every other job keeps the scope it always had.
/// </summary>
[NonParallelizable]
public sealed class UnscopedJobConstructionTest
{
    [Test]
    public async Task AJobThatTakesNothingFromTheContainerIsBuiltWithoutAScope()
    {
        ServiceCollection services = [];
        await using ServiceProvider root = services.BuildServiceProvider(validateScopes: true);
        ScopeCountingProvider container = new(root);

        MicrosoftDependencyInjectionJobFactory factory = new(container);
        JobScope scope = await factory.CreateJob(BundleFor<DisposableJob>(), NewScheduler());

        DisposableJob job = scope.Job.Should().BeOfType<DisposableJob>().Subject;
        container.ScopesOpened.Should().Be(0,
            "an unregistered job with only a parameterless constructor resolves nothing, so a scope could not change it");

        await factory.ReturnJob(scope);

        job.DisposeCalls.Should().Be(1,
            "the scope used to be what disposed a job the factory activated, so returning the job has to do it instead");
    }

    [Test]
    public async Task AJobBuiltWithoutAScopeIsDisposedAsynchronouslyWhenItCanBe()
    {
        ServiceCollection services = [];
        await using ServiceProvider root = services.BuildServiceProvider(validateScopes: true);
        ScopeCountingProvider container = new(root);

        MicrosoftDependencyInjectionJobFactory factory = new(container);
        JobScope scope = await factory.CreateJob(BundleFor<AsyncDisposableJob>(), NewScheduler());
        AsyncDisposableJob job = scope.Job.Should().BeOfType<AsyncDisposableJob>().Subject;

        await factory.ReturnJob(scope);

        container.ScopesOpened.Should().Be(0);
        job.AsyncDisposeCalls.Should().Be(1, "IAsyncDisposable is preferred, exactly as the scope path prefers it");
        job.DisposeCalls.Should().Be(0, "a job disposed asynchronously is not disposed a second time synchronously");
    }

    [Test]
    public async Task ConfigureScopeKeepsTheScope()
    {
        ServiceCollection services = [];
        await using ServiceProvider root = services.BuildServiceProvider(validateScopes: true);
        ScopeCountingProvider container = new(root);

        List<string> prepared = [];
        JobFactoryOptions options = new()
        {
            ConfigureScope = (_, bundle, _) => prepared.Add(bundle.JobDetail.Key.Name)
        };

        MicrosoftDependencyInjectionJobFactory factory = new(container, Options.Create(options));
        JobScope scope = await factory.CreateJob(BundleFor<DisposableJob>(), NewScheduler());
        await factory.ReturnJob(scope);

        container.ScopesOpened.Should().Be(1, "ConfigureScope is handed the scope, so a job with a hook set is built in one");
        prepared.Should().Equal(["jobName"], "the hook still runs, once, for the firing it prepares");
        ((DisposableJob) scope.Job).DisposeCalls.Should().Be(1);
    }

    [Test]
    public async Task ConfigureScopeAddedAfterTheFactoryWasBuiltStillGetsItsScope()
    {
        ServiceCollection services = [];
        await using ServiceProvider root = services.BuildServiceProvider(validateScopes: true);
        ScopeCountingProvider container = new(root);

        JobFactoryOptions options = new();
        MicrosoftDependencyInjectionJobFactory factory = new(container, Options.Create(options));

        await factory.ReturnJob(await factory.CreateJob(BundleFor<DisposableJob>(), NewScheduler()));
        container.ScopesOpened.Should().Be(0);

        bool prepared = false;
        options.ConfigureScope = (_, _, _) => prepared = true;

        await factory.ReturnJob(await factory.CreateJob(BundleFor<DisposableJob>(), NewScheduler()));

        container.ScopesOpened.Should().Be(1,
            "the hook is read per firing, as the scope path reads it, so one added to the shared options later is honoured");
        prepared.Should().BeTrue();
    }

    [Test]
    public async Task TheJobScopeHookConfiguredOnTheBuilderRunsForAJobTheContainerDoesNotHold()
    {
        List<string> prepared = [];

        ServiceCollection services = [];
        services.AddQuartz(quartz => quartz.ConfigureJobScope((_, bundle, _) => prepared.Add(bundle.JobDetail.Key.Name)));
        await using ServiceProvider provider = services.BuildServiceProvider();

        IJobFactory factory = provider.GetRequiredService<IJobFactory>();
        factory.Should().BeOfType<MicrosoftDependencyInjectionJobFactory>();

        JobScope scope = await factory.CreateJob(BundleFor<DisposableJob>(), NewScheduler());
        await factory.ReturnJob(scope);

        prepared.Should().Equal(["jobName"],
            "ConfigureJobScope is the same hook as the options delegate, so it keeps the job in a scope and runs");
    }

    [TestCase(ServiceLifetime.Scoped)]
    [TestCase(ServiceLifetime.Transient)]
    [TestCase(ServiceLifetime.Singleton)]
    public async Task ARegisteredJobIsStillResolvedFromTheContainer(ServiceLifetime lifetime)
    {
        IServiceCollection services = new ServiceCollection();
        services.Add(new ServiceDescriptor(typeof(DisposableJob), _ => new DisposableJob { FromContainer = true }, lifetime));
        await using ServiceProvider root = services.BuildServiceProvider(validateScopes: true);
        ScopeCountingProvider container = new(root);

        MicrosoftDependencyInjectionJobFactory factory = new(container);
        JobScope scope = await factory.CreateJob(BundleFor<DisposableJob>(), NewScheduler());
        DisposableJob job = scope.Job.Should().BeOfType<DisposableJob>().Subject;

        await factory.ReturnJob(scope);

        job.FromContainer.Should().BeTrue("a registration is the application saying how the job is built, and it is honoured");
        container.ScopesOpened.Should().Be(1, "a registered job is resolved from the firing's scope, as it always was");

        if (lifetime == ServiceLifetime.Singleton)
        {
            job.Should().BeSameAs(root.GetRequiredService<DisposableJob>());
            job.DisposeCalls.Should().Be(0, "a singleton belongs to the container, and returning the job must not dispose it");
        }
        else
        {
            job.DisposeCalls.Should().Be(1, "the scope owns what it resolved and disposes it when it closes");
        }
    }

    [Test]
    public async Task AJobKeyedUnderItsSchedulerIsStillResolvedFromTheContainer()
    {
        ServiceCollection services = [];
        services.AddKeyedScoped("reporting", (_, _) => new DisposableJob { FromContainer = true });
        await using ServiceProvider root = services.BuildServiceProvider(validateScopes: true);
        ScopeCountingProvider container = new(root);

        MicrosoftDependencyInjectionJobFactory factory = new(SchedulerScopedServiceProvider.For(container, "reporting"));
        JobScope scope = await factory.CreateJob(BundleFor<DisposableJob>(), NewScheduler());
        await factory.ReturnJob(scope);

        ((DisposableJob) scope.Job).FromContainer.Should().BeTrue(
            "AddJobType<T> registers the job keyed under its scheduler, and that registration is what builds it");
        container.ScopesOpened.Should().Be(1);
    }

    [Test]
    public async Task ANamedSchedulerBuildsAJobItsContainerDoesNotHoldWithoutAScope()
    {
        ServiceCollection services = [];

        // Keyed under another scheduler, which is a registration this one never consults.
        services.AddKeyedScoped("billing", (_, _) => new DisposableJob { FromContainer = true });
        await using ServiceProvider root = services.BuildServiceProvider(validateScopes: true);
        ScopeCountingProvider container = new(root);

        MicrosoftDependencyInjectionJobFactory factory = new(SchedulerScopedServiceProvider.For(container, "reporting"));
        JobScope scope = await factory.CreateJob(BundleFor<DisposableJob>(), NewScheduler());
        await factory.ReturnJob(scope);

        ((DisposableJob) scope.Job).FromContainer.Should().BeFalse();
        container.ScopesOpened.Should().Be(0,
            "the scope path would ask only for this scheduler's key and the unkeyed registration, and there is neither");
    }

    [Test]
    public async Task AJobWhoseConstructorTakesAServiceKeepsTheScope()
    {
        ServiceCollection services = [];
        services.AddScoped<Dependency>();
        await using ServiceProvider root = services.BuildServiceProvider(validateScopes: true);
        ScopeCountingProvider container = new(root);

        MicrosoftDependencyInjectionJobFactory factory = new(container);
        JobScope scope = await factory.CreateJob(BundleFor<DependentJob>(), NewScheduler());
        await factory.ReturnJob(scope);

        ((DependentJob) scope.Job).Dependency.Should().NotBeNull();
        container.ScopesOpened.Should().Be(1, "a constructor that takes a service takes it from the firing's scope");
    }

    [Test]
    public async Task AJobWithASecondPublicConstructorKeepsTheScope()
    {
        ServiceCollection services = [];
        services.AddScoped<Dependency>();
        await using ServiceProvider root = services.BuildServiceProvider(validateScopes: true);
        ScopeCountingProvider container = new(root);

        MicrosoftDependencyInjectionJobFactory factory = new(container);
        JobScope scope = await factory.CreateJob(BundleFor<TwoConstructorJob>(), NewScheduler());
        await factory.ReturnJob(scope);

        ((TwoConstructorJob) scope.Job).Dependency.Should().NotBeNull(
            "ActivatorUtilities chooses between constructors, and here it chooses the one the container can supply");
        container.ScopesOpened.Should().Be(1);
    }

    [Test]
    public async Task AContainerThatCannotSayWhatItHoldsKeepsTheScope()
    {
        ServiceCollection services = [];
        await using ServiceProvider root = services.BuildServiceProvider(validateScopes: true);
        ScopeCountingProvider container = new(root, answersIsService: false);

        MicrosoftDependencyInjectionJobFactory factory = new(container);
        await factory.ReturnJob(await factory.CreateJob(BundleFor<DisposableJob>(), NewScheduler()));

        container.ScopesOpened.Should().Be(1,
            "without IServiceProviderIsService a registered job type cannot be told from an unregistered one");
    }

    [Test]
    public async Task ADerivedFactoryAlwaysOpensAScope()
    {
        ServiceCollection services = [];
        await using ServiceProvider root = services.BuildServiceProvider(validateScopes: true);
        ScopeCountingProvider container = new(root);

        DerivedFactory factory = new(container);
        JobScope scope = await factory.CreateJob(BundleFor<DisposableJob>(), NewScheduler());
        await factory.ReturnJob(scope);

        container.ScopesOpened.Should().Be(1,
            "a derived factory may override ConfigureScope or depend on the scope some other way, so it is left as it was");
        ((DisposableJob) scope.Job).DisposeCalls.Should().Be(1);
    }

    [Test]
    public async Task ADelegateJobIsAlwaysBuiltInAScope()
    {
        ServiceCollection services = [];
        await using ServiceProvider root = services.BuildServiceProvider(validateScopes: true);
        ScopeCountingProvider container = new(root);

        MicrosoftDependencyInjectionJobFactory factory = new(container);
        JobScope scope = await factory.CreateJob(BundleFor<DelegateJob>(), NewScheduler());
        await factory.ReturnJob(scope);

        scope.Job.Should().BeOfType<DelegateJob>();
        container.ScopesOpened.Should().Be(1,
            "a delegate job's constructor takes the firing's scope, which its handler's services come from");
    }

    [Test]
    public async Task AJobTypeKnownOnlyByNameIsBuiltInAScopeUntilItHasBeenLoaded()
    {
        ServiceCollection services = [];
        await using ServiceProvider root = services.BuildServiceProvider(validateScopes: true);
        ScopeCountingProvider container = new(root);

        MicrosoftDependencyInjectionJobFactory factory = new(container);
        TriggerFiredBundle bundle = BundleNamed(typeof(DisposableJob));

        await factory.ReturnJob(await factory.CreateJob(bundle, NewScheduler()));
        container.ScopesOpened.Should().Be(1,
            "resolving the name is the scope path's job, including reporting a name that resolves to nothing");

        await factory.ReturnJob(await factory.CreateJob(bundle, NewScheduler()));
        container.ScopesOpened.Should().Be(1, "once the name has been resolved the type is known, and needs no scope");
    }

    [Test]
    public async Task ANamedTypeThatIsNotAJobIsRefusedOnEveryFiring()
    {
        ServiceCollection services = [];
        await using ServiceProvider root = services.BuildServiceProvider(validateScopes: true);

        MicrosoftDependencyInjectionJobFactory factory = new(root);
        TriggerFiredBundle bundle = BundleNamed(typeof(NotAJob));
        NotAJob.Constructed = 0;

        for (int firing = 0; firing < 2; firing++)
        {
            Func<Task> act = async () => await factory.CreateJob(bundle, NewScheduler());

            await act.Should().ThrowAsync<SchedulerException>().WithMessage($"*{typeof(NotAJob).FullName}*",
                "the name resolved on the first firing, and a type that is not a job is still not one on the second");
        }

        NotAJob.Constructed.Should().Be(0, "nothing of a type that is not a job may run, with a scope or without one");
    }

    [Test]
    public async Task AConstructorThatThrowsFaultsTheSameWayWithOrWithoutAScope()
    {
        ServiceCollection services = [];
        await using ServiceProvider root = services.BuildServiceProvider(validateScopes: true);

        foreach (MicrosoftDependencyInjectionJobFactory factory in new[] { new MicrosoftDependencyInjectionJobFactory(root), new DerivedFactory(root) })
        {
            ValueTask<JobScope> creating = default;
            Action create = () => creating = factory.CreateJob(BundleFor<ThrowingConstructorJob>(), NewScheduler());

            create.Should().NotThrow("a constructor that throws faults the returned task rather than throwing out of CreateJob");

            Func<Task> act = async () => await creating;
            await act.Should().ThrowExactlyAsync<InvalidOperationException>().WithMessage("constructor failed",
                "the constructor's own exception reaches the run shell unwrapped, whichever path built the job");
        }
    }

    [Test]
    public async Task JobDataIsAppliedToAJobBuiltWithoutAScope()
    {
        ServiceCollection services = [];
        await using ServiceProvider root = services.BuildServiceProvider(validateScopes: true);
        ScopeCountingProvider container = new(root);

        MicrosoftDependencyInjectionJobFactory factory = new(container);
        TriggerFiredBundle bundle = BundleFor<DisposableJob>(new JobDataMap { ["Greeting"] = "hello" });

        JobScope scope = await factory.CreateJob(bundle, NewScheduler());
        await factory.ReturnJob(scope);

        ((DisposableJob) scope.Job).Greeting.Should().Be("hello", "the job data map is applied whichever way the job was built");
        container.ScopesOpened.Should().Be(0);
    }

    [Test]
    public async Task AJobWhoseDataDoesNotMatchIsDisposedBeforeTheFailureSurfaces()
    {
        ServiceCollection services = [];
        await using ServiceProvider root = services.BuildServiceProvider(validateScopes: true);

        MicrosoftDependencyInjectionJobFactory factory = new(root)
        {
            PropertyMismatchBehavior = PropertyMismatchBehavior.Throw
        };

        DisposableJob.LastBuilt = null;
        Func<Task> act = async () => await factory.CreateJob(
            BundleFor<DisposableJob>(new JobDataMap { ["NoSuchProperty"] = "value" }),
            NewScheduler());

        await act.Should().ThrowAsync<SchedulerException>();
        DisposableJob.LastBuilt.Should().NotBeNull();
        DisposableJob.LastBuilt!.DisposeCalls.Should().Be(1,
            "ReturnJob is not called when CreateJob throws, so the factory hands the job back on the way out");
    }

    [Test]
    public async Task AFiringOfAJobTheContainerDoesNotHoldRunsAndDisposesIt()
    {
        SignallingJob.Reset();

        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(quartz => quartz.ConfigureScheduler(options => options.InstanceName = "unscoped-job-construction"))
            .BuildScheduler();

        try
        {
            IJobDetail job = JobBuilder.Create<SignallingJob>()
                .WithIdentity("job", "unscoped-job-construction")
                .StoreDurably()
                .Build();

            await scheduler.Start();
            await scheduler.AddJob(job);
            await scheduler.TriggerJob(job.Key);

            await Task.WhenAll(SignallingJob.Executed.Task, SignallingJob.Disposed.Task).WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            await scheduler.Shutdown(waitForJobsToComplete: true);
        }

        SignallingJob.Disposed.Task.IsCompletedSuccessfully.Should().BeTrue(
            "the scheduler returns the job to the factory, which disposes a job it built without a scope");
    }

    private static TriggerFiredBundle BundleFor<T>(JobDataMap? data = null) where T : IJob
    {
        JobBuilder<IJob> builder = JobBuilder.Create()
            .OfType(typeof(T))
            .WithIdentity(new JobKey("jobName", "jobGroup"));

        if (data is not null)
        {
            builder = builder.UsingJobData(data);
        }

        return NewBundle(builder.Build());
    }

    private static TriggerFiredBundle BundleNamed(Type type)
    {
        IJobDetail jobDetail = JobBuilder.Create()
            .OfType((Quartz.JobType) type.AssemblyQualifiedName!)
            .WithIdentity(new JobKey("jobName", "jobGroup"))
            .Build();

        return NewBundle(jobDetail);
    }

    private static TriggerFiredBundle NewBundle(IJobDetail jobDetail)
    {
        return new TriggerFiredBundle
        {
            JobDetail = jobDetail,
            Trigger = new SimpleTriggerImpl { Key = new TriggerKey("triggerName", "triggerGroup"), StartTimeUtc = TimeProvider.System.GetUtcNow() },
            Recovering = false,
            FireTimeUtc = TimeProvider.System.GetUtcNow(),
            ScheduledFireTimeUtc = null,
            PreviousFireTimeUtc = null,
            NextFireTimeUtc = null
        };
    }

    private static IScheduler NewScheduler()
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.Context).Returns(new SchedulerContext());
        return scheduler;
    }

    /// <summary>
    /// The container, counting the scopes opened from it.
    /// </summary>
    private sealed class ScopeCountingProvider : IServiceProvider, IServiceScopeFactory
    {
        private readonly IServiceProvider inner;
        private readonly bool answersIsService;
        private int scopesOpened;

        public ScopeCountingProvider(IServiceProvider inner, bool answersIsService = true)
        {
            this.inner = inner;
            this.answersIsService = answersIsService;
        }

        public int ScopesOpened => Volatile.Read(ref scopesOpened);

        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceScopeFactory))
            {
                return this;
            }

            if (!answersIsService && (serviceType == typeof(IServiceProviderIsService) || serviceType == typeof(IServiceProviderIsKeyedService)))
            {
                return null;
            }

            return inner.GetService(serviceType);
        }

        public IServiceScope CreateScope()
        {
            Interlocked.Increment(ref scopesOpened);
            return inner.GetRequiredService<IServiceScopeFactory>().CreateScope();
        }
    }

    private sealed class DerivedFactory : MicrosoftDependencyInjectionJobFactory
    {
        public DerivedFactory(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }
    }

    public sealed class DisposableJob : IJob, IDisposable
    {
        public static DisposableJob? LastBuilt { get; set; }

        public DisposableJob()
        {
            LastBuilt = this;
        }

        public bool FromContainer { get; init; }

        public string? Greeting { get; set; }

        public int DisposeCalls { get; private set; }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

        public void Dispose() => DisposeCalls++;
    }

    public sealed class AsyncDisposableJob : IJob, IAsyncDisposable, IDisposable
    {
        public int AsyncDisposeCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;

        public ValueTask DisposeAsync()
        {
            AsyncDisposeCalls++;
            return default;
        }

        public void Dispose() => DisposeCalls++;
    }

    public sealed class Dependency
    {
    }

    public sealed class DependentJob : IJob
    {
        public DependentJob(Dependency dependency)
        {
            Dependency = dependency;
        }

        public Dependency Dependency { get; }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class TwoConstructorJob : IJob
    {
        public TwoConstructorJob()
        {
        }

        [ActivatorUtilitiesConstructor]
        public TwoConstructorJob(Dependency dependency)
        {
            Dependency = dependency;
        }

        public Dependency? Dependency { get; }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class ThrowingConstructorJob : IJob
    {
        public ThrowingConstructorJob()
        {
            throw new InvalidOperationException("constructor failed");
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    public sealed class NotAJob
    {
        public static int Constructed;

        public NotAJob()
        {
            Interlocked.Increment(ref Constructed);
        }
    }

    public sealed class SignallingJob : IJob, IDisposable
    {
        public static TaskCompletionSource Executed { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static TaskCompletionSource Disposed { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static void Reset()
        {
            Executed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Executed.TrySetResult();
            return default;
        }

        public void Dispose() => Disposed.TrySetResult();
    }
}
