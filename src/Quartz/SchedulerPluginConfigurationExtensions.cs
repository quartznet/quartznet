using System.Diagnostics.CodeAnalysis;

using Quartz.Impl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz;

/// <summary>
/// Extension methods for registering <see cref="ISchedulerPlugin"/> implementations against
/// any configuration root (<see cref="SchedulerBuilder"/> or the Microsoft DI configurator).
/// These allow third-party plugin authors to create strongly typed configuration extension
/// methods that behave exactly like the built-in ones.
/// </summary>
public static class SchedulerPluginConfigurationExtensions
{
    /// <summary>
    /// Configures the scheduler plugin <typeparamref name="TPlugin"/> into use by setting
    /// the <c>quartz.plugin.{name}.type</c> property.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When the configurer is backed by a Microsoft DI service collection (the <c>AddQuartz</c>
    /// configuration path), the plugin type is also registered as a singleton into the container
    /// and will be constructed via dependency injection, allowing constructor-injected services.
    /// </para>
    /// <para>
    /// When the configurer is a plain <see cref="SchedulerBuilder"/> (property-based configuration),
    /// the plugin is instantiated via reflection and must have a public parameterless constructor.
    /// In both cases remaining <c>quartz.plugin.{name}.*</c> properties are applied to public
    /// setters of the plugin instance.
    /// </para>
    /// <para>
    /// A typical plugin configuration extension method looks like:
    /// <code>
    /// public static T UseMyPlugin&lt;T&gt;(this T configurer, Action&lt;MyPluginOptions&gt;? configure = null)
    ///     where T : IPropertyConfigurationRoot
    /// {
    ///     configurer.UsePlugin&lt;MyPlugin&gt;("myPlugin");
    ///     configure?.Invoke(new MyPluginOptions(configurer));
    ///     return configurer;
    /// }
    /// </code>
    /// where <c>MyPluginOptions</c> derives from <see cref="PropertiesSetter"/> with prefix
    /// <c>quartz.plugin.myPlugin</c>.
    /// </para>
    /// </remarks>
    /// <typeparam name="TPlugin">The plugin type to configure.</typeparam>
    /// <param name="configurer">The configuration root to register the plugin against.</param>
    /// <param name="name">
    /// The plugin name, used as the <c>{name}</c> part of the <c>quartz.plugin.{name}.type</c>
    /// property key. Must not contain '.' characters.
    /// </param>
    /// <returns>The configurer for chaining.</returns>
    public static IPropertyConfigurationRoot UsePlugin<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] TPlugin>(
        this IPropertyConfigurationRoot configurer,
        string name)
        where TPlugin : class, ISchedulerPlugin
    {
        ArgumentNullException.ThrowIfNull(configurer);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Plugin name cannot be null or empty", nameof(name));
        }
        if (name.Contains('.'))
        {
            // a dot would split the property key into an unintended plugin name and property path
            throw new ArgumentException("Plugin name cannot contain '.' characters", nameof(name));
        }

        if (configurer is IContainerConfigurationSupport containerConfigurationSupport)
        {
            containerConfigurationSupport.RegisterSingleton<TPlugin, TPlugin>();
        }
        configurer.SetProperty(StdSchedulerFactory.PropertyPluginPrefix + "." + name + "." + StdSchedulerFactory.PropertyPluginType, typeof(TPlugin).AssemblyQualifiedNameWithoutVersion());
        return configurer;
    }

    /// <summary>
    /// Registers a singleton service into the container backing the configurer, when one is present.
    /// Intended for plugin configuration extension methods that need companion services available
    /// for constructor injection.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns <see langword="false"/> when the configurer is a plain property-based configuration
    /// source such as <see cref="SchedulerBuilder"/> without container support; no registration
    /// takes place in that case.
    /// </para>
    /// <para>
    /// Registration uses add semantics rather than try-add semantics: registering the same service
    /// type twice results in two registrations, with the last one winning when a single instance
    /// is resolved.
    /// </para>
    /// </remarks>
    /// <typeparam name="TService">The service type to register.</typeparam>
    /// <typeparam name="TImplementation">The implementation type to register.</typeparam>
    /// <param name="configurer">The configuration root to register the service against.</param>
    /// <returns><see langword="true"/> when the service was registered into a container; otherwise <see langword="false"/>.</returns>
    public static bool TryRegisterSingleton<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] TImplementation>(
        this IPropertyConfigurationRoot configurer)
        where TService : class
        where TImplementation : class, TService
    {
        ArgumentNullException.ThrowIfNull(configurer);

        if (configurer is IContainerConfigurationSupport containerConfigurationSupport)
        {
            containerConfigurationSupport.RegisterSingleton<TService, TImplementation>();
            return true;
        }
        return false;
    }
}
