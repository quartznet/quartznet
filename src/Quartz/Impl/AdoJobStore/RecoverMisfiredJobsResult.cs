namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// The composite result of one pass of misfire recovery.
/// </summary>
/// <param name="HasMoreMisfiredTriggers">
/// Whether more misfired triggers were found than this pass was allowed to handle, so that another pass
/// should follow immediately rather than after the usual interval.
/// </param>
/// <param name="ProcessedMisfiredTriggerCount">How many misfired triggers this pass handled.</param>
/// <param name="EarliestNewTimeUtc">
/// The earliest next fire time the pass produced, which is when the scheduler is signalled to look again.
/// </param>
public sealed record RecoverMisfiredJobsResult(
    bool HasMoreMisfiredTriggers,
    int ProcessedMisfiredTriggerCount,
    DateTimeOffset EarliestNewTimeUtc)
{
    /// <summary>
    /// The result of a pass that found nothing to do.
    /// </summary>
    public static RecoverMisfiredJobsResult NoOp { get; } = new(false, 0, DateTimeOffset.MaxValue);
}
