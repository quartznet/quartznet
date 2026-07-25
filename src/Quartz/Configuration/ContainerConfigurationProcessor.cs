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
    private readonly QuartzOptions options;

    /// <remarks>
    /// Takes the resolved options rather than <see cref="IOptions{TOptions}"/> so that a named
    /// scheduler is handed its own options instance, instead of every scheduler sharing the unnamed one.
    /// </remarks>
    public ContainerConfigurationProcessor(
        ILogger<XMLSchedulingDataProcessor> logger,
        ITypeLoadHelper typeLoadHelper,
        TimeProvider timeProvider,
        QuartzOptions options)
        : base(logger, typeLoadHelper, timeProvider)
    {
        this.options = options;
    }

    public override bool OverWriteExistingData => options.Scheduling.OverWriteExistingData;
    public override bool IgnoreDuplicates => options.Scheduling.IgnoreDuplicates;
    public override bool ScheduleTriggerRelativeToReplacedTrigger => options.Scheduling.ScheduleTriggerRelativeToReplacedTrigger;

    protected override IReadOnlyList<IJobDetail> LoadedJobs => options.JobDetails;
    protected override IReadOnlyList<ITrigger> LoadedTriggers => options.Triggers;
}