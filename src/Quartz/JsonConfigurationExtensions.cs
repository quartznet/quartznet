using System.Text.Json;

using Quartz.Serialization.Json.Calendars;
using Quartz.Serialization.Json.Converters;
using Quartz.Serialization.Json.Triggers;
using Quartz.Simpl;

namespace Quartz;

public static class JsonConfigurationExtensions
{
    /// <summary>
    /// Use System.Text.Json as data serialization strategy.
    /// </summary>
    public static void UseSystemTextJsonSerializer(
        this SchedulerBuilder.PersistentStoreOptions persistentStoreOptions,
        Action<SystemTextJsonSerializerOptions>? configure = null)
    {
        var options = new SystemTextJsonSerializerOptions();
        configure?.Invoke(options);
        persistentStoreOptions.UseSerializer<SystemTextJsonObjectSerializer>();
    }

    internal static JsonSerializerOptions AddQuartzConverters(this JsonSerializerOptions options, bool newtonsoftCompatibilityMode)
    {
        options.Converters.Add(new CalendarConverter(newtonsoftCompatibilityMode));
        options.Converters.Add(new CronExpressionConverter());
        options.Converters.Add(new DictionaryConverter());
        options.Converters.Add(new JobDataMapConverter());
        options.Converters.Add(new JobKeyConverter());
        options.Converters.Add(new TriggerKeyConverter());
        options.Converters.Add(new NameValueCollectionConverter());
        options.Converters.Add(new TriggerConverter());
        return options;
    }
}

public class SystemTextJsonSerializerOptions
{
    /// <summary>
    /// Add serializer for custom trigger
    /// </summary>
    public SystemTextJsonSerializerOptions AddTriggerSerializer<TTrigger>(ITriggerSerializer serializer) where TTrigger : ITrigger
    {
        SystemTextJsonObjectSerializer.AddTriggerSerializer<TTrigger>(serializer);
        return this;
    }

    /// <summary>
    /// Add serializer for custom calendar
    /// </summary>
    public SystemTextJsonSerializerOptions AddCalendarSerializer<TCalendar>(ICalendarSerializer serializer) where TCalendar : ICalendar
    {
        SystemTextJsonObjectSerializer.AddCalendarSerializer<TCalendar>(serializer);
        return this;
    }
}