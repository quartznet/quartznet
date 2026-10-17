using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;
using Quartz.Xml;

namespace Quartz.Configuration;

/// <summary>
/// Reuse logic for adding and removing items by using XmlSchedulingDataProcessor.
/// </summary>
internal sealed class ContainerConfigurationProcessor : XmlSchedulingDataProcessor
{
    private readonly ILogger<ContainerConfigurationProcessor> logger;
    private readonly QuartzOptions options;
    private readonly List<IJobDetail> jobs;
    private readonly List<ITrigger> triggers;
    private readonly List<GeneratedIdentity> generatedIdentities;

    /// <remarks>
    /// Takes the resolved options and this scheduler's own content registrations rather than
    /// <see cref="IOptions{TOptions}"/> and the container's unkeyed services, so that a named scheduler is
    /// handed its own of both instead of every scheduler sharing the default scheduler's.
    /// </remarks>
    public ContainerConfigurationProcessor(
        ILogger<ContainerConfigurationProcessor> logger,
        ITypeLoader typeLoader,
        TimeProvider timeProvider,
        QuartzOptions options,
        IEnumerable<ISchedulerContent> content)
        : base(logger, typeLoader, timeProvider)
    {
        this.logger = logger;
        this.options = options;

        // Materialized once: each registration builds its jobs and triggers when it is first resolved,
        // and enumerating twice would be two passes over the same lazily built content.
        ISchedulerContent[] parts = content.ToArray();
        jobs = parts.SelectMany(x => x.Jobs).ToList();
        triggers = parts.SelectMany(x => x.Triggers).ToList();
        generatedIdentities = parts.Select(x => x.GeneratedIdentity).OfType<GeneratedIdentity>().ToList();
    }

    /// <summary>
    /// Warns once for each registration whose job or trigger was stored under a generated key, when the
    /// store keeps it.
    /// </summary>
    /// <remarks>
    /// A generated key is new on every start, so a persistent store finds nothing to replace and keeps
    /// another copy each time. The in-memory store starts empty, which makes the same key harmless there.
    /// </remarks>
    /// <param name="storePersists">Whether the scheduler's store keeps what it is given across starts.</param>
    public void ReportGeneratedIdentities(bool storePersists)
    {
        if (!storePersists)
        {
            return;
        }

        foreach (GeneratedIdentity identity in generatedIdentities)
        {
            logger.GeneratedIdentityInPersistentStore(identity.Registration, identity.Kind, identity.Key);
        }
    }

    /// <remarks>
    /// The effective value rather than the property: <see cref="SchedulingOptions.OverwriteExistingData" />
    /// defaults to <see langword="true" />, and setting only
    /// <see cref="SchedulingOptions.IgnoreDuplicates" /> means the default was never a statement.
    /// </remarks>
    public override bool OverwriteExistingData => options.Scheduling.EffectiveOverwriteExistingData;
    public override bool IgnoreDuplicates => options.Scheduling.IgnoreDuplicates;
    public override bool ScheduleTriggerRelativeToReplacedTrigger => options.Scheduling.ScheduleTriggerRelativeToReplacedTrigger;

    protected override List<IJobDetail> LoadedJobs => jobs;
    protected override List<ITrigger> LoadedTriggers => triggers;
}
