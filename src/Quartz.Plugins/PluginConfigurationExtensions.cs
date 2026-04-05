using System;
using System.Globalization;

using Quartz.Plugin.History;
using Quartz.Plugin.Interrupt;
using Quartz.Plugin.Json;
using Quartz.Plugin.Xml;
using Quartz.Util;

namespace Quartz;

public static class PluginConfigurationExtensions
{
    public static T UseXmlSchedulingConfiguration<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        T>(
        this T configurer,
        Action<XmlSchedulingOptions> configure) where T : IPropertyConfigurationRoot
    {
        if (configurer is IContainerConfigurationSupport containerConfigurationSupport)
        {
            containerConfigurationSupport.RegisterSingleton<XMLSchedulingDataProcessorPlugin, XMLSchedulingDataProcessorPlugin>();
        }
        configurer.SetProperty("quartz.plugin.xml.type", typeof(XMLSchedulingDataProcessorPlugin).AssemblyQualifiedNameWithoutVersion());
        configure.Invoke(new XmlSchedulingOptions(configurer));
        return configurer;
    }

    /// <summary>
    /// Configures <see cref="StructuredLoggingJobHistoryPlugin"/> into use.
    /// </summary>
    /// <remarks>
    /// This is a structured logging alternative to <see cref="LoggingJobHistoryPlugin"/>.
    /// Message templates use named parameters for compatibility with structured logging sinks.
    /// </remarks>
    public static T UseStructuredJobLogging<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        T>(
        this T configurer) where T : IPropertyConfigurationRoot
    {
        if (configurer is IContainerConfigurationSupport containerConfigurationSupport)
        {
            containerConfigurationSupport.RegisterSingleton<StructuredLoggingJobHistoryPlugin, StructuredLoggingJobHistoryPlugin>();
        }
        configurer.SetProperty("quartz.plugin.structuredJobLogging.type", typeof(StructuredLoggingJobHistoryPlugin).AssemblyQualifiedNameWithoutVersion());
        return configurer;
    }

    /// <summary>
    /// Configures <see cref="StructuredLoggingTriggerHistoryPlugin"/> into use.
    /// </summary>
    /// <remarks>
    /// This is a structured logging alternative to <see cref="LoggingTriggerHistoryPlugin"/>.
    /// Message templates use named parameters for compatibility with structured logging sinks.
    /// </remarks>
    public static T UseStructuredTriggerLogging<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        T>(
        this T configurer) where T : IPropertyConfigurationRoot
    {
        if (configurer is IContainerConfigurationSupport containerConfigurationSupport)
        {
            containerConfigurationSupport.RegisterSingleton<StructuredLoggingTriggerHistoryPlugin, StructuredLoggingTriggerHistoryPlugin>();
        }
        configurer.SetProperty("quartz.plugin.structuredTriggerLogging.type", typeof(StructuredLoggingTriggerHistoryPlugin).AssemblyQualifiedNameWithoutVersion());
        return configurer;
    }

    /// <summary>
    /// Configures <see cref="JsonSchedulingDataProcessorPlugin"/> to load jobs and triggers from JSON file(s).
    /// </summary>
    /// <remarks>
    /// This is the JSON analog of <see cref="UseXmlSchedulingConfiguration{T}"/>.
    /// </remarks>
    public static T UseJsonSchedulingConfiguration<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        T>(
        this T configurer,
        Action<JsonSchedulingOptions> configure) where T : IPropertyConfigurationRoot
    {
        if (configurer is IContainerConfigurationSupport containerConfigurationSupport)
        {
            containerConfigurationSupport.RegisterSingleton<JsonSchedulingDataProcessorPlugin, JsonSchedulingDataProcessorPlugin>();
        }
        configurer.SetProperty("quartz.plugin.json.type", typeof(JsonSchedulingDataProcessorPlugin).AssemblyQualifiedNameWithoutVersion());
        configure.Invoke(new JsonSchedulingOptions(configurer));
        return configurer;
    }

    /// <summary>
    /// Configures <see cref="JobInterruptMonitorPlugin "/> into use.
    /// </summary>
    public static T UseJobAutoInterrupt<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        T>(
        this T configurer,
        Action<JobAutoInterruptOptions>? configure = null) where T : IPropertyConfigurationRoot
    {
        if (configurer is IContainerConfigurationSupport containerConfigurationSupport)
        {
            containerConfigurationSupport.RegisterSingleton<JobInterruptMonitorPlugin, JobInterruptMonitorPlugin>();
        }
        configurer.SetProperty("quartz.plugin.jobAutoInterrupt.type", typeof(JobInterruptMonitorPlugin).AssemblyQualifiedNameWithoutVersion());
        configure?.Invoke(new JobAutoInterruptOptions(configurer));
        return configurer;
    }
        
}

public class JobAutoInterruptOptions : PropertiesSetter
{
    public JobAutoInterruptOptions(IPropertySetter parent) : base(parent, "quartz.plugin.jobAutoInterrupt")
    {
    }

    /// <summary>
    /// The amount of time the job is allowed to run before job interruption is signaled.
    /// Defaults to 5 minutes.
    /// </summary>
    /// <remarks>
    /// Per-job value can be configured via JobDataMap via key <see cref="JobInterruptMonitorPlugin.JobDataMapKeyMaxRunTime"/>.
    /// </remarks>
    public TimeSpan DefaultMaxRunTime
    {
        set => SetProperty("defaultMaxRunTime", value.TotalMilliseconds.ToString(CultureInfo.InvariantCulture));
    }
}

public class XmlSchedulingOptions : PropertiesSetter
{
    internal XmlSchedulingOptions(IPropertySetter parent) : base(parent, "quartz.plugin.xml")
    {
    }

    public string[] Files
    {
        set => SetProperty("fileNames", string.Join(",", value));
    }

    public bool FailOnFileNotFound
    {
        set => SetProperty("failOnFileNotFound", value.ToString().ToLowerInvariant());
    }

    public bool FailOnSchedulingError
    {
        set => SetProperty("failOnSchedulingError", value.ToString().ToLowerInvariant());
    }

    public TimeSpan ScanInterval
    {
        set => SetProperty("scanInterval", ((int) value.TotalSeconds).ToString());
    }
}

/// <summary>
/// Configuration options for <see cref="JsonSchedulingDataProcessorPlugin"/>.
/// </summary>
public sealed class JsonSchedulingOptions : PropertiesSetter
{
    internal JsonSchedulingOptions(IPropertySetter parent) : base(parent, "quartz.plugin.json")
    {
    }

    /// <summary>
    /// JSON file(s) to read job and trigger definitions from. Default is "quartz_jobs.json".
    /// </summary>
    public string[] Files
    {
        set => SetProperty("fileNames", string.Join(",", value));
    }

    /// <summary>
    /// Whether initialization should fail if the file cannot be found. Default is <c>true</c>.
    /// </summary>
    public bool FailOnFileNotFound
    {
        set => SetProperty("failOnFileNotFound", value.ToString().ToLowerInvariant());
    }

    /// <summary>
    /// Whether starting should fail if the file cannot be processed. Default is <c>false</c>.
    /// </summary>
    public bool FailOnSchedulingError
    {
        set => SetProperty("failOnSchedulingError", value.ToString().ToLowerInvariant());
    }

    /// <summary>
    /// The interval at which to scan for changes to the file. Default is <see cref="TimeSpan.Zero"/> (disabled).
    /// </summary>
    public TimeSpan ScanInterval
    {
        set => SetProperty("scanInterval", ((int) value.TotalSeconds).ToString());
    }
}