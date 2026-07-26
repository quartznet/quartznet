using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;
using Quartz.Xml;

namespace Quartz.Configuration;

/// <summary>
/// Reuse logic for adding and removing items by using XMLSchedulingDataProcessor.
/// </summary>
internal sealed class ContainerConfigurationProcessor : XMLSchedulingDataProcessor
{
    private readonly QuartzOptions options;
    private readonly IJobDetail[] jobs;
    private readonly ITrigger[] triggers;

    /// <remarks>
    /// Takes the resolved options and this scheduler's own content registrations rather than
    /// <see cref="IOptions{TOptions}"/> and the container's unkeyed services, so that a named scheduler is
    /// handed its own of both instead of every scheduler sharing the default scheduler's.
    /// </remarks>
    public ContainerConfigurationProcessor(
        ILogger<XMLSchedulingDataProcessor> logger,
        ITypeLoadHelper typeLoadHelper,
        TimeProvider timeProvider,
        QuartzOptions options,
        IEnumerable<ISchedulerContent> content)
        : base(logger, typeLoadHelper, timeProvider)
    {
        this.options = options;

        // Materialized once: each registration builds its jobs and triggers when it is first resolved,
        // and enumerating twice would be two passes over the same lazily built content.
        var parts = content.ToArray();
        jobs = parts.SelectMany(x => x.Jobs).ToArray();
        triggers = parts.SelectMany(x => x.Triggers).ToArray();
    }

    public override bool OverWriteExistingData => options.Scheduling.OverWriteExistingData;
    public override bool IgnoreDuplicates => options.Scheduling.IgnoreDuplicates;
    public override bool ScheduleTriggerRelativeToReplacedTrigger => options.Scheduling.ScheduleTriggerRelativeToReplacedTrigger;

    protected override IReadOnlyList<IJobDetail> LoadedJobs => jobs;
    protected override IReadOnlyList<ITrigger> LoadedTriggers => triggers;
}
