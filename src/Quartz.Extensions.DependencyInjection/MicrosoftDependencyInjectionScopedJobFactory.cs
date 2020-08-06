using System;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Spi;

namespace Quartz
{
    /// <summary>
    /// https://github.com/AndyPook/QuartzHostedService
    /// </summary>
    internal class MicrosoftDependencyInjectionScopedJobFactory : IJobFactory
    {
        private readonly IServiceProvider _rootServiceProvider;

        public MicrosoftDependencyInjectionScopedJobFactory(IServiceProvider rootServiceProvider)
        {
            _rootServiceProvider = rootServiceProvider ?? throw new ArgumentNullException(nameof(rootServiceProvider));
        }

        public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
        {
            var jobType = bundle.JobDetail.JobType;

            // MA - Generate a scope for the job, this allows the job to be registered
            //	using .AddScoped<T>() which means we can use scoped dependencies 
            //	e.g. database contexts
            var scope = _rootServiceProvider.CreateScope();

            var job = (IJob) scope.ServiceProvider.GetRequiredService(jobType);

            return new ScopedJob(scope, job);
        }

        public void ReturnJob(IJob job)
        {
            (job as IDisposable)?.Dispose();
        }

        private class ScopedJob : IJob, IDisposable
        {
            private readonly IJob _innerJob;
            private readonly IServiceScope _scope;

            public ScopedJob(IServiceScope scope, IJob innerJob)
            {
                _scope = scope;
                _innerJob = innerJob;
            }

            public void Dispose()
            {
                _scope.Dispose();
                (_innerJob as IDisposable)?.Dispose();
            }

            public Task Execute(IJobExecutionContext context)
            {
                return _innerJob.Execute(context);
            }
        }
    }
}