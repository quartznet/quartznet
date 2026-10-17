using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
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
/// A name the user chose keeps its case. Below a key in <see cref="LegacyPropertyKeys.namedChildPrefixes"/>
/// the next segment is a name — <c>DataSource:MyDS:ConnectionString</c> becomes
/// <c>quartz.dataSource.MyDS.connectionString</c> — and below an execution limit or a context key the rest
/// of the path is one name, rejoined with the <c>:</c> it was written with, so
/// <c>ExecutionLimit:tenant:*</c> becomes <c>quartz.executionLimit.tenant:*</c>.
/// </para>
/// <para>
/// Keys that already start with "quartz." at the root level are passed through as written, a <c>:</c> in
/// them included, for backward compatibility.
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
    /// <c>Scheduler:ServedExecutionGroups</c> and <c>Scheduler:RefusedExecutionGroups</c> are lists,
    /// which flatten to one indexed key per entry that the bridge — reading a comma-separated value
    /// under the bare key — would never consult.
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
        "quartz.scheduler.servedExecutionGroups",
        "quartz.scheduler.refusedExecutionGroups",
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
                FlattenAsWritten(child, child.Key, properties);
            }
            else if (reservedSectionNames.Contains(child.Key))
            {
                // Skip — handled by JsonSchedulingHelper
            }
            else
            {
                FlattenSection(child, LegacyPropertyKeys.Prefix + ToCamelCase(child.Key), properties);
            }
        }
    }

    /// <summary>
    /// Writes a section under <paramref name="key"/>, and each child under the key extended by the
    /// child's segment: camel-cased when it is a property, as written when it is a name.
    /// </summary>
    private static void FlattenSection(IConfigurationSection section, string key, NameValueCollection properties)
    {
        if (section.Value is not null && !IsBoundByTypedOptions(key))
        {
            properties[key] = section.Value;
        }

        if (!TryGetNamedChildPrefix(key, out string? prefix, out LegacyPropertyKeys.NameShape shape))
        {
            foreach (IConfigurationSection child in section.GetChildren())
            {
                FlattenSection(child, key + "." + ToCamelCase(child.Key), properties);
            }

            return;
        }

        foreach (IConfigurationSection child in section.GetChildren())
        {
            string named = prefix + "." + child.Key;
            if (shape == LegacyPropertyKeys.NameShape.WholeKey)
            {
                FlattenAsWritten(child, named, properties);
            }
            else
            {
                FlattenSection(child, named, properties);
            }
        }
    }

    /// <summary>
    /// Writes a section and everything beneath it under the key as the configuration spelled it, with
    /// the <c>:</c> that separated the segments put back.
    /// </summary>
    /// <remarks>
    /// <c>:</c> is the configuration path separator, so a key a user wrote with one in it — the prefix
    /// key <c>tenant:*</c>, whether as <c>"tenant:*"</c> in JSON, as <c>"tenant": { "*": … }</c>, or as
    /// <c>tenant__*</c> in an environment variable — arrives as two sections, the first with no value
    /// of its own. Rejoining them with <c>:</c> gives back what was written.
    /// </remarks>
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

    /// <summary>
    /// Whether <paramref name="key"/> is one of <see cref="LegacyPropertyKeys.namedChildPrefixes"/>,
    /// compared without regard to case, and how that prefix is spelled.
    /// </summary>
    private static bool TryGetNamedChildPrefix(
        string key,
        [NotNullWhen(true)] out string? prefix,
        out LegacyPropertyKeys.NameShape shape)
    {
        foreach ((string candidate, LegacyPropertyKeys.NameShape candidateShape) in LegacyPropertyKeys.namedChildPrefixes)
        {
            if (string.Equals(key, candidate, StringComparison.OrdinalIgnoreCase))
            {
                prefix = candidate;
                shape = candidateShape;
                return true;
            }
        }

        prefix = null;
        shape = default;
        return false;
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
