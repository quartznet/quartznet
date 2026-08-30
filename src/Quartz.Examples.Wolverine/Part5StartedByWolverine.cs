using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Wolverine;
using Wolverine.Runtime.Agents;

namespace Quartz.Examples.Wolverine;

/*
 * Part 5 — AutoStart = false, and Quartz started by Wolverine's runtime rather than by the host.
 *
 * QuartzHostedServiceOptions.AutoStart = false leaves the scheduler built, initialized and bound but
 * in SchedulerStatus.Created. Everything that reads a scheduler — ISchedulerRegistry, the dashboard,
 * the HTTP API — still sees it; nothing fires until something calls IScheduler.Start. Shutdown is
 * unaffected: the hosted service still stops every scheduler it created, started or not. The option's
 * own documentation names the case this is: "a library that owns its own leader election".
 *
 * Which "something" should press start depends on whether Wolverine has a message store, and this is
 * the part of the integration where the honest answer is not the tidy one.
 *
 *   - WITHOUT persistence — the default in this example, and what CI compiles and runs — Wolverine
 *     runs no agents at all. WolverineRuntime.startAgentsAsync() opens with
 *     `if (Storage is NullMessageStore) { ...; return; }`, so NodeAgentController is never built and
 *     the IAgentFamily registrations in the container are never read. AddSingularAgent<T>() would
 *     compile, register, and silently never start. So the faithful form here is an ordinary
 *     IHostedService registered after UseWolverine — hosted services start in registration order, so
 *     WolverineRuntime is up before the scheduler is.
 *
 *   - WITH persistence, Wolverine's agent machinery is running and SingularAgent is the supported way
 *     to say "one node in the cluster does this". It is also the one worth being precise about:
 *     SingularAgent.EvaluateAssignmentsAsync picks
 *     `assignments.Nodes.FirstOrDefault(x => !x.IsLeader) ?? assignments.Nodes.FirstOrDefault()`, so
 *     it is once-per-cluster and *prefers a non-leader*, falling back to the leader only when there is
 *     one node. It is not leader-pinned. Wolverine's own leader-pinned family,
 *     LeaderPinnedListenerFamily, is for transport listeners and is registered internally; a user
 *     cannot add to it. Strictly-leader-only means writing an IAgentFamily and calling
 *     AssignmentGrid.RunOnLeader, which is more machinery than this example earns.
 *
 * Either way, note what is NOT being asked of Wolverine: not "which node may fire this trigger". A
 * clustered persistent Quartz store already answers that with its own lock, so a scheduler running on
 * every node still fires each trigger once. What this part buys is that the scheduler's own
 * lifecycle is subordinate to the messaging runtime's — the bus is up before the first job can
 * publish into it.
 */

/// <summary>
/// Starts the scheduler once Wolverine's runtime is up. Registered after <c>UseWolverine</c>, which is
/// what puts it after <c>WolverineRuntime</c> in the hosted-service order.
/// </summary>
public sealed class SchedulerStarter : IHostedService
{
    private readonly ISchedulerFactory schedulerFactory;
    private readonly ILogger<SchedulerStarter> logger;

    public SchedulerStarter(ISchedulerFactory schedulerFactory, ILogger<SchedulerStarter> logger)
    {
        this.schedulerFactory = schedulerFactory;
        this.logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        IScheduler scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        await scheduler.Start(cancellationToken);

        logger.LogInformation("Scheduler '{Name}' started after the Wolverine runtime", scheduler.SchedulerName);
        Ledger.Record(Events.SchedulerStartedByWolverine, "IHostedService ordered after UseWolverine");
    }

    // Nothing to do: the Quartz hosted service shuts the scheduler down whether or not it started it.
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// The same job, expressed as a Wolverine agent, for the deployment that has a message store.
/// </summary>
/// <remarks>
/// Registered with <c>services.AddSingularAgent&lt;QuartzSchedulerAgent&gt;()</c>. Wolverine assigns it
/// to one node and re-assigns it when that node leaves, so the scheduler's <c>Start</c> and
/// <c>Standby</c> follow the cluster's own view of who is up.
/// </remarks>
public sealed class QuartzSchedulerAgent : SingularAgent
{
    private readonly ISchedulerFactory schedulerFactory;
    private readonly ILogger<QuartzSchedulerAgent> logger;
    private IScheduler? started;

    public QuartzSchedulerAgent(ISchedulerFactory schedulerFactory, ILogger<QuartzSchedulerAgent> logger)
        : base("quartz-scheduler")
    {
        this.schedulerFactory = schedulerFactory;
        this.logger = logger;
    }

    protected override async Task startAsync(CancellationToken cancellationToken)
    {
        started = await schedulerFactory.GetScheduler(cancellationToken);
        await started.Start(cancellationToken);

        logger.LogInformation("Scheduler '{Name}' started on this node by Wolverine", started.SchedulerName);
        Ledger.Record(Events.SchedulerStartedByWolverine, "Wolverine SingularAgent");
    }

    protected override async Task stopAsync(CancellationToken cancellationToken)
    {
        // The scheduler the agent started, not a fresh ISchedulerFactory.GetScheduler(): on host
        // shutdown Wolverine stops its agents after the Quartz hosted service has already shut the
        // scheduler down, and asking the factory for it again throws rather than handing back the
        // shut-down instance. Holding the reference and checking Status keeps the stop quiet.
        if (started is null || started.Status is SchedulerStatus.ShuttingDown or SchedulerStatus.Shutdown)
        {
            return;
        }

        // Standby rather than Shutdown: the agent may be re-assigned to this node later, and a
        // shut-down scheduler cannot be started again in the same container.
        await started.Standby(cancellationToken);
    }
}

/// <summary>
/// Registers whichever of the two forms the running configuration can actually honour.
/// </summary>
public static class Part5StartedByWolverine
{
    public static void Register(IServiceCollection services, ExampleOptions options)
    {
        if (options.HasDatabase)
        {
            services.AddSingularAgent<QuartzSchedulerAgent>();
        }
        else
        {
            services.AddHostedService<SchedulerStarter>();
        }
    }
}
