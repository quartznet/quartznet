using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Plugin.History;
using Quartz.Plugin.Interrupt;
using Quartz.Plugin.Json;
using Quartz.Plugin.Xml;
using Quartz.Spi;

namespace Quartz;

/// <summary>
/// Adds the plugins shipped in <c>Quartz.Plugins</c> to a scheduler.
/// </summary>
/// <remarks>
/// Each plugin is registered as an ordinary service, constructed by the container so it gets
/// constructor injection, and then configured from typed options. No plugin is named by a string.
/// </remarks>
public static class PluginConfigurationExtensions
{
    /// <summary>
    /// Loads jobs and triggers from XML files, optionally rescanning them for changes.
    /// </summary>
    public static IQuartzBuilder UseXmlSchedulingConfiguration(
        this IQuartzBuilder builder,
        Action<XmlSchedulingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new XmlSchedulingOptions();
        configure(options);

        return builder.AddConfiguredPlugin<XMLSchedulingDataProcessorPlugin>("xml", plugin =>
        {
            // Left unset, the plugin keeps its own default file name rather than being handed an empty
            // one, which it would try to open as a path.
            if (options.Files.Length > 0)
            {
                plugin.FileNames = string.Join(",", options.Files);
            }

            plugin.FailOnFileNotFound = options.FailOnFileNotFound;
            plugin.FailOnSchedulingError = options.FailOnSchedulingError;
            plugin.ScanInterval = options.ScanInterval;
        });
    }

    /// <summary>
    /// Loads jobs and triggers from JSON files, optionally rescanning them for changes.
    /// </summary>
    public static IQuartzBuilder UseJsonSchedulingConfiguration(
        this IQuartzBuilder builder,
        Action<JsonSchedulingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new JsonSchedulingOptions();
        configure(options);

        return builder.AddConfiguredPlugin<JsonSchedulingDataProcessorPlugin>("json", plugin =>
        {
            if (options.Files.Length > 0)
            {
                plugin.FileNames = string.Join(",", options.Files);
            }

            plugin.FailOnFileNotFound = options.FailOnFileNotFound;
            plugin.FailOnSchedulingError = options.FailOnSchedulingError;
            plugin.ScanInterval = options.ScanInterval;
        });
    }

    /// <summary>
    /// Signals cancellation to jobs that have run longer than they are allowed to.
    /// </summary>
    public static IQuartzBuilder UseJobAutoInterrupt(
        this IQuartzBuilder builder,
        Action<JobAutoInterruptOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new JobAutoInterruptOptions();
        configure?.Invoke(options);

        return builder.AddConfiguredPlugin<JobInterruptMonitorPlugin>(
            "jobAutoInterrupt", plugin => plugin.DefaultMaxRunTime = options.DefaultMaxRunTime);
    }

    /// <summary>
    /// Logs job execution history using structured message templates.
    /// </summary>
    public static IQuartzBuilder UseStructuredJobLogging(this IQuartzBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddPlugin<StructuredLoggingJobHistoryPlugin>();
    }

    /// <summary>
    /// Logs trigger firing history using structured message templates.
    /// </summary>
    public static IQuartzBuilder UseStructuredTriggerLogging(this IQuartzBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddPlugin<StructuredLoggingTriggerHistoryPlugin>();
    }

    /// <summary>
    /// Registers a plugin that the container constructs and the caller then configures.
    /// </summary>
    private static IQuartzBuilder AddConfiguredPlugin<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] TPlugin>(
        this IQuartzBuilder builder,
        string name,
        Action<TPlugin> configure) where TPlugin : class, ISchedulerPlugin
    {
        return builder.AddPlugin(name, provider =>
        {
            var plugin = ActivatorUtilities.CreateInstance<TPlugin>(provider);
            configure(plugin);
            return plugin;
        });
    }
}

/// <summary>
/// Configuration for the job auto-interrupt plugin.
/// </summary>
public sealed class JobAutoInterruptOptions
{
    /// <summary>
    /// How long a job may run before cancellation is signalled to it.
    /// </summary>
    /// <remarks>
    /// A per-job value can be set through the job data map under
    /// <see cref="JobInterruptMonitorPlugin.JobDataMapKeyMaxRunTime"/>.
    /// </remarks>
    public TimeSpan DefaultMaxRunTime { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Configuration for loading the schedule from XML files.
/// </summary>
public sealed class XmlSchedulingOptions
{
    /// <summary>
    /// The files to load the schedule from.
    /// </summary>
    public string[] Files { get; set; } = [];

    /// <summary>
    /// Whether a missing file is an error rather than something to ignore.
    /// </summary>
    public bool FailOnFileNotFound { get; set; } = true;

    /// <summary>
    /// Whether a scheduling error while loading is fatal.
    /// </summary>
    public bool FailOnSchedulingError { get; set; }

    /// <summary>
    /// How often the files are rescanned for changes. Zero means they are read once.
    /// </summary>
    public TimeSpan ScanInterval { get; set; } = TimeSpan.Zero;
}

/// <summary>
/// Configuration for loading the schedule from JSON files.
/// </summary>
public sealed class JsonSchedulingOptions
{
    /// <summary>
    /// The files to load the schedule from.
    /// </summary>
    public string[] Files { get; set; } = [];

    /// <summary>
    /// Whether a missing file is an error rather than something to ignore.
    /// </summary>
    public bool FailOnFileNotFound { get; set; } = true;

    /// <summary>
    /// Whether a scheduling error while loading is fatal.
    /// </summary>
    public bool FailOnSchedulingError { get; set; }

    /// <summary>
    /// How often the files are rescanned for changes. Zero means they are read once.
    /// </summary>
    public TimeSpan ScanInterval { get; set; } = TimeSpan.Zero;
}
