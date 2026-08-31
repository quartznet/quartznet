using Quartz.Configuration;
using Quartz.Plugins.History;
using Quartz.Plugins.Json;
using Quartz.Plugins.Xml;

namespace Quartz;

/// <summary>
/// Adds the plugins shipped in <c>Quartz.Plugins</c> to a scheduler.
/// </summary>
/// <remarks>
/// <para>
/// Each plugin is registered as an ordinary service, constructed by the container so it gets
/// constructor injection, and then configured from typed options. No plugin is named by a string.
/// </para>
/// <para>
/// The options are the scheduler's own named options, so a plugin is configurable from
/// <c>appsettings.json</c> like anything else the container builds:
/// <c>services.Configure&lt;FileSchedulingOptions&gt;(configuration.GetSection("…"))</c> reaches the
/// plugin, and the callback passed here is applied over whatever bound onto them. The flat
/// <c>quartz.plugin.&lt;name&gt;.*</c> keys still work and are applied first, so configuration written
/// in code beats the same setting written as a string.
/// </para>
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

        return builder.AddConfiguredPlugin<XmlSchedulingDataProcessorPlugin, FileSchedulingOptions>(
            "xml", configure, static (plugin, options) =>
            {
                // Left unset, the plugin keeps its own default file name rather than being handed an
                // empty one, which it would try to open as a path.
                if (options.Files.Count > 0)
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

        return builder.AddConfiguredPlugin<JsonSchedulingDataProcessorPlugin, FileSchedulingOptions>(
            "json", configure, static (plugin, options) =>
            {
                if (options.Files.Count > 0)
                {
                    plugin.FileNames = string.Join(",", options.Files);
                }

                plugin.FailOnFileNotFound = options.FailOnFileNotFound;
                plugin.FailOnSchedulingError = options.FailOnSchedulingError;
                plugin.ScanInterval = options.ScanInterval;
            });
    }

    /// <summary>
    /// Logs job execution history using structured message templates.
    /// </summary>
    /// <remarks>
    /// The templates name their values — <c>{JobGroup}</c>, <c>{FireTime}</c> — so a structured log
    /// pipeline can index them, which is why this is the better default. They are overridable for the
    /// same reason the numbered ones are.
    /// </remarks>
    public static IQuartzBuilder UseStructuredJobLogging(
        this IQuartzBuilder builder,
        Action<JobHistoryLoggingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddConfiguredPlugin<StructuredLoggingJobHistoryPlugin, JobHistoryLoggingOptions>(
            "structuredJobHistory", configure, static (plugin, options) =>
            {
                Apply(options.JobSuccessMessage, value => plugin.JobSuccessMessage = value);
                Apply(options.JobFailedMessage, value => plugin.JobFailedMessage = value);
                Apply(options.JobToBeFiredMessage, value => plugin.JobToBeFiredMessage = value);
                Apply(options.JobWasVetoedMessage, value => plugin.JobWasVetoedMessage = value);
            });
    }

    /// <summary>
    /// Logs trigger firing history using structured message templates.
    /// </summary>
    /// <inheritdoc cref="UseStructuredJobLogging" path="/remarks" />
    public static IQuartzBuilder UseStructuredTriggerLogging(
        this IQuartzBuilder builder,
        Action<TriggerHistoryLoggingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddConfiguredPlugin<StructuredLoggingTriggerHistoryPlugin, TriggerHistoryLoggingOptions>(
            "structuredTriggerHistory", configure, static (plugin, options) =>
            {
                Apply(options.TriggerFiredMessage, value => plugin.TriggerFiredMessage = value);
                Apply(options.TriggerMisfiredMessage, value => plugin.TriggerMisfiredMessage = value);
                Apply(options.TriggerCompleteMessage, value => plugin.TriggerCompleteMessage = value);
            });
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

        return builder.AddConfiguredPlugin<LoggingJobHistoryPlugin, JobHistoryLoggingOptions>(
            "jobHistory", configure, static (plugin, options) =>
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

        return builder.AddConfiguredPlugin<LoggingTriggerHistoryPlugin, TriggerHistoryLoggingOptions>(
            "triggerHistory", configure, static (plugin, options) =>
            {
                Apply(options.TriggerFiredMessage, value => plugin.TriggerFiredMessage = value);
                Apply(options.TriggerMisfiredMessage, value => plugin.TriggerMisfiredMessage = value);
                Apply(options.TriggerCompleteMessage, value => plugin.TriggerCompleteMessage = value);
            });
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
/// Configuration for loading the schedule from files, in either XML or JSON form.
/// </summary>
public sealed class FileSchedulingOptions
{
    /// <summary>
    /// The files to load the schedule from.
    /// </summary>
    /// <remarks>
    /// Get-only with an in-place initializer, like <c>QuartzOptions.Properties</c>: a configuration
    /// binder binds into a non-null collection without needing a setter, and one <c>configure</c>
    /// callback cannot discard what another added.
    /// </remarks>
    public List<string> Files { get; } = [];

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
