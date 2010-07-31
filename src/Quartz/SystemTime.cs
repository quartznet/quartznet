using System;

namespace Quartz
{
    /// <summary>
    /// A time source for Quartz.NET that returns the current time.
    /// Original idea by Ayende Rahien:
    /// http://ayende.com/Blog/archive/2008/07/07/Dealing-with-time-in-tests.aspx
    /// </summary>
    public static class SystemTime
    {
#if NET_35
        /// <summary>
        /// Return current time via <see cref="Func&lt;T&gt;" />. Allows easier unit testing.
        /// </summary>
        public static Func<DateTime> UtcNow = () => DateTime.UtcNow;
#else
        /// <summary>
        /// Return current time in UTC.
        /// </summary>
        public static DateTime UtcNow()
        {
            return DateTime.UtcNow;
        }
#endif
    }
}