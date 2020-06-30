﻿using System;
using System.Collections.Generic;
using System.Linq;

using Quartz.Logging;

namespace Quartz.Util
{
    public static class TimeZoneUtil
    {
        private static readonly ILog logger = LogProvider.GetLogger(typeof (TimeZoneUtil));
        private static readonly Dictionary<string, string> timeZoneIdAliases = new Dictionary<string, string>();

        static TimeZoneUtil()
        {
            // Azure has had issues with having both formats
            timeZoneIdAliases["UTC"] = "Coordinated Universal Time";
            timeZoneIdAliases["Coordinated Universal Time"] = "UTC";

            // Mono differs in naming too...
            timeZoneIdAliases["Central European Standard Time"] = "CET";
            timeZoneIdAliases["CET"] = "Central European Standard Time";

            timeZoneIdAliases["Eastern Standard Time"] = "US/Eastern";
            timeZoneIdAliases["US/Eastern"] = "Eastern Standard Time";

            timeZoneIdAliases["Central Standard Time"] = "US/Central";
            timeZoneIdAliases["US/Central"] = "Central Standard Time";

            timeZoneIdAliases["US Central Standard Time"] = "US/Indiana-Stark";
            timeZoneIdAliases["US/Indiana-Stark"] = "US Central Standard Time";

            timeZoneIdAliases["Mountain Standard Time"] = "US/Mountain";
            timeZoneIdAliases["US/Mountain"] = "Mountain Standard Time";

            timeZoneIdAliases["US Mountain Standard Time"] = "US/Arizona";
            timeZoneIdAliases["US/Arizona"] = "US Mountain Standard Time";

            timeZoneIdAliases["Pacific Standard Time"] = "US/Pacific";
            timeZoneIdAliases["US/Pacific"] = "Pacific Standard Time";

            timeZoneIdAliases["Alaskan Standard Time"] = "US/Alaska";
            timeZoneIdAliases["US/Alaska"] = "Alaskan Standard Time";

            timeZoneIdAliases["Hawaiian Standard Time"] = "US/Hawaii";
            timeZoneIdAliases["US/Hawaii"] = "Hawaiian Standard Time";

            timeZoneIdAliases["China Standard Time"] = "Asia/Shanghai";
            timeZoneIdAliases["Asia/Shanghai"] = "China Standard Time";

            timeZoneIdAliases["Pakistan Standard Time"] = "Asia/Karachi";
            timeZoneIdAliases["Asia/Karachi"] = "Pakistan Standard Time";
        }

        public static Func<string, TimeZoneInfo?> CustomResolver = id => null;

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
                return TimeZoneInfo.ConvertTime(dateTimeOffset.UtcDateTime, TimeZoneInfo.Utc, timeZoneInfo);
            }

            return TimeZoneInfo.ConvertTime(dateTimeOffset, timeZoneInfo);
        }

        /// <summary>
        /// TimeZoneInfo.GetUtcOffset(DateTimeOffset) is not supported under mono
        /// </summary>
        /// <param name="dateTimeOffset"></param>
        /// <param name="timeZoneInfo"></param>
        /// <returns></returns>
        public static TimeSpan GetUtcOffset(DateTimeOffset dateTimeOffset, TimeZoneInfo timeZoneInfo)
        {
            if (QuartzEnvironment.IsRunningOnMono)
            {
                return timeZoneInfo.GetUtcOffset(dateTimeOffset.UtcDateTime);
            }

            return timeZoneInfo.GetUtcOffset(dateTimeOffset);
        }

        public static TimeSpan GetUtcOffset(DateTime dateTime, TimeZoneInfo timeZoneInfo)
        {
            // Unlike the default behavior of TimeZoneInfo.GetUtcOffset, it is prefered to choose
            // the DAYLIGHT time when the input is ambiguous, because the daylight instance is the
            // FIRST instance, and time moves in a forward direction.

            TimeSpan offset = timeZoneInfo.IsAmbiguousTime(dateTime)
                ? timeZoneInfo.GetAmbiguousTimeOffsets(dateTime).Max()
                : timeZoneInfo.GetUtcOffset(dateTime);

            return offset;
        }

        /// <summary>
        /// Tries to find time zone with given id, has ability do some fallbacks when necessary.
        /// </summary>
        /// <param name="id">System id of the time zone.</param>
        /// <returns></returns>
        public static TimeZoneInfo FindTimeZoneById(string id)
        {
            TimeZoneInfo? info = null;
            try
            {
                info = TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException ex)
            {
                if (timeZoneIdAliases.TryGetValue(id, out var aliasedId))
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
                    info = CustomResolver(id);
                }

                if (info == null)
                {
                    // we tried our best
                    throw new TimeZoneNotFoundException(
                        $"Could not find time zone with id {id}, consider using Quartz.Plugins.TimeZoneConverter for resolving more time zones ids",
                        ex);
                }
            }

            return info;
        }
    }
}
