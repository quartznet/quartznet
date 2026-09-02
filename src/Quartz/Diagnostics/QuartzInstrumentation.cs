namespace Quartz.Diagnostics;

/// <summary>
/// The names an application subscribes to Quartz's telemetry with.
/// </summary>
/// <remarks>
/// Wiring Quartz into OpenTelemetry begins with these two strings, and until 4.0 they were the only part
/// of the instrumentation surface that was not published: the tag names nobody types by hand were public
/// constants while the two names everybody types were internal, so every sample in existence spells them
/// as literals.
/// <code>
/// services.AddOpenTelemetry()
///     .WithTracing(tracing => tracing.AddSource(QuartzInstrumentation.ActivitySourceName))
///     .WithMetrics(metrics => metrics.AddMeter(QuartzInstrumentation.MeterName));
/// </code>
/// </remarks>
public static class QuartzInstrumentation
{
    /// <summary>
    /// The name of the <see cref="System.Diagnostics.ActivitySource"/> a scheduler emits its spans on —
    /// what <c>AddSource</c> is given.
    /// </summary>
    public const string ActivitySourceName = "Quartz";

    /// <summary>
    /// The name of the <see cref="System.Diagnostics.Metrics.Meter"/> a scheduler publishes its
    /// instruments on — what <c>AddMeter</c> is given.
    /// </summary>
    /// <remarks>
    /// It is the same string as <see cref="ActivitySourceName"/>, and deliberately a separate constant:
    /// traces and metrics are subscribed to independently, and a future divergence should not silently
    /// re-point whichever call site borrowed the other one.
    /// </remarks>
    public const string MeterName = "Quartz";

    /// <summary>
    /// The Quartz assembly version, reported as the version of both the source and the meter.
    /// </summary>
    internal static readonly string? Version = typeof(QuartzInstrumentation).Assembly.GetName().Version?.ToString();

    /// <summary>
    /// The names of the instruments a scheduler publishes on <see cref="MeterName" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The metric counterpart of <see cref="OperationName" />, and here for the same reason: a
    /// dashboard, an alert rule or a metrics view matches on these strings, so they are a contract and
    /// a rename of one is a breaking change for everybody watching. They were literals inside the
    /// meter, which left an integrator copying them out of the documentation and unable to hold a
    /// dashboard to them.
    /// </para>
    /// <para>
    /// The meter builds its instruments from these constants, and a test asserts that every instrument
    /// it creates is named by one and every one of these names an instrument, so the two cannot drift.
    /// What each instrument measures, in what unit and under which tags, is in the
    /// <a href="https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/opentelemetry-integration.html">OpenTelemetry
    /// integration</a> page.
    /// </para>
    /// </remarks>
    public static class Instruments
    {
        /// <summary>Jobs running right now — an up-down counter, <c>{job}</c>.</summary>
        public const string JobExecutionActive = "quartz.job.execution.active";

        /// <summary>How long a job took — a histogram, seconds. Its count is the number of executions and its <c>error.type</c> subset the number of failures.</summary>
        public const string JobExecutionDuration = "quartz.job.execution.duration";

        /// <summary>Trigger misfires the scheduler was notified of — a counter, <c>{trigger}</c>.</summary>
        public const string TriggerMisfire = "quartz.trigger.misfire";

        /// <summary>Retries the scheduler scheduled after a job failed — a counter, <c>{trigger}</c>.</summary>
        public const string TriggerRetry = "quartz.trigger.retry";

        /// <summary>How long one round of the scheduling loop's acquisition took — a histogram, seconds.</summary>
        public const string TriggerAcquisitionDuration = "quartz.trigger.acquisition.duration";

        /// <summary>Triggers acquired for firing — a counter, <c>{trigger}</c>.</summary>
        public const string TriggerAcquired = "quartz.trigger.acquired";

        /// <summary>How long a cluster check-in took — a histogram, seconds.</summary>
        public const string ClusterCheckinDuration = "quartz.cluster.checkin.duration";

        /// <summary>Fired triggers recovered from a failed cluster node — a counter, <c>{trigger}</c>.</summary>
        public const string ClusterRecoveryTrigger = "quartz.cluster.recovery.trigger";

        /// <summary>How long one round trip to the job store took — a histogram, seconds, tagged by operation.</summary>
        public const string JobStoreOperationDuration = "quartz.jobstore.operation.duration";
    }
}
