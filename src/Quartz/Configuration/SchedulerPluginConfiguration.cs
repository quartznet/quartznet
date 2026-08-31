using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Extensibility;

namespace Quartz.Configuration;

/// <summary>
/// What a plugin added in code was configured with, kept as something that can be applied later.
/// </summary>
/// <remarks>
/// Registered rather than baked into the plugin's construction, because <em>when</em> it is applied is
/// the whole point: <see cref="SchedulerPluginFactory" /> applies these after the flat
/// <c>quartz.plugin.&lt;name&gt;.*</c> keys, which is what makes configuration written in code beat the
/// same setting written as a string.
/// </remarks>
/// <param name="SchedulerName">The scheduler the plugin belongs to; empty for the default scheduler.</param>
/// <param name="PluginType">The plugin type this configures.</param>
/// <param name="Apply">Applies the configuration to one instance of that type.</param>
internal sealed record SchedulerPluginConfiguration(
    string SchedulerName,
    Type PluginType,
    Action<ISchedulerPlugin, IServiceProvider> Apply);

/// <summary>
/// Registers a plugin shipped with Quartz together with the typed options it is configured from.
/// </summary>
/// <remarks>
/// Internal, and used by the plugin packages through <c>InternalsVisibleTo</c>: it is the shape every
/// <c>Use*</c> extension has, not a mechanism a third party needs. What it is built from is public —
/// <c>AddPlugin&lt;T, TOptions&gt;</c> and <c>ConfigureOptions&lt;TOptions&gt;</c> — and a plugin
/// outside Quartz says the same thing with those.
/// </remarks>
internal static class ConfiguredPluginExtensions
{
    /// <summary>
    /// Adds a plugin the container builds, and configures it from <typeparamref name="TOptions" /> once
    /// the string keys naming it have been applied.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The options are the scheduler's own named options, so anything that configures named options
    /// reaches the plugin — <c>services.Configure&lt;TOptions&gt;(configuration.GetSection(…))</c> most
    /// of all. That is what the closure this replaced could not do: it built an options object of its
    /// own, which no configuration source could ever reach.
    /// </para>
    /// <para>
    /// <paramref name="configure" /> is applied to this registration's own instance of the options
    /// rather than being registered as another <c>IConfigureOptions&lt;TOptions&gt;</c>. Two shipped
    /// plugins can share an options type — the XML and JSON scheduling processors both take
    /// <c>FileSchedulingOptions</c> — and a callback registered against the type would be applied to
    /// both of them, which is how one processor would come to be handed the other's files. It still
    /// runs after the values bound from configuration, so code beats strings here as everywhere else.
    /// </para>
    /// </remarks>
    /// <typeparam name="TPlugin">The plugin's type.</typeparam>
    /// <typeparam name="TOptions">The plugin's options type.</typeparam>
    /// <param name="builder">The scheduler being configured.</param>
    /// <param name="name">The name the scheduler knows the plugin by, and the name its flat keys use.</param>
    /// <param name="configure">Configures the options, over whatever configuration bound onto them.</param>
    /// <param name="apply">Copies the options onto the plugin.</param>
    internal static IQuartzBuilder AddConfiguredPlugin<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] TPlugin,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] TOptions>(
        this IQuartzBuilder builder,
        string name,
        Action<TOptions>? configure,
        Action<TPlugin, TOptions> apply)
        where TPlugin : class, ISchedulerPlugin
        where TOptions : class
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddPlugin<TPlugin, TOptions>(name: name);

        string optionsName = builder.SchedulerName;
        builder.Services.AddSingleton(new SchedulerPluginConfiguration(
            optionsName,
            typeof(TPlugin),
            (plugin, provider) =>
            {
                TOptions options = provider.GetRequiredService<IOptionsFactory<TOptions>>().Create(optionsName);
                configure?.Invoke(options);
                apply((TPlugin) plugin, options);
            }));

        return builder;
    }
}
