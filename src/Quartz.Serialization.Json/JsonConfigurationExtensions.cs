using System;

using Quartz.Converters;
using Quartz.Simpl;

namespace Quartz
{
    public static class JsonConfigurationExtensions
    {
        /// <summary>
        /// Use JSON as data serialization strategy.
        /// </summary>
        public static void UseJsonSerializer(
            this SchedulerBuilder.PersistentStoreOptions persistentStoreOptions,
            Action<JsonSerializerOptions>? configure = null)
        {
            var options = new JsonSerializerOptions();
            configure?.Invoke(options);
            persistentStoreOptions.UseSerializer<JsonObjectSerializer>();
        }
    }

    public class JsonSerializerOptions
    {
        public void AddCalendarSerializer<TCalendar>(ICalendarSerializer serializer)
        {
            JsonObjectSerializer.AddCalendarSerializer<TCalendar>(serializer);
        }
    }
}