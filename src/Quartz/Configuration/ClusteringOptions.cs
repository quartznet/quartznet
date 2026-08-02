namespace Quartz;

/// <summary>
/// Strongly typed configuration for taking part in a cluster.
/// </summary>
/// <remarks>
/// <para>
/// Binds from the <c>Clustering</c> sub-section of the job store's configuration, and is where
/// clustering is said once. The ADO.NET job store does not repeat these settings: whether a scheduler
/// is clustered is <see cref="Enabled"/>, and <c>IJobStore.Clustered</c> reports it rather than
/// offering a second place to set it.
/// </para>
/// <para>
/// The equivalent flat keys are <c>quartz.jobStore.clustered</c>,
/// <c>quartz.jobStore.clusterCheckinInterval</c> and
/// <c>quartz.jobStore.clusterCheckinMisfireThreshold</c>.
/// </para>
/// </remarks>
public sealed class ClusteringOptions
{
    /// <summary>
    /// Whether this scheduler takes part in a cluster with every other scheduler sharing its database.
    /// </summary>
    /// <remarks>
    /// <c>UsePersistentStore(store =&gt; store.UseClustering())</c> turns this on, so code-first
    /// configuration never sets it directly. It is settable so that a scheduler configured entirely from
    /// a file can turn clustering on the same way it turns anything else on.
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>
    /// How often this scheduler records that it is still alive.
    /// </summary>
    /// <remarks>
    /// Shorter intervals detect a failed node sooner at the cost of more database traffic.
    /// </remarks>
    public TimeSpan CheckinInterval { get; set; } = TimeSpan.FromMilliseconds(7500);

    /// <summary>
    /// How long past a missed check-in another scheduler waits before treating this one as dead and
    /// recovering its triggers.
    /// </summary>
    public TimeSpan CheckinMisfireThreshold { get; set; } = TimeSpan.FromMilliseconds(7500);
}
