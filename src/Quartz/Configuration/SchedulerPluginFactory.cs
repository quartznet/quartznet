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
    public static List<(string Name, ISchedulerPlugin Plugin)> Create(
        IServiceProvider provider,
        IEnumerable<ISchedulerPlugin> registered,
        NameValueCollection properties)
    {
        var plugins = new List<(string, ISchedulerPlugin)>();
        var alreadyRegistered = new HashSet<Type>();

        foreach (var plugin in registered)
        {
            plugins.Add((plugin.GetType().Name, plugin));
            alreadyRegistered.Add(plugin.GetType());
        }

        foreach (var name in PluginNames(properties))
        {
            var prefix = $"{StdSchedulerFactory.PropertyPluginPrefix}.{name}";
            var typeName = properties[$"{prefix}.{StdSchedulerFactory.PropertyPluginType}"];
            if (string.IsNullOrWhiteSpace(typeName))
            {
                Throw.SchedulerException($"SchedulerPlugin type not specified for plugin '{name}'");
            }

            var type = typeLoadHelper.LoadType(typeName);
            if (type is null)
            {
                Throw.SchedulerException($"SchedulerPlugin of type '{typeName}' could not be loaded.");
                continue;
            }

            // A plugin registered in code is configured in code; do not build a second copy of it.
            if (!alreadyRegistered.Add(type))
            {
                ApplyProperties(plugins.Find(x => x.Item2.GetType() == type).Item2, type, prefix, properties);
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
