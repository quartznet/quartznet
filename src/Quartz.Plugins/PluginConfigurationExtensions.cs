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
        internal XmlSchedulingOptions(IPropertyConfigurer parent) : base(parent)
        {
        }

        public string[] Files
        {
            set => SetProperty("quartz.plugin.xml.fileNames", string.Join(",", value));
        }

        public bool FailOnFileNotFound
        {
            set => SetProperty("quartz.plugin.xml.failOnFileNotFound", value.ToString().ToLowerInvariant());
        }

        public bool FailOnSchedulingError
        {
            set => SetProperty("quartz.plugin.xml.failOnSchedulingError", value.ToString().ToLowerInvariant());
        }

        public TimeSpan ScanInterval
        {
            set => SetProperty("quartz.plugin.xml.scanInterval", ((int) value.TotalSeconds).ToString());
        }
    }
}