using Quartz.Simpl;

namespace Quartz
{
    public static class JsonConfigurationExtensions
    {
        /// <summary>
        /// Use JSON as data serialization strategy.
        /// </summary>
        public static SchedulerBuilder.PersistentStoreOptions WithJsonSerializer(this SchedulerBuilder.PersistentStoreOptions options)
        {
            return options.WithSerializer<JsonObjectSerializer>();
        }
    }
}