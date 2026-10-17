using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Options;

using Quartz.Logging;
using Quartz.Spi;
using Quartz.Xml;

namespace Quartz;

/// <summary>
/// Reuse logic for adding and removing items by using XMLSchedulingDataProcessor.
/// </summary>
internal sealed class ContainerConfigurationProcessor : XMLSchedulingDataProcessor
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

    /// <summary>
    /// Schedules the registered jobs and triggers, then warns once for each registration whose job or
    /// trigger was stored under a generated key, when the store keeps it.
    /// </summary>
    /// <remarks>
    /// A generated key is new on every start, so a persistent store finds nothing to replace and keeps
    /// another copy each time, and every copy of a trigger fires. The in-memory store starts empty, which
    /// makes the same key harmless there.
    /// </remarks>
    public override async Task ScheduleJobs(IScheduler sched, CancellationToken cancellationToken = default)
    {
        await base.ScheduleJobs(sched, cancellationToken).ConfigureAwait(false);

        List<GeneratedIdentity> generatedIdentities = options.Value.generatedIdentities;
        if (generatedIdentities.Count == 0)
        {
            return;
        }

        SchedulerMetaData metaData = await sched.GetMetaData(cancellationToken).ConfigureAwait(false);
        if (!metaData.JobStoreSupportsPersistence)
        {
            return;
        }

        // Asked for here rather than when the processor is built: the container sets the log provider
        // as the scheduler factory initializes, which is after this processor was constructed.
        ILog log = LogProvider.GetLogger(typeof(ContainerConfigurationProcessor));
        foreach (GeneratedIdentity identity in generatedIdentities)
        {
            log.Warn(
                $"{identity.Registration} gave its {identity.Kind} no identity, so it was stored under the generated key "
                + $"'{identity.Key}'. A persistent store keeps it, so each start adds another copy, and every copy of a "
                + "trigger fires. Give it a stable key with WithIdentity(...)");
        }
    }
}
