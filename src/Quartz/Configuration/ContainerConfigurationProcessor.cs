using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Spi;
using Quartz.Xml;

namespace Quartz.Configuration;

/// <summary>
/// Reuse logic for adding and removing items by using XMLSchedulingDataProcessor.
/// </summary>
internal sealed class ContainerConfigurationProcessor : XMLSchedulingDataProcessor
{
    private readonly IOptions<QuartzOptions> options;

    public ContainerConfigurationProcessor(
        ILogger<XMLSchedulingDataProcessor> logger,
        ITypeLoadHelper typeLoadHelper,
        TimeProvider timeProvider,
        IOptions<QuartzOptions> options)
        : base(logger, typeLoadHelper, timeProvider)
    {
        this.options = options;
    }

    public override bool OverWriteExistingData => options.Value.Scheduling.OverWriteExistingData;
    public override bool IgnoreDuplicates => options.Value.Scheduling.IgnoreDuplicates;
    public override bool ScheduleTriggerRelativeToReplacedTrigger => options.Value.Scheduling.ScheduleTriggerRelativeToReplacedTrigger;

    protected override IReadOnlyList<IJobDetail> LoadedJobs => options.Value.JobDetails;
    protected override IReadOnlyList<ITrigger> LoadedTriggers => options.Value.Triggers;
}