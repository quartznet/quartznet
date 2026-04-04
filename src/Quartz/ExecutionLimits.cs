using System;
using System.Collections;
using System.Collections.Generic;

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
/// </remarks>
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
    /// Set the concurrency limit for a named execution group.
    /// </summary>
    /// <param name="group">The execution group name.</param>
    /// <param name="maxConcurrent">Maximum concurrent threads, or <c>0</c> to forbid execution.</param>
    /// <returns>This instance for fluent chaining.</returns>
    public ExecutionLimits ForGroup(string group, int maxConcurrent)
    {
        if (group is null)
        {
            throw new ArgumentNullException(nameof(group));
        }

        limits[group] = maxConcurrent;
        return this;
    }

    /// <summary>
    /// Set the concurrency limit for triggers that have no execution group.
    /// </summary>
    /// <param name="maxConcurrent">Maximum concurrent threads, or <c>0</c> to forbid execution.</param>
    /// <returns>This instance for fluent chaining.</returns>
    public ExecutionLimits ForDefaultGroup(int maxConcurrent)
    {
        limits[DefaultGroupKey] = maxConcurrent;
        return this;
    }

    /// <summary>
    /// Set the default concurrency limit applied to any execution group not explicitly configured.
    /// </summary>
    /// <param name="maxConcurrent">Maximum concurrent threads, or <c>0</c> to forbid execution.</param>
    /// <returns>This instance for fluent chaining.</returns>
    public ExecutionLimits ForOtherGroups(int maxConcurrent)
    {
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
        if (group is null)
        {
            throw new ArgumentNullException(nameof(group));
        }

        limits[group] = null;
        return this;
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
        else if (availableLimits.TryGetValue(OtherGroups, out int? otherLimit))
        {
            limit = otherLimit;
        }
        else
        {
            return true; // no limit configured at all
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
