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
    /// <remarks>
    /// <para>
    /// It is also the window this scheduler's own check-in loop retries a failed check-in inside: a
    /// check-in that fails is attempted again with half of what is left of the interval plus this
    /// threshold, never later than <see cref="AdoJobStoreOptions.DbRetryInterval" />, so a database
    /// blip shorter than the threshold does not get the node written off. Only once the window has
    /// closed does the loop back off <see cref="AdoJobStoreOptions.DbRetryInterval" /> between attempts.
    /// </para>
    /// <para>
    /// Raise it past the environment's worst <em>pause</em> — a garbage collection, a virtual machine
    /// migration, a database failover — rather than its worst clock error: a genuinely dead node's work
    /// waits interval plus threshold to be taken over, and a live node that misses that window is
    /// recovered while it is still working.
    /// </para>
    /// </remarks>
    public TimeSpan CheckinMisfireThreshold { get; set; } = TimeSpan.FromMilliseconds(7500);
}
