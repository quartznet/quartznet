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

    protected override ValueTask<JobScope> CreateJobInstance(
        TriggerFiredBundle bundle,
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        //  Generate a scope for the job, this allows the job to be registered
        //	using .AddScoped<T>() which means we can use scoped dependencies
        //	e.g. database contexts
        var scope = serviceProvider.CreateScope();
        ConfigureScope(scope, bundle, scheduler);
        var (job, fromContainer) = ResolveJob(bundle, scope.ServiceProvider);

        // The scope rides along as the job's state so that ReturnJob can tear it down. The job
        // itself is handed to the scheduler unwrapped, so listeners and the execution context see
        // the type the user wrote rather than something of ours standing in front of it.
        return new ValueTask<JobScope>(new JobScope(job, new ScopeState(scope, disposeJob: !fromContainer)));
    }

    public override async ValueTask ReturnJob(JobScope scope, CancellationToken cancellationToken = default)
    {
        if (scope.State is not ScopeState state)
        {
            // A derived factory replaced the state with something of its own; let the base decide.
            await base.ReturnJob(scope, cancellationToken).ConfigureAwait(false);
            return;
        }

        // A job the container produced belongs to the scope and is disposed along with it, so
        // disposing it here too would hand user code a second Dispose call. One we activated
        // ourselves is ours to dispose.
        if (state.DisposeJob)
        {
            await base.ReturnJob(new JobScope(scope.Job), cancellationToken).ConfigureAwait(false);
        }

        await state.DisposeScope().ConfigureAwait(false);
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

    private sealed class ScopeState
    {
        private readonly IServiceScope scope;

        public ScopeState(IServiceScope scope, bool disposeJob)
        {
            this.scope = scope;
            DisposeJob = disposeJob;
        }

        public bool DisposeJob { get; }

        public ValueTask DisposeScope()
        {
            if (scope is IAsyncDisposable asyncDisposableScope)
            {
                return asyncDisposableScope.DisposeAsync();
            }

            scope.Dispose();
            return default;
        }
    }
}
