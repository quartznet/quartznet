namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Helper class for returning the composite result of trying
/// to recover misfired jobs.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="RecoverMisfiredJobsResult"/> class.
/// </remarks>
/// <param name="hasMoreMisfiredTriggers">if set to <c>true</c> [has more misfired triggers].</param>
/// <param name="processedMisfiredTriggerCount">The processed misfired trigger count.</param>
/// <param name="earliestNewTimeUtc"></param>
public sealed class RecoverMisfiredJobsResult(bool hasMoreMisfiredTriggers, int processedMisfiredTriggerCount, DateTimeOffset earliestNewTimeUtc)
{
    public static readonly RecoverMisfiredJobsResult NoOp = new RecoverMisfiredJobsResult(false, 0, DateTimeOffset.MaxValue);

    /// <summary>
    /// Gets a value indicating whether this instance has more misfired triggers.
    /// </summary>
    /// <value>
    /// 	<c>true</c> if this instance has more misfired triggers; otherwise, <c>false</c>.
    /// </value>
    public bool HasMoreMisfiredTriggers { get; } = hasMoreMisfiredTriggers;

    /// <summary>
    /// Gets the processed misfired trigger count.
    /// </summary>
    /// <value>The processed misfired trigger count.</value>
    public int ProcessedMisfiredTriggerCount { get; } = processedMisfiredTriggerCount;

    public DateTimeOffset EarliestNewTime { get; } = earliestNewTimeUtc;
}