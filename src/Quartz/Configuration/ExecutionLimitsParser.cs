using System.Collections.Specialized;

using Quartz.Impl;

namespace Quartz.Configuration;

/// <summary>
/// Reads execution group limits from the <c>quartz.executionLimit.*</c> and
/// <c>quartz.clusterExecutionLimit.*</c> property keys.
/// </summary>
internal static class ExecutionLimitsParser
{
    /// <summary>
    /// Parses the execution limits, or returns <see langword="null"/> when none are configured.
    /// </summary>
    public static ExecutionLimits? Parse(NameValueCollection properties)
    {
        var builder = ExecutionLimitsBuilder.Create();
        var nodePrefix = LegacyPropertyKeys.ExecutionLimitPrefix + ".";
        var clusterPrefix = LegacyPropertyKeys.ClusterExecutionLimitPrefix + ".";
        var configured = false;

        foreach (var key in properties.AllKeys)
        {
            if (key is null)
            {
                continue;
            }

            if (key.StartsWith(nodePrefix, StringComparison.Ordinal))
            {
                configured |= Apply(builder, key[nodePrefix.Length..].Trim(), properties[key]?.Trim(), key, ExecutionLimitScope.Node);
            }
            else if (key.StartsWith(clusterPrefix, StringComparison.Ordinal))
            {
                configured |= Apply(builder, key[clusterPrefix.Length..].Trim(), properties[key]?.Trim(), key, ExecutionLimitScope.Cluster);
            }
        }

        // Whether anything was configured is tracked as it happens rather than read back off the
        // builder, because "unlimited" for the catch-all or default group is a key that configures
        // nothing at all.
        return configured ? builder.Build() : null;
    }

    /// <summary>
    /// Applies one key, and reports whether it configured anything.
    /// </summary>
    /// <remarks>
    /// The key is read by <see cref="ExecutionLimitsBuilder.ForConfigurationKey" />, the reading the HTTP
    /// API shares: a group name, <c>_</c> or <c>null</c>, <c>*</c>, or a prefix such as <c>tenant:*</c>.
    /// What it refuses is reported against the property that said it.
    /// </remarks>
    private static bool Apply(ExecutionLimitsBuilder builder, string groupKey, string? rawValue, string key, ExecutionLimitScope scope)
    {
        if (groupKey.Length == 0)
        {
            Throw.SchedulerConfigException($"Empty execution limit group key in property '{key}'.");
        }

        var limit = ParseLimit(rawValue, groupKey);

        try
        {
            return builder.ForConfigurationKey(groupKey, limit, scope);
        }
        catch (ArgumentException e)
        {
            throw new SchedulerConfigException($"Invalid execution limit property '{key}': {e.Message}", e);
        }
    }

    private static int? ParseLimit(string? rawValue, string groupKey)
    {
        if (string.IsNullOrEmpty(rawValue)
            || string.Equals(rawValue, "unlimited", StringComparison.OrdinalIgnoreCase)
            || string.Equals(rawValue, "none", StringComparison.OrdinalIgnoreCase)
            || string.Equals(rawValue, ExecutionLimits.DefaultGroupNullAlias, StringComparison.OrdinalIgnoreCase))
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
