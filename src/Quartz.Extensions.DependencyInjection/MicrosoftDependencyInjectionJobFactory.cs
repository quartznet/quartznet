using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz
{
    internal sealed class MicrosoftDependencyInjectionJobFactory : PropertySettingJobFactory
    {
        private readonly IServiceProvider serviceProvider;
        private readonly IOptions<QuartzOptions> options;

        public MicrosoftDependencyInjectionJobFactory(
            IServiceProvider serviceProvider,
            IOptions<QuartzOptions> options)
        {
            this.serviceProvider = serviceProvider;
            this.options = options;
        }

        protected override IJob InstantiateJob(TriggerFiredBundle bundle, IScheduler scheduler)
        {
            if (!options.Value.JobFactory.AllowDefaultConstructor)
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