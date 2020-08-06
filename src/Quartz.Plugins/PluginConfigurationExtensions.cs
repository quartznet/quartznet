using System;

using Quartz.Plugin.Xml;
using Quartz.Util;

namespace Quartz
{
    public static class PluginConfigurationExtensions
    {
        public static T UseXmlSchedulingConfiguration<T>(
            this T configurer,
            Action<XmlSchedulingOptions> configure) where T : IPropertyConfigurer
        {
            configurer.SetProperty("quartz.plugin.xml.type", typeof(XMLSchedulingDataProcessorPlugin).AssemblyQualifiedNameWithoutVersion());
            configure.Invoke(new XmlSchedulingOptions(configurer));
            return configurer;
        }

    }

    public class XmlSchedulingOptions : PropertiesHolder
    {
        internal XmlSchedulingOptions(IPropertyConfigurer parent) : base(parent, "quartz.plugin.xml")
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
}