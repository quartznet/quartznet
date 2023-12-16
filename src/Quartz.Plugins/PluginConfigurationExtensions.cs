using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Quartz.Plugin.Interrupt;
using Quartz.Plugin.Xml;
using Quartz.Util;

namespace Quartz;

public static class PluginConfigurationExtensions
{
    public static T UseXmlSchedulingConfiguration<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
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
    /// Configures <see cref="JobInterruptMonitorPlugin "/> into use.
    /// </summary>
    public static T UseJobAutoInterrupt<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
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