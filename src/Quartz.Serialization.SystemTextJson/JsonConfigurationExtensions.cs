using System.Text.Json;

using Quartz.Calendars;
using Quartz.Converters;
using Quartz.Simpl;

namespace Quartz;

public static class JsonConfigurationExtensions
{
    /// <summary>
    /// Use Newtonsoft JSON as data serialization strategy.
    /// </summary>
    public static void UseSystemTextJsonSerializer(
        this SchedulerBuilder.PersistentStoreOptions persistentStoreOptions,
        Action<SystemTextJsonSerializerOptions>? configure = null)
    {
        var options = new SystemTextJsonSerializerOptions();
        configure?.Invoke(options);
        persistentStoreOptions.UseSerializer<SystemTextJsonObjectSerializer>();
    }

    public static void AddQuartzConverters(this JsonSerializerOptions options)
    {
        options.Converters.Add(new CalendarConverter());
        options.Converters.Add(new CronExpressionConverter());
        options.Converters.Add(new JobDataMapConverter());
        options.Converters.Add(new KeyConverter<JobKey>());
        options.Converters.Add(new KeyConverter<TriggerKey>());
        options.Converters.Add(new NameValueCollectionConverter());
        options.Converters.Add(new TriggerConverter());
    }
}

public class SystemTextJsonSerializerOptions
{
    public void AddCalendarSerializer<TCalendar>(ICalendarSerializer serializer)
    {
        SystemTextJsonObjectSerializer.AddCalendarSerializer<TCalendar>(serializer);
    }
}