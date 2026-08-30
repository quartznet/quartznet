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
    /// <remarks>
    /// <para>
    /// Defaults to 1, which is also the point at which a database-backed store needs no cluster-wide
    /// lock to acquire: raising it makes every acquisition cycle take the <c>TRIGGER_ACCESS</c> row
    /// lock, including cycles that acquire nothing. It pays off where triggers genuinely arrive in
    /// bunches, and costs lock traffic where they do not.
    /// </para>
    /// <para>
    /// This is only the upper bound. What decides the size of a batch is
    /// <see cref="BatchTriggerAcquisitionFireAheadTimeWindow"/>: after the first trigger, only triggers
    /// due within that window of it join the batch, and the window defaults to
    /// <see cref="TimeSpan.Zero"/>. Raising this alone leaves the effective batch at one trigger for any
    /// schedule whose fire times are spread out — the two are one setting in two halves, so move them
    /// together.
    /// </para>
    /// <para>
    /// Must not exceed <see cref="ThreadPoolOptions.MaxConcurrency"/>: triggers acquired beyond the
    /// number of threads there are to run them on are held by this node, unfireable by any other, until
    /// the pool drains.
    /// </para>
    /// </remarks>
    public int MaxBatchSize { get; set; } = 1;

    /// <summary>
    /// How far past the current time a trigger may fire in order to be included in the current
    /// acquisition batch.
    /// </summary>
    /// <remarks>
    /// The other half of <see cref="MaxBatchSize"/>; neither batches anything on its own. At the default
    /// of <see cref="TimeSpan.Zero"/> a batch holds the triggers due at the same instant and nothing
    /// else. Widening it fires triggers early by up to this much, which is what the batching costs.
    /// </remarks>
    public TimeSpan BatchTriggerAcquisitionFireAheadTimeWindow { get; set; } = TimeSpan.Zero;

    /// <summary>
    /// When a shutting-down scheduler signals cancellation to the jobs still executing.
    /// </summary>
    /// <remarks>
    /// This was two independent booleans, one for a shutdown that waits for jobs and one for a shutdown
    /// that does not. They were never independent: together they answered a single four-way question,
    /// and the pair could be set to a combination — both false — that spelled the default in two ways.
    /// </remarks>
    public ShutdownJobInterruption ShutdownJobInterruption { get; set; }

    /// <summary>
    /// Whether the scheduler records the trace context of the call that scheduled a trigger, so that the
    /// firing links back to it. On by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A scheduled job runs minutes, hours or days after the call that asked for it, quite possibly on
    /// another node. With this on, a scheduling call made inside an activity leaves the W3C
    /// <c>traceparent</c> on the trigger — under <see cref="SchedulerConstants.TraceParent" /> and
    /// <see cref="SchedulerConstants.TraceState" /> — and the firing's <c>Quartz.Job.Execute</c> span
    /// carries an <see cref="System.Diagnostics.ActivityLink" /> to it. A link and not a parent: the
    /// firing is its own trace root, because a trace spanning the wait would be a trace nothing could
    /// display.
    /// </para>
    /// <para>
    /// Turn it off to keep the two reserved keys out of trigger data entirely — for a store whose rows
    /// are read by something that does not expect them, or where the extra two entries per trigger are
    /// not worth what they buy. Nothing else changes: the execute span is emitted either way.
    /// </para>
    /// </remarks>
    public bool PropagateTraceContext { get; set; } = true;

    /// <summary>
    /// Values seeded into <see cref="SchedulerContext"/> when the scheduler is created.
    /// </summary>
    /// <remarks>Replaces the <c>quartz.context.key.*</c> property keys.</remarks>
    public Dictionary<string, string> Context { get; } = new(StringComparer.Ordinal);
}
