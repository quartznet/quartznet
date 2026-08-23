using System.Runtime.ExceptionServices;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Extensibility;

namespace Quartz.Impl;

/// <summary>
/// Integrates job instantiation with Microsoft DI system.
/// </summary>
public class MicrosoftDependencyInjectionJobFactory : PropertySettingJobFactory
{
    private readonly IServiceProvider serviceProvider;
    private readonly JobActivatorCache activatorCache = new();
    private readonly JobFactoryOptions options;

    /// <summary>
    /// The service key this factory's scheduler registers its parts under, or <see langword="null" />
    /// for the default scheduler, whose registrations are the unkeyed ones.
    /// </summary>
    private readonly object? schedulerKey;

    /// <param name="serviceProvider">The container jobs are built from.</param>
    /// <param name="options">
    /// The factory's settings, which is where <see cref="JobFactoryOptions.ConfigureScope"/> arrives from.
    /// Optional so that a derived factory constructing this one by hand does not have to supply it; the
    /// container always does.
    /// </param>
    public MicrosoftDependencyInjectionJobFactory(
        IServiceProvider serviceProvider,
        IOptions<JobFactoryOptions>? options = null)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        this.options = options?.Value ?? new JobFactoryOptions();

        // Read once rather than per fire. A factory handed the raw container - by a caller constructing
        // one itself, or because this is the default scheduler, which has no wrapper - has no key, and
        // resolves jobs exactly as it always did.
        schedulerKey = (serviceProvider as SchedulerScopedServiceProvider)?.SchedulerServiceKey;
    }

    /// <remarks>
    /// Deliberately not an <c>async</c> method: an async state machine would restore the caller's
    /// <see cref="System.Threading.ExecutionContext" /> when its synchronous part returns, discarding
    /// any <see cref="System.Threading.AsyncLocal{T}" /> that <see cref="ConfigureScope" /> set — which
    /// is most of the reason that hook exists (#1528).
    /// </remarks>
    protected override ValueTask<JobScope> CreateJobInstance(
        TriggerFiredBundle bundle,
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
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
    /// this factory activated itself is not, and is disposed here.
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
        var jobType = bundle.JobDetail.JobType.Type;

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
