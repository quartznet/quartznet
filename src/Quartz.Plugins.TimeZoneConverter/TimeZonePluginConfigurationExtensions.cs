using Quartz.Plugin.TimeZoneConverter;
using Quartz.Util;

namespace Quartz
{
    public static class TimeZonePluginConfigurationExtensions
    {
        public static SchedulerBuilder UseTimeZoneConverter(this SchedulerBuilder schedulerBuilder)
        {
            schedulerBuilder.SetProperty("quartz.plugin.timeZoneConverter.type", typeof(TimeZoneConverterPlugin).AssemblyQualifiedNameWithoutVersion());
            return schedulerBuilder;
        }
    }
}