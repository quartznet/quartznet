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
    /// The configuration paths a typed options binding already owns, which are therefore not synthesized
    /// into the flat bag. A path stands for itself and everything beneath it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A hierarchical section is read twice: it binds onto its typed options, and it is flattened onto
    /// the <c>quartz.*</c> keys <see cref="QuartzPropertyBridge"/> translates. For most keys the two have
    /// in common the second pass is doing something the binder cannot —
    /// <c>Scheduler:InterruptJobsOnShutdown</c> is a spelling no property carries any more,
    /// <c>Scheduler:IdleWaitTime</c> may be the legacy count of milliseconds that the binder would read
    /// as a count of days, <c>ThreadPool:Type</c> selects an implementation rather than setting a value,
    /// and a third-party component's own knobs have no options type at all. Those overlaps are
    /// corrections and they stay.
    /// </para>
    /// <para>
    /// The paths here are the ones where the second pass is not doing anything, in one of two ways.
    /// <c>ThreadPool:MaxConcurrency</c> and <c>Scheduler:InstanceName</c> synthesize a key the bridge
    /// reads back onto the very property the binder just set: two writers of one value in a last-wins
    /// pipeline, which is a coin flip rather than a fallback. <c>Scheduler:MaxBatchSize</c> and
    /// <c>Scheduler:Context</c> synthesize a key <em>nothing</em> reads — the bridge's own spellings are
    /// <c>quartz.scheduler.batchTriggerAcquisitionMaxCount</c> and <c>quartz.context.key.*</c>, and
    /// <see cref="LegacyPropertyKeys.Validate" /> rejects both synthesized spellings by name, so a bag
    /// taken from <see cref="QuartzOptions.ToProperties" /> and handed to
    /// <c>AddQuartz(NameValueCollection)</c> was refused for keys Quartz had put there itself.
    /// </para>
    /// <para>
    /// Either way the typed binder is the reader to keep: it is the binding the section has first class,
    /// it is the one the source generator writes a binder for, and a member added to the options type
    /// later binds through it without needing an entry anywhere else. Nothing else under those sections
    /// is affected — <c>ThreadPool:Type</c>, the legacy <c>ThreadPool:ThreadCount</c> spelling,
    /// <c>Scheduler:InstanceId</c> and a third-party component's own settings are all still flattened,
    /// because the bridge is the only reader any of them has.
    /// </para>
    /// <para>
    /// <c>JobStore</c> is the overlap that is <em>not</em> here, and deliberately. It binds onto
    /// <see cref="AdoJobStoreOptions"/> and flattens onto keys the bridge maps onto the same options, so
    /// around fifteen properties — <c>TablePrefix</c>, <c>LockOnInsert</c>,
    /// <c>AcquireTriggersWithinLock</c>, <c>MaxMisfiresToHandleAtATime</c>, <c>SelectWithLockSql</c> and
    /// the rest — do have two writers of one value. They agree, and the section cannot be excluded a
    /// path at a time the way these can: <c>JobStore:Type</c>, <c>JobStore:Clustered</c>,
    /// <c>JobStore:UseProperties</c>, <c>JobStore:MisfireThreshold</c> in milliseconds and a third-party
    /// store's own knobs all have the bridge as their only reader, and they are interleaved with the
    /// duplicated ones rather than sitting under a sub-section of their own.
    /// </para>
    /// <para>
    /// Only the <em>synthesized</em> spelling is dropped. A configuration that writes
    /// <c>quartz.threadPool.maxConcurrency</c> as a flat key is passed through untouched and read by the
    /// bridge, which is also the only reader on the paths that have no <see cref="IConfiguration"/> at
    /// all — <c>AddQuartz(NameValueCollection)</c> and its dictionary twins.
    /// </para>
    /// </remarks>
    private static readonly string[] boundByTypedOptions =
    [
        "quartz.threadPool.maxConcurrency",
        "quartz.scheduler.instanceName",
        "quartz.scheduler.maxBatchSize",
        "quartz.scheduler.context",
    ];

    /// <summary>
    /// Whether <paramref name="key"/> is one of <see cref="boundByTypedOptions"/> or sits beneath one.
    /// </summary>
    /// <remarks>
    /// <c>Scheduler:Context</c> is a dictionary, so what has to be skipped is the whole subtree rather
    /// than a key anybody could list; the others have no subtree and so behave as exact matches.
    /// </remarks>
    private static bool IsBoundByTypedOptions(string key)
    {
        foreach (string owned in boundByTypedOptions)
        {
            if (key.StartsWith(owned, StringComparison.OrdinalIgnoreCase)
                && (key.Length == owned.Length || key[owned.Length] == '.'))
            {
                return true;
            }
        }

        return false;
    }

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
        if (section.Value is not null && !IsBoundByTypedOptions(key))
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
