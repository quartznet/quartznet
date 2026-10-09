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

using Quartz.Extensibility;

namespace Quartz.Configuration;

/// <summary>
/// One way a scheduler in another process is reached from this one: an HTTP target, an attached store,
/// an agent, or the cluster several of those turned out to be.
/// </summary>
/// <remarks>
/// A target is the first half of every key its schedulers are shown under — <c>prod/reporting</c> — and
/// the place a reader asks for what a key's scheduler keeps elsewhere: its history, and its live events.
/// Both are asked for through delegates rather than resolved by a service key, because an agent's target
/// arrives after the container is built and a cluster's is computed, and neither has a registration to
/// key by.
/// </remarks>
internal sealed class SchedulerTarget
{
    /// <summary>
    /// The target's name, which never contains <c>/</c> or <c>+</c>.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// What a listing reports a scheduler behind this target as.
    /// </summary>
    public required SchedulerOrigin Origin { get; init; }

    /// <summary>
    /// Where one scheduler's live events are read from, given the container and the scheduler's name,
    /// or <see langword="null" /> for a target that streams none — a window onto a database.
    /// </summary>
    public Func<IServiceProvider, string, ISchedulerEventSource?>? Events { get; init; }

    /// <summary>
    /// Where one scheduler's execution history is read from, given the container and the scheduler's
    /// name. A delegate that answers <see langword="null" /> says the target keeps no history this
    /// process can read; a target with no delegate at all says the same.
    /// </summary>
    public Func<IServiceProvider, string, IExecutionHistoryStore?>? History { get; init; }

    /// <summary>
    /// The targets a <see cref="SchedulerOrigin.Cluster" /> is made of, sorted ordinal-ignore-case; empty
    /// for every other origin.
    /// </summary>
    public string[] Members { get; init; } = [];

    /// <summary>
    /// What is known of the target's liveness without asking it, for a target whose process speaks to this
    /// one rather than being asked — an agent, which heartbeats. <see langword="null" /> for a target whose
    /// liveness is asked.
    /// </summary>
    /// <remarks>
    /// Read by the listing at the time of listing. A status it answers stands in for the one the scheduler
    /// would have been asked for, so an agent that stopped heartbeating is reported
    /// <see cref="SchedulerStatus.Unknown" /> without a round trip to a process that is not answering, and
    /// one that said goodbye is reported <see cref="SchedulerStatus.Shutdown" /> rather than unreachable. A
    /// null status leaves the asking to the listing, which is what a live agent answers through.
    /// </remarks>
    public Func<TargetLiveness>? Liveness { get; init; }
}

/// <summary>
/// What a target that speaks to this process says about itself between two listings.
/// </summary>
/// <param name="Status">
/// The state to report without asking, or <see langword="null" /> to ask the scheduler as usual.
/// </param>
/// <param name="SchedulerInstanceId">The node, when <paramref name="Status" /> stands in for asking.</param>
/// <param name="LastSeenUtc">When the process last spoke to this one.</param>
internal sealed record TargetLiveness(SchedulerStatus? Status, string? SchedulerInstanceId, DateTimeOffset? LastSeenUtc);

/// <summary>
/// Every target this container reaches a scheduler through, by name, with the uniqueness rule that keeps
/// a key readable: one target per name, whatever kind of target it is.
/// </summary>
/// <remarks>
/// <para>
/// One per container, registered by <c>AddQuartzSharedServices</c>, and written at run time as well as
/// at start-up: an HTTP target is added when its scheduler is built, a store when the dashboard's
/// discovery starts, an agent when it connects, and a cluster when the fleet monitor finds one. The
/// listing, the history lookup and the event lookup read it; <see cref="Changed" /> is what lets the
/// fleet monitor re-evaluate when the set moves.
/// </para>
/// <para>
/// <c>/</c> is refused in a name because it separates a target from a scheduler's name in a key, and
/// <c>+</c> because it joins the members of a cluster's. Names are compared ignoring case, the way every
/// scheduler lookup in Quartz is.
/// </para>
/// </remarks>
internal sealed class SchedulerTargets
{
    private readonly Dictionary<string, SchedulerTarget> targets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock syncRoot = new();

    /// <summary>
    /// Raised after a target is added or removed, on the thread that changed the set and outside its
    /// lock.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Records a target, refusing a name already held by a target of any kind.
    /// </summary>
    /// <exception cref="SchedulerConfigException">
    /// The name is taken, or contains <c>/</c> or <c>+</c>.
    /// </exception>
    public void Add(SchedulerTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        // A cluster's name is its members joined with '+', which is the one place the character is
        // allowed: it is what the rule reserves it for.
        if (target.Origin == SchedulerOrigin.Cluster)
        {
            if (string.IsNullOrWhiteSpace(target.Name) || target.Name.Contains(SchedulerRef.Separator, StringComparison.Ordinal))
            {
                Throw.SchedulerConfigException(Problem(target.Name)!);
            }
        }
        else
        {
            ValidateName(target.Name);
        }

        lock (syncRoot)
        {
            if (targets.TryGetValue(target.Name, out SchedulerTarget? existing))
            {
                Throw.SchedulerConfigException(
                    $"'{target.Name}' is already a target: {Describe(existing)}, and {Describe(target)} cannot take the same name. "
                    + "A target's name is half of every key its schedulers are shown under, so two targets under one name "
                    + "would give two schedulers one spelling; give the second one a name of its own.");
            }

            targets[target.Name] = target;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Forgets a target, as an agent is forgotten or a cluster dissolves.
    /// </summary>
    /// <returns><see langword="true" /> when a target of that name was held.</returns>
    public bool Remove(string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);

        bool removed;
        lock (syncRoot)
        {
            removed = targets.Remove(target);
        }

        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    /// <summary>
    /// The target of that name, or <see langword="null" /> when none is held.
    /// </summary>
    public SchedulerTarget? Find(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return null;
        }

        lock (syncRoot)
        {
            return targets.TryGetValue(target, out SchedulerTarget? found) ? found : null;
        }
    }

    /// <summary>
    /// Every target held right now, in name order.
    /// </summary>
    public List<SchedulerTarget> Snapshot()
    {
        lock (syncRoot)
        {
            List<SchedulerTarget> snapshot = [.. targets.Values];
            snapshot.Sort(static (left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
            return snapshot;
        }
    }

    /// <summary>
    /// What makes a string a target name: not blank, no <c>/</c>, no <c>+</c>.
    /// </summary>
    /// <exception cref="SchedulerConfigException">It is not one.</exception>
    public static void ValidateName(string? target)
    {
        if (Problem(target) is { } problem)
        {
            Throw.SchedulerConfigException(problem);
        }
    }

    /// <summary>
    /// Why <paramref name="target" /> cannot be a target name, or <see langword="null" /> when it can.
    /// </summary>
    public static string? Problem(string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return "A target name is required and cannot be blank.";
        }

        if (target.Contains(SchedulerRef.Separator, StringComparison.Ordinal))
        {
            return $"'{target}' cannot be a target name: '{SchedulerRef.Separator}' is what separates a target from the "
                + "scheduler's name in a key, so a target containing one would be unreadable wherever a scheduler is shown.";
        }

        if (target.Contains(ClusterTargetSeparator, StringComparison.Ordinal))
        {
            return $"'{target}' cannot be a target name: '{ClusterTargetSeparator}' is what joins the members of a cluster's "
                + "target, so a target containing one would read as a cluster.";
        }

        return null;
    }

    /// <summary>
    /// What joins the member targets of a cluster into its own: <c>a+b+c</c>.
    /// </summary>
    public const char ClusterTargetSeparator = '+';

    /// <summary>
    /// The name of the cluster made of <paramref name="members" />: the members sorted ordinal-ignore-case
    /// and joined with <see cref="ClusterTargetSeparator" />.
    /// </summary>
    public static string ClusterTargetName(IEnumerable<string> members)
    {
        List<string> sorted = [.. members];
        sorted.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join(ClusterTargetSeparator, sorted);
    }

    private static string Describe(SchedulerTarget target)
    {
        return target.Origin switch
        {
            SchedulerOrigin.Window => $"a store attached as '{target.Name}'",
            SchedulerOrigin.Remote => $"an HTTP target registered as '{target.Name}'",
            SchedulerOrigin.Agent => $"an agent registered as '{target.Name}'",
            SchedulerOrigin.Cluster => $"a cluster named '{target.Name}'",
            _ => $"a target named '{target.Name}'",
        };
    }
}
