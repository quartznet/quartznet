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
/// <para>
/// Everything here is one scheduler's: the property bag is that scheduler's, the names are read from
/// it, and a plugin type registered as a service is looked for under that scheduler's key. Two
/// schedulers each configuring an XML plugin therefore get two instances reading their own files,
/// which is what the properties format has always promised.
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
    /// <param name="schedulerKey">The scheduler these plugins belong to.</param>
    public static List<(string Name, ISchedulerPlugin Plugin)> Create(
        IServiceProvider provider,
        IEnumerable<ISchedulerPlugin> registered,
        NameValueCollection properties,
        SchedulerKey schedulerKey)
    {
        var schedulerName = schedulerKey.OptionsName;
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
            var plugin = Build(provider, schedulerKey.Key, type);

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

    /// <summary>
    /// Produces the plugin instance a <c>quartz.plugin.&lt;name&gt;.type</c> entry names: this
    /// scheduler's registration of that type when it has one, and otherwise a fresh instance built
    /// through the container.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The probe is <em>keyed</em>, and only ever finds this scheduler's own registration. Asking the
    /// container unkeyed would hand every scheduler naming the type one shared instance — the default
    /// scheduler's, when there is one — instead of an instance configured from its own property bag.
    /// A plugin is told which scheduler it extends by <see cref="ISchedulerPlugin.Initialize" />, so
    /// two schedulers sharing one instance means the second initialization overwrites the first.
    /// </para>
    /// <para>
    /// There is deliberately no fallback from the keyed probe to the unkeyed registration: that
    /// fallback <em>is</em> the leak. A plugin instance meant to be shared is said with
    /// <c>AddPlugin&lt;T&gt;(provider =&gt; …)</c> on each scheduler that should have it, which names
    /// what is shared rather than leaving it to whichever scheduler was registered first.
    /// </para>
    /// </remarks>
    private static ISchedulerPlugin Build(IServiceProvider provider, object? schedulerKey, Type type)
    {
        try
        {
            var registered = schedulerKey is null
                ? provider.GetService(type)
                : provider.GetKeyedService(type, schedulerKey);

            return registered as ISchedulerPlugin
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
