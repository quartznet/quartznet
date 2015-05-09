#region License
/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
 * 
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not 
 * use this file except in compliance with the License. You may obtain a copy 
 * of the License at 
 * 
 *   http://www.apache.org/licenses/LICENSE-2.0 
 *   
 * Unless required by applicable law or agreed to in writing, software 
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT 
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the 
 * License for the specific language governing permissions and limitations 
 * under the License.
 * 
 */
#endregion

using System;

using Quartz.Collection;
using Quartz.Impl.Triggers;

namespace Quartz
{
    /// <summary>
    /// A <see cref="ITrigger" /> that is used to fire a <see cref="IJobDetail" />
    /// based upon daily repeating time intervals.
    /// </summary>
    /// <remarks>
    /// <para>The trigger will fire every N (see <see cref="RepeatInterval"/> ) seconds, minutes or hours
    /// (see <see cref="RepeatIntervalUnit"/>) during a given time window on specified days of the week.</para>
    /// 
    /// <para>For example#1, a trigger can be set to fire every 72 minutes between 8:00 and 11:00 everyday. It's fire times 
    /// be 8:00, 9:12, 10:24, then next day would repeat: 8:00, 9:12, 10:24 again.</para>
    /// 
    /// <para>For example#2, a trigger can be set to fire every 23 minutes between 9:20 and 16:47 Monday through Friday. </para>
    /// 
    /// <para>On each day, the starting fire time is reset to startTimeOfDay value, and then it will add repeatInterval value to it until
    /// the endTimeOfDay is reached. If you set daysOfWeek values, then fire time will only occur during those week days period.</para>
    /// 
    /// <para>The default values for fields if not set are: startTimeOfDay defaults to 00:00:00, the endTimeOfDay default to 23:59:59, 
    /// and daysOfWeek is default to every day. The startTime default to current time-stamp now, while endTime has not value.</para>
    /// 
    /// <para>If startTime is before startTimeOfDay, then it has no affect. Else if startTime after startTimeOfDay, then the first fire time
    /// for that day will be normal startTimeOfDay incremental values after startTime value. Same reversal logic is applied to endTime
    /// with endTimeOfDay.</para>
    /// </remarks>
    /// <see cref="DailyTimeIntervalTriggerImpl" />
    /// <see cref="DailyTimeIntervalScheduleBuilder"/>
    /// <author>James House</author>
    /// <author>Zemian Deng saltnlight5@gmail.com</author>
    /// <author>Nuno Maia (.NET)</author>
    public interface IDailyTimeIntervalTrigger : ITrigger
    {
        /// <summary>
        /// Get the number of times for interval this trigger should repeat, 
        /// after which it will be automatically deleted.
        /// </summary>
        int RepeatCount { get; }

        /// <summary>
        /// Get the interval unit - the time unit on with the interval applies.
        /// The only intervals that are valid for this type of trigger are <see cref="IntervalUnit.Second"/>,
        /// <see cref="IntervalUnit.Minute"/>, and <see cref="IntervalUnit.Hour"/>
        /// </summary>
        IntervalUnit RepeatIntervalUnit { get; }

        /// <summary>
        /// Get the time interval that will be added to the <see cref="IDailyTimeIntervalTrigger" />'s
        /// fire time (in the set repeat interval unit) in order to calculate the time of the
        /// next trigger repeat.
        /// </summary>
        int RepeatInterval { get; }

        /// <summary>
        /// The time of day to start firing at the given interval.
        /// </summary>
        TimeOfDay StartTimeOfDay { get;  }
        
        /// <summary>
        /// The time of day to complete firing at the given interval.
        /// </summary>
        TimeOfDay EndTimeOfDay { get; }

        /// <summary>
        /// The days of the week upon which to fire.
        /// </summary>
        /// <returns>
        /// A Set containing the integers representing the days of the week, per the values 0-6 as defined by
        /// DayOfWees.Sunday - DayOfWeek.Saturday. 
        /// </returns>
        ISet<DayOfWeek> DaysOfWeek { get; set; }

        /// <summary>
        /// Get the number of times the <see cref="IDailyTimeIntervalTrigger" /> has already fired.
        /// </summary>
        int TimesTriggered { get; set; }

        /// <summary>
        /// Gets the time zone within which time calculations related to this trigger will be performed.
        /// </summary>
        /// <remarks>
        /// If null, the system default TimeZone will be used.
        /// </remarks>
        TimeZoneInfo TimeZone { get; }
    }
}
