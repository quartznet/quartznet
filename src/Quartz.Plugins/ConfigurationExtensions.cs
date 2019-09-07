using System;

using Quartz.Plugin.Xml;
using Quartz.Util;

namespace Quartz
{
    public static class ConfigurationExtensions
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

        public XmlSchedulingOptions WithFiles(params string[] fileNames)
        {
            SetProperty("quartz.plugin.xml.fileNames", string.Join(",", fileNames));
            return this;
        }

        public XmlSchedulingOptions FailOnFileNotFound(bool fail = true)
        {
            SetProperty("quartz.plugin.xml.failOnFileNotFound", fail.ToString().ToLowerInvariant());
            return this;
        }

        public XmlSchedulingOptions FailOnSchedulingError(bool fail = true)
        {
            SetProperty("quartz.plugin.xml.failOnSchedulingError", fail.ToString().ToLowerInvariant());
            return this;
        }

        public XmlSchedulingOptions WithChangeDetection(TimeSpan interval)
        {
            SetProperty("quartz.plugin.xml.scanInterval", ((int) interval.TotalSeconds).ToString());
            return this;
        }
    }
}