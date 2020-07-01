using System;

using Quartz.Plugin.Xml;
using Quartz.Util;

namespace Quartz
{
    public static class PluginConfigurationExtensions
    {
        public static SchedulerBuilder UseXmlSchedulingConfiguration(
            this SchedulerBuilder schedulerBuilder,
            Action<XmlSchedulingOptions> configure)
        {
            schedulerBuilder.SetProperty("quartz.plugin.xml.type", typeof(XMLSchedulingDataProcessorPlugin).AssemblyQualifiedNameWithoutVersion());
            configure.Invoke(new XmlSchedulingOptions(schedulerBuilder));
            return schedulerBuilder;
        }

    }

    public class XmlSchedulingOptions : PropertiesHolder
    {
        public XmlSchedulingOptions(PropertiesHolder parent) : base(parent)
        {
        }

        public XmlSchedulingOptions SetFiles(params string[] fileNames)
        {
            SetProperty("quartz.plugin.xml.fileNames", string.Join(",", fileNames));
            return this;
        }

        public XmlSchedulingOptions SetFailOnFileNotFound(bool fail = true)
        {
            SetProperty("quartz.plugin.xml.failOnFileNotFound", fail.ToString().ToLowerInvariant());
            return this;
        }

        public XmlSchedulingOptions SetFailOnSchedulingError(bool fail = true)
        {
            SetProperty("quartz.plugin.xml.failOnSchedulingError", fail.ToString().ToLowerInvariant());
            return this;
        }

        public XmlSchedulingOptions SetScanInterval(TimeSpan interval)
        {
            SetProperty("quartz.plugin.xml.scanInterval", ((int) interval.TotalSeconds).ToString());
            return this;
        }
    }
}