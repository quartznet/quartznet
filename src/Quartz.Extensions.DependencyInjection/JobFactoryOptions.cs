using System;

namespace Quartz
{
    public class JobFactoryOptions
    {
        /// <summary>
        /// When DI has not been configured with the job type, should the default no-arg public constructor be tried.
        /// </summary>
        [Obsolete("The value is ignored as all jobs are created with ActivatorUtilities.CreateInstance (see https://github.com/quartznet/quartznet/issues/1120) or via lookup from DI.")]
        public bool AllowDefaultConstructor { get; set; }

        /// <summary>
        /// Whether to use scopes when building job instances, enables injection of scoped services like
        /// Entity Framework's DbContext.
        /// </summary>
        public bool CreateScope { get; set; }
    }
}