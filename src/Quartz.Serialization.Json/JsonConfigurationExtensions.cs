using System;

using Quartz.Serialization.Json.Triggers;
using Quartz.Simpl;

namespace Quartz;

public static class JsonConfigurationExtensions
{
    /// <summary>
    /// Use JSON as data serialization strategy.
    /// </summary>
    [Obsolete("Use UseNewtonsoftJsonSerializer instead")]
    public static void UseJsonSerializer(
        this SchedulerBuilder.PersistentStoreOptions persistentStoreOptions,
        Action<JsonSerializerOptions>? configure = null)
    {
        var options = new JsonSerializerOptions();
        configure?.Invoke(options);
        persistentStoreOptions.UseSerializer<JsonObjectSerializer>();
    }

    /// <summary>
    /// Use Newtonfsoft JSON as data serialization strategy.
    /// </summary>
    public static void UseNewtonsoftJsonSerializer(
        this SchedulerBuilder.PersistentStoreOptions persistentStoreOptions,
        Action<NewtonsoftJsonSerializerOptions>? configure = null)
    {
        var options = new NewtonsoftJsonSerializerOptions();
        configure?.Invoke(options);
        persistentStoreOptions.UseSerializer<JsonObjectSerializer>();
        persistentStoreOptions.SetProperty("quartz.serializer.RegisterTriggerConverters", options.RegisterTriggerConverters.ToString());
    }
}

[Obsolete("Use NewtonsoftJsonSerializerOptions instead")]
public class JsonSerializerOptions
{
    public void AddCalendarSerializer<TCalendar>(ICalendarSerializer serializer)
    {
        JsonObjectSerializer.AddCalendarSerializer<TCalendar>(serializer);
    }
}

public class NewtonsoftJsonSerializerOptions
{
    /// <summary>
    /// Whether to register optimized default trigger converters for persistence storage. These are compatible with STJ
    /// serializer, but might not work if you have existing data in database which has been serialized with old behavior.
    /// Defaults to false.
    /// </summary>
    public bool RegisterTriggerConverters { get; set; }

    /// <summary>
    /// Add serializer for custom trigger
    /// </summary>
    public NewtonsoftJsonSerializerOptions AddTriggerSerializer<TTrigger>(ITriggerSerializer serializer) where TTrigger : ITrigger
    {
        JsonObjectSerializer.AddTriggerSerializer<TTrigger>(serializer);
        return this;
    }

    /// <summary>
    /// Add serializer for custom calendar
    /// </summary>
    public NewtonsoftJsonSerializerOptions AddCalendarSerializer<TCalendar>(ICalendarSerializer serializer)
    {
        JsonObjectSerializer.AddCalendarSerializer<TCalendar>(serializer);
        return this;
    }
}