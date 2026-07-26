using System.Runtime.ExceptionServices;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;

namespace Quartz.Impl;

/// <summary>
/// Integrates job instantiation with Microsoft DI system.
/// </summary>
public class MicrosoftDependencyInjectionJobFactory : PropertySettingJobFactory
{
    private readonly IServiceProvider serviceProvider;
    private readonly JobActivatorCache activatorCache = new();

    public MicrosoftDependencyInjectionJobFactory(IServiceProvider serviceProvider)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
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
            return new ValueTask<JobScope>(new JobScope(job, new ScopeState(scope, disposeJob: !fromContainer)));
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

    public override async ValueTask ReturnJob(JobScope scope, CancellationToken cancellationToken = default)
    {
        // A job the container produced belongs to the scope and is disposed along with it, so
        // disposing it here as well would hand user code a second Dispose call. One we activated
        // ourselves is ours to dispose. When a derived factory has replaced the state we cannot tell
        // which it is, so fall back to the base and rely on ScopeState's own guard to keep the scope
        // from being torn down twice.
        if (scope.State is not ScopeState state)
        {
            await base.ReturnJob(scope, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            if (state.DisposeJob)
            {
                await base.ReturnJob(new JobScope(scope.Job), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            await state.DisposeAsync().ConfigureAwait(false);
        }
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

    protected virtual void ConfigureScope(IServiceScope scope, TriggerFiredBundle bundle, IScheduler scheduler)
    {
        // Configuration point for Services that are Scoped and need
        // the ambient context of a Job
    }

    private (IJob Job, bool FromContainer) ResolveJob(TriggerFiredBundle bundle, IServiceProvider serviceProvider)
    {
        var job = (IJob?) serviceProvider.GetService(bundle.JobDetail.JobType);

        if (job is not null)
        {
            // use the registered one
            return (job, true);
        }

        return (activatorCache.CreateInstance(serviceProvider, bundle.JobDetail.JobType), false);
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
        private int disposed;

        public ScopeState(IServiceScope scope, bool disposeJob)
        {
            this.scope = scope;
            DisposeJob = disposeJob;
        }

        public bool DisposeJob { get; }

        public ValueTask DisposeAsync()
        {
            // A derived factory may pass this to the base ladder as well as leaving it to us, and a
            // scope closed twice throws from the container rather than from anything we control.
            if (Interlocked.Exchange(ref disposed, 1) != 0)
            {
                return default;
            }

            return DisposeScope(scope);
        }
    }

}
