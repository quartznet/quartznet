using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Quartz;

/// <summary>
/// Options for the Quartz scheduler health check registered by
/// <see cref="QuartzHealthCheckExtensions.AddQuartz(Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder, Action{QuartzHealthCheckOptions})" />
/// and by <see cref="QuartzHealthCheckExtensions.AddQuartzHealthChecks(IQuartzBuilder, Action{QuartzHealthCheckOptions})" />.
/// </summary>
public sealed class QuartzHealthCheckOptions
{
    /// <summary>
    /// The name used to register the health check.
    /// </summary>
    /// <remarks>
    /// Left unset, the check is called <c>quartz-scheduler</c>, or
    /// <c>quartz-scheduler-&lt;scheduler name&gt;</c> for a named scheduler — which is why it is
    /// nullable rather than carrying one of those as its value: the name depends on which scheduler the
    /// check is for, and that is not known to an options object.
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>
    /// Tags associated with the health check, allowing it to be filtered (for example into
    /// separate liveness and readiness probes).
    /// </summary>
    /// <remarks>
    /// Get-only with an in-place initializer, like every other collection on a Quartz options type: a
    /// configuration binder binds into a non-null collection without needing a setter, and one
    /// <c>configure</c> callback cannot discard the tags another added. Add to it —
    /// <c>options.Tags.Add("ready")</c> — rather than assigning a new collection.
    /// </remarks>
    public List<string> Tags { get; } = [];

    /// <summary>
    /// The <see cref="HealthStatus" /> reported when the check fails. When <see langword="null" />
    /// the default (<see cref="HealthStatus.Unhealthy" />) is used.
    /// </summary>
    public HealthStatus? FailureStatus { get; set; }

    /// <summary>
    /// The <see cref="HealthStatus" /> reported while the scheduler is in
    /// <see cref="SchedulerStatus.Standby" />. When <see langword="null" /> the default
    /// (<see cref="HealthStatus.Degraded" />) is used.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Degraded is the right default and the wrong answer for some deployments. Standby is deliberate
    /// and reversible, so calling it healthy would hide an application that never started its
    /// scheduler and calling it unhealthy would take a node out of rotation for doing what it was
    /// told. But a report is only read by whoever it reaches: an ASP.NET Core application can remap
    /// degraded on the endpoint with <c>MapHealthChecks(…, new HealthCheckOptions { ResultStatusCodes
    /// = … })</c>, while a worker has no endpoint at all — its only reader is the probe that asks the
    /// <c>HealthCheckService</c> directly, and a standby node it must not route to has to say
    /// <see cref="HealthStatus.Unhealthy" /> here.
    /// </para>
    /// <para>
    /// This is the standby verdict alone. A scheduler still in <see cref="SchedulerStatus.Created" />
    /// because <see cref="QuartzHostedServiceOptions.AutoStart" /> is <see langword="false" /> also
    /// reports degraded, and keeps doing so: that is a window between the host starting and the
    /// application pressing start, not a state a node sits in.
    /// </para>
    /// </remarks>
    public HealthStatus? StandbyStatus { get; set; }

    /// <summary>
    /// How many of its own check-in intervals a clustered node may go without checking in before the
    /// check reports <see cref="HealthStatus.Degraded" />. <c>3</c> by default; <see langword="null" />
    /// or <c>0</c> turns the reading off.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rest of this check asserts that the scheduler can fire and that its store answers. Neither
    /// says anything about the cluster manager, which runs on its own timer: a node whose check-in loop
    /// has wedged still answers a store query, still reports <see cref="SchedulerStatus.Running" />, and
    /// is meanwhile being recovered by its peers — they take its triggers because it looks dead to them,
    /// while its own probe says it is fine.
    /// </para>
    /// <para>
    /// So a clustered scheduler is also asked when it last checked in, through
    /// <see cref="SchedulerQueryExtensions" />' cluster listing, and a last check-in older than this
    /// many of the node's own configured intervals is reported as degraded rather than healthy. Three
    /// because that is the same order of magnitude cluster recovery uses to decide a node is gone — one
    /// interval is an ordinary scheduling delay, and a node that has missed three is not merely late.
    /// </para>
    /// <para>
    /// The reading happens only when the store says it is clustered, so a scheduler on an in-memory or
    /// unclustered store makes no extra query. Set this to <see langword="null" /> or <c>0</c> to make
    /// no query at all.
    /// </para>
    /// </remarks>
    public double? ClusterCheckinTolerance { get; set; } = 3;

    /// <summary>
    /// How many of the store's own misfire thresholds a trigger may be overdue before the check reports
    /// <see cref="HealthStatus.Degraded" />, and half of how many before it reports
    /// <see cref="HealthStatus.Unhealthy" />. <see langword="null" /> by default, which turns the
    /// reading off; <c>0</c> does the same. <c>3</c> is the value to start from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the silent stall: nothing has fired for a while although something should have. A
    /// scheduler in that state passes everything else this check asks — it is
    /// <see cref="SchedulerStatus.Running" />, its store answers, its cluster manager may well still be
    /// checking in — because none of those questions is about work actually leaving the queue. A wedged
    /// scheduler thread, a thread pool with nothing free, a lock nobody releases and a job store whose
    /// acquisition query has begun timing out all look identical from outside and all look healthy.
    /// </para>
    /// <para>
    /// So the check asks the store for a trigger that is schedulable
    /// (<see cref="TriggerState.Normal" />) and whose next fire time has passed by more than this many
    /// misfire thresholds. Finding one means the scheduler is late by more than lateness is defined to
    /// be; finding one twice as late is reported as unhealthy, because a backlog that keeps growing is
    /// no longer a delay. The overdue trigger's key and the instant it was due are in the report's data,
    /// so an operator sees which trigger is waiting and since when.
    /// </para>
    /// <para>
    /// The misfire threshold is the unit because it is the store's own definition of "late enough to
    /// matter" — <see cref="AdoJobStoreOptions.MisfireThreshold" /> or
    /// <see cref="InMemoryJobStoreOptions.MisfireThreshold" />, whichever this scheduler runs, and one
    /// minute for a store that has neither. It is also, on the database store, the default sweep
    /// interval of the misfire handler, which is why the multiplier has to be more than one: a trigger
    /// can be a threshold late before it counts as misfired and another sweep late before the handler
    /// reaches it, so anything under <c>3</c> can report ordinary recovery as a stall.
    /// </para>
    /// <para>
    /// Off by default, unlike <see cref="ClusterCheckinTolerance" />, because what counts as overdue is
    /// the application's to say: a scheduler whose triggers are deliberately paused-by-calendar, or one
    /// that is meant to run a backlog down after a maintenance window, is not stalled. A standby or
    /// paused scheduler is never read this way at all — those are deliberate, and the check reports them
    /// as it always has.
    /// </para>
    /// </remarks>
    public double? StaleFiringTolerance { get; set; }
}
