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

        foreach (var key in properties.AllKeys)
        {
            if (key is null || !key.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var groupKey = key[prefix.Length..].Trim();
            if (groupKey.Length == 0)
            {
                Throw.SchedulerConfigException($"Empty execution limit group key in property '{key}'.");
            }

            var limitValue = ParseLimit(properties[key]?.Trim(), groupKey);

            if (groupKey == "*")
            {
                if (limitValue.HasValue)
                {
                    limits.ForOtherGroups(limitValue.Value);
                }
            }
            else if (groupKey == "_" || string.Equals(groupKey, "null", StringComparison.OrdinalIgnoreCase))
            {
                // Underscore and "null" are aliases for the default (null) execution group.
                if (limitValue.HasValue)
                {
                    limits.ForDefaultGroup(limitValue.Value);
                }
            }
            else if (limitValue.HasValue)
            {
                limits.ForGroup(groupKey, limitValue.Value);
            }
            else
            {
                limits.Unlimited(groupKey);
            }
        }

        return limits.Count > 0 ? limits : null;
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
