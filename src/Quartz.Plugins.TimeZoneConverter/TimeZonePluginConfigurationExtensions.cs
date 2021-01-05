using Quartz.Plugin.TimeZoneConverter;
using Quartz.Util;

namespace Quartz
{
    public static class TimeZonePluginConfigurationExtensions
    {
        public static T UseTimeZoneConverter<T>(this T schedulerBuilder) where T : IPropertySetter
        {
            schedulerBuilder.SetProperty("quartz.plugin.timeZoneConverter.type", typeof(TimeZoneConverterPlugin).AssemblyQualifiedNameWithoutVersion());
            return schedulerBuilder;
        }
    }
}
