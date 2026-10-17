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
/// A name the user chose keeps its case: <c>DataSource:MyDS:ConnectionString</c> becomes
/// <c>quartz.dataSource.MyDS.connectionString</c>, and <c>ExecutionLimit:Reports</c> becomes
/// <c>quartz.executionLimit.Reports</c>. The names are those of execution groups, context keys, data
/// sources and listeners. A plugin's name is still camel-cased: the XML plugin names its stored jobs after it.
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
                FlattenSection(child, "quartz." + ToCamelCase(child.Key), properties);
            }
        }
    }

    /// <summary>
    /// The prefixes whose children are names the user chose rather than property names, spelled as
    /// <see cref="Impl.StdSchedulerFactory"/> spells them, and whether the whole rest of the key is the name.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A property name is matched without regard to case, so it can be camel-cased on the way to the flat
    /// key. A name cannot be: an execution group, a context key, a data source and a listener are each
    /// looked up ordinally. Below an execution limit or a context key the
    /// rest of the path is the name, rejoined with the <c>:</c> it was written with; below the others the
    /// next segment is the name and what follows it are properties.
    /// </para>
    /// <para>
    /// <c>StdSchedulerFactory</c> matches the prefixes themselves ordinally too, which configuration keys,
    /// being case-insensitive, do not promise, so a section that matches one of these without regard to
    /// case is written in the spelling here.
    /// </para>
    /// <para>
    /// A plugin's name is left camel-cased on purpose. Nothing looks it up ordinally, and the XML plugin
    /// names its persisted file-scan job and trigger after it, so a change of case would leave the
    /// previous version's job behind, failing on every scan.
    /// </para>
    /// </remarks>
    private static readonly (string Prefix, bool WholeKey)[] namedChildPrefixes =
    {
        (Impl.StdSchedulerFactory.PropertyExecutionLimitPrefix, true),
        (Impl.StdSchedulerFactory.PropertySchedulerContextPrefix, true),
        (Impl.StdSchedulerFactory.PropertyDataSourcePrefix, false),
        (Impl.StdSchedulerFactory.PropertyJobListenerPrefix, false),
        (Impl.StdSchedulerFactory.PropertyTriggerListenerPrefix, false),
    };

    private static void FlattenSection(
        IConfigurationSection section,
        string key,
        NameValueCollection properties)
    {
        if (section.Value is not null)
        {
            properties[key] = section.Value;
        }

        string? prefix = NamedChildPrefix(key, out bool wholeKey);
        foreach (IConfigurationSection child in section.GetChildren())
        {
            if (prefix is null)
            {
                FlattenSection(child, key + "." + ToCamelCase(child.Key), properties);
            }
            else if (wholeKey)
            {
                FlattenAsWritten(child, prefix + "." + child.Key, properties);
            }
            else
            {
                FlattenSection(child, prefix + "." + child.Key, properties);
            }
        }
    }

    /// <summary>
    /// Writes a section and everything beneath it under the key as the configuration spelled it, with
    /// the <c>:</c> that separated the segments put back.
    /// </summary>
    private static void FlattenAsWritten(IConfigurationSection section, string key, NameValueCollection properties)
    {
        if (section.Value is not null)
        {
            properties[key] = section.Value;
        }

        foreach (IConfigurationSection child in section.GetChildren())
        {
            FlattenAsWritten(child, key + ConfigurationPath.KeyDelimiter + child.Key, properties);
        }
    }

    private static string? NamedChildPrefix(string key, out bool wholeKey)
    {
        foreach ((string prefix, bool isWholeKey) in namedChildPrefixes)
        {
            if (string.Equals(key, prefix, StringComparison.OrdinalIgnoreCase))
            {
                wholeKey = isWholeKey;
                return prefix;
            }
        }

        wholeKey = false;
        return null;
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
