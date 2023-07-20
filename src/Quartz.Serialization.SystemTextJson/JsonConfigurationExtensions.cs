using System.Text.Json;

using Quartz.Converters;

namespace Quartz;

public static class JsonConfigurationExtensions
{
    public static void AddQuartzConverters(this JsonSerializerOptions options)
    {
        options.Converters.Add(new CalendarConverter());
        options.Converters.Add(new TriggerConverter());
        options.Converters.Add(new JobDataMapConverter());
    }
}