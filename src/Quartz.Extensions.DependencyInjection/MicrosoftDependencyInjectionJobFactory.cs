using System;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Spi;

namespace Quartz
{
    public class MicrosoftDependencyInjectionJobFactory : IJobFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public MicrosoftDependencyInjectionJobFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler)
        {
            return (IJob) _serviceProvider.GetRequiredService(bundle.JobDetail.JobType);
        }

        public void ReturnJob(IJob job)
        {
        }
    }
}