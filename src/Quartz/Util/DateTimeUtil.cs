using System;

#if NET_20
using NullableDateTime = System.Nullable<System.DateTime>;
#else
using Nullables;
#endif

namespace Quartz.Util
{
    /// <summary>
    /// DateTime related utility methods.
    /// </summary>
    public class DateTimeUtil
    {
        /// <summary>
        /// Assumes that given input is in UTC and sets the kind to be UTC.
        /// Just a precaution if somebody does not set it explicitly.
        /// <strong>This only works in .NET Framework 2.0 onwards.</strong>
        /// </summary>
        /// <param name="dt">The datetime to check.</param>
        /// <returns>DateTime with kind set to UTC.</returns>
        public static DateTime AssumeUniversalTime(DateTime dt)
        {
#if NET_20
            return new DateTime(dt.Ticks, DateTimeKind.Utc);
#else
            // can't really do anything in 1.x
            return dt;
#endif
        }

        /// <summary>
        /// Assumes that given input is in UTC and sets the kind to be UTC.
        /// Just a precaution if somebody does not set it explicitly.
        /// </summary>
        /// <param name="dt">The datetime to check.</param>
        /// <returns>DateTime with kind set to UTC.</returns>
        public static NullableDateTime AssumeUniversalTime(NullableDateTime dt)
        {
            if (dt.HasValue)
            {
                return AssumeUniversalTime(dt.Value);
            }
            else
            {
                return null;
            }
        }
    }
}
