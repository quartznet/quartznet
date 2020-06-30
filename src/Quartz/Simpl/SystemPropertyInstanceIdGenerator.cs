using System;
using System.Threading;
using System.Threading.Tasks;

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

        private string? prepend;
        private string? postpend;
        private string systemPropertyName = SystemProperty;

        /// <summary>
        /// Returns the cluster wide value for this scheduler instance's id, based on a system property.
        /// </summary>
        public Task<string?> GenerateInstanceId(CancellationToken cancellationToken = default)
        {
            var property = Environment.GetEnvironmentVariable(SystemPropertyName);
            if (property == null)
            {
                throw new SchedulerException("No value for '" + SystemProperty + "' system property found, please configure your environment accordingly!");
            }

            if (Prepend != null)
            {
                property = Prepend + property;
            }
            if (Postpend != null)
            {
                property += Postpend;
            }

            return Task.FromResult<string?>(property);
        }

        /// <summary>
        /// A string of text to prepend (add to the beginning) to the instanceId found in the system property.
        /// </summary>
        public string? Prepend
        {
            get => prepend;
            set => prepend = value?.Trim();
        }

        /// <summary>
        /// A string of text to postpend (add to the end) to the instanceId found in the system property.
        /// </summary>
        public string? Postpend
        {
            get => postpend;
            set => postpend = value?.Trim();
        }

        /// <summary>
        /// The name of the system property from which to obtain the instanceId.
        /// </summary>
        /// <remarks>
        /// Defaults to <see cref="SystemProperty"/>.
        /// </remarks>
        public string SystemPropertyName
        {
            get => systemPropertyName;
            set => systemPropertyName = value?.Trim() ?? SystemProperty;
        }
    }
}