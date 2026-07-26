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

    protected override async ValueTask<JobScope> CreateJobInstance(
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
            return new JobScope(job, new ScopeState(scope, disposeJob: !fromContainer));
        }
        catch
        {
            // ReturnJob is not called when CreateJob throws, so the scope we just opened would be
            // abandoned - along with every scoped dependency already resolved into it.
            await DisposeScope(scope).ConfigureAwait(false);
            throw;
        }
    }

    public override ValueTask ReturnJob(JobScope scope, CancellationToken cancellationToken = default)
    {
        // A job the container produced belongs to the scope and is disposed along with it, so
        // disposing it here too would hand user code a second Dispose call. One we activated
        // ourselves is ours to dispose. Anything else - a derived factory's own state - is left to
        // the base, which disposes the job and then the state.
        if (scope.State is ScopeState { DisposeJob: false } state)
        {
            return base.ReturnJob(new JobScope(NoDisposeJob.Instance, state), cancellationToken);
        }

        return base.ReturnJob(scope, cancellationToken);
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

        public ScopeState(IServiceScope scope, bool disposeJob)
        {
            this.scope = scope;
            DisposeJob = disposeJob;
        }

        public bool DisposeJob { get; }

        public ValueTask DisposeAsync() => DisposeScope(scope);
    }

    /// <summary>
    /// Stands in for a job the container owns, so the base factory's disposal of "the job" is a
    /// no-op while its disposal of the scope still runs.
    /// </summary>
    private sealed class NoDisposeJob : IJob
    {
        public static readonly NoDisposeJob Instance = new();

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
