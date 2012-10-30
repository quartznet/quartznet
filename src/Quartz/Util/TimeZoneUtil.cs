using System;

namespace Quartz.Util
{
    public static class TimeZoneUtil
    {
        /// <summary>
        /// TimeZoneInfo.ConvertTime is not supported under mono
        /// </summary>
        /// <param name="dateTimeOffset"></param>
        /// <param name="timeZoneInfo"></param>
        /// <returns></returns>
        public static DateTimeOffset ConvertTime(DateTimeOffset dateTimeOffset, TimeZoneInfo timeZoneInfo)
        {
            if (QuartzEnvironment.IsRunningOnMono)
            {
                return TimeZoneInfo.ConvertTimeFromUtc(dateTimeOffset.UtcDateTime, timeZoneInfo);
            }

            return TimeZoneInfo.ConvertTime(dateTimeOffset, timeZoneInfo);
        }
    }
}