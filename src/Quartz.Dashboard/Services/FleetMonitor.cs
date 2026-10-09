#region License
/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */
#endregion

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Dashboard.Services;

/// <summary>
/// Finds the targets that front the nodes of one cluster and presents them as one scheduler.
/// </summary>
/// <remarks>
/// <para>
/// A round asks every HTTP and agent target whose scheduler shares a name with another's what store it
/// runs on and which nodes it sees. Targets on a clustered persistent store that see a node in common
/// are one cluster: a <see cref="ClusterAwareScheduler" /> is bound under the target
/// <c>a+b+c</c>, a <see cref="SchedulerTarget" /> of that name carries the members, and the listing shows
/// the cluster's row in place of the members'. The members stay bound and reachable by their own keys,
/// which is how the Cluster page reaches one node.
/// </para>
/// <para>
/// Membership is sticky. A member that fails to answer a round stays in its cluster and is shown as an
/// unreachable node; it leaves only when it answers with a node set disjoint from what the members that
/// did answer see, or when its target is removed. So the cluster's key changes on a topology change and
/// never on an outage, and a remembered selection, a link and a hub group all survive a node going down.
/// The preferred member — the one the store-backed calls are forwarded to — is re-elected each round
/// among those that answered, and the proxy is rebuilt only when that, the members or their nodes change.
/// </para>
/// <para>
/// The first round runs once the host has started, after the HTTP targets have been bound and the
/// attached stores discovered; then one runs every <see cref="QuartzDashboardOptions.ClusterDetectionInterval" />
/// on the container's clock, and one whenever a target is added or removed. A null interval turns the
/// monitor off.
/// </para>
/// </remarks>
internal sealed class FleetMonitor : IHostedLifecycleService, IDisposable
{
    private readonly ISchedulerRepository repository;
    private readonly SchedulerTargets targets;

    /// <summary>
    /// Whether the container holds a repository at all. A dashboard registered beside no scheduler of any
    /// kind holds none, and then there is nothing to monitor and nothing is started.
    /// </summary>
    private readonly bool hasFleet;

    private readonly IOptions<QuartzDashboardOptions> options;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<FleetMonitor> logger;

    /// <summary>
    /// One round at a time; a request that arrives during a round runs another after it.
    /// </summary>
    private readonly SemaphoreSlim rounds = new(1, 1);

    /// <summary>
    /// The clusters this monitor has formed, by their current target.
    /// </summary>
    /// <remarks>
    /// Read and written inside a round only, which <see cref="rounds" /> serialises.
    /// </remarks>
    private readonly Dictionary<string, Cluster> clusters = new(StringComparer.OrdinalIgnoreCase);

    private readonly CancellationTokenSource stopping = new();

    private ITimer? timer;
    private int roundRequested;
    private bool subscribed;

    /// <summary>
    /// Set on the thread while this monitor is writing the target registry, so that the change it is
    /// told about is not taken as a reason for another round.
    /// </summary>
    /// <remarks>
    /// Thread-static because <see cref="SchedulerTargets.Changed" /> is raised on the thread that made the
    /// change, which is the one place the monitor can tell its own writes from an agent's. Written only
    /// through <see cref="EnterOwnChange" /> and <see cref="ExitOwnChange" />.
    /// </remarks>
    [ThreadStatic]
    private static bool changingTargets;

    /// <param name="services">
    /// The container, which the repository and the target registry are read from when it holds them. A
    /// dashboard registered into a container with no scheduler of any kind holds neither, and then there
    /// is nothing to monitor.
    /// </param>
    /// <param name="options">The dashboard's options, for the detection interval.</param>
    /// <param name="logger">Where a cluster forming, changing and dissolving is recorded.</param>
    public FleetMonitor(
        IServiceProvider services,
        IOptions<QuartzDashboardOptions> options,
        ILogger<FleetMonitor> logger)
    {
        ArgumentNullException.ThrowIfNull(services);

        ISchedulerRepository? found = services.GetService<ISchedulerRepository>();
        hasFleet = found is not null;
        repository = found ?? new SchedulerRepository();
        targets = services.GetService<SchedulerTargets>() ?? new SchedulerTargets();
        timeProvider = services.GetService<TimeProvider>() ?? TimeProvider.System;
        this.options = options;
        this.logger = logger;
    }

    public Task StartingAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// The first round, awaited, so that a host that has started has a listing with its clusters in it;
    /// then the timer and the subscription.
    /// </summary>
    public async Task StartedAsync(CancellationToken cancellationToken)
    {
        if (options.Value.ClusterDetectionInterval is not { } interval || !hasFleet)
        {
            return;
        }

        targets.Changed += OnTargetsChanged;
        subscribed = true;

        await RunRound(cancellationToken).ConfigureAwait(false);

        timer = timeProvider.CreateTimer(
            static state => ((FleetMonitor) state!).RequestRound(),
            this,
            interval,
            interval);
    }

    public Task StoppingAsync(CancellationToken cancellationToken)
    {
        Halt();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task StoppedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <remarks>
    /// A round started from the timer may still be in flight; it releases <see cref="rounds" /> guarded,
    /// so disposing the semaphore under it is safe.
    /// </remarks>
    public void Dispose()
    {
        Halt();
        stopping.Dispose();
        rounds.Dispose();
    }

    private void Halt()
    {
        if (subscribed)
        {
            targets.Changed -= OnTargetsChanged;
            subscribed = false;
        }

        timer?.Dispose();
        timer = null;

        if (!stopping.IsCancellationRequested)
        {
            stopping.Cancel();
        }
    }

    private void OnTargetsChanged(object? sender, EventArgs e)
    {
        if (changingTargets)
        {
            return;
        }

        RequestRound();
    }

    /// <summary>
    /// Asks for a round, which runs now if none is running and after the current one otherwise.
    /// </summary>
    /// <remarks>
    /// Started and not awaited, from a timer callback and an event handler: every failure a round can
    /// produce is handled inside it, so there is no fault to observe.
    /// </remarks>
    private void RequestRound()
    {
        Interlocked.Exchange(ref roundRequested, 1);
        _ = RunRequestedRounds(stopping.Token);
    }

    /// <summary>
    /// One round, awaited, for the caller that wants the clusters settled when it returns.
    /// </summary>
    internal async Task RunRound(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref roundRequested, 1);
        await RunRequestedRounds(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunRequestedRounds(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (!await rounds.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
            {
                // A round is running; it reads the request before it releases, or the loop below does.
                return;
            }

            try
            {
                while (Interlocked.Exchange(ref roundRequested, 0) == 1 && !stopping.IsCancellationRequested)
                {
                    await Round(cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                ReleaseRounds();
            }

            if (Volatile.Read(ref roundRequested) == 0)
            {
                return;
            }
        }
    }

    private void ReleaseRounds()
    {
        try
        {
            rounds.Release();
        }
        catch (ObjectDisposedException)
        {
            // Disposed under a round the timer started; there is nothing left to serialise.
        }
    }

    private async Task Round(CancellationToken cancellationToken)
    {
        try
        {
            await Detect(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || stopping.IsCancellationRequested)
        {
            // The host is stopping, which is not a detection failure.
        }
        catch (Exception failure)
        {
            logger.FleetRoundFailed(failure);
        }
    }

    /// <summary>
    /// One detection round: the candidates, the clusters pruned of targets that went away, every group
    /// of two or more asked at once, and each group settled.
    /// </summary>
    private async Task Detect(CancellationToken cancellationToken)
    {
        Dictionary<string, Candidate> candidates = Candidates();
        PruneClusters(candidates);

        Dictionary<string, List<Candidate>> byName = GroupByName(candidates);
        await AskGroups(byName, cancellationToken).ConfigureAwait(false);

        foreach ((string schedulerName, List<Candidate> group) in byName)
        {
            if (group.Count >= 2)
            {
                Settle(schedulerName, group);
            }
        }
    }

    /// <summary>
    /// Every proxy reached through a target, by target.
    /// </summary>
    private Dictionary<string, Candidate> Candidates()
    {
        Dictionary<string, Candidate> candidates = new(StringComparer.OrdinalIgnoreCase);
        foreach (IScheduler scheduler in repository.LookupAll())
        {
            if (scheduler is IProxyScheduler { Origin: SchedulerOrigin.Remote or SchedulerOrigin.Agent, Target: { } target })
            {
                candidates[target] = new Candidate(target, scheduler);
            }
        }

        return candidates;
    }

    /// <summary>
    /// A member whose target is gone leaves its cluster; a cluster left with one member dissolves.
    /// </summary>
    private void PruneClusters(Dictionary<string, Candidate> candidates)
    {
        foreach (Cluster cluster in clusters.Values.ToList())
        {
            foreach (string member in cluster.Members.Keys.ToList())
            {
                if (!candidates.TryGetValue(member, out Candidate? candidate)
                    || !string.Equals(candidate.Scheduler.SchedulerName, cluster.SchedulerName, StringComparison.OrdinalIgnoreCase))
                {
                    cluster.Members.Remove(member);
                }
            }

            if (cluster.Members.Count < 2)
            {
                Dissolve(cluster);
            }
        }
    }

    private static Dictionary<string, List<Candidate>> GroupByName(Dictionary<string, Candidate> candidates)
    {
        Dictionary<string, List<Candidate>> byName = new(StringComparer.OrdinalIgnoreCase);
        foreach (Candidate candidate in candidates.Values)
        {
            if (!byName.TryGetValue(candidate.Scheduler.SchedulerName, out List<Candidate>? group))
            {
                byName[candidate.Scheduler.SchedulerName] = group = [];
            }

            group.Add(candidate);
        }

        return byName;
    }

    /// <summary>
    /// Asks every candidate of every group of two or more, all at once under one deadline, as the listing
    /// asks. A name one target fronts has nothing to merge and is not asked.
    /// </summary>
    private async Task AskGroups(Dictionary<string, List<Candidate>> byName, CancellationToken cancellationToken)
    {
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
        deadline.CancelAfter(ContainerSchedulerRegistry.StatusTimeout);

        List<Task> asked = byName.Values
            .Where(group => group.Count >= 2)
            .SelectMany(group => group)
            .Select(candidate => Ask(candidate, deadline.Token, cancellationToken))
            .ToList();

        await Task.WhenAll(asked).ConfigureAwait(false);
    }

    /// <summary>
    /// Asks one candidate what store it runs on and which nodes it sees, recording the answer on the
    /// candidate, or nothing when it did not answer in time.
    /// </summary>
    /// <remarks>
    /// Any failure is this candidate's alone: a round is about every target, and one that cannot be asked
    /// is left as the last round left it. Only the caller's own cancellation ends the round.
    /// </remarks>
    private async Task Ask(Candidate candidate, CancellationToken deadline, CancellationToken cancellationToken)
    {
        try
        {
            SchedulerMetadata metadata = await candidate.Scheduler.GetMetadata(deadline).ConfigureAwait(false);
            List<ClusterNode> nodes = await candidate.Scheduler.QueryClusterNodes(deadline).ConfigureAwait(false);

            HashSet<string> nodeIds = new(StringComparer.Ordinal);
            foreach (ClusterNode node in nodes)
            {
                nodeIds.Add(node.InstanceId);
            }

            candidate.Answer = new Answer(
                metadata.SchedulerInstanceId,
                metadata.JobStoreClustered && metadata.JobStorePersistent,
                nodeIds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            logger.FleetTargetUnanswered(candidate.Target, failure);
        }
    }

    /// <summary>
    /// Applies one round's answers to the clusters of one scheduler name: departures first, then the
    /// joins and formations, then what is left is published or dissolved.
    /// </summary>
    private void Settle(string schedulerName, List<Candidate> group)
    {
        Dictionary<string, Candidate> byTarget = new(StringComparer.OrdinalIgnoreCase);
        foreach (Candidate candidate in group)
        {
            byTarget[candidate.Target] = candidate;
        }

        List<Cluster> existing = clusters.Values
            .Where(x => string.Equals(x.SchedulerName, schedulerName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (Cluster cluster in existing)
        {
            ApplyDepartures(cluster, byTarget);
        }

        HashSet<Cluster> kept = [];
        foreach (List<Entity> component in Components(JoinGraph(existing, group)))
        {
            MergeComponent(schedulerName, component, kept);
        }

        foreach (Cluster cluster in existing.Where(x => !kept.Contains(x)))
        {
            // Every member left, or the cluster shrank below two without a candidate joining it.
            if (cluster.Members.Count >= 2)
            {
                kept.Add(cluster);
            }
            else
            {
                Dissolve(cluster);
            }
        }

        foreach (Cluster cluster in kept)
        {
            if (cluster.Members.Count < 2)
            {
                Dissolve(cluster);
            }
            else
            {
                Publish(cluster, byTarget);
            }
        }
    }

    /// <summary>
    /// Takes the members that answered out of a cluster when they no longer belong to it, and keeps the
    /// ones that did not answer.
    /// </summary>
    /// <remarks>
    /// A member belongs as long as it sees a node that the members which answered <em>this round</em>
    /// also see. Judged against this round's answers only: a member that did not answer has nothing
    /// current to say, and when no other member answered there is nothing to judge against, so the
    /// member stays — a cluster of two with one node down and the other restarted under a new instance
    /// id is still the same cluster. A member whose store stopped being a clustered persistent one has
    /// left whatever the others say.
    /// </remarks>
    private void ApplyDepartures(Cluster cluster, Dictionary<string, Candidate> byTarget)
    {
        foreach (Member member in cluster.Members.Values.ToList())
        {
            if (byTarget[member.Target].Answer is not { } answer)
            {
                logger.ClusterMemberUnreachable(member.Target, cluster.Target);
                member.Answered = false;
                continue;
            }

            member.Answered = true;
            member.InstanceId = answer.InstanceId;

            if (!answer.Clustered)
            {
                cluster.Members.Remove(member.Target);
                continue;
            }

            HashSet<string> seenByOthers = NodesAnsweredByOthers(cluster, member, byTarget);
            if (seenByOthers.Count > 0 && !answer.Nodes.Overlaps(seenByOthers))
            {
                cluster.Members.Remove(member.Target);
                continue;
            }

            member.Nodes = answer.Nodes;
        }
    }

    /// <summary>
    /// The nodes the other members of a cluster answered with this round.
    /// </summary>
    private static HashSet<string> NodesAnsweredByOthers(Cluster cluster, Member member, Dictionary<string, Candidate> byTarget)
    {
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (Answer answer in cluster.Members.Values
                     .Where(other => !ReferenceEquals(other, member))
                     .Select(other => byTarget[other.Target].Answer)
                     .OfType<Answer>())
        {
            seen.UnionWith(answer.Nodes);
        }

        return seen;
    }

    /// <summary>
    /// The vertices of the join graph: the surviving clusters, and the standalone candidates that
    /// answered and pass the gate. A candidate that has never answered is never merged.
    /// </summary>
    private static List<Entity> JoinGraph(List<Cluster> existing, List<Candidate> group)
    {
        List<Entity> entities = [];
        HashSet<string> absorbed = new(StringComparer.OrdinalIgnoreCase);

        foreach (Cluster cluster in existing.Where(x => x.Members.Count >= 1))
        {
            entities.Add(new Entity(cluster, cluster.NodeUnion()));
            absorbed.UnionWith(cluster.Members.Keys);
        }

        foreach (Candidate candidate in group)
        {
            if (!absorbed.Contains(candidate.Target) && candidate.Answer is { Clustered: true } answer && answer.Nodes.Count > 0)
            {
                entities.Add(new Entity(candidate, answer.Nodes));
            }
        }

        return entities;
    }

    /// <summary>
    /// The connected components of the join graph, two vertices being joined when they see a node in
    /// common.
    /// </summary>
    private static List<List<Entity>> Components(List<Entity> entities)
    {
        int[] parent = Enumerable.Range(0, entities.Count).ToArray();
        for (int i = 0; i < entities.Count; i++)
        {
            for (int j = i + 1; j < entities.Count; j++)
            {
                if (entities[i].Nodes.Overlaps(entities[j].Nodes))
                {
                    Union(parent, i, j);
                }
            }
        }

        Dictionary<int, List<Entity>> components = [];
        for (int i = 0; i < entities.Count; i++)
        {
            int root = Find(parent, i);
            if (!components.TryGetValue(root, out List<Entity>? component))
            {
                components[root] = component = [];
            }

            component.Add(entities[i]);
        }

        return [.. components.Values];
    }

    /// <summary>
    /// Turns one component into one cluster: the first surviving cluster in it keeps the state, the
    /// other clusters hand it their members and dissolve, and the standalone candidates join. Two or more
    /// standalone candidates with no cluster among them form a new one.
    /// </summary>
    private void MergeComponent(string schedulerName, List<Entity> component, HashSet<Cluster> kept)
    {
        List<Cluster> merging = component.Select(x => x.Cluster).OfType<Cluster>().ToList();
        List<Candidate> joining = component.Select(x => x.Candidate).OfType<Candidate>().ToList();

        Cluster? cluster = merging.FirstOrDefault();
        if (cluster is null)
        {
            if (joining.Count < 2)
            {
                return;
            }

            cluster = new Cluster(schedulerName);
        }

        foreach (Cluster absorbed in merging.Skip(1))
        {
            foreach (Member member in absorbed.Members.Values)
            {
                cluster.Members[member.Target] = member;
            }

            absorbed.Members.Clear();
            Dissolve(absorbed);
        }

        foreach (Candidate candidate in joining)
        {
            cluster.Members[candidate.Target] = new Member(candidate.Target)
            {
                InstanceId = candidate.Answer!.InstanceId,
                Nodes = candidate.Answer.Nodes,
                Answered = true,
            };
        }

        kept.Add(cluster);
    }

    /// <summary>
    /// Binds a cluster's proxy under its target and registers the target, replacing what an earlier
    /// round published when the target, the members, their nodes or the preferred member changed.
    /// </summary>
    private void Publish(Cluster cluster, Dictionary<string, Candidate> byTarget)
    {
        string previousTarget = cluster.Target;
        string target = SchedulerTargets.ClusterTargetName(cluster.Members.Keys);

        List<ClusterMember> members = cluster.Members.Values
            .Select(member => new ClusterMember(member.Target, byTarget[member.Target].Scheduler, member.InstanceId))
            .OrderBy(member => member.Target, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int preferred = ElectPreferred(cluster, members);
        string signature = target + "|" + members[preferred].Target + "|"
            + string.Join(',', members.Select(x => x.Target + "=" + x.InstanceId));

        if (string.Equals(signature, cluster.Signature, StringComparison.Ordinal))
        {
            return;
        }

        ClusterAwareScheduler proxy = new(target, members, preferred);
        bool formed = cluster.Target.Length == 0;
        bool renamed = !formed && !string.Equals(previousTarget, target, StringComparison.OrdinalIgnoreCase);

        EnterOwnChange();
        try
        {
            if (!formed)
            {
                repository.Remove(cluster.SchedulerName, cluster.Target);
                if (renamed)
                {
                    targets.Remove(cluster.Target);
                }
            }

            cluster.Target = target;
            cluster.PreferredTarget = members[preferred].Target;
            cluster.Signature = signature;
            cluster.Current = proxy;

            repository.Bind(proxy, target);

            if (formed || renamed)
            {
                clusters.Remove(previousTarget);
                clusters[target] = cluster;
                targets.Add(ClusterTarget(cluster, target, members));
            }
        }
        finally
        {
            ExitOwnChange();
        }

        string memberList = string.Join(", ", members.Select(x => x.Target));
        if (formed)
        {
            logger.ClusterFormed(target, cluster.SchedulerName, memberList);
        }
        else if (renamed)
        {
            logger.ClusterChanged(previousTarget, cluster.SchedulerName, target, memberList);
        }
    }

    /// <summary>
    /// The preferred member: the first, by target, that answered this round; failing that the one that
    /// was preferred, and failing that the first.
    /// </summary>
    private static int ElectPreferred(Cluster cluster, List<ClusterMember> members)
    {
        int preferred = members.FindIndex(x => cluster.Members[x.Target].Answered);
        if (preferred < 0)
        {
            preferred = Math.Max(0, members.FindIndex(x => string.Equals(x.Target, cluster.PreferredTarget, StringComparison.OrdinalIgnoreCase)));
        }

        return preferred;
    }

    /// <summary>
    /// The cluster's registration: the history is the preferred member's — the shared table when the
    /// nodes write there — and the events are every member's, interleaved. Both read the cluster's
    /// current shape at the time of asking, so a re-election needs no re-registration.
    /// </summary>
    private SchedulerTarget ClusterTarget(Cluster cluster, string target, List<ClusterMember> members)
    {
        return new SchedulerTarget
        {
            Name = target,
            Origin = SchedulerOrigin.Cluster,
            Members = members.Select(x => x.Target).ToArray(),
            History = (provider, name) => cluster.Current is { } current
                ? targets.Find(current.Preferred.Target)?.History?.Invoke(provider, name)
                : null,
            Events = (provider, name) => MergedEvents(cluster, provider, name),
        };
    }

    private MergedSchedulerEventSource? MergedEvents(Cluster cluster, IServiceProvider provider, string schedulerName)
    {
        if (cluster.Current is not { } current)
        {
            return null;
        }

        List<ISchedulerEventSource> sources = current.Members
            .Select(member => targets.Find(member.Target)?.Events?.Invoke(provider, schedulerName))
            .OfType<ISchedulerEventSource>()
            .ToList();

        return sources.Count == 0 ? null : new MergedSchedulerEventSource(sources);
    }

    private void Dissolve(Cluster cluster)
    {
        if (cluster.Target.Length == 0)
        {
            return;
        }

        EnterOwnChange();
        try
        {
            repository.Remove(cluster.SchedulerName, cluster.Target);
            targets.Remove(cluster.Target);
            clusters.Remove(cluster.Target);
        }
        finally
        {
            ExitOwnChange();
        }

        logger.ClusterDissolved(cluster.Target, cluster.SchedulerName);
        cluster.Target = string.Empty;
        cluster.Current = null;
        cluster.Members.Clear();
    }

    /// <summary>
    /// Marks the current thread as the monitor writing the target registry, until <see cref="ExitOwnChange" />.
    /// </summary>
    private static void EnterOwnChange()
    {
        changingTargets = true;
    }

    private static void ExitOwnChange()
    {
        changingTargets = false;
    }

    private static int Find(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }

        return i;
    }

    private static void Union(int[] parent, int i, int j)
    {
        int rootI = Find(parent, i);
        int rootJ = Find(parent, j);
        if (rootI != rootJ)
        {
            parent[rootJ] = rootI;
        }
    }

    /// <summary>One target asked this round, and what it answered.</summary>
    private sealed class Candidate(string target, IScheduler scheduler)
    {
        public string Target { get; } = target;

        public IScheduler Scheduler { get; } = scheduler;

        public Answer? Answer { get; set; }
    }

    /// <summary>What a target answered: its node, whether its store can cluster, and the nodes it sees.</summary>
    private sealed record Answer(string InstanceId, bool Clustered, HashSet<string> Nodes);

    /// <summary>A member of a formed cluster, and what is last known of it.</summary>
    private sealed class Member(string target)
    {
        public string Target { get; } = target;

        public string? InstanceId { get; set; }

        public HashSet<string> Nodes { get; set; } = new(StringComparer.Ordinal);

        public bool Answered { get; set; }
    }

    /// <summary>A cluster this monitor has formed, and the state the next round starts from.</summary>
    private sealed class Cluster(string schedulerName)
    {
        public string SchedulerName { get; } = schedulerName;

        /// <summary>The target the cluster is bound under, or empty before it is first published.</summary>
        public string Target { get; set; } = string.Empty;

        public string? PreferredTarget { get; set; }

        public string? Signature { get; set; }

        public ClusterAwareScheduler? Current { get; set; }

        public Dictionary<string, Member> Members { get; } = new(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> NodeUnion()
        {
            HashSet<string> union = new(StringComparer.Ordinal);
            foreach (Member member in Members.Values)
            {
                union.UnionWith(member.Nodes);
            }

            return union;
        }
    }

    /// <summary>A vertex of the join graph: a surviving cluster, or a standalone candidate.</summary>
    private sealed record Entity(Cluster? Cluster, Candidate? Candidate, HashSet<string> Nodes)
    {
        public Entity(Cluster cluster, HashSet<string> nodes) : this(cluster, Candidate: null, nodes)
        {
        }

        public Entity(Candidate candidate, HashSet<string> nodes) : this(Cluster: null, candidate, nodes)
        {
        }
    }
}
