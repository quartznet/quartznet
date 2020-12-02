using System;

namespace Quartz
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class RetryAttribute : Attribute
    {
        public RetryAttribute(int maxRetries = 3, int interval = 5)
        {
            MaxRetries = maxRetries;
            Interval = interval;
        }

        public int MaxRetries { get; set; }

        public int Interval { get; set; }
    }
}
