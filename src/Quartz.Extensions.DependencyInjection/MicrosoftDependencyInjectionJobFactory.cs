using System;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz
{
    internal sealed class MicrosoftDependencyInjectionJobFactory : PropertySettingJobFactory
    {
        private readonly IServiceProvider serviceProvider;
        private readonly JobFactoryOptions options;

        public MicrosoftDependencyInjectionJobFactory(
            IServiceProvider serviceProvider,
            JobFactoryOptions options)
        {
            this.serviceProvider = serviceProvider;
            this.options = options;
        }

        protected override IJob InstantiateJob(TriggerFiredBundle bundle, IScheduler scheduler)
        {
            if (!options.AllowDefaultConstructor)
            {
                return (IJob) serviceProvider.GetRequiredService(bundle.JobDetail.JobType);
            }

            return (IJob) serviceProvider.GetService(bundle.JobDetail.JobType) ?? base.InstantiateJob(bundle, scheduler);

        }

        public override void ReturnJob(IJob job)
        {
        }
    }
}