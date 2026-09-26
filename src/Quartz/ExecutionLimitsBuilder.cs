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
/// <see cref="IQuartzBuilder.UseExecutionLimits(Action{ExecutionLimitsBuilder})"/> hands one of these
/// to a callback, which is the usual way to configure limits; its
/// <see cref="QuartzBuilderExtensions.UseExecutionLimits(IQuartzBuilder, Action{IServiceProvider, ExecutionLimitsBuilder})"/>
/// sibling does the same once the container exists, for a limit that comes from a service.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// ExecutionLimits limits = ExecutionLimitsBuilder.Create()
///     .ForGroup("high-cpu", 2)                                     // two on this node
///     .ForGroup("tenant-acme", 8, ExecutionLimitScope.Cluster)     // eight across the cluster
///     .ForGroupsWithPrefix("tenant:", 2, ExecutionLimitScope.Cluster) // two for each other tenant
///     .ForOtherGroups(5)
///     .Build();
/// </code>
/// </example>
public sealed class ExecutionLimitsBuilder
{
    private readonly Dictionary<string, ExecutionGroupAllowance> limits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ExecutionGroupAllowance> prefixes = new(StringComparer.Ordinal);
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
    /// <exception cref="ArgumentException"><paramref name="group"/> is a reserved name, or ends with
    /// <c>*</c>, which is how a configuration key names a prefix: use <see cref="ForGroupsWithPrefix"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrent"/> is negative, or
    /// <paramref name="scope"/> is not one of the defined values.</exception>
    public ExecutionLimitsBuilder ForGroup(string group, int maxConcurrent, ExecutionLimitScope scope = ExecutionLimitScope.Node)
    {
        limits[RequireGroupName(group)] = new ExecutionGroupAllowance(RequireNonNegative(maxConcurrent), RequireDefinedScope(scope));
        return this;
    }

    /// <summary>
    /// Set the concurrency limit for every execution group that starts with a prefix. Each such group gets
    /// the limit on its own: <c>ForGroupsWithPrefix("tenant:", 2)</c> lets <c>tenant:acme</c> and
    /// <c>tenant:initech</c> run two each, not two between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is <see cref="ForOtherGroups"/> narrowed to a family of groups, and it is decided in the same
    /// place: a group's own <see cref="ForGroup"/> or <see cref="Unlimited"/> wins, then the longest prefix
    /// the group starts with, then the catch-all. A prefix equal to a named group's whole name therefore
    /// governs the other groups that start with it and not that one. Triggers with no execution group are
    /// never matched.
    /// </para>
    /// <para>
    /// Each group is counted on its own, and a group with nothing in flight holds no count, so a family of
    /// many tenants costs what those running at the moment cost.
    /// </para>
    /// </remarks>
    /// <param name="prefix">The start of the group names, compared ordinally. Configuration spells it
    /// <c>quartz.executionLimit.tenant:*</c>; the <c>*</c> is not part of the prefix.</param>
    /// <param name="maxConcurrent">Maximum concurrent threads for each group (must be &gt;= 0), or
    /// <c>0</c> to forbid execution.</param>
    /// <param name="scope">Whether each group's limit counts what this node runs or what the whole
    /// cluster runs.</param>
    /// <returns>This builder for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="prefix"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="prefix"/> is blank, or ends with <c>*</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrent"/> is negative, or
    /// <paramref name="scope"/> is not one of the defined values.</exception>
    public ExecutionLimitsBuilder ForGroupsWithPrefix(string prefix, int maxConcurrent, ExecutionLimitScope scope = ExecutionLimitScope.Node)
    {
        prefixes[ExecutionLimits.RequirePrefix(prefix, nameof(prefix))] = new ExecutionGroupAllowance(RequireNonNegative(maxConcurrent), RequireDefinedScope(scope));
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
        ExecutionGroupPrefixAllowance[] ordered = new ExecutionGroupPrefixAllowance[prefixes.Count];
        int i = 0;
        foreach (KeyValuePair<string, ExecutionGroupAllowance> pair in prefixes)
        {
            ordered[i++] = new ExecutionGroupPrefixAllowance(pair.Key, pair.Value);
        }

        // Longest first, so the first prefix a group starts with is the one that governs it. Ties cannot
        // match the same group, so their order only has to be stable.
        Array.Sort(ordered, static (left, right) =>
        {
            int byLength = right.Prefix.Length.CompareTo(left.Prefix.Length);
            return byLength != 0 ? byLength : string.CompareOrdinal(left.Prefix, right.Prefix);
        });

        return new ExecutionLimits(
            new Dictionary<string, ExecutionGroupAllowance>(limits, StringComparer.Ordinal),
            ordered,
            useTriggerGroupWhenUnset);
    }

    /// <summary>
    /// Applies one limit spelled the way configuration and the HTTP API key it — a group name, <c>_</c> or
    /// <c>null</c> for the default bucket, <c>*</c> for the catch-all, <c>tenant:*</c> for a prefix — and
    /// reports whether it configured anything.
    /// </summary>
    /// <remarks>
    /// The one reading of a configuration key, shared by the property bridge, the HTTP endpoint and the
    /// HTTP client, so that a limit read back from any of them means what it meant when it was written.
    /// An unlimited catch-all or default bucket configures nothing, because both are unlimited already.
    /// </remarks>
    /// <exception cref="ArgumentException">A prefix is given no count: a prefix limit cannot be unlimited.</exception>
    internal bool ForConfigurationKey(string key, int? maxConcurrent, ExecutionLimitScope scope)
    {
        string trimmed = key.Trim();

        if (trimmed == ExecutionLimits.OtherGroups || ExecutionLimits.IsDefaultGroupAlias(trimmed))
        {
            if (maxConcurrent is not int limit)
            {
                return false;
            }

            if (trimmed == ExecutionLimits.OtherGroups)
            {
                ForOtherGroups(limit, scope);
            }
            else
            {
                ForDefaultGroup(limit, scope);
            }

            return true;
        }

        if (ExecutionLimits.TryReadPrefixKey(trimmed, out string? prefix))
        {
            if (maxConcurrent is not int prefixLimit)
            {
                throw new ArgumentException(
                    $"'{trimmed}' limits every group starting with '{prefix}', and takes a count: a prefix cannot be unlimited. Leave it out instead.",
                    nameof(key));
            }

            ForGroupsWithPrefix(prefix, prefixLimit, scope);
            return true;
        }

        if (maxConcurrent is int groupLimit)
        {
            ForGroup(trimmed, groupLimit, scope);
        }
        else
        {
            // Unlimited takes no scope: there is no number to count, in either of them.
            Unlimited(trimmed);
        }

        return true;
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

        // Configuration spells a prefix as the prefix followed by '*', so a named group spelled that way
        // could not be written back — over the HTTP API or into properties — as the group it is.
        if (ExecutionLimits.TryReadPrefixKey(trimmed, out string? prefix))
        {
            throw new ArgumentException(
                $"'{trimmed}' is how configuration names every group starting with '{prefix}'. Use ForGroupsWithPrefix(\"{prefix}\", …) for that; a single group's name cannot end with '*'.",
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
