using System.Collections.Specialized;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Spi;
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
    private static readonly SimpleTypeLoadHelper typeLoadHelper = new();

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
        var alreadyRegistered = new HashSet<Type>();

        // Names chosen where the plugin was added, so a plugin configured in code is known by the same
        // name as the same plugin configured by properties.
        var chosenNames = provider.GetServices<SchedulerPluginName>()
            .Where(x => string.Equals(x.SchedulerName, schedulerName, StringComparison.Ordinal))
            .ToDictionary(x => x.PluginType, x => x.Name);

        foreach (var plugin in registered)
        {
            var type = plugin.GetType();
            plugins.Add((chosenNames.TryGetValue(type, out var chosen) ? chosen : type.Name, plugin));
            alreadyRegistered.Add(type);
        }

        var loader = provider.GetService<ITypeLoadHelper>() ?? typeLoadHelper;

        foreach (var name in PluginNames(properties))
        {
            var prefix = $"{StdSchedulerFactory.PropertyPluginPrefix}.{name}";
            var typeName = properties[$"{prefix}.{StdSchedulerFactory.PropertyPluginType}"];

            if (string.IsNullOrWhiteSpace(typeName))
            {
                // No type key. The plugin itself was added in code, and these are settings for it — so
                // find it by the name it was added under rather than refusing to start.
                var index = plugins.FindIndex(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
                if (index < 0)
                {
                    Throw.SchedulerException($"SchedulerPlugin type not specified for plugin '{name}'");
                }

                ApplyProperties(plugins[index].Plugin, plugins[index].Plugin.GetType(), prefix, properties);
                continue;
            }

            var type = loader.LoadType(typeName);
            if (type is null)
            {
                Throw.SchedulerException($"SchedulerPlugin of type '{typeName}' could not be loaded.");
                continue;
            }

            // A plugin registered in code is configured in code; do not build a second copy of it.
            if (!alreadyRegistered.Add(type))
            {
                ApplyProperties(plugins.Find(x => x.Plugin.GetType() == type).Plugin, type, prefix, properties);
                continue;
            }

            ISchedulerPlugin plugin;
            try
            {
                plugin = provider.GetService(type) as ISchedulerPlugin
                    ?? (ISchedulerPlugin) ActivatorUtilities.CreateInstance(provider, type);
            }
            catch (Exception e)
            {
                Throw.SchedulerException($"SchedulerPlugin of type '{typeName}' could not be instantiated.", e);
                continue;
            }

            ApplyProperties(plugin, type, prefix, properties);
            plugins.Add((name, plugin));
        }

        return plugins;
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
            if (!string.Equals(stripped, StdSchedulerFactory.PropertyPluginType, StringComparison.Ordinal))
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
        var start = StdSchedulerFactory.PropertyPluginPrefix + ".";
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
