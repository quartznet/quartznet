using System.Collections.Specialized;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl;
using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Configuration;

/// <summary>
/// Produces the plugins a scheduler should run, from both container registrations and the
/// <c>quartz.plugin.*</c> property keys.
/// </summary>
/// <remarks>
/// <para>
/// Plugins are resolved when the scheduler is created rather than when services are registered. That is
/// deliberate: a plugin can be named by configuration that only exists once the container is built —
/// deferred configuration is the obvious case — and resolving late means both spellings work through
/// the same path instead of one of them silently producing no plugins.
/// </para>
/// <para>
/// A plugin named by configuration is still constructed through the container, so it gets constructor
/// injection like anything else. Only the leftover string properties are applied by reflection
/// afterwards, which is what keeps existing <c>quartz.plugin.&lt;name&gt;.&lt;property&gt;</c>
/// configuration working.
/// </para>
/// </remarks>
internal static class SchedulerPluginFactory
{
    private static readonly SimpleTypeLoader typeLoader = new();

    /// <summary>
    /// Creates the plugins for a scheduler, paired with the names they were configured under.
    /// </summary>
    /// <param name="provider">The container, used to resolve or construct each plugin.</param>
    /// <param name="registered">Plugins already registered as services.</param>
    /// <param name="properties">The flat properties that may name further plugins.</param>
    /// <param name="schedulerName">The options name of the scheduler these plugins belong to.</param>
    public static List<(string Name, ISchedulerPlugin Plugin)> Create(
        IServiceProvider provider,
        IEnumerable<ISchedulerPlugin> registered,
        NameValueCollection properties,
        string schedulerName)
    {
        var plugins = new List<(string Name, ISchedulerPlugin Plugin)>();

        // Names chosen where the plugin was added, so a plugin configured in code is known by the same
        // name as the same plugin configured by properties. The same type can be added more than once,
        // so the last name registered for it stands rather than the lookup throwing.
        var chosenNames = new Dictionary<Type, string>();
        foreach (var chosen in provider.GetServices<SchedulerPluginName>())
        {
            if (string.Equals(chosen.SchedulerName, schedulerName, StringComparison.Ordinal))
            {
                chosenNames[chosen.PluginType] = chosen.Name;
            }
        }

        foreach (var plugin in registered)
        {
            var type = plugin.GetType();
            plugins.Add((chosenNames.TryGetValue(type, out var name) ? name : type.Name, plugin));
        }

        var loader = provider.GetService<ITypeLoader>() ?? typeLoader;

        foreach (var name in PluginNames(properties))
        {
            var prefix = $"{LegacyPropertyKeys.PluginPrefix}.{name}";

            // A plugin already added in code under this name is configured in code; apply the leftover
            // settings to it rather than building a second copy. Matching on the name rather than the
            // type is what lets two entries of the same type — one XML plugin per tenant, say — each
            // have their own instance and their own files, as the properties format has always allowed.
            var existing = plugins.FindIndex(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0)
            {
                ApplyProperties(plugins[existing].Plugin, plugins[existing].Plugin.GetType(), prefix, properties);
                continue;
            }

            var type = ResolveType(properties, prefix, name, loader);
            var plugin = Build(provider, type);

            ApplyProperties(plugin, type, prefix, properties);
            plugins.Add((name, plugin));
        }

        return plugins;
    }

    /// <summary>
    /// Loads the type a plugin entry names. A missing type key means the entry configures a plugin that
    /// was never added, since one added in code would have been matched by name already.
    /// </summary>
    private static Type ResolveType(NameValueCollection properties, string prefix, string name, ITypeLoader loader)
    {
        var typeName = properties[$"{prefix}.{LegacyPropertyKeys.PluginType}"];
        if (string.IsNullOrWhiteSpace(typeName))
        {
            Throw.SchedulerException($"SchedulerPlugin type not specified for plugin '{name}'");
        }

        var type = loader.LoadType(typeName);
        if (type is null)
        {
            Throw.SchedulerException($"SchedulerPlugin of type '{typeName}' could not be loaded.");
        }

        return type!;
    }

    private static ISchedulerPlugin Build(IServiceProvider provider, Type type)
    {
        try
        {
            return provider.GetService(type) as ISchedulerPlugin
                ?? (ISchedulerPlugin) ActivatorUtilities.CreateInstance(provider, type);
        }
        catch (Exception e)
        {
            Throw.SchedulerException($"SchedulerPlugin of type '{type}' could not be instantiated.", e);
            return default!;
        }
    }

    private static void ApplyProperties(ISchedulerPlugin plugin, Type type, string prefix, NameValueCollection properties)
    {
        var pluginProperties = new NameValueCollection();
        var start = prefix + ".";
        foreach (var key in properties.AllKeys)
        {
            if (key is null || !key.StartsWith(start, StringComparison.Ordinal))
            {
                continue;
            }

            var stripped = key[start.Length..];
            if (!string.Equals(stripped, LegacyPropertyKeys.PluginType, StringComparison.Ordinal))
            {
                pluginProperties[stripped] = properties[key];
            }
        }

        if (pluginProperties.Count == 0)
        {
            return;
        }

        try
        {
            ObjectUtils.SetObjectProperties(plugin, pluginProperties);
        }
        catch (Exception e)
        {
            Throw.SchedulerException($"SchedulerPlugin of type '{type}' properties could not be configured.", e);
        }
    }

    private static HashSet<string> PluginNames(NameValueCollection properties)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var start = LegacyPropertyKeys.PluginPrefix + ".";
        foreach (var key in properties.AllKeys)
        {
            if (key is null || !key.StartsWith(start, StringComparison.Ordinal))
            {
                continue;
            }

            var remainder = key[start.Length..];
            var separator = remainder.IndexOf('.');
            if (separator > 0)
            {
                names.Add(remainder[..separator]);
            }
        }

        return names;
    }
}
