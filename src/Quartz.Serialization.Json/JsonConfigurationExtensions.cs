using Quartz.Simpl;

namespace Quartz
{
    public static class JsonConfigurationExtensions
    {
        /// <summary>
        /// Use JSON as data serialization strategy.
        /// </summary>
        public static void UseJsonSerializer(this SchedulerBuilder.PersistentStoreOptions options)
        {
            options.UseSerializer<JsonObjectSerializer>();
        }
    }
}