using System;

using Quartz.Spi;

namespace Quartz.Simpl
{
    /// <summary>
    /// InstanceIdGenerator that will use a <see cref="SystemProperty" /> to configure the scheduler.
    /// If no value set for the property, a <see cref="SchedulerException" /> is thrown.
    /// <author>Alex Snaps</author>
    /// </summary>
    public class SystemPropertyInstanceIdGenerator : IInstanceIdGenerator
    {
        /// <summary>
        /// System property to read the instanceId from.
        /// </summary>
        public const string SystemProperty = "quartz.scheduler.instanceId";

        /// <summary>
        /// Returns the cluster wide value for this scheduler instance's id, based on a system property.
        /// </summary>
        public string GenerateInstanceId()
        {
            string property = Environment.GetEnvironmentVariable(SystemProperty);
            if (property == null)
            {
                throw new SchedulerException("No value for '" + SystemProperty + "' system property found, please configure your environment accordingly!");
            }
            return property;
        }
    }
}