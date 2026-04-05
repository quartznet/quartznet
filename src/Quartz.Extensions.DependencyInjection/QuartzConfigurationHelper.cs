using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;

using Microsoft.Extensions.Configuration;

namespace Quartz;

/// <summary>
/// Converts hierarchical <see cref="IConfiguration"/> sections into flat Quartz property keys.
/// </summary>
/// <remarks>
/// <para>
/// Each JSON path segment is converted to camelCase and joined with dots, then prefixed with "quartz.".
/// For example, the JSON path <c>Scheduler:InstanceName</c> becomes <c>quartz.scheduler.instanceName</c>.
/// </para>
/// <para>
/// Keys that already start with "quartz." at the root level are passed through unchanged for backward compatibility.
/// </para>
/// </remarks>
public static class QuartzConfigurationHelper
{
    // Section names that contain scheduling data, not configuration properties.
    // These are handled separately by the scheduling data processor.
    // Section names that are not configuration properties — handled by other code paths.
    private static readonly HashSet<string> reservedSectionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Schedule",
        "Scheduling",
        "Schedulers",
    };

    /// <summary>
    /// Converts a hierarchical <see cref="IConfiguration"/> section into a flat <see cref="NameValueCollection"/>
    /// of Quartz configuration properties.
    /// </summary>
    /// <param name="configuration">
    /// The configuration section to convert, typically <c>Configuration.GetSection("Quartz")</c>.
    /// </param>
    /// <returns>A <see cref="NameValueCollection"/> containing flat Quartz property keys and their values.</returns>
    /// <example>
    /// <code>
    /// // Use with StdSchedulerFactory directly (without DI):
    /// var properties = QuartzConfigurationHelper.ToNameValueCollection(config.GetSection("Quartz"));
    /// var factory = new StdSchedulerFactory();
    /// factory.Initialize(properties);
    /// </code>
    /// </example>
    public static NameValueCollection ToNameValueCollection(IConfiguration configuration)
    {
        var properties = new NameValueCollection();
        PopulateProperties(configuration, properties);
        return properties;
    }

    internal static void PopulateProperties(IConfiguration configuration, NameValueCollection properties)
    {
        foreach (IConfigurationSection child in configuration.GetChildren())
        {
            if (child.Key.StartsWith("quartz.", StringComparison.OrdinalIgnoreCase))
            {
                // Legacy flat key at root level — pass through unchanged
                if (child.Value is not null)
                {
                    properties[child.Key] = child.Value;
                }
            }
            else if (reservedSectionNames.Contains(child.Key))
            {
                // Skip scheduling data sections — handled by JsonSchedulingHelper
            }
            else
            {
                FlattenSection(child, ToCamelCase(child.Key), properties);
            }
        }
    }

    private static void FlattenSection(
        IConfigurationSection section,
        string currentPath,
        NameValueCollection properties)
    {
        if (section.Value is not null)
        {
            properties["quartz." + currentPath] = section.Value;
        }

        foreach (IConfigurationSection child in section.GetChildren())
        {
            string childPath = currentPath + "." + ToCamelCase(child.Key);
            FlattenSection(child, childPath, properties);
        }
    }

    private static string ToCamelCase(string value)
    {
        if (value.Length == 0 || char.IsLower(value[0]))
        {
            return value;
        }

        return char.ToLower(value[0], CultureInfo.InvariantCulture) + value.Substring(1);
    }
}
