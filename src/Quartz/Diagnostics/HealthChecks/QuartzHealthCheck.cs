using System.Globalization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;

namespace Quartz;

/// <summary>
/// Which scheduler a health check reports on: a named one, or the default one.
/// </summary>
/// <remarks>
/// Handed to the check as a constructor argument rather than resolved from the container, because
/// several checks can be registered in one container and each has to know its own scheduler.
/// </remarks>
internal sealed record SchedulerHealthCheckTarget(string? SchedulerName);

/// <summary>
/// When a health check registration first evaluated its required jobs: the instant a job with no recorded
/// success is judged from.
/// </summary>
/// <remarks>
/// One per registration rather than per check, because the health-check service builds a new check for
/// every run, and a start that moved with each run would never let a window elapse.
/// </remarks>
internal sealed class RequiredJobsBaseline
{
    private long firstUtcTicks;

    /// <summary>
    /// The first instant this was asked at, which is <paramref name="now" /> the first time.
    /// </summary>
    public DateTimeOffset Since(DateTimeOffset now)
    {
        long first = Interlocked.CompareExchange(ref firstUtcTicks, now.UtcTicks, 0);
        return first == 0 ? now : new DateTimeOffset(first, TimeSpan.Zero);
    }
}

internal sealed class QuartzHealthCheck : IHealthCheck
{
    /// <summary>
    /// What a store that is neither of the two this repository ships is judged by. The database store's
    /// default, and the more forgiving of the two defaults.
    /// </summary>
    private static readonly TimeSpan DefaultMisfireThreshold = TimeSpan.FromMinutes(1);

    private readonly IServiceProvider serviceProvider;
    private readonly SchedulerHealthCheckTarget target;
    private readonly IOptionsMonitor<QuartzHostedServiceOptions> hostedServiceOptions;
    private readonly IOptionsMonitor<QuartzHealthCheckOptions> checkOptions;
    private readonly RequiredJobsBaseline baseline;

    public QuartzHealthCheck(
        IServiceProvider serviceProvider,
        SchedulerHealthCheckTarget target,
        IOptionsMonitor<QuartzHostedServiceOptions> hostedServiceOptions,
        IOptionsMonitor<QuartzHealthCheckOptions> checkOptions,
        RequiredJobsBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(hostedServiceOptions);
        ArgumentNullException.ThrowIfNull(checkOptions);
        ArgumentNullException.ThrowIfNull(baseline);

        this.serviceProvider = serviceProvider;
        this.target = target;
        this.hostedServiceOptions = hostedServiceOptions;
        this.checkOptions = checkOptions;
        this.baseline = baseline;
    }

    async Task<HealthCheckResult> IHealthCheck.CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        // Resolved here rather than in the constructor, and asked for rather than demanded: a container
        // whose schedulers are all named has no unkeyed ISchedulerFactory, and a missing scheduler should
        // be an unhealthy probe with something to read, not an InvalidOperationException out of the
        // health-check pipeline.
        ISchedulerFactory? schedulerFactory = target.SchedulerName is null
            ? serviceProvider.GetService<ISchedulerFactory>()
            : serviceProvider.GetKeyedService<ISchedulerFactory>(target.SchedulerName);

        if (schedulerFactory is not null)
        {
            return await Evaluate(
                await schedulerFactory.GetScheduler(cancellationToken).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);
        }

        // No registration under that name — but a scheduler added at runtime has none and is still a
        // scheduler this container is running. It is in the repository, which is where everything else
        // that reads across both kinds looks, so the check does too. A health check is registered at
        // build time, when a tenant that has not been added yet has no name for anybody to write, so
        // AddQuartz("acme") on the health-checks builder is how a check waits for one.
        if (target.SchedulerName is not null
            && serviceProvider.GetService<Extensibility.ISchedulerRepository>()?.Lookup(target.SchedulerName) is { } tenant)
        {
            // Unless it is a window onto an attached store, which is in the repository for the same
            // reason every other scheduler is and is emphatically not a scheduler of this process. It is
            // never started, so this check would report it Unhealthy — "created but never started" —
            // for a cluster that is running perfectly well somewhere else, and a probe that fails
            // because a dashboard is attached to a database is a node taken out of rotation for
            // nothing.
            if (serviceProvider.GetService<SchedulerWindowRegistry>()?.TargetOf(target.SchedulerName) is { } window)
            {
                return HealthCheckResult.Unhealthy(
                    $"'{target.SchedulerName}' is a window onto the store attached as '{window}', not a scheduler this "
                    + "process runs, so there is nothing here to report on. Its liveness is its cluster's, which the "
                    + "dashboard derives from the nodes' check-ins; health-check the nodes themselves, or drop this "
                    + "registration.");
            }

            return await Evaluate(tenant, cancellationToken).ConfigureAwait(false);
        }

        return HealthCheckResult.Unhealthy(target.SchedulerName is null
            ? "There is no default Quartz scheduler in this container, so this health check has nothing to "
              + "report on. Every scheduler here is registered under a name; call AddQuartzHealthChecks() on "
              + "the scheduler's own builder, or AddQuartz(schedulerName) on the health checks builder, so the "
              + "check knows which one it is for."
            : $"There is no Quartz scheduler named '{target.SchedulerName}' in this container, nor a scheduler "
              + "added at runtime under that name.");
    }

    /// <summary>
    /// Reports on a scheduler, however it was found.
    /// </summary>
    private async ValueTask<HealthCheckResult> Evaluate(IScheduler scheduler, CancellationToken cancellationToken)
    {
        string name = scheduler.SchedulerName;

        switch (scheduler.Status)
        {
            case SchedulerStatus.Running:
                break;

            // Alive, reachable and deliberately firing nothing. Reporting healthy would hide an
            // application that never started its scheduler; reporting unhealthy would take a node out of
            // rotation for doing exactly what it was told. QuartzHealthCheckOptions.StandbyStatus is
            // there for the deployment that needs the other answer and has no endpoint to remap it on.
            case SchedulerStatus.Standby:
                return await WithRequiredJobs(
                    scheduler,
                    new HealthCheckResult(StandbyStatus(), $"Quartz scheduler '{name}' is in standby"),
                    cancellationToken).ConfigureAwait(false);

            // The same argument as standby, one step earlier: a scheduler whose AutoStart is false was
            // never meant to be started by the host, so its being Created is the configuration working
            // rather than failing. Unhealthy here would take a correctly configured node out of
            // rotation for the window between the host starting and the application pressing start.
            case SchedulerStatus.Created when StartedByTheApplication():
                return HealthCheckResult.Degraded($"Quartz scheduler '{name}' has been created and is started by the application");

            case SchedulerStatus.Created:
                return HealthCheckResult.Unhealthy($"Quartz scheduler '{name}' has been created but never started");

            case SchedulerStatus.ShuttingDown:
                return HealthCheckResult.Unhealthy($"Quartz scheduler '{name}' is shutting down");

            case SchedulerStatus.Shutdown:
                return HealthCheckResult.Unhealthy($"Quartz scheduler '{name}' has been shut down");

            default:
                return HealthCheckResult.Unhealthy($"Quartz scheduler '{name}' did not report a state it is in");
        }

        SchedulerMetadata metadata;
        try
        {
            // Ask for a job we know doesn't exist
            await scheduler.Exists(new JobKey(Guid.NewGuid().ToString()), cancellationToken).ConfigureAwait(false);
            metadata = await scheduler.GetMetadata(cancellationToken).ConfigureAwait(false);
        }
        catch (SchedulerException)
        {
            return HealthCheckResult.Unhealthy($"Quartz scheduler '{name}' cannot connect to the store");
        }

        HealthCheckResult? problem = null;

        if (metadata.JobStoreClustered && Tolerance(static options => options.ClusterCheckinTolerance) is { } tolerance)
        {
            problem = await CheckClusterCheckin(scheduler, name, tolerance, cancellationToken).ConfigureAwait(false);
        }

        if (Tolerance(static options => options.StaleFiringTolerance) is { } staleTolerance)
        {
            // Both readings are taken, and the worse verdict wins - HealthStatus counts down from
            // Unhealthy, so the lower value is the graver one. A node whose check-in has stopped is
            // degraded and one that has stopped firing altogether can be unhealthy, so returning the
            // first finding would let the milder one hide the graver one, and a node kept in rotation by
            // a downgraded verdict is the failure this check exists to prevent.
            HealthCheckResult? stale = await CheckStaleFiring(scheduler, name, staleTolerance, cancellationToken).ConfigureAwait(false);
            if (stale is { } found && (problem is not { } reported || found.Status < reported.Status))
            {
                problem = found;
            }
        }

        return await WithRequiredJobs(
            scheduler,
            problem ?? HealthCheckResult.Healthy($"Quartz scheduler '{name}' is ready"),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The scheduler's own verdict, made worse by any required job that has not succeeded within its window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only a running or standby scheduler gets here, and only when its verdict is not already unhealthy:
    /// nothing a job's history says can make that worse. Standby is included because a job that has stopped
    /// succeeding has stopped whatever the reason, and a node taken out of rotation for a day is one whose
    /// nightly job did not run.
    /// </para>
    /// <para>
    /// The worse status wins. When both say something, the graver description comes first - the scheduler's
    /// on a tie - and the data of both is kept, so the milder finding is reported rather than hidden.
    /// </para>
    /// </remarks>
    private async ValueTask<HealthCheckResult> WithRequiredJobs(
        IScheduler scheduler,
        HealthCheckResult verdict,
        CancellationToken cancellationToken)
    {
        if (verdict.Status == HealthStatus.Unhealthy)
        {
            return verdict;
        }

        List<RequiredJobOptions> required = CheckOptions().RequiredJobs;
        if (required.Count == 0)
        {
            return verdict;
        }

        HealthCheckResult? jobs = await CheckRequiredJobs(scheduler, required, cancellationToken).ConfigureAwait(false);
        if (jobs is not { } found)
        {
            return verdict;
        }

        if (verdict.Status == HealthStatus.Healthy)
        {
            return found;
        }

        (HealthCheckResult first, HealthCheckResult second) = found.Status < verdict.Status ? (found, verdict) : (verdict, found);

        Dictionary<string, object> data = new(verdict.Data);
        foreach (KeyValuePair<string, object> entry in found.Data)
        {
            data[entry.Key] = entry.Value;
        }

        return new HealthCheckResult(
            first.Status,
            $"{first.Description?.TrimEnd('.')}. {second.Description}",
            first.Exception,
            data);
    }

    /// <summary>
    /// The required jobs that have not succeeded within their windows, or <see langword="null" /> when every
    /// one has.
    /// </summary>
    /// <remarks>
    /// One status read for all of them, however many there are. A job with no recorded success is judged
    /// from the first time this registration evaluated its requirements, so a process that has just started
    /// gives each job one window to run in. A store that turns out to keep no status is a misconfiguration,
    /// reported as unhealthy with what to do about it rather than read as every job being late.
    /// </remarks>
    private async ValueTask<HealthCheckResult?> CheckRequiredJobs(
        IScheduler scheduler,
        List<RequiredJobOptions> required,
        CancellationToken cancellationToken)
    {
        string name = scheduler.SchedulerName;
        DateTimeOffset now = scheduler.TimeProvider.GetUtcNow();
        DateTimeOffset watchedSince = baseline.Since(now);

        // The last entry for a job wins, which is what "adding the same job twice replaces the first" means
        // for a list that a configuration section and a callback can both have added to.
        Dictionary<JobKey, RequiredJobOptions> requirements = [];
        foreach (RequiredJobOptions requirement in required)
        {
            requirements[new JobKey(requirement.Name, requirement.Group)] = requirement;
        }

        IExecutionHistoryStore? store = RequiredJobsHistory.Find(serviceProvider, target.SchedulerName, out string? refusal);
        if (store is null)
        {
            return HealthCheckResult.Unhealthy(refusal);
        }

        PagedResult<JobRunStatus> page;
        try
        {
            page = await store.QueryJobRunStatuses(
                new JobRunStatusQuery { SchedulerName = name, Jobs = [.. requirements.Keys], Take = requirements.Count },
                cancellationToken).ConfigureAwait(false);
        }
        catch (NotSupportedException e)
        {
            // Startup validation asks the store the same question about no jobs at all, which a store that
            // has to go elsewhere for its answer - a remote host older than 4.4 - can answer without going.
            return HealthCheckResult.Unhealthy(RequiredJobsHistory.KeepsNoStatus(target.SchedulerName, store, e));
        }
        catch (SchedulerException)
        {
            return HealthCheckResult.Unhealthy($"Quartz scheduler '{name}' cannot read its jobs' run status from its execution history");
        }

        Dictionary<JobKey, JobRunStatus> statuses = [];
        foreach (JobRunStatus status in page.Items)
        {
            statuses[status.Job] = status;
        }

        List<LateJob> late = [];
        foreach ((JobKey job, RequiredJobOptions requirement) in requirements)
        {
            JobRunStatus? status = statuses.GetValueOrDefault(job);
            if (now - (status?.LastSucceededAtUtc ?? watchedSince) > requirement.SucceededWithin)
            {
                late.Add(new LateJob(job, requirement, status));
            }
        }

        if (late.Count == 0)
        {
            return null;
        }

        // HealthStatus counts down from Unhealthy, so ascending puts the gravest first; OrderBy is stable, so
        // jobs of equal status keep the order they were required in.
        late = [.. late.OrderBy(static x => x.Requirement.Status)];

        Dictionary<string, object> data = [];
        foreach (LateJob job in late)
        {
            data[job.Job.ToString()] = new Dictionary<string, object>
            {
                ["lastSucceededAtUtc"] = job.Status?.LastSucceededAtUtc is { } succeeded ? succeeded : "never",
                ["consecutiveFailures"] = job.Status?.ConsecutiveFailures ?? 0,
                ["succeededWithin"] = job.Requirement.SucceededWithin
            };
        }

        return new HealthCheckResult(late[0].Requirement.Status, Describe(name, late, now, watchedSince), exception: null, data);
    }

    /// <summary>
    /// Names the first late job, says how late it is, and counts the others.
    /// </summary>
    private static string Describe(string name, List<LateJob> late, DateTimeOffset now, DateTimeOffset watchedSince)
    {
        LateJob first = late[0];

        string lastSuccess = first.Status?.LastSucceededAtUtc is { } last
            ? $"it last succeeded {Span(now - last)} ago"
            : $"it has not succeeded in the {Span(now - watchedSince)} since the check started watching it";

        string failures = first.Status?.ConsecutiveFailures switch
        {
            null or 0 => "",
            1 => " and its last run failed",
            { } count => $" and its last {count} runs failed"
        };

        string others = late.Count switch
        {
            1 => "",
            2 => ". 1 other required job has not succeeded within its window either",
            _ => $". {late.Count - 1} other required jobs have not succeeded within their windows either"
        };

        return $"Quartz scheduler '{name}' requires job '{first.Job}' to succeed within "
               + $"{Span(first.Requirement.SucceededWithin)}; {lastSuccess}{failures}{others}";
    }

    private static string Span(TimeSpan span) => span.ToString("c", CultureInfo.InvariantCulture);

    /// <summary>
    /// This check's options, read under its scheduler's name like every other per-scheduler setting.
    /// </summary>
    private QuartzHealthCheckOptions CheckOptions() => checkOptions.Get(target.SchedulerName ?? Options.DefaultName);

    /// <summary>
    /// A required job that has not succeeded within its window, with its status when the store has one.
    /// </summary>
    private readonly record struct LateJob(JobKey Job, RequiredJobOptions Requirement, JobRunStatus? Status);

    /// <summary>
    /// Whether this node's cluster manager is still checking in, or <see langword="null" /> when it is.
    /// </summary>
    /// <remarks>
    /// Everything above this asserts that the scheduler can fire and that the store answers. A node
    /// whose check-in loop has stopped passes both while its peers are recovering its triggers, because
    /// to them it is dead — which is the one failure a health check over a clustered scheduler most
    /// needs to report, and the one it could not.
    /// </remarks>
    private static async ValueTask<HealthCheckResult?> CheckClusterCheckin(
        IScheduler scheduler,
        string name,
        double tolerance,
        CancellationToken cancellationToken)
    {
        List<ClusterNode> nodes;
        try
        {
            nodes = await scheduler.QueryClusterNodes(cancellationToken).ConfigureAwait(false);
        }
        catch (SchedulerException)
        {
            return HealthCheckResult.Unhealthy($"Quartz scheduler '{name}' cannot read its cluster's check-in state");
        }

        ClusterNode? self = nodes.FirstOrDefault(node => node.IsCurrentNode);
        if (self is null || self.LastCheckInUtc is not { } lastCheckIn || self.CheckInInterval is not { } interval)
        {
            // A node that has not checked in yet has no row to read, and a store that keeps no check-in
            // history has nothing to say either. Neither is a wedged cluster manager.
            return null;
        }

        TimeSpan allowed = interval * tolerance;
        TimeSpan since = scheduler.TimeProvider.GetUtcNow() - lastCheckIn;
        if (since <= allowed)
        {
            return null;
        }

        return HealthCheckResult.Degraded(
            $"Quartz scheduler '{name}' last checked in {since.TotalSeconds:F0}s ago on node "
            + $"'{self.InstanceId}', which is more than {tolerance:0.##} × its {interval.TotalSeconds:F0}s "
            + "check-in interval. Its cluster manager is not running, so its peers will recover its "
            + "triggers while it reports itself as running.");
    }

    /// <summary>
    /// Whether this scheduler is still getting work out of its queue, or <see langword="null" /> when it
    /// is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The silent stall. Everything above this — running, store answers, cluster manager alive — can be
    /// true of a scheduler that has not fired anything for an hour, because none of those questions is
    /// about work leaving the queue. So the store is asked for a trigger that is schedulable and whose
    /// fire time has passed by more than <paramref name="tolerance" /> misfire thresholds, which is the
    /// store's own unit for "late enough to matter".
    /// </para>
    /// <para>
    /// Two bars, one question: past the first is degraded, past twice the first is unhealthy, because a
    /// backlog that keeps growing has stopped being a delay. The second query only runs once the first
    /// has found something, so a scheduler that is keeping up is asked exactly once — and it is a filter
    /// rather than an ordering so that the database store answers each with a seek on the index it
    /// already carries over trigger state and next fire time.
    /// </para>
    /// </remarks>
    private async ValueTask<HealthCheckResult?> CheckStaleFiring(
        IScheduler scheduler,
        string name,
        double tolerance,
        CancellationToken cancellationToken)
    {
        TimeSpan threshold = MisfireThreshold();
        TimeSpan allowed = threshold * tolerance;
        DateTimeOffset now = scheduler.TimeProvider.GetUtcNow();
        DateTimeOffset unhealthyCutoff = now - allowed - allowed;

        TriggerHeader? overdue;
        HealthStatus status = HealthStatus.Degraded;
        double crossed = tolerance;

        try
        {
            overdue = await Overdue(scheduler, now - allowed, cancellationToken).ConfigureAwait(false);
            if (overdue is null)
            {
                // Nothing schedulable is late. A paused scheduler lands here too, and deliberately: a
                // paused trigger is not in TriggerState.Normal, so pausing everything is not a stall.
                return null;
            }

            if (overdue.NextFireTimeUtc < unhealthyCutoff)
            {
                status = HealthStatus.Unhealthy;
                crossed = tolerance + tolerance;
            }
            else if (await Overdue(scheduler, unhealthyCutoff, cancellationToken).ConfigureAwait(false) is { } worse)
            {
                // The first answer is whichever overdue trigger the store listed first, so this is what
                // establishes that none of the others is past the worse bar either.
                overdue = worse;
                status = HealthStatus.Unhealthy;
                crossed = tolerance + tolerance;
            }
        }
        catch (SchedulerException)
        {
            return HealthCheckResult.Unhealthy($"Quartz scheduler '{name}' cannot read the triggers it is due to fire");
        }

        DateTimeOffset due = overdue.NextFireTimeUtc.GetValueOrDefault();
        TimeSpan late = now - due;

        return new HealthCheckResult(
            status,
            $"Quartz scheduler '{name}' has not fired trigger '{overdue.Key}', which was due {late.TotalSeconds:F0}s "
            + $"ago — more than {crossed:0.##} × its {threshold.TotalSeconds:F0}s misfire threshold. The scheduler "
            + "reports itself as running and its store answers, so work is not leaving its queue.",
            exception: null,
            new Dictionary<string, object>
            {
                ["overdueTrigger"] = overdue.Key.ToString(),
                ["overdueSince"] = due,
                ["overdueBy"] = late,
                ["misfireThreshold"] = threshold
            });
    }

    /// <summary>
    /// The first schedulable trigger whose fire time passed before <paramref name="cutoff" />, or
    /// <see langword="null" /> when none has.
    /// </summary>
    /// <remarks>
    /// The answer is re-read rather than trusted: a job store from outside this repository is free to
    /// ignore a filter it has never heard of, and a stall reported from a trigger that is not overdue
    /// would be worse than one not reported at all.
    /// </remarks>
    private static async ValueTask<TriggerHeader?> Overdue(
        IScheduler scheduler,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken)
    {
        PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(
            new TriggerQuery { State = TriggerState.Normal, NextFireTimeBefore = cutoff, Take = 1 },
            cancellationToken).ConfigureAwait(false);

        TriggerHeader? first = page.Items.Count > 0 ? page.Items[0] : null;
        return first?.NextFireTimeUtc < cutoff ? first : null;
    }

    /// <summary>
    /// What this scheduler's store calls "late": the misfire threshold it is actually applying.
    /// </summary>
    /// <remarks>
    /// Read off the store the container holds for this scheduler — the same singleton the scheduler was
    /// built from — rather than off <see cref="AdoJobStoreOptions" /> or
    /// <see cref="InMemoryJobStoreOptions" />, so the number is the one in force however it was set. A
    /// store of somebody else's has no threshold to read and gets one minute, the database store's
    /// default and the more forgiving of the two.
    /// </remarks>
    private TimeSpan MisfireThreshold()
    {
        IJobStore? store = serviceProvider.GetSchedulerService<IJobStore>(target.SchedulerName);

        return store is null ? DefaultMisfireThreshold : JobStores.Unwrap(store) switch
        {
            RAMJobStore inMemory => inMemory.MisfireThreshold,
            AdoJobStoreBase ado => ado.MisfireThreshold,
            _ => DefaultMisfireThreshold
        };
    }

    /// <summary>
    /// One of this check's tolerances, or <see langword="null" /> when that reading is turned off.
    /// </summary>
    /// <remarks>
    /// Zero and <see langword="null" /> mean the same thing — do not ask — so a deployment can turn a
    /// reading off by binding a number as well as by clearing the setting.
    /// </remarks>
    private double? Tolerance(Func<QuartzHealthCheckOptions, double?> select)
    {
        double? tolerance = select(checkOptions.Get(target.SchedulerName ?? Options.DefaultName));
        return tolerance is > 0 ? tolerance : null;
    }

    /// <summary>
    /// Whether this check's scheduler is one the application starts itself rather than one the hosted
    /// service starts.
    /// </summary>
    /// <remarks>
    /// The scheduler's own options, read under its own name, the way every other per-scheduler setting
    /// is. A container with no hosted service in it has nothing configuring these options at all, so
    /// <see cref="QuartzHostedServiceOptions.AutoStart" /> is its default and a created scheduler stays
    /// unhealthy — which is what it should be, since nothing is going to start it.
    /// </remarks>
    private bool StartedByTheApplication()
    {
        return !hostedServiceOptions.Get(target.SchedulerName ?? Options.DefaultName).AutoStart;
    }

    /// <summary>
    /// What standby reports for this scheduler: <see cref="HealthStatus.Degraded" />, unless
    /// <see cref="QuartzHealthCheckOptions.StandbyStatus" /> asks for something else.
    /// </summary>
    /// <remarks>
    /// Read here rather than at registration, because the registration's <c>failureStatus</c> is
    /// ASP.NET Core's own slot for "what this check reports when it says it failed" and standby is not
    /// a failure. Read under the scheduler's name, like every other per-scheduler setting.
    /// </remarks>
    private HealthStatus StandbyStatus()
    {
        return checkOptions.Get(target.SchedulerName ?? Options.DefaultName).StandbyStatus ?? HealthStatus.Degraded;
    }
}
