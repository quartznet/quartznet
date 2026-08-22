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
/// Builds the <see cref="ExecutionLimits"/> a scheduler applies when it acquires triggers.
/// </summary>
/// <remarks>
/// <para>
/// The builder is mutable and the <see cref="ExecutionLimits"/> that <see cref="Build"/> returns is
/// not, so a snapshot handed to <see cref="IScheduler.SetExecutionLimits"/> cannot change underneath
/// the scheduler thread that reads it.
/// </para>
/// <para>
/// <see cref="IQuartzBuilder.UseExecutionLimits"/> hands one of these to a callback, which is the
/// usual way to configure limits.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// ExecutionLimits limits = ExecutionLimitsBuilder.Create()
///     .ForGroup("high-cpu", 2)                                     // two on this node
///     .ForGroup("tenant-acme", 8, ExecutionLimitScope.Cluster)     // eight across the cluster
///     .ForOtherGroups(5)
///     .Build();
/// </code>
/// </example>
public sealed class ExecutionLimitsBuilder
{
    private readonly Dictionary<string, ExecutionGroupAllowance> limits = new(StringComparer.Ordinal);
    private bool useTriggerGroupWhenUnset;

    internal ExecutionLimitsBuilder()
    {
    }

    /// <summary>
    /// Create an ExecutionLimitsBuilder with no limits configured.
    /// </summary>
    /// <returns>the new ExecutionLimitsBuilder</returns>
    public static ExecutionLimitsBuilder Create()
    {
        return new ExecutionLimitsBuilder();
    }

    /// <summary>
    /// Set the concurrency limit for a named execution group.
    /// </summary>
    /// <param name="group">The execution group name.</param>
    /// <param name="maxConcurrent">Maximum concurrent threads (must be &gt;= 0), or <c>0</c> to forbid execution.</param>
    /// <param name="scope">Whether the limit counts what this node runs or what the whole cluster runs.
    /// Node-scoped unless said otherwise, which is what execution limits have always meant.</param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="group"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="group"/> is a reserved name.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrent"/> is negative, or
    /// <paramref name="scope"/> is not one of the defined values.</exception>
    public ExecutionLimitsBuilder ForGroup(string group, int maxConcurrent, ExecutionLimitScope scope = ExecutionLimitScope.Node)
    {
        limits[RequireGroupName(group)] = new ExecutionGroupAllowance(RequireNonNegative(maxConcurrent), RequireDefinedScope(scope));
        return this;
    }

    /// <summary>
    /// Set the concurrency limit for triggers that have no execution group.
    /// </summary>
    /// <param name="maxConcurrent">Maximum concurrent threads (must be &gt;= 0), or <c>0</c> to forbid execution.</param>
    /// <param name="scope">Whether the limit counts what this node runs or what the whole cluster runs.</param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrent"/> is negative, or
    /// <paramref name="scope"/> is not one of the defined values.</exception>
    public ExecutionLimitsBuilder ForDefaultGroup(int maxConcurrent, ExecutionLimitScope scope = ExecutionLimitScope.Node)
    {
        limits[ExecutionLimits.DefaultGroupKey] = new ExecutionGroupAllowance(RequireNonNegative(maxConcurrent), RequireDefinedScope(scope));
        return this;
    }

    /// <summary>
    /// Set the default concurrency limit applied to any execution group not explicitly configured.
    /// </summary>
    /// <remarks>
    /// The catch-all hands each unlisted group an allowance of its own rather than one they share, and
    /// that holds whichever scope it is declared in: <c>ForOtherGroups(1, ExecutionLimitScope.Cluster)</c>
    /// lets three unlisted tenants run one job each across the cluster, not one job between them.
    /// </remarks>
    /// <param name="maxConcurrent">Maximum concurrent threads (must be &gt;= 0), or <c>0</c> to forbid execution.</param>
    /// <param name="scope">Whether the limit counts what this node runs or what the whole cluster runs.</param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrent"/> is negative, or
    /// <paramref name="scope"/> is not one of the defined values.</exception>
    public ExecutionLimitsBuilder ForOtherGroups(int maxConcurrent, ExecutionLimitScope scope = ExecutionLimitScope.Node)
    {
        limits[ExecutionLimits.OtherGroups] = new ExecutionGroupAllowance(RequireNonNegative(maxConcurrent), RequireDefinedScope(scope));
        return this;
    }

    /// <summary>
    /// Mark a group as having no concurrency limit (unlimited).
    /// </summary>
    /// <remarks>
    /// This is not the same as leaving the group out: an unlisted group falls back to
    /// <see cref="ForOtherGroups"/>, while an explicitly unlimited one does not. It takes no scope,
    /// because unlimited on one node and unlimited across the cluster are the same permission.
    /// </remarks>
    /// <param name="group">The execution group name.</param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="group"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="group"/> is a reserved name.</exception>
    public ExecutionLimitsBuilder Unlimited(string group)
    {
        limits[RequireGroupName(group)] = new ExecutionGroupAllowance(null, ExecutionLimitScope.Node);
        return this;
    }

    /// <summary>
    /// Treats a trigger that carries no execution group as belonging to a group named after its own
    /// <see cref="Key{T}.Group" />, for the purpose of these limits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a schedule that already partitions work by trigger group — a tenant per group, a subsystem
    /// per group — this caps each partition without restating every group name as an execution group on
    /// every trigger. <see cref="ForGroup" /> then names trigger groups, and
    /// <see cref="ForOtherGroups" /> caps the ones not named.
    /// </para>
    /// <para>
    /// The derivation is applied where a limit is evaluated and nowhere else: the trigger still carries
    /// no execution group, and the store still persists none. A trigger that does carry one is limited
    /// by that one. Two consequences worth knowing: ungrouped triggers stop falling under
    /// <see cref="ForDefaultGroup" /> — with this on, nothing is ungrouped — and a trigger whose group
    /// happens to be a name the limits reserve (<c>*</c>, <c>_</c>, <c>null</c>) is left ungrouped
    /// rather than folded into the bucket that name means.
    /// </para>
    /// </remarks>
    public ExecutionLimitsBuilder UseTriggerGroupWhenUnset()
    {
        useTriggerGroupWhenUnset = true;
        return this;
    }

    /// <summary>
    /// Takes an immutable snapshot of what has been configured so far.
    /// </summary>
    public ExecutionLimits Build()
    {
        return new ExecutionLimits(new Dictionary<string, ExecutionGroupAllowance>(limits, StringComparer.Ordinal), useTriggerGroupWhenUnset);
    }

    private static string RequireGroupName(string group)
    {
        ArgumentNullException.ThrowIfNull(group);
        string trimmed = group.Trim();

        if (ExecutionLimits.IsReservedGroupName(trimmed))
        {
            throw new ArgumentException(
                $"Group name '{trimmed}' is reserved. Use ForDefaultGroup() for the default group or ForOtherGroups() for the catch-all.",
                nameof(group));
        }

        return trimmed;
    }

    private static int RequireNonNegative(int maxConcurrent)
    {
        if (maxConcurrent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrent), maxConcurrent, "Execution limit must be non-negative.");
        }

        return maxConcurrent;
    }

    private static ExecutionLimitScope RequireDefinedScope(ExecutionLimitScope scope)
    {
        if (scope is not (ExecutionLimitScope.Node or ExecutionLimitScope.Cluster))
        {
            throw new ArgumentOutOfRangeException(nameof(scope), scope, "Execution limit scope must be Node or Cluster.");
        }

        return scope;
    }
}
