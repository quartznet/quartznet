using System;

namespace Quartz
{
    public class JobFactoryOptions
    {
        /// <summary>
        /// Type to use, overrides 
        /// </summary>
        public Type? Type { get; set; }

        /// <summary>
        /// When DI has not configured with job, can we call default constructor if it's present.
        /// </summary>
        public bool AllowDefaultConstructor { get; set; }

        /// <summary>
        /// Whether to use scopes when building job instances, enables injection of scoped services.
        /// </summary>
        public bool CreateScope { get; set; }
    }
}