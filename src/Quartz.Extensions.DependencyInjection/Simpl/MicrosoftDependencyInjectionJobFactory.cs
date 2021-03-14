using System;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Spi;

namespace Quartz.Simpl
{
    /// <summary>
    /// Integrates job instantiation with Microsoft DI system.
    /// </summary>
    public class MicrosoftDependencyInjectionJobFactory : PropertySettingJobFactory
    {
        private readonly IServiceProvider serviceProvider;
        private readonly IOptions<QuartzOptions> options;
        private readonly JobActivatorCache activatorCache;

        public MicrosoftDependencyInjectionJobFactory(
            IServiceProvider serviceProvider,
            IOptions<QuartzOptions> options)
        {
            this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.activatorCache = new JobActivatorCache();
        }

        protected override IJob InstantiateJob(TriggerFiredBundle bundle, IScheduler scheduler)
        {
            if (options.Value.JobFactory.CreateScope)
            {
                //  Generate a scope for the job, this allows the job to be registered
                //	using .AddScoped<T>() which means we can use scoped dependencies 
                //	e.g. database contexts
                var scope = serviceProvider.CreateScope();

                var job = CreateJob(bundle, scope.ServiceProvider);

                return new ScopedJob(scope, job);
            }

            return CreateJob(bundle, serviceProvider);
        }

        private IJob CreateJob(TriggerFiredBundle bundle, IServiceProvider serviceProvider)
        {
            return activatorCache.CreateInstance(serviceProvider, bundle.JobDetail.JobType);
        }

        public override void ReturnJob(IJob job)
        {
            (job as IDisposable)?.Dispose();
        }

        private sealed class ScopedJob : IJob, IDisposable
        {
            private readonly IJob innerJob;
            private readonly IServiceScope scope;

            public ScopedJob(IServiceScope scope, IJob innerJob)
            {
                this.scope = scope;
                this.innerJob = innerJob;
            }

            public void Dispose()
            {
                (innerJob as IDisposable)?.Dispose();
                scope.Dispose();
            }

            public Task Execute(IJobExecutionContext context)
            {
                return innerJob.Execute(context);
            }
        }
    }
}