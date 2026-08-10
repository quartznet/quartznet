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

namespace Quartz;

/// <summary>
/// An immutable set of per-node thread limits for execution groups. Execution groups are optional
/// tags on triggers that characterize the resource requirements of the associated job
/// (e.g. "batch-jobs", "high-cpu", "large-ram").
/// </summary>
/// <remarks>
/// <para>Each scheduler node can declare its own limits independently:
/// <list type="bullet">
///   <item>A positive value limits how many threads the group may consume concurrently.</item>
///   <item>A value of <c>0</c> forbids the group from running on this node.</item>
///   <item><see langword="null"/> means unlimited (no restriction).</item>
/// </list>
/// </para>
/// <para>Use <see cref="OtherGroups"/> as a catch-all default for groups not explicitly listed.</para>
/// <para>Build one with <see cref="ExecutionLimitsBuilder"/>, either directly or through
/// <see cref="IQuartzBuilder.UseExecutionLimits"/>; hand it to
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
    /// caller can use: <see cref="ExecutionGroupLimit.Scope"/> reports the default group as
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

    private readonly Dictionary<string, int?> limits;
    private ExecutionGroupLimit[]? groups;

    internal ExecutionLimits(Dictionary<string, int?> limits)
    {
        this.limits = limits;
    }

    /// <summary>
    /// <see langword="true"/> when nothing is limited, in which case every trigger is free to fire.
    /// </summary>
    public bool IsEmpty => limits.Count == 0;

    /// <summary>
    /// Every configured group and its limit.
    /// </summary>
    public IReadOnlyList<ExecutionGroupLimit> Groups => groups ??= Materialize();

    /// <summary>
    /// Reads the limit configured for one scope.
    /// </summary>
    /// <param name="scope">The scope to read: <see cref="ExecutionGroupScope.Default"/>,
    /// <see cref="ExecutionGroupScope.OtherGroups"/>, or a named group via
    /// <see cref="ExecutionGroupScope.Named"/>.</param>
    /// <param name="maxConcurrent">The limit: a positive count, <c>0</c> when the scope is forbidden,
    /// or <see langword="null"/> when it is explicitly unlimited.</param>
    /// <returns><see langword="true"/> when the scope has a limit of its own. <see langword="false"/>
    /// does not mean unlimited — <see cref="ExecutionGroupScope.OtherGroups"/> may still apply to a
    /// named group.</returns>
    public bool TryGetLimit(ExecutionGroupScope scope, out int? maxConcurrent)
    {
        return limits.TryGetValue(scope.StorageKey, out maxConcurrent);
    }

    private ExecutionGroupLimit[] Materialize()
    {
        ExecutionGroupLimit[] result = new ExecutionGroupLimit[limits.Count];
        int i = 0;
        foreach (KeyValuePair<string, int?> pair in limits)
        {
            result[i++] = new ExecutionGroupLimit(ExecutionGroupScope.FromStorageKey(pair.Key), pair.Value);
        }
        return result;
    }

    /// <summary>
    /// Creates a ledger of the slots these limits allow, for one trigger acquisition to count down as it
    /// takes triggers. The snapshot itself is unaffected, so a retried acquisition starts from the limits
    /// again by creating another ledger.
    /// </summary>
    public ExecutionSlots CreateSlots()
    {
        return new ExecutionSlots(ToWorkingCopy());
    }

    /// <summary>
    /// Creates a mutable working copy of the configured limits.
    /// </summary>
    internal Dictionary<string, int?> ToWorkingCopy()
    {
        return new Dictionary<string, int?>(limits, StringComparer.Ordinal);
    }

    /// <summary>
    /// Normalizes a possibly-null execution group name to the internal key format.
    /// </summary>
    internal static string NormalizeGroupKey(string? executionGroup)
    {
        return executionGroup ?? DefaultGroupKey;
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
}

/// <summary>
/// One entry in an <see cref="ExecutionLimits"/> snapshot.
/// </summary>
/// <param name="Scope">Which bucket the limit applies to: the default (ungrouped) bucket, the
/// catch-all for other groups, or one named group.</param>
/// <param name="MaxConcurrent">The limit: a positive count, <c>0</c> when the scope is forbidden on
/// this node, or <see langword="null"/> when it is explicitly unlimited.</param>
public readonly record struct ExecutionGroupLimit(ExecutionGroupScope Scope, int? MaxConcurrent);

/// <summary>
/// Which bucket an execution limit applies to: the default (ungrouped) bucket, the catch-all for
/// groups not explicitly configured, or one named group.
/// </summary>
/// <remarks>
/// <para>
/// This is the read-side shape of what <see cref="ExecutionLimitsBuilder"/> writes:
/// <see cref="ExecutionLimitsBuilder.ForDefaultGroup"/> configures <see cref="Default"/>,
/// <see cref="ExecutionLimitsBuilder.ForOtherGroups"/> configures <see cref="OtherGroups"/>, and
/// <see cref="ExecutionLimitsBuilder.ForGroup"/> / <see cref="ExecutionLimitsBuilder.Unlimited"/>
/// configure <see cref="Named"/> scopes. Configuration keys keep their own spellings (<c>_</c> or
/// <c>null</c> for the default bucket, <c>*</c> for the catch-all); this type exists so code reading
/// limits back never has to know them.
/// </para>
/// <para>
/// Modeled on <see cref="PreferredNode"/>, the other place a closed set of cases refuses to be a
/// nullable string with a sentinel.
/// </para>
/// </remarks>
public readonly record struct ExecutionGroupScope
{
    // The key the limits dictionary holds: "" or null for the default bucket, "*" for the
    // catch-all, otherwise the group name. Keeping storage's own shape makes reading a limit a
    // plain dictionary hit.
    private readonly string? name;

    private ExecutionGroupScope(string? name)
    {
        this.name = name;
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
    /// Whether this is the bucket for triggers that have no execution group.
    /// </summary>
    public bool IsDefault => string.IsNullOrEmpty(name);

    /// <summary>
    /// Whether this is the catch-all for named groups not explicitly configured.
    /// </summary>
    public bool IsOtherGroups => name == ExecutionLimits.OtherGroups;

    /// <summary>
    /// The group name of a <see cref="Named"/> scope; <see langword="null"/> for
    /// <see cref="Default"/> and <see cref="OtherGroups"/>.
    /// </summary>
    public string? Name => IsDefault || IsOtherGroups ? null : name;

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
    /// The spelling configuration and the HTTP API use for this scope: the group name, <c>*</c> for
    /// the catch-all, and <c>_</c> for the default bucket (a property or JSON key cannot be empty).
    /// </summary>
    internal string ToConfigurationKey()
    {
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

        return name!;
    }
}
