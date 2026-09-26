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

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace Quartz;

/// <summary>
/// An immutable set of concurrency limits for execution groups. Execution groups are optional
/// tags on triggers that characterize the resource requirements of the associated job
/// (e.g. "batch-jobs", "high-cpu", "large-ram").
/// </summary>
/// <remarks>
/// <para>Every limit says how many concurrent executions its group may have:
/// <list type="bullet">
///   <item>A positive value limits how many threads the group may consume concurrently.</item>
///   <item>A value of <c>0</c> forbids the group from running.</item>
///   <item><see langword="null"/> means unlimited (no restriction).</item>
/// </list>
/// </para>
/// <para>Every limit also says what it is counted against — see <see cref="ExecutionLimitScope"/>.
/// A <see cref="ExecutionLimitScope.Node"/> limit, the default, is what this node may run; a
/// <see cref="ExecutionLimitScope.Cluster"/> limit is what every node sharing the job store may run
/// between them. The two coexist: a heterogeneous cluster caps heavy work per node, a multi-tenant
/// one caps a tenant across the cluster, and one deployment can want both.</para>
/// <para>Use <see cref="OtherGroups"/> as a catch-all default for groups not explicitly listed, and
/// <see cref="ExecutionLimitsBuilder.ForGroupsWithPrefix"/> for a family of groups — one per tenant, say —
/// that each get an allowance of their own without being listed one by one. A group's own limit wins,
/// then the longest matching prefix, then the catch-all.</para>
/// <para>Build one with <see cref="ExecutionLimitsBuilder"/>, either directly or through
/// <see cref="IQuartzBuilder.UseExecutionLimits(Action{ExecutionLimitsBuilder})"/>; hand it to
/// <see cref="IScheduler.SetExecutionLimits"/> to apply it.</para>
/// </remarks>
public sealed class ExecutionLimits
{
    /// <summary>
    /// The group name that carries the default limit for execution groups not explicitly configured.
    /// </summary>
    public const string OtherGroups = "*";

    /// <summary>
    /// The key used internally to represent triggers that have no execution group
    /// (<see cref="ITrigger.ExecutionGroup"/> is <see langword="null"/>). It is never a group name a
    /// caller can use: <see cref="ExecutionGroupLimit.Group"/> reports the default group as
    /// <see cref="ExecutionGroupScope.Default"/>, and
    /// <see cref="ExecutionLimitsBuilder.ForDefaultGroup"/> configures it.
    /// </summary>
    internal const string DefaultGroupKey = "";

    /// <summary>
    /// The alias configuration uses for the default group, because a property key and a JSON object
    /// key cannot be empty.
    /// </summary>
    internal const string DefaultGroupAlias = "_";

    /// <summary>
    /// The second alias configuration accepts for the default group, spelling out what an absent
    /// execution group is.
    /// </summary>
    internal const string DefaultGroupNullAlias = "null";

    private readonly Dictionary<string, ExecutionGroupAllowance> limits;

    // Longest prefix first, so the first match is the one that governs. Configuration, never lowered:
    // what a prefix hands out is materialized under the group's own key in a working copy.
    private readonly ExecutionGroupPrefixAllowance[] prefixes;
    private ExecutionGroupLimit[]? groups;

    internal ExecutionLimits(Dictionary<string, ExecutionGroupAllowance> limits, bool usesTriggerGroupWhenUnset = false)
        : this(limits, [], usesTriggerGroupWhenUnset)
    {
    }

    internal ExecutionLimits(
        Dictionary<string, ExecutionGroupAllowance> limits,
        ExecutionGroupPrefixAllowance[] prefixes,
        bool usesTriggerGroupWhenUnset)
    {
        this.limits = limits;
        this.prefixes = prefixes;
        UsesTriggerGroupWhenUnset = usesTriggerGroupWhenUnset;

        foreach (KeyValuePair<string, ExecutionGroupAllowance> pair in limits)
        {
            // Null is unlimited and zero is forbidden; neither needs a count to enforce, so neither is
            // a reason to make a store go and read one.
            if (NeedsClusterCount(pair.Value))
            {
                HasClusterScopedLimits = true;
                break;
            }
        }

        foreach (ExecutionGroupPrefixAllowance prefix in prefixes)
        {
            if (!HasClusterScopedLimits && NeedsClusterCount(prefix.Allowance))
            {
                HasClusterScopedLimits = true;
            }
        }
    }

    private static bool NeedsClusterCount(ExecutionGroupAllowance allowance)
    {
        return allowance.Scope == ExecutionLimitScope.Cluster && allowance.MaxConcurrent > 0;
    }

    /// <summary>
    /// <see langword="true"/> when nothing is limited, in which case every trigger is free to fire.
    /// </summary>
    public bool IsEmpty => limits.Count == 0 && prefixes.Length == 0;

    /// <summary>
    /// Whether any group is limited across the cluster rather than on this node alone.
    /// </summary>
    /// <remarks>
    /// A job store reads this to decide whether a cluster-wide in-flight count is worth fetching:
    /// with no cluster-scoped limit there is nothing such a count could constrain, so the round trip
    /// is skipped. A group that is explicitly unlimited, or forbidden outright with <c>0</c>, does not
    /// make this <see langword="true"/> whatever scope it was declared in — neither answer depends on
    /// what is in flight.
    /// </remarks>
    public bool HasClusterScopedLimits { get; }

    /// <summary>
    /// Whether a trigger that carries no execution group of its own is limited as though it belonged to
    /// a group named after its own <see cref="Key{T}.Group"/>. Off unless
    /// <see cref="ExecutionLimitsBuilder.UseTriggerGroupWhenUnset"/> asked for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The derivation is an evaluation-time rule and nothing else: what a trigger carries and what the
    /// store persists in EXECUTION_GROUP are unchanged, and <see cref="ITrigger.ExecutionGroup"/> still
    /// reads <see langword="null"/>. It exists for schedules that already partition work by trigger
    /// group — a tenant per group, a subsystem per group — where restating every group as an execution
    /// group would be a second copy of the same fact.
    /// </para>
    /// <para>
    /// An explicit execution group always wins. Turning this on also moves the ungrouped triggers out of
    /// <see cref="ExecutionLimitsBuilder.ForDefaultGroup"/>'s bucket and under
    /// <see cref="OtherGroups"/>'s catch-all, because they are no longer ungrouped as far as the limits
    /// are concerned.
    /// </para>
    /// </remarks>
    public bool UsesTriggerGroupWhenUnset { get; }

    /// <summary>
    /// Every configured group and its limit.
    /// </summary>
    public IReadOnlyList<ExecutionGroupLimit> Groups => groups ??= Materialize();

    /// <summary>
    /// Reads the limit configured for one group.
    /// </summary>
    /// <remarks>
    /// Only the number; whether it is counted per node or per cluster is on the entries
    /// <see cref="Groups"/> hands out.
    /// </remarks>
    /// <param name="group">The bucket to read: <see cref="ExecutionGroupScope.Default"/>,
    /// <see cref="ExecutionGroupScope.OtherGroups"/>, a named group via
    /// <see cref="ExecutionGroupScope.Named"/>, or a prefix via
    /// <see cref="ExecutionGroupScope.GroupsWithPrefix"/>.</param>
    /// <param name="maxConcurrent">The limit: a positive count, <c>0</c> when the group is forbidden,
    /// or <see langword="null"/> when it is explicitly unlimited.</param>
    /// <returns><see langword="true"/> when the group has a limit of its own. <see langword="false"/>
    /// does not mean unlimited — a prefix or <see cref="ExecutionGroupScope.OtherGroups"/> may still
    /// apply to a named group.</returns>
    public bool TryGetLimit(ExecutionGroupScope group, out int? maxConcurrent)
    {
        if (group.IsPrefix)
        {
            foreach (ExecutionGroupPrefixAllowance prefix in prefixes)
            {
                if (string.Equals(prefix.Prefix, group.Prefix, StringComparison.Ordinal))
                {
                    maxConcurrent = prefix.Allowance.MaxConcurrent;
                    return true;
                }
            }
        }
        else if (limits.TryGetValue(group.StorageKey, out ExecutionGroupAllowance allowance))
        {
            maxConcurrent = allowance.MaxConcurrent;
            return true;
        }

        maxConcurrent = null;
        return false;
    }

    private ExecutionGroupLimit[] Materialize()
    {
        ExecutionGroupLimit[] result = new ExecutionGroupLimit[limits.Count + prefixes.Length];
        int i = 0;
        foreach (KeyValuePair<string, ExecutionGroupAllowance> pair in limits)
        {
            result[i++] = new ExecutionGroupLimit(
                ExecutionGroupScope.FromStorageKey(pair.Key),
                pair.Value.MaxConcurrent,
                pair.Value.Scope);
        }

        foreach (ExecutionGroupPrefixAllowance prefix in prefixes)
        {
            result[i++] = new ExecutionGroupLimit(
                ExecutionGroupScope.FromPrefix(prefix.Prefix),
                prefix.Allowance.MaxConcurrent,
                prefix.Allowance.Scope);
        }

        return result;
    }

    /// <summary>
    /// Creates a ledger of the slots these limits allow, for one trigger acquisition to count down as it
    /// takes triggers. The snapshot itself is unaffected, so a retried acquisition starts from the limits
    /// again by creating another ledger.
    /// </summary>
    /// <param name="clusterInFlight">What the whole cluster already holds in flight, one entry per
    /// distinct (execution group, trigger group) pair, or <see langword="null"/> when the caller has no
    /// such count. Only <see cref="ExecutionLimitScope.Cluster"/> limits are lowered by it: a
    /// <see cref="ExecutionLimitScope.Node"/> limit has already had this node's running work subtracted
    /// by the scheduler thread, and subtracting a count that includes the same firings again would halve
    /// the limit on a busy node.</param>
    public ExecutionSlots CreateSlots(IReadOnlyCollection<ExecutionGroupInFlight>? clusterInFlight = null)
    {
        Dictionary<string, ExecutionGroupAllowance> working = ToWorkingCopy();

        if (clusterInFlight is not null)
        {
            foreach (ExecutionGroupInFlight inFlight in clusterInFlight)
            {
                SubtractInFlight(
                    working,
                    prefixes,
                    ResolveGroupKey(inFlight.ExecutionGroup, inFlight.TriggerGroup, UsesTriggerGroupWhenUnset),
                    inFlight.Count,
                    ExecutionLimitScope.Cluster);
            }
        }

        return new ExecutionSlots(working, prefixes, UsesTriggerGroupWhenUnset);
    }

    /// <summary>
    /// These limits with what this node is running taken off the <see cref="ExecutionLimitScope.Node" />
    /// ones, which is what the scheduler thread hands the store for one acquisition.
    /// </summary>
    /// <param name="running">What this node has in flight, keyed by <see cref="ResolveGroupKey" />.</param>
    internal ExecutionLimits LowerByNodeInFlight(IEnumerable<KeyValuePair<string, int>> running)
    {
        Dictionary<string, ExecutionGroupAllowance> available = ToWorkingCopy();

        foreach (KeyValuePair<string, int> inFlight in running)
        {
            SubtractInFlight(available, prefixes, inFlight.Key, inFlight.Value, ExecutionLimitScope.Node);
        }

        // The prefixes and the derivation flag travel with the remaining-capacity map, because the store
        // that reads it has to resolve a candidate's group the same way this ledger did.
        return new ExecutionLimits(available, prefixes, UsesTriggerGroupWhenUnset);
    }

    /// <summary>
    /// Creates a mutable working copy of the configured limits.
    /// </summary>
    internal Dictionary<string, ExecutionGroupAllowance> ToWorkingCopy()
    {
        return new Dictionary<string, ExecutionGroupAllowance>(limits, StringComparer.Ordinal);
    }

    /// <summary>
    /// Lowers one group's remaining allowance by what is already in flight against it, but only when
    /// that allowance is counted in <paramref name="scope"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both places that subtract in-flight work go through this, and they subtract in different scopes:
    /// the scheduler thread takes off what is running on this node
    /// (<see cref="ExecutionLimitScope.Node"/>), and a store takes off what the cluster holds
    /// (<see cref="ExecutionLimitScope.Cluster"/>). Each skips the other's limits, which is what keeps a
    /// cluster-scoped group from being charged twice for the same firing — this node's reservations are
    /// already rows in the store's ledger.
    /// </para>
    /// <para>
    /// Repeated calls for one key compose, because the second one reads the value the first wrote.
    /// </para>
    /// </remarks>
    internal static void SubtractInFlight(
        Dictionary<string, ExecutionGroupAllowance> available,
        ExecutionGroupPrefixAllowance[] prefixes,
        string groupKey,
        int inFlight,
        ExecutionLimitScope scope)
    {
        if (inFlight <= 0)
        {
            return;
        }

        if (available.TryGetValue(groupKey, out ExecutionGroupAllowance allowance))
        {
            if (allowance.Scope == scope && allowance.MaxConcurrent is int limit)
            {
                available[groupKey] = allowance with { MaxConcurrent = Math.Max(limit - inFlight, 0) };
            }

            // A null limit is explicitly unlimited, and a limit in the other scope is not this
            // caller's to lower.
            return;
        }

        // A prefix, then OtherGroups ("*"), for named groups only, never for the ungrouped bucket — the
        // same rule ExecutionSlots.TryTake applies. Materializing the entry here gives each group its own
        // allowance rather than one shared between the family.
        if (TryInherit(available, prefixes, groupKey, out ExecutionGroupAllowance inherited)
            && inherited.Scope == scope
            && inherited.MaxConcurrent is int inheritedLimit)
        {
            available[groupKey] = inherited with { MaxConcurrent = Math.Max(inheritedLimit - inFlight, 0) };
        }
    }

    /// <summary>
    /// The allowance a named group without a limit of its own falls back to: the longest prefix it starts
    /// with, else the catch-all. The ungrouped bucket inherits nothing.
    /// </summary>
    /// <remarks>
    /// Exactly one allowance governs a group. A matching prefix counted in the other scope still governs,
    /// and the catch-all is not consulted behind it, or the same group would be limited by two numbers.
    /// </remarks>
    internal static bool TryInherit(
        Dictionary<string, ExecutionGroupAllowance> available,
        ExecutionGroupPrefixAllowance[] prefixes,
        string groupKey,
        out ExecutionGroupAllowance allowance)
    {
        if (string.Equals(groupKey, DefaultGroupKey, StringComparison.Ordinal))
        {
            allowance = default;
            return false;
        }

        foreach (ExecutionGroupPrefixAllowance prefix in prefixes)
        {
            if (groupKey.StartsWith(prefix.Prefix, StringComparison.Ordinal))
            {
                allowance = prefix.Allowance;
                return true;
            }
        }

        return available.TryGetValue(OtherGroups, out allowance);
    }

    /// <summary>
    /// Normalizes a possibly-null execution group name to the internal key format.
    /// </summary>
    internal static string NormalizeGroupKey(string? executionGroup)
    {
        return executionGroup ?? DefaultGroupKey;
    }

    /// <summary>
    /// The key a trigger's firing counts against, applying the
    /// <see cref="UsesTriggerGroupWhenUnset" /> derivation. Every place that evaluates a limit — both
    /// stores' acquisition filters and the scheduler thread's in-flight ledger — goes through this, or
    /// the ledger and the filter would be counting different things.
    /// </summary>
    internal static string ResolveGroupKey(string? executionGroup, string triggerGroup, bool useTriggerGroupWhenUnset)
    {
        if (executionGroup is not null || !useTriggerGroupWhenUnset)
        {
            return NormalizeGroupKey(executionGroup);
        }

        // A trigger group is free to be called "*" or "_"; an execution group is not. Deriving one from
        // the other would otherwise quietly drop such a trigger into the catch-all or the default bucket,
        // which is not what its name says. It stays ungrouped instead.
        return IsReservedGroupName(triggerGroup) ? DefaultGroupKey : triggerGroup;
    }

    /// <summary>
    /// Tells whether a configuration key names the default (ungrouped) bucket.
    /// </summary>
    internal static bool IsDefaultGroupAlias(string key)
    {
        return key.Length == 0
               || key == DefaultGroupAlias
               || key.Equals(DefaultGroupNullAlias, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tells whether a trimmed group name is one of the names limits configuration reserves for
    /// itself, and therefore cannot be a trigger's execution group.
    /// </summary>
    internal static bool IsReservedGroupName(string trimmedGroup)
    {
        return trimmedGroup == OtherGroups || IsDefaultGroupAlias(trimmedGroup);
    }

    /// <summary>
    /// Tells whether a trimmed configuration key names a family of groups, the way <c>tenant:*</c> does,
    /// and reads the prefix it names. A lone <c>*</c> is the catch-all, not a prefix.
    /// </summary>
    internal static bool TryReadPrefixKey(string trimmedKey, [NotNullWhen(true)] out string? prefix)
    {
        if (trimmedKey.Length > 1 && trimmedKey[^1] == '*')
        {
            prefix = trimmedKey[..^1];
            return true;
        }

        prefix = null;
        return false;
    }

    /// <summary>
    /// Checks a prefix a caller has named, and returns it as the limits hold it.
    /// </summary>
    /// <remarks>
    /// Leading white space is dropped, because a group name never starts with any. Trailing white space is
    /// kept, because <c>"tenant "</c> is a prefix of <c>"tenant acme"</c>. A trailing <c>*</c> is refused:
    /// the prefix is what comes before the <c>*</c> of a configuration key, and one written with the star
    /// would match only the groups that literally start with it.
    /// </remarks>
    internal static string RequirePrefix(string prefix, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(prefix, parameterName);
        string trimmed = prefix.TrimStart();

        if (trimmed.Length == 0)
        {
            Throw.ArgumentException("A prefix names the start of the groups it limits; use ForOtherGroups() for every group.", parameterName);
        }

        if (trimmed[^1] == '*')
        {
            Throw.ArgumentException(
                $"The prefix is what comes before the '*' of a configuration key: '{trimmed[..^1]}', not '{trimmed}'.",
                parameterName);
        }

        return trimmed;
    }

    /// <summary>
    /// Whether <see cref="RequirePrefix" /> would accept the prefix, for a caller that reports rather
    /// than throws.
    /// </summary>
    internal static bool IsValidPrefix(string prefix)
    {
        string trimmed = prefix.TrimStart();
        return trimmed.Length > 0 && trimmed[^1] != '*';
    }
}

/// <summary>
/// One prefix limit as the limits hold it: the prefix, and what each group starting with it is allowed.
/// </summary>
/// <param name="Prefix">The start of the group names the allowance applies to.</param>
/// <param name="Allowance">What each such group is allowed, counted per group.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ExecutionGroupPrefixAllowance(string Prefix, ExecutionGroupAllowance Allowance);

/// <summary>
/// One entry in an <see cref="ExecutionLimits"/> snapshot.
/// </summary>
/// <param name="Group">Which bucket the limit applies to: the default (ungrouped) bucket, the
/// catch-all for other groups, one named group, or each of the groups that start with a prefix.</param>
/// <param name="MaxConcurrent">The limit: a positive count, <c>0</c> when the group is forbidden,
/// or <see langword="null"/> when it is explicitly unlimited.</param>
/// <param name="Scope">What the limit is counted against: this node alone, or the whole cluster.</param>
public readonly record struct ExecutionGroupLimit(
    ExecutionGroupScope Group,
    int? MaxConcurrent,
    ExecutionLimitScope Scope = ExecutionLimitScope.Node);

/// <summary>
/// What an execution limit is counted against.
/// </summary>
/// <remarks>
/// <para>
/// The two are not alternatives to each other and one set of limits may use both. A node-scoped limit
/// describes what this machine can stand — the reason a batch node and an API node in the same cluster
/// declare different numbers. A cluster-scoped limit describes a quota — the reason a tenant may run
/// eight jobs at a time no matter how many nodes are up.
/// </para>
/// </remarks>
public enum ExecutionLimitScope
{
    /// <summary>
    /// The limit is what this node may run concurrently. Every node enforces its own copy, so an
    /// N-node cluster can be running N times the number. This is the default, and it is what execution
    /// limits have always meant.
    /// </summary>
    Node = 0,

    /// <summary>
    /// The limit is what every node sharing the job store may run between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The count comes from the store, not from any node's memory: for the ADO.NET store it is the
    /// <c>QRTZ_FIRED_TRIGGERS</c> rows, which already hold both reservations and running executions and
    /// are already cleaned up when a node dies. That makes the ceiling fail closed — a node that cannot
    /// reach the store cannot acquire anything either — but also approximate: the default acquisition
    /// path takes no cluster lock, so two nodes can read the same remaining count and each take from it.
    /// The overshoot is bounded by the nodes acquiring at that instant, and setting
    /// <c>AcquireTriggersWithinLock</c> removes it at the cost of serializing acquisition.
    /// </para>
    /// <para>
    /// A store whose <see cref="Extensibility.IJobStore.Clustered"/> is <see langword="false"/> has one
    /// node, so a cluster-scoped limit and a node-scoped one are the same number there.
    /// </para>
    /// </remarks>
    Cluster = 1,
}

/// <summary>
/// How much work one execution group already has in flight across the cluster, as a store reports it
/// to <see cref="ExecutionLimits.CreateSlots"/>.
/// </summary>
/// <remarks>
/// Both group names are carried because a limit's key is derived from the pair rather than from either
/// alone: <see cref="ExecutionLimits.UsesTriggerGroupWhenUnset"/> lets the trigger group stand in when
/// the trigger carries no execution group. A store therefore reports what it has — for the ADO.NET
/// store, one row per distinct pair in <c>QRTZ_FIRED_TRIGGERS</c> — and the limits resolve the key,
/// so that the count and the filter can never key work differently.
/// </remarks>
/// <param name="ExecutionGroup">The execution group the in-flight work carries, or
/// <see langword="null"/> when it carries none.</param>
/// <param name="TriggerGroup">The trigger group the in-flight work belongs to.</param>
/// <param name="Count">How many reservations and executions the pair has in flight.</param>
public readonly record struct ExecutionGroupInFlight(string? ExecutionGroup, string TriggerGroup, int Count);

/// <summary>
/// What one execution group is allowed, as the limits hold it internally: the count and the scope it
/// is counted in.
/// </summary>
/// <remarks>
/// The public read-side shape is <see cref="ExecutionGroupLimit"/>, which pairs the same two values
/// with the group they belong to; this one is the dictionary value, so the group is the key.
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ExecutionGroupAllowance(int? MaxConcurrent, ExecutionLimitScope Scope);

/// <summary>
/// Which bucket an execution limit applies to: the default (ungrouped) bucket, the catch-all for
/// groups not explicitly configured, one named group, or the family of groups that start with a prefix.
/// </summary>
/// <remarks>
/// <para>
/// This is the read-side shape of what <see cref="ExecutionLimitsBuilder"/> writes:
/// <see cref="ExecutionLimitsBuilder.ForDefaultGroup"/> configures <see cref="Default"/>,
/// <see cref="ExecutionLimitsBuilder.ForOtherGroups"/> configures <see cref="OtherGroups"/>,
/// <see cref="ExecutionLimitsBuilder.ForGroup"/> / <see cref="ExecutionLimitsBuilder.Unlimited"/>
/// configure <see cref="Named"/> scopes, and <see cref="ExecutionLimitsBuilder.ForGroupsWithPrefix"/>
/// configures <see cref="GroupsWithPrefix"/> ones. Configuration keys keep their own spellings (<c>_</c>
/// or <c>null</c> for the default bucket, <c>*</c> for the catch-all, <c>tenant:*</c> for a prefix);
/// this type exists so code reading limits back never has to know them.
/// </para>
/// <para>
/// Modeled on <see cref="PreferredNode"/>, the other place a closed set of cases refuses to be a
/// nullable string with a sentinel.
/// </para>
/// </remarks>
public readonly record struct ExecutionGroupScope
{
    // The key the limits dictionary holds: "" or null for the default bucket, "*" for the
    // catch-all, otherwise the group name — or, when isPrefix is set, the prefix. Keeping storage's own
    // shape makes reading a limit a plain dictionary hit.
    private readonly string? name;
    private readonly bool isPrefix;

    private ExecutionGroupScope(string? name, bool isPrefix = false)
    {
        this.name = name;
        this.isPrefix = isPrefix;
    }

    /// <summary>
    /// The bucket for triggers that have no execution group
    /// (<see cref="ITrigger.ExecutionGroup"/> is <see langword="null"/>). The default value of
    /// this type.
    /// </summary>
    public static ExecutionGroupScope Default => default;

    /// <summary>
    /// The catch-all applied to any named group not explicitly configured. It never applies to
    /// ungrouped triggers — those always read <see cref="Default"/>.
    /// </summary>
    public static ExecutionGroupScope OtherGroups => new(ExecutionLimits.OtherGroups);

    /// <summary>
    /// The scope of one named execution group.
    /// </summary>
    /// <param name="name">The execution group name.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is blank or one of the names
    /// reserved by limits configuration. Use <see cref="Default"/> for ungrouped triggers and
    /// <see cref="OtherGroups"/> for the catch-all.</exception>
    public static ExecutionGroupScope Named(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string trimmed = name.Trim();

        if (trimmed.Length == 0)
        {
            Throw.ArgumentException("An execution group scope needs a group name; use ExecutionGroupScope.Default for triggers that have none.", nameof(name));
        }

        if (ExecutionLimits.IsReservedGroupName(trimmed))
        {
            Throw.ArgumentException($"Group name '{trimmed}' is reserved. Use ExecutionGroupScope.Default for the default bucket or ExecutionGroupScope.OtherGroups for the catch-all.", nameof(name));
        }

        return new ExecutionGroupScope(trimmed);
    }

    /// <summary>
    /// The scope of every execution group that starts with <paramref name="prefix"/>, each of which is
    /// allowed the limit on its own.
    /// </summary>
    /// <param name="prefix">The start of the group names, such as <c>tenant:</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="prefix"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="prefix"/> is blank, or ends with the
    /// <c>*</c> only a configuration key spells.</exception>
    public static ExecutionGroupScope GroupsWithPrefix(string prefix)
    {
        return new ExecutionGroupScope(ExecutionLimits.RequirePrefix(prefix, nameof(prefix)), isPrefix: true);
    }

    /// <summary>
    /// Whether this is the bucket for triggers that have no execution group.
    /// </summary>
    public bool IsDefault => !isPrefix && string.IsNullOrEmpty(name);

    /// <summary>
    /// Whether this is the catch-all for named groups not explicitly configured.
    /// </summary>
    public bool IsOtherGroups => !isPrefix && name == ExecutionLimits.OtherGroups;

    /// <summary>
    /// Whether this is the family of groups that start with <see cref="Prefix"/>.
    /// </summary>
    public bool IsPrefix => isPrefix;

    /// <summary>
    /// The group name of a <see cref="Named"/> scope; <see langword="null"/> for the others.
    /// </summary>
    public string? Name => IsDefault || IsOtherGroups || isPrefix ? null : name;

    /// <summary>
    /// The prefix of a <see cref="GroupsWithPrefix"/> scope; <see langword="null"/> for the others.
    /// </summary>
    public string? Prefix => isPrefix ? name : null;

    /// <summary>
    /// The key the limits dictionary holds for this scope.
    /// </summary>
    internal string StorageKey => name ?? ExecutionLimits.DefaultGroupKey;

    /// <summary>
    /// Rebuilds the scope from a limits-dictionary key.
    /// </summary>
    internal static ExecutionGroupScope FromStorageKey(string key)
    {
        return key == ExecutionLimits.DefaultGroupKey ? default : new ExecutionGroupScope(key);
    }

    /// <summary>
    /// Rebuilds a prefix scope from a prefix the limits hold, which was checked when it was configured.
    /// </summary>
    internal static ExecutionGroupScope FromPrefix(string prefix)
    {
        return new ExecutionGroupScope(prefix, isPrefix: true);
    }

    /// <summary>
    /// The spelling configuration and the HTTP API use for this scope: the group name, <c>*</c> for
    /// the catch-all, the prefix followed by <c>*</c> for a prefix, and <c>_</c> for the default bucket
    /// (a property or JSON key cannot be empty).
    /// </summary>
    internal string ToConfigurationKey()
    {
        if (isPrefix)
        {
            return name + ExecutionLimits.OtherGroups;
        }

        return IsDefault ? ExecutionLimits.DefaultGroupAlias : name!;
    }

    /// <inheritdoc />
    public override string ToString()
    {
        if (IsDefault)
        {
            return "default";
        }

        if (IsOtherGroups)
        {
            return "other groups";
        }

        return isPrefix ? name + ExecutionLimits.OtherGroups : name!;
    }
}
