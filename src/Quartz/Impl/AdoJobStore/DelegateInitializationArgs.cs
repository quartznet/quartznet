using System.Collections.Specialized;

using Common.Logging;

using Quartz.Impl.AdoJobStore.Common;
using Quartz.Spi;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Initialization arguments holder for <see cref="IDriverDelegate" /> implementations.
    /// </summary>
    public class DelegateInitializationArgs
    {
        /// <summary>
        /// Whether simple <see cref="NameValueCollection"/> should be used (for serialization safety).
        /// </summary>
        public bool UseProperties { get; set; }

        /// <summary>
        /// The logger to use during execution.
        /// </summary>
        public ILog Logger { get; set; }

        /// <summary>
        /// The prefix of all table names.
        /// </summary>
        public string TablePrefix { get; set; }

        /// <summary>
        /// The instance's name.
        /// </summary>
        public string InstanceName { get; set; }

        /// <summary>
        /// The instance id.
        /// </summary>
        public string InstanceId { get; set; }

        /// <summary>
        /// The db provider.
        /// </summary>
        public IDbProvider DbProvider { get; set; }

        /// <summary>
        /// The type loading strategy.
        /// </summary>
        public ITypeLoadHelper TypeLoadHelper { get; set; }

        /// <summary>
        /// Object serializer and deserializer strategy to use.
        /// </summary>
        public IObjectSerializer ObjectSerializer { get; set; }

        /// <summary>
        /// Custom driver delegate initialization.
        /// </summary>
        /// <remarks>
        /// initStrings are of the format:
        /// settingName=settingValue|otherSettingName=otherSettingValue|...
        /// </remarks>
        public string InitString { get; set; }
    }
}