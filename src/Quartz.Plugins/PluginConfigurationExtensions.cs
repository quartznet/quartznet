using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Plugins.History;
using Quartz.Plugins.Interrupt;
using Quartz.Plugins.Json;
using Quartz.Plugins.Management;
using Quartz.Plugins.Xml;
using Quartz.Extensibility;

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
        Action<FileSchedulingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new FileSchedulingOptions();
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
        Action<FileSchedulingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new FileSchedulingOptions();
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
    /// Logs job execution history using classic numbered format strings.
    /// </summary>
    /// <remarks>
    /// <see cref="UseStructuredJobLogging" /> is the better default; this one exists for deployments
    /// whose log pipeline expects the 3.x message shape.
    /// </remarks>
    public static IQuartzBuilder UseJobHistoryLogging(
        this IQuartzBuilder builder,
        Action<JobHistoryLoggingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        JobHistoryLoggingOptions options = new JobHistoryLoggingOptions();
        configure?.Invoke(options);

        return builder.AddConfiguredPlugin<LoggingJobHistoryPlugin>("jobHistory", plugin =>
        {
            Apply(options.JobSuccessMessage, value => plugin.JobSuccessMessage = value);
            Apply(options.JobFailedMessage, value => plugin.JobFailedMessage = value);
            Apply(options.JobToBeFiredMessage, value => plugin.JobToBeFiredMessage = value);
            Apply(options.JobWasVetoedMessage, value => plugin.JobWasVetoedMessage = value);
        });
    }

    /// <summary>
    /// Logs trigger firing history using classic numbered format strings.
    /// </summary>
    /// <remarks>
    /// <see cref="UseStructuredTriggerLogging" /> is the better default; this one exists for
    /// deployments whose log pipeline expects the 3.x message shape.
    /// </remarks>
    public static IQuartzBuilder UseTriggerHistoryLogging(
        this IQuartzBuilder builder,
        Action<TriggerHistoryLoggingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        TriggerHistoryLoggingOptions options = new TriggerHistoryLoggingOptions();
        configure?.Invoke(options);

        return builder.AddConfiguredPlugin<LoggingTriggerHistoryPlugin>("triggerHistory", plugin =>
        {
            Apply(options.TriggerFiredMessage, value => plugin.TriggerFiredMessage = value);
            Apply(options.TriggerMisfiredMessage, value => plugin.TriggerMisfiredMessage = value);
            Apply(options.TriggerCompleteMessage, value => plugin.TriggerCompleteMessage = value);
        });
    }

    /// <summary>
    /// Shuts the scheduler down when the process exits.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="cleanShutdown">Whether to wait for executing jobs to finish first.</param>
    public static IQuartzBuilder UseShutdownHook(this IQuartzBuilder builder, bool cleanShutdown = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddConfiguredPlugin<ShutdownHookPlugin>(
            "shutdownHook", plugin => plugin.CleanShutdown = cleanShutdown);
    }

    /// <summary>
    /// Applies a message template only when the caller set one, so the plugin keeps its own default
    /// rather than a copy of it that can drift.
    /// </summary>
    private static void Apply(string? value, Action<string> set)
    {
        if (value is not null)
        {
            set(value);
        }
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
        return builder.AddPlugin<TPlugin>(
            provider =>
            {
                var plugin = ActivatorUtilities.CreateInstance<TPlugin>(provider);
                configure(plugin);
                return plugin;
            },
            name);
    }
}

/// <summary>
/// Message templates for the classic job history logging plugin.
/// </summary>
/// <remarks>
/// The plugin's own <c>Name</c> is not configurable here: the scheduler assigns it the registration
/// name when it initialises the plugin, so anything set for it would be discarded.
/// </remarks>
public sealed class JobHistoryLoggingOptions
{
    /// <summary>Overrides the plugin's own template for a job completes successfully.</summary>
    public string? JobSuccessMessage { get; set; }

    /// <summary>Overrides the plugin's own template for a job throws.</summary>
    public string? JobFailedMessage { get; set; }

    /// <summary>Overrides the plugin's own template for a job is about to fire.</summary>
    public string? JobToBeFiredMessage { get; set; }

    /// <summary>Overrides the plugin's own template for a trigger listener vetoes a job.</summary>
    public string? JobWasVetoedMessage { get; set; }
}

/// <summary>
/// Message templates for the classic trigger history logging plugin.
/// </summary>
/// <remarks>
/// The plugin's own <c>Name</c> is not configurable here: the scheduler assigns it the registration
/// name when it initialises the plugin, so anything set for it would be discarded.
/// </remarks>
public sealed class TriggerHistoryLoggingOptions
{
    /// <summary>Overrides the plugin's own template for a trigger fires.</summary>
    public string? TriggerFiredMessage { get; set; }

    /// <summary>Overrides the plugin's own template for a trigger misfires.</summary>
    public string? TriggerMisfiredMessage { get; set; }

    /// <summary>Overrides the plugin's own template for a trigger completes.</summary>
    public string? TriggerCompleteMessage { get; set; }
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
/// Configuration for loading the schedule from files, in either XML or JSON form.
/// </summary>
public sealed class FileSchedulingOptions
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
