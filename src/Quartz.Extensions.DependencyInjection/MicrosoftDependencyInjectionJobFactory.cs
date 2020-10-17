using System;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz
{
    /// <summary>
    /// Integrates job instantiation with Microsoft DI system.
    /// </summary>
    public class MicrosoftDependencyInjectionJobFactory : PropertySettingJobFactory
    {
        private readonly IServiceProvider serviceProvider;
        private readonly IOptions<QuartzOptions> options;

        public MicrosoftDependencyInjectionJobFactory(
            IServiceProvider serviceProvider,
            IOptions<QuartzOptions> options)
        {
            this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            this.options = options;
        }

        protected override IJob InstantiateJob(TriggerFiredBundle bundle, IScheduler scheduler)
        {
            IJob job;
            if (!options.Value.JobFactory.AllowDefaultConstructor || options.Value.JobFactory.CreateScope)
            {
                job = (IJob) serviceProvider.GetRequiredService(bundle.JobDetail.JobType);
            }
            else
            {
                job = (IJob) (serviceProvider.GetService(bundle.JobDetail.JobType) ?? base.InstantiateJob(bundle, scheduler));
            }
            
            if (options.Value.JobFactory.CreateScope)
            {
                //  Generate a scope for the job, this allows the job to be registered
                //	using .AddScoped<T>() which means we can use scoped dependencies 
                //	e.g. database contexts
                var scope = serviceProvider.CreateScope();
                job = new ScopedJob(scope, job);
            }
            
            return job;
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
                scope.Dispose();
                (innerJob as IDisposable)?.Dispose();
            }

            public Task Execute(IJobExecutionContext context)
            {
                return innerJob.Execute(context);
            }
        }
    }
}