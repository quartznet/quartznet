using Microsoft.Extensions.Options;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Configuration;

/// <summary>
/// Answers <see cref="ISchedulerRegistry" /> from what one container was told to register, joined with
/// what its repository currently holds.
/// </summary>
/// <remarks>
/// <para>
/// The two halves answer different questions and neither is enough on its own.
/// <see cref="SchedulerNameRegistry" /> is written while <c>AddQuartz</c> runs, so it knows every
/// registration whether or not anything ever resolved it — but it holds names, not schedulers.
/// <see cref="ISchedulerRepository" /> holds schedulers, but only the ones something already built, plus
/// any a caller bound by hand. Joining them by name is what turns "these exist" and "these are alive"
/// into one answer.
/// </para>
/// <para>
/// One registration per <see cref="SchedulerRegistration.Key" /> — a name and the target it is reached
/// through — rather than one per name: two targets fronting schedulers of one name are two rows, each
/// with its own status, and the bare name is the row of the scheduler reached through no target. The
/// members of a detected cluster are left out, because the cluster's row carries them.
/// </para>
/// <para>
/// Nothing here creates a scheduler. That is the point: enumerating tenants must not start them.
/// </para>
/// </remarks>
internal sealed class ContainerSchedulerRegistry : ISchedulerRegistry
{
    private readonly SchedulerNameRegistry names;
    private readonly ISchedulerRepository repository;
    private readonly IOptionsMonitor<QuartzSchedulerOptions> schedulerOptions;
    private readonly SchedulerWindowRegistry windows;
    private readonly SchedulerTargets targets;

    public ContainerSchedulerRegistry(
        SchedulerNameRegistry names,
        ISchedulerRepository repository,
        IOptionsMonitor<QuartzSchedulerOptions> schedulerOptions,
        SchedulerWindowRegistry windows,
        SchedulerTargets targets)
    {
        this.names = names;
        this.repository = repository;
        this.schedulerOptions = schedulerOptions;
        this.windows = windows;
        this.targets = targets;
    }

    /// <summary>
    /// How long the whole listing may spend asking schedulers what state they are in.
    /// </summary>
    /// <remarks>
    /// One budget for the query rather than one per scheduler: a listing is a page render, and a
    /// process fronting five unreachable targets must not take five times as long to say so. Every
    /// scheduler is asked before any answer is waited for, so sharing the budget costs a reachable
    /// target nothing. A local scheduler spends none of it — its default interface members answer the
    /// properties, which are fields.
    /// </remarks>
    internal static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(2);

    public async ValueTask<List<SchedulerRegistration>> QuerySchedulers(CancellationToken cancellationToken = default)
    {
        // The targets a cluster has absorbed: their proxies stay bound and resolvable by key, and the
        // cluster's row lists them, so a row of their own would show every node twice.
        HashSet<string> absorbed = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, SchedulerTarget> clusters = new(StringComparer.OrdinalIgnoreCase);
        foreach (SchedulerTarget target in targets.Snapshot())
        {
            if (target.Origin == SchedulerOrigin.Cluster)
            {
                clusters[target.Name] = target;
                absorbed.UnionWith(target.Members);
            }
        }

        // Keys are matched the way the repository indexes names, so a registration and the scheduler
        // built from it are never reported as two schedulers because their spelling differs in case.
        Dictionary<string, LiveScheduler> live = new(StringComparer.OrdinalIgnoreCase);
        foreach (IScheduler scheduler in repository.LookupAll())
        {
            LiveScheduler entry = Classify(scheduler);
            if (entry.Target is not null && absorbed.Contains(entry.Target) && entry.Origin is SchedulerOrigin.Remote or SchedulerOrigin.Agent)
            {
                continue;
            }

            // Several local entries under one name - nodes of one cluster in one process - are one
            // scheduler as far as a registration is concerned. The first one answers for it.
            live.TryAdd(entry.Key, entry);
        }

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(StatusTimeout);

        // Every scheduler is asked before any answer is waited for. The budget is the listing's rather
        // than each scheduler's, so a target that spends all of it must not be able to spend anybody
        // else's: asked in turn, one stalled proxy would leave every scheduler behind it in the loop
        // being asked with a token that was already cancelled, and reported as unreachable although it
        // answered at once. A local scheduler completes before this loop reaches the next entry.
        Dictionary<string, Task<LiveState>> asked = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, TargetLiveness> heard = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, LiveScheduler> entry in live)
        {
            // A target whose process speaks to this one has said how it is, and a status it stated stands
            // in for asking: an agent that stopped heartbeating is not asked over a tunnel nobody answers.
            TargetLiveness? liveness = entry.Value.Target is { } target ? targets.Find(target)?.Liveness?.Invoke() : null;
            if (liveness is not null)
            {
                heard[entry.Key] = liveness;
            }

            if (liveness?.Status is { } stated)
            {
                asked[entry.Key] = Task.FromResult(new LiveState(stated, liveness.SchedulerInstanceId));
                continue;
            }

            asked[entry.Key] = entry.Value.Origin is SchedulerOrigin.Window
                ? AskWindow(entry.Value.Scheduler, deadline.Token, cancellationToken)
                : Ask(entry.Value.Scheduler, deadline.Token, cancellationToken);
        }

        List<SchedulerRegistration> registrations = [];
        HashSet<string> reported = new(StringComparer.OrdinalIgnoreCase);

        foreach (string name in RegisteredNames())
        {
            if (!reported.Add(name))
            {
                continue;
            }

            LiveState state = asked.TryGetValue(name, out Task<LiveState>? pending)
                ? await pending.ConfigureAwait(false)
                : default;

            registrations.Add(new SchedulerRegistration(name, SchedulerOrigin.Container, state.Status)
            {
                SchedulerInstanceId = state.SchedulerInstanceId
            });
        }

        foreach (LiveScheduler entry in live.Values)
        {
            if (!reported.Add(entry.Key))
            {
                continue;
            }

            LiveState state = await asked[entry.Key].ConfigureAwait(false);

            // A window and a cluster are not nodes: the id a window's store carries is one this process
            // invented, and a cluster is every node at once. Showing either would put a node in the
            // listing that does not exist. The nodes are on the Cluster page.
            bool isNode = entry.Origin is not (SchedulerOrigin.Window or SchedulerOrigin.Cluster);

            registrations.Add(new SchedulerRegistration(entry.Scheduler.SchedulerName, entry.Origin, state.Status)
            {
                SchedulerInstanceId = isNode ? state.SchedulerInstanceId : null,
                Target = entry.Target,
                Members = entry.Target is not null && clusters.TryGetValue(entry.Target, out SchedulerTarget? cluster)
                    ? cluster.Members
                    : [],
                LastSeenUtc = heard.TryGetValue(entry.Key, out TargetLiveness? liveness) ? liveness.LastSeenUtc : null,
            });
        }

        // Deterministic order, ordinal, as the paged queries over a job store are: by name, and the
        // schedulers of one name by target, the bare one first.
        registrations.Sort(static (left, right) =>
        {
            int byName = string.CompareOrdinal(left.Name, right.Name);
            return byName != 0 ? byName : string.CompareOrdinal(left.Target, right.Target);
        });

        return registrations;
    }

    /// <summary>
    /// What one bound scheduler is: its key, the target it is reached through, and where it came from.
    /// </summary>
    /// <remarks>
    /// A window is a scheduler this container built and bound, so nothing about the object tells it from
    /// a runtime one. What it is, is a registration fact, and the window registry is where that fact is
    /// kept. A proxy says for itself which target it stands behind and what kind of target that is.
    /// </remarks>
    private LiveScheduler Classify(IScheduler scheduler)
    {
        if (scheduler is IProxyScheduler proxy)
        {
            return new LiveScheduler(scheduler, proxy.Target, proxy.Origin);
        }

        string? windowTarget = windows.TargetOf(scheduler.SchedulerName);
        return windowTarget is not null
            ? new LiveScheduler(scheduler, windowTarget, SchedulerOrigin.Window)
            : new LiveScheduler(scheduler, Target: null, SchedulerOrigin.Runtime);
    }

    /// <summary>
    /// The names <c>AddQuartz</c> registered, the default scheduler included.
    /// </summary>
    /// <remarks>
    /// The default scheduler is the one registration whose name is not the name it was registered
    /// under — it has no service key at all, and its name is whatever its options say. Reading them is
    /// therefore the only way to learn it, and it is also what validates them.
    /// </remarks>
    private IEnumerable<string> RegisteredNames()
    {
        if (names.HasDefaultScheduler)
        {
            yield return schedulerOptions.Get(Options.DefaultName).InstanceName;
        }

        foreach (string name in names.Names)
        {
            yield return name;
        }
    }

    /// <summary>
    /// Asks a scheduler what state it is in and which node it is, treating an unanswerable question as
    /// <see cref="SchedulerStatus.Unknown" /> and no id.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked through <see cref="IScheduler.GetStatus" /> and
    /// <see cref="IScheduler.GetSchedulerInstanceId" /> rather than through the properties those default
    /// interface members answer: a local scheduler pays nothing either way, and for a scheduler in
    /// another process the properties can only do it by blocking the calling thread until the client's
    /// timeout — 100 seconds by default — expires. This is a listing, and a listing runs on a request
    /// thread.
    /// </para>
    /// <para>
    /// A listing of tenants is exactly the call that must not fail because one of them is unreachable.
    /// <see cref="SchedulerStatus.Unknown" /> already means "state could not be determined", so it is
    /// reported rather than the registration being dropped or the exception escaping. The caller's own
    /// cancellation is not swallowed: only the deadline's is.
    /// </para>
    /// </remarks>
    private static async Task<LiveState> Ask(
        IScheduler scheduler,
        CancellationToken deadline,
        CancellationToken cancellationToken)
    {
        try
        {
            SchedulerStatus status = await scheduler.GetStatus(deadline).ConfigureAwait(false);
            string instanceId = await scheduler.GetSchedulerInstanceId(deadline).ConfigureAwait(false);
            return new LiveState(status, instanceId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LiveState(SchedulerStatus.Unknown, SchedulerInstanceId: null);
        }
        catch (SchedulerException)
        {
            return new LiveState(SchedulerStatus.Unknown, SchedulerInstanceId: null);
        }
        catch (HttpRequestException)
        {
            return new LiveState(SchedulerStatus.Unknown, SchedulerInstanceId: null);
        }
    }

    /// <summary>
    /// What a window reports: the cluster's liveness, and no instance id of its own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asking a window <see cref="IScheduler.GetStatus" /> would answer <see cref="SchedulerStatus.Created" />
    /// for as long as it exists, and an operator reading that would conclude the cluster had never
    /// started. <see cref="WindowLiveness" /> asks the question that can be answered from a shared
    /// store instead.
    /// </para>
    /// <para>
    /// No instance id, because a window is not a node: the id its store carries is the one this process
    /// invented so that the window's own row could never be mistaken for a worker's, and showing it
    /// would put a node in the listing that does not exist. The nodes are on the Cluster page, which
    /// reads them from the same rows.
    /// </para>
    /// </remarks>
    private static async Task<LiveState> AskWindow(
        IScheduler scheduler,
        CancellationToken deadline,
        CancellationToken cancellationToken)
    {
        try
        {
            WindowLiveness liveness = await WindowLiveness.Read(scheduler, deadline).ConfigureAwait(false);
            return new LiveState(liveness.Status, SchedulerInstanceId: null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LiveState(SchedulerStatus.Unknown, SchedulerInstanceId: null);
        }
        catch (SchedulerException)
        {
            return new LiveState(SchedulerStatus.Unknown, SchedulerInstanceId: null);
        }
    }

    /// <summary>
    /// One bound scheduler, the target it is reached through, and where it came from.
    /// </summary>
    private readonly record struct LiveScheduler(IScheduler Scheduler, string? Target, SchedulerOrigin Origin)
    {
        public string Key => new SchedulerRef(Scheduler.SchedulerName, Target).Key;
    }

    /// <summary>
    /// What one live scheduler answered, or nothing at all when there is no scheduler to ask.
    /// </summary>
    private readonly record struct LiveState(SchedulerStatus? Status, string? SchedulerInstanceId);
}
