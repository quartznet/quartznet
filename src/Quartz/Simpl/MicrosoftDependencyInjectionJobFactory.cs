using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Spi;

namespace Quartz.Simpl;

/// <summary>
/// Integrates job instantiation with Microsoft DI system.
/// </summary>
public class MicrosoftDependencyInjectionJobFactory : PropertySettingJobFactory
{
    private readonly IServiceProvider serviceProvider;
    private readonly IOptions<QuartzOptions> options;
    private readonly JobActivatorCache activatorCache = new();

    public MicrosoftDependencyInjectionJobFactory(
        IServiceProvider serviceProvider,
        IOptions<QuartzOptions> options)
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
    }

    protected override IJob InstantiateJob(TriggerFiredBundle bundle, IScheduler scheduler)
    {
        //  Generate a scope for the job, this allows the job to be registered
        //	using .AddScoped<T>() which means we can use scoped dependencies
        //	e.g. database contexts
        var scope = serviceProvider.CreateScope();
        ConfigureScope(scope, bundle, scheduler);
        var (job, fromContainer) = CreateJob(bundle, scope.ServiceProvider);

        return new AsyncScopedJob(scope, job, canDispose: !fromContainer);
    }

    protected virtual void ConfigureScope(IServiceScope scope, TriggerFiredBundle bundle, IScheduler scheduler)
    {
        // Configuration point for Services that are Scoped and need
        // the ambient context of a Job
    }

    private (IJob Job, bool FromContainer) CreateJob(TriggerFiredBundle bundle, IServiceProvider serviceProvider)
    {
        var job = (IJob?) serviceProvider.GetService(bundle.JobDetail.JobType);

        if (job is not null)
        {
            // use the registered one
            return (job, true);
        }

        return (activatorCache.CreateInstance(serviceProvider, bundle.JobDetail.JobType), false);
    }

    private sealed class AsyncScopedJob : IJob, IJobWrapper, IAsyncDisposable
    {
        private readonly IServiceScope scope;
        private readonly bool canDispose;

        public AsyncScopedJob(IServiceScope scope, IJob innerJob, bool canDispose)
        {
            this.scope = scope;
            this.canDispose = canDispose;
            this.Target = innerJob;
        }

        public IJob Target { get; }

        public async ValueTask DisposeAsync()
        {
            if (canDispose)
            {
                if (Target is IAsyncDisposable asyncDisposableInnerJob)
                {
                    await asyncDisposableInnerJob.DisposeAsync().ConfigureAwait(false);
                }
                else if (Target is IDisposable disposableInnerJob)
                {
                    disposableInnerJob.Dispose();
                }
            }

            if (scope is IAsyncDisposable scopeAsyncDisposable)
            {
                await scopeAsyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                scope.Dispose();
            }
        }

        public ValueTask Execute(IJobExecutionContext context)
        {
            return Target.Execute(context);
        }
    }
}