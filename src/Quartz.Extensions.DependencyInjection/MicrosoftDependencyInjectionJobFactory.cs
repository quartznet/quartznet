using System;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz
{
    internal sealed class MicrosoftDependencyInjectionJobFactory : PropertySettingJobFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public MicrosoftDependencyInjectionJobFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        protected override IJob InstantiateJob(TriggerFiredBundle bundle, IScheduler scheduler)
        {
            return (IJob) _serviceProvider.GetRequiredService(bundle.JobDetail.JobType);
        }

        public override void ReturnJob(IJob job)
        {
        }
    }
}