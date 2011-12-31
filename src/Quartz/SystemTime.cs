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
        /// <summary>
        /// Return current UTC time via <see cref="Func&lt;T&gt;" />. Allows easier unit testing.
        /// </summary>
        public static Func<DateTimeOffset> UtcNow = () => DateTimeOffset.UtcNow;

        /// <summary>
        /// Return current time in current time zone via <see cref="Func&lt;T&gt;" />. Allows easier unit testing.
        /// </summary>
        public static Func<DateTimeOffset> Now = () => DateTimeOffset.Now;
    }
}