using System;
using System.Collections.Generic;

using Common.Logging;

namespace Quartz.Util
{
    public static class TimeZoneUtil
    {
        private static readonly ILog logger = LogManager.GetLogger(typeof(TimeZoneUtil));
        private static readonly Dictionary<string, string> timeZoneIdAliases = new Dictionary<string, string>();

        static TimeZoneUtil()
        {
            timeZoneIdAliases["UTC"] = "Coordinated Universal Time";
            timeZoneIdAliases["Coordinated Universal Time"] = "UTC";
        }

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

        /// <summary>
        /// Tries to find time zone with given id, has ability do some fallbacks when necessary.
        /// </summary>
        /// <param name="id">System id of the time zone.</param>
        /// <returns></returns>
        public static TimeZoneInfo FindTimeZoneById(string id)
        {
            TimeZoneInfo info = null;
            try
            {
                info = TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                string aliasedId;
                if (timeZoneIdAliases.TryGetValue(id, out aliasedId))
                {
                    try
                    {
                        info = TimeZoneInfo.FindSystemTimeZoneById(aliasedId);
                    }
                    catch
                    {
                        logger.ErrorFormat("Could not find time zone using alias id " + aliasedId);
                    }
                }

                if (info == null)
                {
                    // we tried our best
                    throw;
                }
            }

            return info;
        }
    }
}