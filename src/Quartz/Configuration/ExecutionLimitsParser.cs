using System.Collections.Specialized;

using Quartz.Impl;

namespace Quartz.Configuration;

/// <summary>
/// Reads per-node execution group limits from the <c>quartz.executionLimit.*</c> property keys.
/// </summary>
internal static class ExecutionLimitsParser
{
    /// <summary>
    /// Parses the execution limits, or returns <see langword="null"/> when none are configured.
    /// </summary>
    public static ExecutionLimits? Parse(NameValueCollection properties)
    {
        var limits = new ExecutionLimits();
        var prefix = StdSchedulerFactory.PropertyExecutionLimitPrefix + ".";
        var configured = false;

        foreach (var key in properties.AllKeys)
        {
            if (key is null || !key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            configured |= Apply(limits, key[prefix.Length..].Trim(), properties[key]?.Trim(), key);
        }

        // Whether anything was configured is tracked as it happens rather than read back off the
        // limits, because "unlimited" for the catch-all or default group is a key that configures
        // nothing at all.
        return configured ? limits : null;
    }

    /// <summary>
    /// Applies one key, and reports whether it configured anything.
    /// </summary>
    private static bool Apply(ExecutionLimits limits, string groupKey, string? rawValue, string key)
    {
        if (groupKey.Length == 0)
        {
            Throw.SchedulerConfigException($"Empty execution limit group key in property '{key}'.");
        }

        var limit = ParseLimit(rawValue, groupKey);

        if (groupKey == ExecutionLimits.OtherGroups)
        {
            if (!limit.HasValue)
            {
                return false;
            }

            limits.ForOtherGroups(limit.Value);
            return true;
        }

        // Underscore and "null" are aliases for the default (null) execution group.
        if (groupKey == "_" || string.Equals(groupKey, "null", StringComparison.OrdinalIgnoreCase))
        {
            if (!limit.HasValue)
            {
                return false;
            }

            limits.ForDefaultGroup(limit.Value);
            return true;
        }

        if (limit.HasValue)
        {
            limits.ForGroup(groupKey, limit.Value);
        }
        else
        {
            limits.Unlimited(groupKey);
        }

        return true;
    }

    private static int? ParseLimit(string? rawValue, string groupKey)
    {
        if (string.IsNullOrEmpty(rawValue)
            || string.Equals(rawValue, "unlimited", StringComparison.OrdinalIgnoreCase)
            || string.Equals(rawValue, "none", StringComparison.OrdinalIgnoreCase)
            || string.Equals(rawValue, "null", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!int.TryParse(rawValue, out var parsed) || parsed < 0)
        {
            Throw.SchedulerConfigException(
                $"Invalid execution limit value '{rawValue}' for group '{groupKey}'. " +
                "Expected a non-negative integer, 'unlimited', 'none', or 'null'.");
        }

        return parsed;
    }
}
