using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Extensibility;

namespace Quartz.Impl;

/// <summary>
/// Integrates job instantiation with Microsoft DI system.
/// </summary>
/// <remarks>
/// A firing's job is built in a dependency injection scope of its own, and the scope is closed when the
/// job is returned. A job that takes nothing from the container is the exception: it is constructed
/// directly, and no scope is opened for it. <see cref="CreateJobInstance" /> lists what that takes.
/// </remarks>
public class MicrosoftDependencyInjectionJobFactory : PropertySettingJobFactory
{
    /// <summary>
    /// What a job built without a scope carries as <see cref="JobScope.State" />, so that
    /// <see cref="ReturnJob" /> knows the job is its to dispose and that there is no scope to close.
    /// </summary>
    private static readonly object Unscoped = new();

    private readonly IServiceProvider serviceProvider;
    private readonly JobActivatorCache activatorCache = new();
    private readonly JobFactoryOptions options;

    /// <summary>
    /// The service key this factory's scheduler registers its parts under, or <see langword="null" />
    /// for the default scheduler, whose registrations are the unkeyed ones.
    /// </summary>
    private readonly object? schedulerKey;

    /// <summary>
    /// Whether this instance is exactly this type. A derived factory may override
    /// <see cref="ConfigureScope" /> or rely on the scope in some other way, so it always gets one.
    /// </summary>
    private readonly bool mayBuildWithoutScope;

    /// <summary>
    /// Per job type, the constructor a job that takes nothing from the container is built with, or
    /// <see langword="null" /> when the type has to be built in a scope.
    /// </summary>
    /// <remarks>
    /// Kept per factory rather than beside <see cref="JobTypeInformation" />, because half the answer is
    /// whether this factory's container has a registration for the type, and two schedulers can have
    /// two containers. A built container's registrations do not change, so the answer is read once.
    /// </remarks>
    private readonly ConcurrentDictionary<Type, ConstructorInvoker?> unscopedConstructors = new();

    /// <param name="serviceProvider">The container jobs are built from.</param>
    /// <param name="options">
    /// The factory's settings, which is where <see cref="JobFactoryOptions.ConfigureScope"/> arrives from.
    /// Optional so that a derived factory constructing this one by hand does not have to supply it; the
    /// container always does.
    /// </param>
    /// <param name="loggerFactory">
    /// Where the base factories create their loggers, so that "producing instance of job" and a
    /// property that did not match reach the application's logging. Optional for the same reason
    /// <paramref name="options" /> is; the container always supplies it.
    /// </param>
    public MicrosoftDependencyInjectionJobFactory(
        IServiceProvider serviceProvider,
        IOptions<JobFactoryOptions>? options = null,
        ILoggerFactory? loggerFactory = null) : base(loggerFactory)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        this.options = options?.Value ?? new JobFactoryOptions();

        // Read once rather than per fire. A factory handed the raw container - by a caller constructing
        // one itself, or because this is the default scheduler, which has no wrapper - has no key, and
        // resolves jobs exactly as it always did.
        schedulerKey = (serviceProvider as SchedulerScopedServiceProvider)?.SchedulerServiceKey;

        mayBuildWithoutScope = GetType() == typeof(MicrosoftDependencyInjectionJobFactory);
    }

    /// <remarks>
    /// <para>
    /// Deliberately not an <c>async</c> method: an async state machine would restore the caller's
    /// <see cref="System.Threading.ExecutionContext" /> when its synchronous part returns, discarding
    /// any <see cref="System.Threading.AsyncLocal{T}" /> that <see cref="ConfigureScope" /> set — which
    /// is most of the reason that hook exists (#1528).
    /// </para>
    /// <para>
    /// A job is built without a scope when a scope could not make a difference to it: the factory is
    /// exactly this type rather than one derived from it, <see cref="JobFactoryOptions.ConfigureScope" />
    /// is not set, the container has no registration of the job type — keyed under this scheduler's
    /// name, or unkeyed — and the type's only public constructor takes no parameters. Such a job is
    /// constructed directly and disposed by <see cref="ReturnJob" />, which is what the scope did for it.
    /// Every other job is built in a scope as before.
    /// </para>
    /// </remarks>
    protected override ValueTask<JobScope> CreateJobInstance(
        TriggerFiredBundle bundle,
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        if (FindUnscopedConstructor(bundle) is { } constructor)
        {
            IJob job;
            try
            {
                job = (IJob) constructor.Invoke();
            }
            catch (Exception e)
            {
                // Faulted rather than thrown, which is how a constructor that throws in a scope surfaces.
                return ValueTask.FromException<JobScope>(e);
            }

            return new ValueTask<JobScope>(new JobScope(job, Unscoped));
        }

        //  Generate a scope for the job, this allows the job to be registered
        //	using .AddScoped<T>() which means we can use scoped dependencies
        //	e.g. database contexts
        var scope = serviceProvider.CreateScope();

        try
        {
            ConfigureScope(scope, bundle, scheduler);
            var (job, fromContainer) = ResolveJob(bundle, scope.ServiceProvider);

            // The scope rides along as the job's state so that ReturnJob can tear it down. The job
            // itself is handed to the scheduler unwrapped, so listeners and the execution context see
            // the type the user wrote rather than something of ours standing in front of it.
            return new ValueTask<JobScope>(new JobScope(job, new ScopeState(scope, job, disposeJob: !fromContainer)));
        }
        catch (Exception e)
        {
            // ReturnJob is not called when CreateJob throws, so the scope we just opened would be
            // abandoned - along with every scoped dependency already resolved into it.
            return DisposeScopeAndRethrow(scope, e);
        }

        static async ValueTask<JobScope> DisposeScopeAndRethrow(IServiceScope scope, Exception failure)
        {
            await DisposeScope(scope).ConfigureAwait(false);
            ExceptionDispatchInfo.Capture(failure).Throw();
            return default;
        }
    }

    /// <summary>
    /// Returns the job, closing the dependency injection scope it was built in.
    /// </summary>
    /// <remarks>
    /// The scope is carried in <see cref="JobScope.State" />, and it knows whether the job is its to
    /// dispose: one the container resolved is registered with the scope and disposed by it, while one
    /// this factory activated itself is not, and is disposed here. A job built without a scope is
    /// disposed here too, and there is no scope to close.
    /// <para>
    /// A derived factory that overrides <see cref="PropertySettingJobFactory.CreateJobInstance" /> and
    /// returns state of its own takes over that decision completely: this method will not touch the
    /// job, and it disposes the replacement state only if that state is itself disposable. The
    /// replacement's disposal must therefore cascade to the state this factory produced — wrap it
    /// rather than discard it; it is <see cref="IAsyncDisposable" /> for exactly that reason. A
    /// derived factory whose replacement state is not disposable must override this method as well
    /// and do its own teardown.
    /// </para>
    /// </remarks>
    public override ValueTask ReturnJob(JobScope scope, CancellationToken cancellationToken = default)
    {
        if (scope.State is ScopeState state)
        {
            // Disposes the job (only when we activated it) and then the scope, once.
            return state.DisposeAsync();
        }

        if (ReferenceEquals(scope.State, Unscoped))
        {
            // Built by this factory with nothing from the container, so nothing else will dispose it.
            return DisposeIfDisposable(scope.Job, cancellationToken);
        }

        // A derived factory replaced the state, so it owns the teardown. Dispose what it gave us and
        // leave the job alone: we can no longer tell whether the container owns it, and disposing one
        // it owns would hand user code a second Dispose call.
        return DisposeIfDisposable(scope.State, cancellationToken);
    }

    private static ValueTask DisposeScope(IServiceScope scope)
    {
        if (scope is IAsyncDisposable asyncDisposableScope)
        {
            return asyncDisposableScope.DisposeAsync();
        }

        scope.Dispose();
        return default;
    }

    /// <summary>
    /// Prepares the dependency injection scope a job is about to be built in.
    /// </summary>
    /// <remarks>
    /// The configuration point for services that are scoped and need the ambient context of a job. It
    /// runs before the job is resolved, and is synchronous so that an
    /// <see cref="System.Threading.AsyncLocal{T}" /> set here survives into <c>Execute</c>.
    /// <para>
    /// Overriding this is no longer the only way to reach it:
    /// <see cref="JobFactoryOptions.ConfigureScope" /> is the same hook as a delegate, for an application
    /// that has no other reason to write a job factory. An override that does not call base takes the
    /// delegate's place.
    /// </para>
    /// </remarks>
    protected virtual void ConfigureScope(IServiceScope scope, TriggerFiredBundle bundle, IScheduler scheduler)
    {
        options.ConfigureScope?.Invoke(scope, bundle, scheduler);
    }

    /// <summary>
    /// Produces the job instance for one fire: this scheduler's registration of the job type, then the
    /// container's, and failing both an instance this factory activates itself.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The keyed lookup is what lets two schedulers in one container build the same job type
    /// differently — <c>AddJobType&lt;T&gt;</c> is how that registration is made. It is skipped entirely
    /// for the default scheduler, which has no service key, so the single-scheduler case resolves in
    /// exactly one lookup as it always has.
    /// </para>
    /// <para>
    /// The unkeyed registration remains the fallback rather than being replaced, because it is where
    /// <c>AddJob&lt;T&gt;</c> puts the job type and where an application registering the type itself
    /// most naturally puts it. A scheduler that was given nothing of its own therefore still gets what
    /// the container holds.
    /// </para>
    /// </remarks>
    private (IJob Job, bool FromContainer) ResolveJob(TriggerFiredBundle bundle, IServiceProvider serviceProvider)
    {
        var jobType = bundle.JobDetail.JobType.ResolvedType;

        // Before the container is asked anything: GetService constructs whatever is registered for the
        // type it is handed, and a cast afterwards is a cast on an object that already exists.
        JobType.EnsureIsJob(jobType);

        var job = schedulerKey is null ? null : (IJob?) serviceProvider.GetKeyedService(jobType, schedulerKey);
        job ??= (IJob?) serviceProvider.GetService(jobType);

        if (job is not null)
        {
            // use the registered one
            return (job, true);
        }

        return (activatorCache.CreateInstance(serviceProvider, jobType), false);
    }

    /// <summary>
    /// The constructor to build this firing's job with when no scope is needed for it, or
    /// <see langword="null" /> when the job has to be built in a scope.
    /// </summary>
    /// <remarks>
    /// The type is taken only when something has already resolved it. A job type known by a name that
    /// has not been loaded yet goes through the scope, whose path loads it and reports it when it cannot
    /// be loaded, as it always has.
    /// </remarks>
    private ConstructorInvoker? FindUnscopedConstructor(TriggerFiredBundle bundle)
    {
        // Read per firing rather than once: the options instance is shared, and a hook added to it after
        // this factory was built is one the scope path would run.
        if (!mayBuildWithoutScope || options.ConfigureScope is not null)
        {
            return null;
        }

        if (bundle.JobDetail.JobType.GetLoadedType() is not { } jobType)
        {
            return null;
        }

        // Looked up before it is decided rather than through a GetOrAdd factory, for the reason
        // JobActivatorCache gives: a lambda's parameter carries no annotation.
        if (unscopedConstructors.TryGetValue(jobType, out ConstructorInvoker? constructor))
        {
            return constructor;
        }

        return unscopedConstructors.GetOrAdd(jobType, DecideUnscopedConstructor(jobType));
    }

    /// <summary>
    /// Whether a job type takes nothing from the container, answered with the constructor that builds it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every condition is one under which the scope path would build the job with nothing from the
    /// container: that path resolves a registered type from the scope, keyed under this scheduler's name
    /// first, and otherwise hands the type to <see cref="ActivatorUtilities" />, which for a single public
    /// constructor without parameters calls it and resolves nothing.
    /// </para>
    /// <para>
    /// A second public constructor keeps the scope, because <see cref="ActivatorUtilities" /> chooses
    /// between constructors by what the container can supply, or refuses the type. So does a container
    /// that cannot say what it holds: without <see cref="IServiceProviderIsService" /> there is no telling
    /// a registered type from an unregistered one. <see cref="DelegateJob" /> is built from the firing's
    /// scope by its constructor, so it never qualifies.
    /// </para>
    /// </remarks>
    private ConstructorInvoker? DecideUnscopedConstructor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type jobType)
    {
        // Not a job, or not one anything could construct: the scope path refuses it with the message it
        // always has.
        if (!typeof(IJob).IsAssignableFrom(jobType) || jobType.IsAbstract || jobType.IsValueType || jobType.ContainsGenericParameters)
        {
            return null;
        }

        ConstructorInfo[] constructors = jobType.GetConstructors();
        if (constructors.Length != 1 || constructors[0].GetParameters().Length != 0)
        {
            return null;
        }

        if (serviceProvider.GetService(typeof(IServiceProviderIsService)) is not IServiceProviderIsService registrations
            || registrations.IsService(jobType))
        {
            return null;
        }

        if (schedulerKey is not null
            && (serviceProvider.GetService(typeof(IServiceProviderIsKeyedService)) is not IServiceProviderIsKeyedService keyedRegistrations
                || keyedRegistrations.IsKeyedService(jobType, schedulerKey)))
        {
            return null;
        }

        return ConstructorInvoker.Create(constructors[0]);
    }

    /// <summary>
    /// The dependency injection scope a job was built in, carried as <see cref="JobScope.State" />.
    /// </summary>
    /// <remarks>
    /// It is <see cref="IAsyncDisposable" /> rather than something only this class knows how to take
    /// apart, so that a derived factory which wraps it in state of its own can still dispose it
    /// without naming the type.
    /// </remarks>
    private sealed class ScopeState : IAsyncDisposable
    {
        private readonly IServiceScope scope;
        private readonly IJob job;
        private readonly bool disposeJob;
        private int disposed;

        public ScopeState(IServiceScope scope, IJob job, bool disposeJob)
        {
            this.scope = scope;
            this.job = job;
            this.disposeJob = disposeJob;
        }

        public async ValueTask DisposeAsync()
        {
            // A derived factory may dispose this as well as leaving it to us, and a scope closed
            // twice throws from the container rather than from anything we control.
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return;
            }

            try
            {
                // Only a job we activated ourselves; one the container produced is registered with
                // the scope and disposed by it below.
                if (disposeJob)
                {
                    await DisposeIfDisposable(job).ConfigureAwait(false);
                }
            }
            finally
            {
                await DisposeScope(scope).ConfigureAwait(false);
            }
        }
    }

}
