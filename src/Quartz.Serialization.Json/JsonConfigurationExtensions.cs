using System;

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
    public void AddCalendarSerializer<TCalendar>(ICalendarSerializer serializer)
    {
        JsonObjectSerializer.AddCalendarSerializer<TCalendar>(serializer);
    }
}