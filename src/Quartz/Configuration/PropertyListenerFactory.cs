using System.Collections.Specialized;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl;
using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Configuration;

/// <summary>
/// Produces the job and trigger listeners named by the <c>quartz.jobListener.*</c> and
/// <c>quartz.triggerListener.*</c> property keys.
/// </summary>
/// <remarks>
/// <para>
/// Listeners added in code are registered as services and carry their own matchers. These are the ones
/// named by configuration instead, which have no matchers to carry and therefore listen to everything —
/// the same shape the properties format has always had.
/// </para>
/// <para>
/// As with plugins, a listener named by configuration is still constructed through the container, so it
/// gets constructor injection; only the leftover <c>&lt;prefix&gt;.&lt;name&gt;.&lt;property&gt;</c>
/// values are applied by reflection afterwards.
/// </para>
/// </remarks>
internal static class PropertyListenerFactory
{
    private static readonly SimpleTypeLoader typeLoader = new();

    /// <summary>
    /// Creates the listeners of one kind, paired with the names they were configured under.
    /// </summary>
    /// <param name="provider">The container, used to resolve or construct each listener.</param>
    /// <param name="properties">The flat properties that may name listeners.</param>
    /// <param name="prefix">
    /// The property prefix to read, <see cref="LegacyPropertyKeys.JobListenerPrefix"/> or
    /// <see cref="LegacyPropertyKeys.TriggerListenerPrefix"/>.
    /// </param>
    public static List<TListener> Create<TListener>(
        IServiceProvider provider,
        NameValueCollection properties,
        string prefix) where TListener : class
    {
        var listeners = new List<TListener>();
        var start = prefix + ".";
        var loader = provider.GetService<ITypeLoader>() ?? typeLoader;

        foreach (var name in Names(properties, start))
        {
            var listenerProperties = Group(properties, start + name + ".");
            var typeName = listenerProperties[LegacyPropertyKeys.ListenerType];
            if (string.IsNullOrWhiteSpace(typeName))
            {
                Throw.SchedulerException($"Listener type not specified for listener '{name}'");
            }

            var type = loader.LoadType(typeName);
            if (type is null)
            {
                Throw.SchedulerException($"Listener of type '{typeName}' could not be loaded.");
                continue;
            }

            if (!typeof(TListener).IsAssignableFrom(type))
            {
                Throw.SchedulerException($"Listener '{name}' of type '{typeName}' does not implement {typeof(TListener).Name}.");
                continue;
            }

            TListener listener;
            try
            {
                listener = (TListener) (provider.GetService(type) ?? ActivatorUtilities.CreateInstance(provider, type));
            }
            catch (Exception e)
            {
                Throw.SchedulerException($"Listener of type '{typeName}' could not be instantiated.", e);
                continue;
            }

            listenerProperties.Remove(LegacyPropertyKeys.ListenerType);
            NameListener(listener, name, listenerProperties);

            if (listenerProperties.Count > 0)
            {
                try
                {
                    PropertyBinder.SetObjectProperties(listener, listenerProperties);
                }
                catch (Exception e)
                {
                    Throw.SchedulerException($"Listener '{typeName}' properties could not be configured.", e);
                }
            }

            listeners.Add(listener);
        }

        return listeners;
    }

    /// <summary>
    /// A listener is known to the listener manager by its name, and the name it was configured under is
    /// the only one the configuration gives it.
    /// </summary>
    private static void NameListener(object listener, string name, NameValueCollection listenerProperties)
    {
        if (listenerProperties["Name"] is not null || listenerProperties["name"] is not null)
        {
            return;
        }

        var nameProperty = listener.GetType().GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
        if (nameProperty is not null && nameProperty.CanWrite)
        {
            listenerProperties["Name"] = name;
        }
    }

    private static HashSet<string> Names(NameValueCollection properties, string start)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
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

    private static NameValueCollection Group(NameValueCollection properties, string start)
    {
        var group = new NameValueCollection();
        foreach (var key in properties.AllKeys)
        {
            if (key is not null && key.StartsWith(start, StringComparison.Ordinal))
            {
                group[key[start.Length..]] = properties[key];
            }
        }

        return group;
    }
}
