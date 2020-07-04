using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Spi;
using Quartz.Xml;

namespace Quartz
{
    /// <summary>
    /// Reuse logic for adding and removing items by using XMLSchedulingDataProcessor.
    /// </summary>
    internal class ContainerConfigurationProcessor : XMLSchedulingDataProcessor
    {
        private readonly IServiceProvider serviceProvider;

        public ContainerConfigurationProcessor(IServiceProvider serviceProvider) 
            : base(serviceProvider.GetRequiredService<ITypeLoadHelper>())
        {
            this.serviceProvider = serviceProvider;
            var options = serviceProvider.GetService<QuartzSchedulingOptions>() ?? new QuartzSchedulingOptions();
            OverWriteExistingData = options.OverWriteExistingData;
            IgnoreDuplicates = options.IgnoreDuplicates;
            ScheduleTriggerRelativeToReplacedTrigger = options.ScheduleTriggerRelativeToReplacedTrigger;
        }

        protected override IReadOnlyList<IJobDetail> LoadedJobs => serviceProvider.GetServices<IJobDetail>().ToList();
        protected override IReadOnlyList<ITrigger> LoadedTriggers => serviceProvider.GetServices<ITrigger>().ToList();
    }
}