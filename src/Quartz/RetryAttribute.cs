using System;

namespace Quartz
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class RetryAttribute : Attribute
    {
        /// <summary>
        /// </summary>
        /// <param name="maxRetries"></param>
        /// <param name="interval"></param>
        /// <remarks>
        /// Thanks to Harvey Delaney for the idea/implementation for this attribute.
        /// https://blog.harveydelaney.com/quartz-job-exception-retrying/
        /// </remarks>
        public RetryAttribute(int maxRetries = 3, int interval = 5)
        {
            MaxRetries = maxRetries;
            Interval = interval;
        }

        public int MaxRetries { get; set; }

        public int Interval { get; set; }
    }
}
