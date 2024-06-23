using Quartz.Simpl;

namespace Quartz;

public static class JsonConfigurationExtensions
{
    /// <summary>
    /// Use Newtonsoft JSON as data serialization strategy.
    /// </summary>
    public static void UseNewtonsoftJsonSerializer(
        this SchedulerBuilder.PersistentStoreOptions persistentStoreOptions,
        Action<NewtonsoftJsonSerializerOptions>? configure = null)
    {
        var options = new NewtonsoftJsonSerializerOptions();
        configure?.Invoke(options);
        persistentStoreOptions.UseSerializer<NewtonsoftJsonObjectSerializer>();
    }
}

public class NewtonsoftJsonSerializerOptions
{
    public void AddCalendarSerializer<TCalendar>(ICalendarSerializer serializer)
    {
        NewtonsoftJsonObjectSerializer.AddCalendarSerializer<TCalendar>(serializer);
    }
}