using Quartz.Serialization.Newtonsoft;
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
#pragma warning disable CA1822
    public void AddCalendarSerializer<TCalendar>(ICalendarSerializer serializer)
#pragma warning restore CA1822
    {
        NewtonsoftJsonObjectSerializer.AddCalendarSerializer<TCalendar>(serializer);
    }
}