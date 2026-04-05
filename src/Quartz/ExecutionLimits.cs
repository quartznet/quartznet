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

using System.Collections;

namespace Quartz;

/// <summary>
/// Configures per-node thread limits for execution groups. Execution groups are
/// optional tags on triggers that characterize the resource requirements of the
/// associated job (e.g. "batch-jobs", "high-cpu", "large-ram").
/// </summary>
/// <remarks>
/// <para>Each scheduler node can declare its own limits independently:
/// <list type="bullet">
///   <item>A positive value limits how many threads the group may consume concurrently.</item>
///   <item>A value of <c>0</c> forbids the group from running on this node.</item>
///   <item><see langword="null"/> means unlimited (no restriction).</item>
/// </list>
/// </para>
/// <para>Use <see cref="OtherGroups"/> as a catch-all default for groups not
/// explicitly listed.</para>
/// <para>Instances passed to <see cref="IScheduler.SetExecutionLimits"/> are
/// snapshotted — subsequent mutations do not affect the scheduler.</para>
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1710:Identifiers should have correct suffix")]
public sealed class ExecutionLimits : IReadOnlyDictionary<string, int?>
{
    /// <summary>
    /// Key used to specify the default limit for execution groups not explicitly configured.
    /// </summary>
    public const string OtherGroups = "*";

    /// <summary>
    /// Key used internally to represent triggers that have no execution group (<see langword="null"/>).
    /// In property-based configuration, the underscore (<c>_</c>) character is used as an alias.
    /// </summary>
    internal const string DefaultGroupKey = "";

    private readonly Dictionary<string, int?> limits = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new empty <see cref="ExecutionLimits"/> instance.
    /// </summary>
    public ExecutionLimits()
    {
    }

    /// <summary>
    /// Copy constructor — creates a frozen snapshot of the given limits.
    /// </summary>
    private ExecutionLimits(Dictionary<string, int?> source)
    {
        limits = new Dictionary<string, int?>(source, StringComparer.Ordinal);
    }

    /// <summary>
    /// Set the concurrency limit for a named execution group.
    /// </summary>
    /// <param name="group">The execution group name.</param>
    /// <param name="maxConcurrent">Maximum concurrent threads (must be &gt;= 0), or <c>0</c> to forbid execution.</param>
    /// <returns>This instance for fluent chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="group"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrent"/> is negative.</exception>
    public ExecutionLimits ForGroup(string group, int maxConcurrent)
    {
        ArgumentNullException.ThrowIfNull(group);
        group = group.Trim();

        if (group.Length == 0 || group == OtherGroups || group == "_" || group.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Group name '{group}' is reserved. Use ForDefaultGroup() for the default group or ForOtherGroups() for the catch-all.",
                nameof(group));
        }

        if (maxConcurrent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrent), maxConcurrent, "Execution limit must be non-negative.");
        }

        limits[group] = maxConcurrent;
        return this;
    }

    /// <summary>
    /// Set the concurrency limit for triggers that have no execution group.
    /// </summary>
    /// <param name="maxConcurrent">Maximum concurrent threads (must be &gt;= 0), or <c>0</c> to forbid execution.</param>
    /// <returns>This instance for fluent chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrent"/> is negative.</exception>
    public ExecutionLimits ForDefaultGroup(int maxConcurrent)
    {
        if (maxConcurrent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrent), maxConcurrent, "Execution limit must be non-negative.");
        }

        limits[DefaultGroupKey] = maxConcurrent;
        return this;
    }

    /// <summary>
    /// Set the default concurrency limit applied to any execution group not explicitly configured.
    /// </summary>
    /// <param name="maxConcurrent">Maximum concurrent threads (must be &gt;= 0), or <c>0</c> to forbid execution.</param>
    /// <returns>This instance for fluent chaining.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxConcurrent"/> is negative.</exception>
    public ExecutionLimits ForOtherGroups(int maxConcurrent)
    {
        if (maxConcurrent < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrent), maxConcurrent, "Execution limit must be non-negative.");
        }

        limits[OtherGroups] = maxConcurrent;
        return this;
    }

    /// <summary>
    /// Mark a group as having no concurrency limit (unlimited).
    /// This is the same as not listing the group at all, but can be useful
    /// to explicitly override a previously configured limit.
    /// </summary>
    /// <param name="group">The execution group name.</param>
    /// <returns>This instance for fluent chaining.</returns>
    public ExecutionLimits Unlimited(string group)
    {
        ArgumentNullException.ThrowIfNull(group);
        group = group.Trim();

        if (group.Length == 0 || group == OtherGroups || group == "_" || group.Equals("null", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"Group name '{group}' is reserved. Use ForDefaultGroup() for the default group or ForOtherGroups() for the catch-all.",
                nameof(group));
        }

        limits[group] = null;
        return this;
    }

    /// <summary>
    /// Creates an immutable snapshot of these limits. The returned instance is
    /// safe to share across threads without further mutation concerns.
    /// </summary>
    internal ExecutionLimits Snapshot()
    {
        return new ExecutionLimits(limits);
    }

    /// <summary>
    /// Checks whether a trigger with the given execution group is allowed to fire
    /// and decrements the available count if so.
    /// </summary>
    /// <param name="executionGroup">The trigger's execution group (may be <see langword="null"/>).</param>
    /// <param name="availableLimits">A mutable working copy of the limits to decrement. The key
    /// is the normalized group name (<see cref="NormalizeGroupKey"/>).</param>
    /// <returns><see langword="true"/> if the trigger is allowed; <see langword="false"/> if its group
    /// has reached its limit or is forbidden.</returns>
    internal static bool CheckExecutionLimits(string? executionGroup, Dictionary<string, int?> availableLimits)
    {
        string key = NormalizeGroupKey(executionGroup);

        int? limit;
        if (availableLimits.TryGetValue(key, out int? groupLimit))
        {
            limit = groupLimit;
        }
        else if (key != DefaultGroupKey && availableLimits.TryGetValue(OtherGroups, out int? otherLimit))
        {
            // OtherGroups ("*") is a catch-all for named groups only,
            // not for the default (null/ungrouped) triggers
            limit = otherLimit;
        }
        else
        {
            return true; // no limit configured for this group
        }

        if (limit is null)
        {
            return true; // unlimited
        }

        if (limit <= 0)
        {
            return false; // forbidden or exhausted
        }

        // Decrement the available count for the specific group key
        // (even if the value came from the OtherGroups default, we track per-group)
        availableLimits[key] = limit - 1;
        return true;
    }

    /// <summary>
    /// Creates a mutable working copy of the configured limits, suitable for
    /// passing to <see cref="CheckExecutionLimits"/>.
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

    // IReadOnlyDictionary<string, int?> implementation

    /// <inheritdoc />
    public int? this[string key] => limits[key];

    /// <inheritdoc />
    public IEnumerable<string> Keys => limits.Keys;

    /// <inheritdoc />
    public IEnumerable<int?> Values => limits.Values;

    /// <inheritdoc />
    public int Count => limits.Count;

    /// <inheritdoc />
    public bool ContainsKey(string key) => limits.ContainsKey(key);

    /// <inheritdoc />
    public bool TryGetValue(string key, out int? value) => limits.TryGetValue(key, out value);

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<string, int?>> GetEnumerator() => limits.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
