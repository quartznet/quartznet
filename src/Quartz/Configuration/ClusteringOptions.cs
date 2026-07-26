namespace Quartz;

/// <summary>
/// Strongly typed configuration for taking part in a cluster.
/// </summary>
/// <remarks>
/// Binds from the <c>JobStore</c> section alongside the rest of the job store's settings; these are the
/// clustering subset, separated so <c>UseClustering</c> offers only what is relevant to it.
/// </remarks>
public sealed class ClusteringOptions
{
    /// <summary>
    /// How often this scheduler records that it is still alive.
    /// </summary>
    /// <remarks>
    /// Shorter intervals detect a failed node sooner at the cost of more database traffic. Left unset,
    /// whatever the job store is already configured with stands, so <c>UseClustering()</c> does not
    /// quietly undo a value that came from configuration.
    /// </remarks>
    public TimeSpan? CheckinInterval { get; set; }

    /// <summary>
    /// How long past a missed check-in another scheduler waits before treating this one as dead and
    /// recovering its triggers.
    /// </summary>
    /// <remarks>
    /// Left unset, the job store's existing setting stands.
    /// </remarks>
    public TimeSpan? CheckinMisfireThreshold { get; set; }
}
