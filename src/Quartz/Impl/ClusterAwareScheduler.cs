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

namespace Quartz.Impl;

/// <summary>
/// One target of a cluster: the proxy that reaches it, and the node it answered as when it was last
/// asked.
/// </summary>
/// <param name="Target">The target's name.</param>
/// <param name="Scheduler">The proxy that reaches the node behind the target.</param>
/// <param name="InstanceId">
/// The node's scheduler instance id as it last answered it, or <see langword="null" /> when it never
/// has — which is what lets a firing be routed to the node that owns it without asking every node
/// again.
/// </param>
internal sealed record ClusterMember(string Target, IScheduler Scheduler, string? InstanceId);

/// <summary>
/// Several targets fronting the nodes of one cluster, presented as one scheduler.
/// </summary>
/// <remarks>
/// <para>
/// Every node shares the store, so every member of <see cref="IScheduler" /> that reads or writes the
/// schedule is answered the same whichever node is asked — and this forwards those to the
/// <em>preferred</em> member, the first that answered the last detection round, through
/// <see cref="DelegatingScheduler" />. What belongs to one node is the part that needs saying: an
/// interruption goes to the node running the firing, an execution limit to every node, and a lifecycle
/// verb — start, stand-by, shutdown — to none of them, because <see cref="IScheduler" /> has no way to
/// name a node and acting on all of them from one button is the wrong default. A node is reached for
/// those through its own key, from the Cluster page.
/// </para>
/// <para>
/// Failover is per detection round rather than per call: the fleet monitor re-elects the preferred
/// member each round and rebuilds this. Between rounds a dead preferred member surfaces as that member's
/// error, for at most the detection interval.
/// </para>
/// <para>
/// Disposal is inherited and reaches the preferred member, which owns nothing to release: a proxy's
/// <see cref="IAsyncDisposable.DisposeAsync" /> pointedly does not shut down the scheduler it stands for.
/// </para>
/// </remarks>
internal sealed class ClusterAwareScheduler : DelegatingScheduler, IProxyScheduler
{
    private readonly ClusterMember[] members;

    /// <param name="target">The cluster's target, <c>a+b+c</c>.</param>
    /// <param name="members">The member targets; held sorted by target whatever order they arrive in.</param>
    /// <param name="preferred">
    /// Which of <paramref name="members" /> — by the position it has in the sorted list — the store-backed
    /// members are forwarded to.
    /// </param>
    public ClusterAwareScheduler(string target, IReadOnlyList<ClusterMember> members, int preferred)
        : this(target, Sorted(members), preferred)
    {
    }

    private ClusterAwareScheduler(string target, ClusterMember[] members, int preferred)
        : base(members[preferred].Scheduler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        Target = target;
        this.members = members;
        Preferred = members[preferred];
    }

    private static ClusterMember[] Sorted(IReadOnlyList<ClusterMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (members.Count < 2)
        {
            throw new ArgumentException("A cluster is at least two members.", nameof(members));
        }

        ClusterMember[] sorted = [.. members];
        Array.Sort(sorted, static (left, right) => string.Compare(left.Target, right.Target, StringComparison.OrdinalIgnoreCase));
        return sorted;
    }

    /// <summary>
    /// The cluster's target: its members' targets, sorted and joined with <c>+</c>.
    /// </summary>
    public string Target { get; }

    string? IProxyScheduler.Target => Target;

    public SchedulerOrigin Origin => SchedulerOrigin.Cluster;

    /// <summary>
    /// The members, sorted by target.
    /// </summary>
    public IReadOnlyList<ClusterMember> Members => members;

    /// <summary>
    /// The member every store-backed call is forwarded to.
    /// </summary>
    public ClusterMember Preferred { get; }

    /// <inheritdoc />
    /// <remarks>
    /// The cluster's target, as the listing reports no node for a cluster: it is every node at once.
    /// </remarks>
    public override string SchedulerInstanceId => Target;

    /// <inheritdoc />
    public override ValueTask<string> GetSchedulerInstanceId(CancellationToken cancellationToken = default)
    {
        return new ValueTask<string>(Target);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Blocking, as every proxy's is: <see cref="IScheduler" /> declares this a property. Prefer
    /// <see cref="GetStatus" />.
    /// </remarks>
    public override SchedulerStatus Status => GetStatusBlocking();

    private SchedulerStatus GetStatusBlocking()
    {
#pragma warning disable CA2012
        return GetStatus(CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
#pragma warning restore CA2012
    }

    /// <inheritdoc />
    /// <remarks>
    /// Derived from the members that answered: <see cref="SchedulerStatus.Running" /> if any is, else
    /// <see cref="SchedulerStatus.Standby" /> if any is, else what the rest agree on, and
    /// <see cref="SchedulerStatus.Unknown" /> when none answered at all.
    /// </remarks>
    public override async ValueTask<SchedulerStatus> GetStatus(CancellationToken cancellationToken = default)
    {
        List<SchedulerStatus> answered = await AskEach(
            static (scheduler, token) => scheduler.GetStatus(token),
            rethrowWhenNoneAnswered: false,
            cancellationToken).ConfigureAwait(false);

        return Derive(answered);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The preferred member's, with what is a node's own figure summed over every member that answered:
    /// jobs executed, jobs executing, the earliest start. The row must not read as one node.
    /// </remarks>
    public override async ValueTask<SchedulerMetadata> GetMetadata(CancellationToken cancellationToken = default)
    {
        SchedulerMetadata preferred = await InnerScheduler.GetMetadata(cancellationToken).ConfigureAwait(false);

        List<SchedulerMetadata> answered = await AskEach(
            static (scheduler, token) => scheduler.GetMetadata(token),
            rethrowWhenNoneAnswered: false,
            cancellationToken).ConfigureAwait(false);

        int jobsExecuted = 0;
        int executing = 0;
        DateTimeOffset? runningSince = null;
        List<SchedulerStatus> statuses = new(answered.Count);
        foreach (SchedulerMetadata metadata in answered)
        {
            jobsExecuted += metadata.JobsExecuted;
            executing += metadata.LocalExecutingJobs;
            statuses.Add(metadata.Status);
            if (metadata.RunningSince is { } since && (runningSince is null || since < runningSince))
            {
                runningSince = since;
            }
        }

        return preferred with
        {
            SchedulerInstanceId = Target,
            IsProxy = true,
            Status = Derive(statuses),
            JobsExecuted = jobsExecuted,
            LocalExecutingJobs = executing,
            RunningSince = runningSince,
        };
    }

    /// <inheritdoc />
    public override SchedulerContext Context => throw NotSupportedOnACluster(
        nameof(Context),
        "the context is a live object in each node's own process");

    /// <inheritdoc />
    public override IListenerManager ListenerManager => throw NotSupportedOnACluster(
        nameof(ListenerManager),
        "listeners run in the process the jobs run in, which is every node's and not this one's");

    /// <inheritdoc />
    public override ValueTask Start(CancellationToken cancellationToken = default)
    {
        throw NotSupportedOnACluster(nameof(Start), NodeLocal);
    }

    /// <inheritdoc />
    public override ValueTask StartDelayed(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        throw NotSupportedOnACluster(nameof(StartDelayed), NodeLocal);
    }

    /// <inheritdoc />
    public override ValueTask Standby(CancellationToken cancellationToken = default)
    {
        throw NotSupportedOnACluster(nameof(Standby), NodeLocal);
    }

    /// <inheritdoc />
    public override ValueTask Shutdown(bool waitForJobsToComplete = false, CancellationToken cancellationToken = default)
    {
        throw NotSupportedOnACluster(nameof(Shutdown), NodeLocal);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Asked of every member, because the job may be running on any node; <see langword="true" /> when
    /// any of them interrupted something.
    /// </remarks>
    public override async ValueTask<bool> Interrupt(JobKey jobKey, CancellationToken cancellationToken = default)
    {
        List<bool> answered = await AskEach(
            (scheduler, token) => scheduler.Interrupt(jobKey, token),
            rethrowWhenNoneAnswered: true,
            cancellationToken).ConfigureAwait(false);

        return answered.Contains(true);
    }

    /// <inheritdoc />
    /// <remarks>
    /// A firing belongs to one node. The store says which — every node sees every firing of a persistent
    /// store — so the interruption goes to the member that answered as that node, and to every member
    /// when no member did.
    /// </remarks>
    public override async ValueTask<bool> InterruptFireInstance(string fireInstanceId, CancellationToken cancellationToken = default)
    {
        string? owner = await OwnerOf(fireInstanceId, cancellationToken).ConfigureAwait(false);
        ClusterMember? owning = owner is null
            ? null
            : members.FirstOrDefault(member => string.Equals(member.InstanceId, owner, StringComparison.OrdinalIgnoreCase));

        if (owning is not null)
        {
            return await owning.Scheduler.InterruptFireInstance(fireInstanceId, cancellationToken).ConfigureAwait(false);
        }

        List<bool> answered = await AskEach(
            (scheduler, token) => scheduler.InterruptFireInstance(fireInstanceId, token),
            rethrowWhenNoneAnswered: true,
            cancellationToken).ConfigureAwait(false);

        return answered.Contains(true);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Set on every member, in target order: a node-scoped limit is each node's own, and a cluster-scoped
    /// one is counted in the store and harmless to set everywhere. The first failure is reported with the
    /// members already set, so an operator knows what the cluster was left with.
    /// </remarks>
    public override async ValueTask SetExecutionLimits(ExecutionLimits? limits, CancellationToken cancellationToken = default)
    {
        List<string> done = [];
        foreach (ClusterMember member in members)
        {
            try
            {
                await member.Scheduler.SetExecutionLimits(limits, cancellationToken).ConfigureAwait(false);
                done.Add(member.Target);
            }
            catch (Exception failure) when (failure is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                string already = done.Count == 0 ? "no member" : string.Join(", ", done);
                throw new SchedulerException(
                    $"Setting execution limits on cluster '{Target}' failed at member '{member.Target}' after setting them on {already}: {failure.Message}",
                    failure);
            }
        }
    }

    private const string NodeLocal = "it acts on one node, and a cluster is every node at once; open the Cluster page and pick the node";

    private NotSupportedException NotSupportedOnACluster(string member, string reason)
    {
        return new NotSupportedException($"{member} is not supported on cluster '{Target}': {reason}.");
    }

    /// <summary>
    /// The node a firing belongs to, as the store records it, or <see langword="null" /> when the store
    /// holds no such firing.
    /// </summary>
    private async ValueTask<string?> OwnerOf(string fireInstanceId, CancellationToken cancellationToken)
    {
        const int pageSize = 250;
        const int mostPages = 40;

        for (int page = 0; page < mostPages; page++)
        {
            PagedResult<FireInstance> firings = await InnerScheduler.QueryFireInstances(
                new FireInstanceQuery { State = null, Skip = page * pageSize, Take = pageSize },
                cancellationToken).ConfigureAwait(false);

            FireInstance? firing = firings.Items.FirstOrDefault(x => string.Equals(x.FireInstanceId, fireInstanceId, StringComparison.Ordinal));
            if (firing is not null)
            {
                return firing.SchedulerInstanceId;
            }

            if (!firings.HasMore)
            {
                break;
            }
        }

        return null;
    }

    /// <summary>
    /// Asks every member the same question at once and keeps the answers of those that gave one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A member that is unreachable, or that refuses, is left out rather than failing the whole
    /// question: the cluster is what the reachable nodes are. When none answered and the caller needs an
    /// answer, the first failure is what it gets.
    /// </para>
    /// <para>
    /// The caller's token bounds the question as a whole: a member still pending when it fires is one
    /// that did not answer, and what the others said stands — a listing under its deadline reports the
    /// nodes that answered in time. Only when nothing answered at all is the cancellation the answer.
    /// </para>
    /// </remarks>
    private async ValueTask<List<T>> AskEach<T>(
        Func<IScheduler, CancellationToken, ValueTask<T>> ask,
        bool rethrowWhenNoneAnswered,
        CancellationToken cancellationToken)
    {
        Task<T>[] asked = new Task<T>[members.Length];
        for (int i = 0; i < members.Length; i++)
        {
            try
            {
                asked[i] = ask(members[i].Scheduler, cancellationToken).AsTask();
            }
            catch (Exception failure)
            {
                // A proxy that refuses before it yields is a member that did not answer, like one that
                // refuses after.
                asked[i] = Task.FromException<T>(failure);
            }
        }

        List<T> answered = new(members.Length);
        Exception? firstFailure = null;
        foreach (Task<T> task in asked)
        {
            try
            {
                answered.Add(await task.ConfigureAwait(false));
            }
            catch (Exception failure)
            {
                firstFailure ??= failure;
            }
        }

        if (answered.Count == 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (rethrowWhenNoneAnswered && firstFailure is not null)
            {
                throw new SchedulerException($"No member of cluster '{Target}' answered: {firstFailure.Message}", firstFailure);
            }
        }

        return answered;
    }

    /// <summary>
    /// One status for the cluster out of its members': running if any node is, else standing by if any
    /// is, else what the nodes that answered say, and unknown when none did.
    /// </summary>
    private static SchedulerStatus Derive(List<SchedulerStatus> answered)
    {
        // The default of the enum is Unknown, which is also the answer when nothing in the precedence
        // list was answered.
        return answered.Count == 0 ? SchedulerStatus.Unknown : Precedence.FirstOrDefault(answered.Contains);
    }

    private static readonly SchedulerStatus[] Precedence =
    [
        SchedulerStatus.Running,
        SchedulerStatus.Standby,
        SchedulerStatus.ShuttingDown,
        SchedulerStatus.Created,
        SchedulerStatus.Shutdown,
    ];
}
