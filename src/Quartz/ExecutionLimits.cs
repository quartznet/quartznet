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
    /// caller can use: <see cref="ExecutionGroupLimit.Group"/> reports the default group as
    /// <see langword="null"/>, and <see cref="ExecutionLimitsBuilder.ForDefaultGroup"/> configures it.
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
    /// Reads the limit configured for one execution group.
    /// </summary>
    /// <param name="executionGroup">The group name, <see langword="null"/> for triggers that have no
    /// execution group, or <see cref="OtherGroups"/> for the catch-all.</param>
    /// <param name="maxConcurrent">The limit: a positive count, <c>0</c> when the group is forbidden,
    /// or <see langword="null"/> when it is explicitly unlimited.</param>
    /// <returns><see langword="true"/> when the group has a limit of its own. <see langword="false"/>
    /// does not mean unlimited — <see cref="OtherGroups"/> may still apply to a named group.</returns>
    public bool TryGetLimit(string? executionGroup, out int? maxConcurrent)
    {
        return limits.TryGetValue(NormalizeGroupKey(executionGroup), out maxConcurrent);
    }

    private ExecutionGroupLimit[] Materialize()
    {
        ExecutionGroupLimit[] result = new ExecutionGroupLimit[limits.Count];
        int i = 0;
        foreach (KeyValuePair<string, int?> pair in limits)
        {
            result[i++] = new ExecutionGroupLimit(pair.Key == DefaultGroupKey ? null : pair.Key, pair.Value);
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
/// One execution group's entry in an <see cref="ExecutionLimits"/> snapshot.
/// </summary>
/// <param name="Group">The execution group, <see langword="null"/> for triggers that have no
/// execution group, or <see cref="ExecutionLimits.OtherGroups"/> for the catch-all.</param>
/// <param name="MaxConcurrent">The limit: a positive count, <c>0</c> when the group is forbidden on
/// this node, or <see langword="null"/> when it is explicitly unlimited.</param>
public readonly record struct ExecutionGroupLimit(string? Group, int? MaxConcurrent);
