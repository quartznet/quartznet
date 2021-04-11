using System.Collections.Generic;

using Microsoft.Extensions.Options;

using Quartz.Logging;
using Quartz.Spi;
using Quartz.Xml;

namespace Quartz
{
    /// <summary>
    /// Reuse logic for adding and removing items by using XMLSchedulingDataProcessor.
    /// </summary>
    internal class ContainerConfigurationProcessor : XMLSchedulingDataProcessor
    {
        private readonly IOptions<QuartzOptions> options;

        public ContainerConfigurationProcessor(
            ITypeLoadHelper typeLoadHelper,
            IOptions<QuartzOptions> options) 
            : base(typeLoadHelper)
        {
            this.options = options;
        }

        public override bool OverWriteExistingData => options.Value.Scheduling.OverWriteExistingData;
        public override bool IgnoreDuplicates => options.Value.Scheduling.IgnoreDuplicates;
        public override bool ScheduleTriggerRelativeToReplacedTrigger => options.Value.Scheduling.ScheduleTriggerRelativeToReplacedTrigger;

        protected override IReadOnlyList<IJobDetail> LoadedJobs => options.Value.JobDetails;
        protected override IReadOnlyList<ITrigger> LoadedTriggers => options.Value.Triggers;

        protected override void ReportDuplicateTrigger(IMutableTrigger trigger)
        {
            Log.WarnFormat("Possibly duplicately named ({0}) trigger in jobs configuration. You can ignore this be setting " + nameof(QuartzOptions.Scheduling.IgnoreDuplicates) + " to true.", trigger.Key);
        }
    }
}