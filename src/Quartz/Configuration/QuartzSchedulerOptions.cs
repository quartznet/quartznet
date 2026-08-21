namespace Quartz;

/// <summary>
/// Strongly typed configuration for the scheduler itself.
/// </summary>
/// <remarks>
/// Binds from the <c>Scheduler</c> section of the Quartz configuration, and is the typed
/// replacement for the <c>quartz.scheduler.*</c> property keys.
/// </remarks>
public sealed class QuartzSchedulerOptions
{
    /// <summary>
    /// The default value for <see cref="InstanceName"/>.
    /// </summary>
    public const string DefaultInstanceName = "QuartzScheduler";

    /// <summary>
    /// The value for <see cref="InstanceId"/> that marks a scheduler as not participating in a cluster.
    /// </summary>
    public const string DefaultInstanceId = "NON_CLUSTERED";

    /// <summary>
    /// The name of the scheduler, which must be unique within the process.
    /// </summary>
    public string InstanceName { get; set; } = DefaultInstanceName;

    /// <summary>
    /// The id of the scheduler, which must be unique within a cluster.
    /// </summary>
    /// <remarks>
    /// Leave at the default to opt out of clustering. Set <see cref="GenerateInstanceId"/> instead of
    /// assigning a literal id when the id should be derived at startup.
    /// </remarks>
    public string InstanceId { get; set; } = DefaultInstanceId;

    /// <summary>
    /// When <see langword="true"/>, <see cref="InstanceId"/> is generated at startup by the registered
    /// <see cref="Extensibility.IInstanceIdGenerator"/> instead of being taken from <see cref="InstanceId"/>.
    /// </summary>
    public bool GenerateInstanceId { get; set; }

    /// <summary>
    /// How long the scheduler waits before re-querying the job store when it finds no triggers to fire.
    /// </summary>
    /// <remarks>Must be at least one second. Lower values increase job store load for little benefit.</remarks>
    public TimeSpan IdleWaitTime { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The maximum number of triggers the scheduler acquires in a single batch.
    /// </summary>
    public int MaxBatchSize { get; set; } = 1;

    /// <summary>
    /// How far past the current time a trigger may fire in order to be included in the current
    /// acquisition batch.
    /// </summary>
    public TimeSpan BatchTriggerAcquisitionFireAheadTimeWindow { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// Whether executing jobs have their cancellation token signalled when the scheduler shuts down.
    /// </summary>
    public bool InterruptJobsOnShutdown { get; set; }

    /// <summary>
    /// Whether executing jobs have their cancellation token signalled on a shutdown that waits for
    /// jobs to complete.
    /// </summary>
    public bool InterruptJobsOnShutdownWithWait { get; set; }

    /// <summary>
    /// Values seeded into <see cref="SchedulerContext"/> when the scheduler is created.
    /// </summary>
    /// <remarks>Replaces the <c>quartz.context.key.*</c> property keys.</remarks>
    public Dictionary<string, string> Context { get; } = new(StringComparer.Ordinal);
}
