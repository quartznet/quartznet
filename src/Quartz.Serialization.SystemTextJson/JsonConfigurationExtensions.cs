using System;
using System.Text.Json;

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

    internal static JsonSerializerOptions AddQuartzConverters(this JsonSerializerOptions options, bool newtonsoftCompatibilityMode)
    {
        options.Converters.Add(new CalendarConverter(newtonsoftCompatibilityMode));
        options.Converters.Add(new CronExpressionConverter());
        options.Converters.Add(new JobDataMapConverter());
        options.Converters.Add(new JobKeyConverter());
        options.Converters.Add(new TriggerKeyConverter());
        options.Converters.Add(new NameValueCollectionConverter());
        options.Converters.Add(new TriggerConverter());
        return options;
    }
}

public class SystemTextJsonSerializerOptions;