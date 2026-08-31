using System.Collections.Specialized;
using System.Globalization;

using Microsoft.Extensions.Configuration;

namespace Quartz.Configuration;

/// <summary>
/// Produces the flat <see cref="NameValueCollection"/> the property readers take, from whichever shape
/// the caller had: a hierarchical <see cref="IConfiguration"/> section, or a sequence of key/value pairs.
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
internal static class QuartzConfigurationHelper
{
    private static readonly HashSet<string> reservedSectionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Schedule",
        "Scheduling",
        "Schedulers",
    };

    /// <summary>
    /// The flat keys a typed options binding already owns, which are therefore not synthesized from the
    /// hierarchical section they would come from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A hierarchical section is read twice: it binds onto its typed options, and it is flattened onto
    /// the <c>quartz.*</c> keys <see cref="QuartzPropertyBridge"/> translates. For nearly every key the
    /// two have in common the second pass is doing something the binder cannot —
    /// <c>Scheduler:InterruptJobsOnShutdown</c> is a spelling no property carries any more,
    /// <c>Scheduler:IdleWaitTime</c> may be the legacy count of milliseconds that the binder would read
    /// as a count of days, <c>ThreadPool:Type</c> selects an implementation rather than setting a value,
    /// and a third-party component's own knobs have no options type at all. Those overlaps are
    /// corrections and they stay.
    /// </para>
    /// <para>
    /// <c>ThreadPool:MaxConcurrency</c> is the one key where both readers write the same property, from
    /// the same key, in the same shape. Two writers of one value in a last-wins pipeline is a coin flip
    /// rather than a fallback, so one of them has to go, and it is this one: the typed binder is the
    /// reader the section has a first-class binding for, it is the one the source generator writes a
    /// binder for, and a member added to <see cref="ThreadPoolOptions"/> later binds through it without
    /// needing an entry anywhere else. Nothing else under <c>ThreadPool</c> is affected — the type key,
    /// the legacy <c>threadCount</c> spelling and a third-party pool's own settings are all still
    /// flattened, because the bridge is the only reader any of them has.
    /// </para>
    /// <para>
    /// Only the <em>synthesized</em> spelling is dropped. A configuration that writes
    /// <c>quartz.threadPool.maxConcurrency</c> as a flat key is passed through untouched and read by the
    /// bridge, which is also the only reader on the paths that have no <see cref="IConfiguration"/> at
    /// all — <c>AddQuartz(NameValueCollection)</c> and its dictionary twins.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> boundByTypedOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        "quartz.threadPool.maxConcurrency",
    };

    /// <summary>
    /// Converts a hierarchical <see cref="IConfiguration"/> section into a flat <see cref="NameValueCollection"/>
    /// of Quartz configuration properties.
    /// </summary>
    /// <param name="configuration">
    /// The configuration section to convert, typically <c>Configuration.GetSection("Quartz")</c>.
    /// </param>
    /// <returns>A <see cref="NameValueCollection"/> containing flat Quartz property keys and their values.</returns>
    public static NameValueCollection ToNameValueCollection(IConfiguration configuration)
    {
        var properties = new NameValueCollection();
        PopulateProperties(configuration, properties);
        return properties;
    }

    /// <summary>
    /// Copies key/value pairs into the flat <see cref="NameValueCollection"/> the property readers take.
    /// </summary>
    /// <remarks>
    /// Every pair is copied, including ones whose value is <see langword="null"/> or whitespace. Deciding
    /// that an empty value means "not configured" belongs to the reader, which is where it happens; a
    /// converter that dropped keys of its own accord would make a key set to an empty string
    /// indistinguishable from one that was never given. A key given twice keeps the last value rather
    /// than accumulating both, which is what assignment means and what a dictionary source would produce.
    /// </remarks>
    internal static NameValueCollection ToNameValueCollection(IEnumerable<KeyValuePair<string, string?>> properties)
    {
        NameValueCollection collection = properties.TryGetNonEnumeratedCount(out int count) ? new NameValueCollection(count) : [];
        foreach (KeyValuePair<string, string?> pair in properties)
        {
            collection[pair.Key] = pair.Value;
        }

        return collection;
    }

    /// <summary>
    /// Flattens a configuration section into the collection, in place.
    /// </summary>
    /// <param name="configuration">The section to flatten.</param>
    /// <param name="properties">The collection to add the flattened keys to.</param>
    internal static void PopulateProperties(IConfiguration configuration, NameValueCollection properties)
    {
        foreach (var child in configuration.GetChildren())
        {
            if (child.Key.StartsWith("quartz.", StringComparison.OrdinalIgnoreCase))
            {
                if (child.Value is not null)
                {
                    properties[child.Key] = child.Value;
                }
            }
            else if (reservedSectionNames.Contains(child.Key))
            {
                // Skip — handled by JsonSchedulingHelper
            }
            else
            {
                FlattenSection(child, ToCamelCase(child.Key), properties);
            }
        }
    }

    private static void FlattenSection(IConfigurationSection section, string currentPath, NameValueCollection properties)
    {
        var key = "quartz." + currentPath;
        if (section.Value is not null && !boundByTypedOptions.Contains(key))
        {
            properties[key] = section.Value;
        }

        foreach (var child in section.GetChildren())
        {
            var childPath = currentPath + "." + ToCamelCase(child.Key);
            FlattenSection(child, childPath, properties);
        }
    }

    private static string ToCamelCase(string value)
    {
        if (value.Length == 0 || char.IsLower(value[0]))
        {
            return value;
        }

        return char.ToLower(value[0], CultureInfo.InvariantCulture) + value[1..];
    }
}
