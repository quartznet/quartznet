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
using Quartz.Util;

namespace Quartz.Impl.Triggers
{
    /// <summary>
    /// A concrete implementation of DailyTimeIntervalTrigger that is used to fire a <see cref="IJobDetail"/>
    /// based upon daily repeating time intervals.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The trigger will fire every N (<see cref="IDailyTimeIntervalTrigger.RepeatInterval"/> ) seconds, minutes or hours
    /// (see <see cref="IDailyTimeIntervalTrigger.RepeatInterval"/>) during a given time window on specified days of the week.
    /// </para>
    /// <para>
    /// For example#1, a trigger can be set to fire every 72 minutes between 8:00 and 11:00 everyday. It's fire times would
    /// be 8:00, 9:12, 10:24, then next day would repeat: 8:00, 9:12, 10:24 again.
    /// </para>
    /// <para>
    /// For example#2, a trigger can be set to fire every 23 minutes between 9:20 and 16:47 Monday through Friday.
    /// </para>
    /// <para>
    /// On each day, the starting fire time is reset to startTimeOfDay value, and then it will add repeatInterval value to it until
    /// the endTimeOfDay is reached. If you set daysOfWeek values, then fire time will only occur during those week days period. Again,
    /// remember this trigger will reset fire time each day with startTimeOfDay, regardless of your interval or endTimeOfDay!
    /// </para>
    /// <para>
    /// The default values for fields if not set are: startTimeOfDay defaults to 00:00:00, the endTimeOfDay default to 23:59:59,
    /// and daysOfWeek is default to every day. The startTime default to current time-stamp now, while endTime has not value.
    /// </para>
    /// <para>
    /// If startTime is before startTimeOfDay, then startTimeOfDay will be used and startTime has no affect other than to specify
    /// the first day of firing. Else if startTime is  after startTimeOfDay, then the first fire time for that day will be the next
    /// interval after the startTime. For example, if you set startingTimeOfDay=9am, endingTimeOfDay=11am, interval=15 mins, and startTime=9:33am, 
    /// then the next fire time will be 9:45pm. Note also that if you do not set startTime value, the trigger builder will default to current time, and current time 
    /// maybe before or after the startTimeOfDay! So be aware how you set your startTime.
    /// </para>
    /// <para>
    /// This trigger also supports "repeatCount" feature to end the trigger fire time after
    /// a certain number of count is reached. Just as the SimpleTrigger, setting repeatCount=0 
    /// means trigger will fire once only! Setting any positive count then the trigger will repeat 
    /// count + 1 times. Unlike SimpleTrigger, the default value of repeatCount of this trigger
    /// is set to REPEAT_INDEFINITELY instead of 0 though.
    /// </para>
    /// </remarks>
    /// <see cref="IDailyTimeIntervalTrigger"/>
    /// <see cref="DailyTimeIntervalScheduleBuilder"/>
    /// <since>2.0</since>
    /// <author>James House</author>
    /// <author>Zemian Deng saltnlight5@gmail.com</author>
    /// <author>Nuno Maia (.NET)</author>
    [Serializable]
    public class DailyTimeIntervalTriggerImpl : AbstractTrigger, IDailyTimeIntervalTrigger
    {
        /// <summary>
        /// Used to indicate the 'repeat count' of the trigger is indefinite. Or in
        /// other words, the trigger should repeat continually until the trigger's
        /// ending timestamp.
        /// </summary>
        public const int RepeatIndefinitely = -1;

        private static readonly int YearToGiveupSchedulingAt = DateTime.Now.Year + 100;

        private DateTimeOffset startTimeUtc;
        private DateTimeOffset? endTimeUtc;
        private DateTimeOffset? nextFireTimeUtc;
        private DateTimeOffset? previousFireTimeUtc;
        private int repeatInterval = 1;
        private IntervalUnit repeatIntervalUnit = IntervalUnit.Minute;
        private ISet<DayOfWeek> daysOfWeek;
        private TimeOfDay startTimeOfDay;
        private TimeOfDay endTimeOfDay;
        private int timesTriggered;
        private bool complete;
        private int repeatCount = RepeatIndefinitely;
        private TimeZoneInfo timeZone;

        /// <summary>
        /// Create a  <see cref="IDailyTimeIntervalTrigger"/> with no settings.
        /// </summary>
        public DailyTimeIntervalTriggerImpl()
        {
        }

        /// <summary>
        /// Create a <see cref="IDailyTimeIntervalTrigger" /> that will occur immediately, and
        /// repeat at the given interval.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="startTimeOfDayUtc">The <see cref="TimeOfDay" /> that the repeating should begin occurring.</param>
        /// <param name="endTimeOfDayUtc">The <see cref="TimeOfDay" /> that the repeating should stop occurring.</param>
        /// <param name="intervalUnit">The repeat interval unit. The only intervals that are valid for this type of trigger are
        /// <see cref="IntervalUnit.Second"/>, <see cref="IntervalUnit.Minute"/>, and <see cref="IntervalUnit.Hour"/>.</param>
        /// <param name="repeatInterval"></param>
        public DailyTimeIntervalTriggerImpl(string name, TimeOfDay startTimeOfDayUtc, TimeOfDay endTimeOfDayUtc,
                                            IntervalUnit intervalUnit, int repeatInterval)
            : this(name, null, startTimeOfDayUtc, endTimeOfDayUtc, intervalUnit, repeatInterval)
        {
        }

        /// <summary>
        /// Create a <see cref="IDailyTimeIntervalTrigger" /> that will occur immediately, and
        /// repeat at the given interval.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="group"></param>
        /// <param name="startTimeOfDayUtc">The <see cref="TimeOfDay" /> that the repeating should begin occurring.</param>
        /// <param name="endTimeOfDayUtc">The <see cref="TimeOfDay" /> that the repeating should stop occurring.</param>
        /// <param name="intervalUnit">The repeat interval unit. The only intervals that are valid for this type of trigger are
        /// <see cref="IntervalUnit.Second"/>, <see cref="IntervalUnit.Minute"/>, and <see cref="IntervalUnit.Hour"/>.</param>
        /// <param name="repeatInterval"></param>
        public DailyTimeIntervalTriggerImpl(string name, string group, TimeOfDay startTimeOfDayUtc,
                                            TimeOfDay endTimeOfDayUtc, IntervalUnit intervalUnit, int repeatInterval)
            : this(name, group, SystemTime.UtcNow(), null, startTimeOfDayUtc, endTimeOfDayUtc, intervalUnit,
                   repeatInterval)
        {
        }

        /// <summary>
        /// Create a <see cref="IDailyTimeIntervalTrigger" /> that will occur at the given time,
        /// and repeat at the given interval until the given end time.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="startTimeUtc">A <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" />to fire.</param>
        /// <param name="endTimeUtc">A <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" />to quit repeat firing.</param>
        /// <param name="startTimeOfDayUtc">The <see cref="TimeOfDay" /> that the repeating should begin occurring.</param>
        /// <param name="endTimeOfDayUtc">The <see cref="TimeOfDay" /> that the repeating should stop occurring.</param>
        /// <param name="intervalUnit">The repeat interval unit. The only intervals that are valid for this type of trigger are
        /// <see cref="IntervalUnit.Second"/>, <see cref="IntervalUnit.Minute"/>, and <see cref="IntervalUnit.Hour"/>.</param>
        /// <param name="repeatInterval">The number of milliseconds to pause between the repeat firing.</param>
        public DailyTimeIntervalTriggerImpl(string name, DateTimeOffset startTimeUtc,
                                            DateTimeOffset? endTimeUtc, TimeOfDay startTimeOfDayUtc, TimeOfDay endTimeOfDayUtc,
                                            IntervalUnit intervalUnit, int repeatInterval)
            : this(name, null, startTimeUtc, endTimeUtc, startTimeOfDayUtc, endTimeOfDayUtc, intervalUnit, repeatInterval)
        {
        }

        /// <summary>
        /// Create a <see cref="IDailyTimeIntervalTrigger" /> that will occur at the given time,
        /// and repeat at the given interval until the given end time.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="group"></param>
        /// <param name="startTimeUtc">A <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" />to fire.</param>
        /// <param name="endTimeUtc">A <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" />to quit repeat firing.</param>
        /// <param name="startTimeOfDayUtc">The <see cref="TimeOfDay" /> that the repeating should begin occurring.</param>
        /// <param name="endTimeOfDayUtc">The <see cref="TimeOfDay" /> that the repeating should stop occurring.</param>
        /// <param name="intervalUnit">The repeat interval unit. The only intervals that are valid for this type of trigger are
        /// <see cref="IntervalUnit.Second"/>, <see cref="IntervalUnit.Minute"/>, and <see cref="IntervalUnit.Hour"/>.</param>
        /// <param name="repeatInterval">The number of milliseconds to pause between the repeat firing.</param>
        public DailyTimeIntervalTriggerImpl(string name, string group, DateTimeOffset startTimeUtc,
                                            DateTimeOffset? endTimeUtc, TimeOfDay startTimeOfDayUtc, TimeOfDay endTimeOfDayUtc,
                                            IntervalUnit intervalUnit, int repeatInterval)
            : base(name, group)
        {
            StartTimeUtc = startTimeUtc;
            EndTimeUtc = endTimeUtc;
            RepeatIntervalUnit = intervalUnit;
            RepeatInterval = repeatInterval;
            StartTimeOfDay = startTimeOfDayUtc;
            EndTimeOfDay = endTimeOfDayUtc;
        }

        /// <summary>
        /// Create a <see cref="IDailyTimeIntervalTrigger" /> that will occur at the given time,
        /// fire the identified job and repeat at the given
        /// interval until the given end time.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="group"></param>
        /// <param name="jobName"></param>
        /// <param name="jobGroup"></param>
        /// <param name="startTimeUtc">A <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" />to fire.</param>
        /// <param name="endTimeUtc">A <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" />to quit repeat firing.</param>
        /// <param name="startTimeOfDayUtc">The <see cref="TimeOfDay" /> that the repeating should begin occurring.</param>
        /// <param name="endTimeOfDayUtc">The <see cref="TimeOfDay" /> that the repeating should stop occurring.</param>
        /// <param name="intervalUnit">The repeat interval unit. The only intervals that are valid for this type of trigger are
        /// <see cref="IntervalUnit.Second"/>, <see cref="IntervalUnit.Minute"/>, and <see cref="IntervalUnit.Hour"/>.</param>
        /// <param name="repeatInterval">The number of milliseconds to pause between the repeat firing.</param>
        public DailyTimeIntervalTriggerImpl(string name, string group, string jobName,
                                            string jobGroup, DateTimeOffset startTimeUtc, DateTimeOffset? endTimeUtc,
                                            TimeOfDay startTimeOfDayUtc, TimeOfDay endTimeOfDayUtc,
                                            IntervalUnit intervalUnit, int repeatInterval)
            : base(name, group, jobName, jobGroup)
        {
            StartTimeUtc = startTimeUtc;
            EndTimeUtc = endTimeUtc;
            RepeatIntervalUnit = intervalUnit;
            RepeatInterval = repeatInterval;
            StartTimeOfDay = startTimeOfDayUtc;
            EndTimeOfDay = endTimeOfDayUtc;
        }

        /// <summary>
        /// The time at which the <see cref="IDailyTimeIntervalTrigger" /> should occur.
        /// </summary>
        public override DateTimeOffset StartTimeUtc
        {
            get
            {
                if (startTimeUtc == DateTimeOffset.MinValue)
                {
                    startTimeUtc = SystemTime.UtcNow();
                }
                return startTimeUtc;
            }
            set
            {
                if (value == DateTimeOffset.MinValue)
                {
                    throw new ArgumentException("Start time cannot be DateTimeOffset.MinValue");
                }

                DateTimeOffset? eTime = EndTimeUtc;
                if (eTime != null && eTime < value)
                {
                    throw new ArgumentException("End time cannot be before start time");
                }

                startTimeUtc = value;
            }
        }

        /// <summary>
        /// the time at which the <see cref="IDailyTimeIntervalTrigger" /> should quit repeating.
        /// </summary>
        /// <see cref="DailyTimeIntervalTriggerImpl.FinalFireTimeUtc"/>
        public override DateTimeOffset? EndTimeUtc
        {
            get { return endTimeUtc; }
            set
            {
                DateTimeOffset sTime = StartTimeUtc;
                if (value != null && sTime > value)
                {
                    throw new ArgumentException("End time cannot be before start time");
                }

                endTimeUtc = value;
            }
        }

        /// <summary>
        /// Get the number of times for interval this trigger should repeat, 
        /// after which it will be automatically deleted.
        /// </summary>
        public int RepeatCount
        {
            get { return repeatCount; }
            set
            {
                if (value < 0 && value != RepeatIndefinitely)
                {
                    throw new ArgumentException("Repeat count must be >= 0, use the constant RepeatIndefinitely for infinite.");
                }

                repeatCount = value;
            }
        }

        /// <summary>
        /// the interval unit - the time unit on with the interval applies.
        /// </summary>
        /// <remarks>
        /// The repeat interval unit. The only intervals that are valid for this type of trigger are
        /// <see cref="IntervalUnit.Second"/>, <see cref="IntervalUnit.Minute"/>, and <see cref="IntervalUnit.Hour"/>.
        /// </remarks>
        public IntervalUnit RepeatIntervalUnit
        {
            get { return repeatIntervalUnit; }
            set
            {
                if (!((value == IntervalUnit.Second) ||
                      (value == IntervalUnit.Minute) ||
                      (value == IntervalUnit.Hour)))
                {
                    throw new ArgumentException("Invalid repeat IntervalUnit (must be Second, Minute or Hour)");
                }

                repeatIntervalUnit = value;
            }
        }

        /// <summary>
        /// the time interval that will be added to the <see cref="IDailyTimeIntervalTrigger" />'s
        /// fire time (in the set repeat interval unit) in order to calculate the time of the
        /// next trigger repeat.
        /// </summary>
        public int RepeatInterval
        {
            get { return repeatInterval; }
            set
            {
                if (value < 0)
                {
                    throw new ArgumentException("Repeat interval must be >= 1");
                }

                repeatInterval = value;
            }
        }

        /// <summary>
        /// the number of times the <see cref="IDailyTimeIntervalTrigger" /> has already
        /// fired.
        /// </summary>
        public int TimesTriggered
        {
            get { return timesTriggered; }
            set { timesTriggered = value; }
        }

        public TimeZoneInfo TimeZone
        {
            get
            {
                if (timeZone == null)
                {
                    timeZone = TimeZoneInfo.Local;
                }
                return timeZone;
            }

            set { timeZone = value; }
        }

        protected override bool ValidateMisfireInstruction(int misfireInstruction)
        {
            if (misfireInstruction < Quartz.MisfireInstruction.IgnoreMisfirePolicy)
            {
                return false;
            }
            if (misfireInstruction > Quartz.MisfireInstruction.DailyTimeIntervalTrigger.DoNothing)
            {
                return false;
            }

            return true;
        }

        /// <summary> 
        /// Updates the <see cref="ICalendarIntervalTrigger" />'s state based on the
        /// MisfireInstruction.XXX that was selected when the <see cref="IDailyTimeIntervalTrigger" />
        /// was created.
        /// </summary>
        /// <remarks>
        /// If the misfire instruction is set to <see cref="MisfireInstruction.SmartPolicy" />,
        /// then the following scheme will be used:
        /// <ul>
        ///     <li>The instruction will be interpreted as <see cref="MisfireInstruction.DailyTimeIntervalTrigger.FireOnceNow" /></li>
        /// </ul>
        /// </remarks>
        public override void UpdateAfterMisfire(ICalendar cal)
        {
            int instr = MisfireInstruction;

            if (instr == Quartz.MisfireInstruction.IgnoreMisfirePolicy)
            {
                return;
            }

            if (instr == Quartz.MisfireInstruction.SmartPolicy)
            {
                instr = Quartz.MisfireInstruction.DailyTimeIntervalTrigger.FireOnceNow;
            }

            if (instr == Quartz.MisfireInstruction.DailyTimeIntervalTrigger.DoNothing)
            {
                DateTimeOffset? newFireTime = GetFireTimeAfter(SystemTime.UtcNow());
                while (newFireTime != null && cal != null && !cal.IsTimeIncluded(newFireTime.Value))
                {
                    newFireTime = GetFireTimeAfter(newFireTime);
                }
                SetNextFireTimeUtc(newFireTime);
            }
            else if (instr == Quartz.MisfireInstruction.CalendarIntervalTrigger.FireOnceNow)
            {
                // fire once now...
                SetNextFireTimeUtc(SystemTime.UtcNow());
                // the new fire time afterward will magically preserve the original  
                // time of day for firing for day/week/month interval triggers, 
                // because of the way getFireTimeAfter() works - in its always restarting
                // computation from the start time.
            }
        }

        /// <summary>
        /// Called when the scheduler has decided to 'fire'
        /// the trigger (execute the associated job), in order to
        /// give the trigger a chance to update itself for its next
        /// triggering (if any).
        /// </summary>
        /// <param name="calendar"></param>
        /// <see cref="AbstractTrigger.ExecutionComplete"/>
        public override void Triggered(ICalendar calendar)
        {
            timesTriggered++;
            previousFireTimeUtc = nextFireTimeUtc;
            nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);

            while (nextFireTimeUtc != null && calendar != null
                   && !calendar.IsTimeIncluded(nextFireTimeUtc.Value))
            {
                nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);

                if (nextFireTimeUtc == null)
                {
                    break;
                }

                //avoid infinite loop
                if (nextFireTimeUtc.Value.Year > YearToGiveupSchedulingAt)
                {
                    nextFireTimeUtc = null;
                }
            }

            if (nextFireTimeUtc == null)
            {
                complete = true;
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="calendar"></param>
        /// <param name="misfireThreshold"></param>
        /// <see cref="AbstractTrigger.UpdateWithNewCalendar"/>
        public override void UpdateWithNewCalendar(ICalendar calendar, TimeSpan misfireThreshold)
        {
            nextFireTimeUtc = GetFireTimeAfter(previousFireTimeUtc);

            if (nextFireTimeUtc == null || calendar == null)
            {
                return;
            }

            DateTimeOffset now = SystemTime.UtcNow();
            while (nextFireTimeUtc != null && !calendar.IsTimeIncluded(nextFireTimeUtc.Value))
            {
                nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);

                if (nextFireTimeUtc == null)
                {
                    break;
                }

                //avoid infinite loop
                if (nextFireTimeUtc.Value.Year > YearToGiveupSchedulingAt)
                {
                    nextFireTimeUtc = null;
                }

                if (nextFireTimeUtc != null && nextFireTimeUtc < now)
                {
                    TimeSpan diff = now - nextFireTimeUtc.Value;
                    if (diff >= misfireThreshold)
                    {
                        nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);
                    }
                }
            }
        }

        /// <summary>
        /// This method should not be used by the Quartz client.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Called by the scheduler at the time a <see cref="ITrigger" /> is first
        /// added to the scheduler, in order to have the <see cref="ITrigger" />
        /// compute its first fire time, based on any associated calendar.
        /// </para>
        /// 
        /// <para>
        /// After this method has been called, <see cref="ITrigger.GetNextFireTimeUtc" />
        /// should return a valid answer.
        /// </para>
        /// </remarks>
        /// <returns> 
        /// The first time at which the <see cref="ITrigger" /> will be fired
        /// by the scheduler, which is also the same value <see cref="ITrigger.GetNextFireTimeUtc" />
        /// will return (until after the first firing of the <see cref="ITrigger" />).
        /// </returns>        
        public override DateTimeOffset? ComputeFirstFireTimeUtc(ICalendar calendar)
        {
            nextFireTimeUtc = GetFireTimeAfter(StartTimeUtc.AddSeconds(-1));

            // Check calendar for date-time exclusion
            while (nextFireTimeUtc != null && calendar != null
                   && !calendar.IsTimeIncluded(nextFireTimeUtc.Value))
            {
                nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);

                if (nextFireTimeUtc == null)
                {
                    break;
                }

                //avoid infinite loop
                if (nextFireTimeUtc.Value.Year > YearToGiveupSchedulingAt)
                {
                    return null;
                }
            }

            return nextFireTimeUtc;
        }

        private DateTimeOffset CreateCalendarTime(DateTimeOffset dateTime)
        {
            return TimeZoneUtil.ConvertTime(dateTime, TimeZone);
        }

        /// <summary>
        /// Returns the next time at which the <see cref="ITrigger" /> is scheduled to fire. If
        /// the trigger will not fire again, <see langword="null" /> will be returned.  Note that
        /// the time returned can possibly be in the past, if the time that was computed
        /// for the trigger to next fire has already arrived, but the scheduler has not yet
        /// been able to fire the trigger (which would likely be due to lack of resources
        /// e.g. threads).
        /// </summary>
        ///<remarks>
        /// The value returned is not guaranteed to be valid until after the <see cref="ITrigger" />
        /// has been added to the scheduler.
        /// </remarks>
        /// <returns></returns>
        public override DateTimeOffset? GetNextFireTimeUtc()
        {
            return nextFireTimeUtc;
        }

        /// <summary>
        /// Returns the previous time at which the <see cref="ICalendarIntervalTrigger" /> fired.
        /// If the trigger has not yet fired, <see langword="null" /> will be returned.
        /// </summary>
        public override DateTimeOffset? GetPreviousFireTimeUtc()
        {
            return previousFireTimeUtc;
        }

        /// <summary>
        /// Set the next time at which the <see cref="IDailyTimeIntervalTrigger" /> should fire.
        /// </summary>
        /// <remarks>
        /// This method should not be invoked by client code.
        /// </remarks>
        /// <param name="value"></param>
        public override void SetNextFireTimeUtc(DateTimeOffset? value)
        {
            this.nextFireTimeUtc = value;
        }

        /// <summary>
        /// Set the previous time at which the <see cref="IDailyTimeIntervalTrigger" /> fired.
        /// </summary>
        /// <remarks>
        /// This method should not be invoked by client code.
        /// </remarks>
        /// <param name="previousFireTimeUtc"></param>
        public override void SetPreviousFireTimeUtc(DateTimeOffset? previousFireTimeUtc)
        {
            this.previousFireTimeUtc = previousFireTimeUtc;
        }

        /// <summary>
        /// Returns the next time at which the <see cref="IDailyTimeIntervalTrigger" /> will
        /// fire, after the given time. If the trigger will not fire after the given
        /// time, <see langword="null" /> will be returned.
        /// </summary>
        /// <param name="afterTime"></param>
        /// <returns></returns>
        public override DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTime)
        {
            // Check if trigger has completed or not.
            if (complete)
            {
                return null;
            }

            // Check repeatCount limit
            if (repeatCount != RepeatIndefinitely && timesTriggered > repeatCount)
            {
                return null;
            }

            // a. Increment afterTime by a second, so that we are comparing against a time after it!
            if (afterTime == null)
            {
                afterTime = SystemTime.UtcNow().AddSeconds(1);
            }
            else
            {
                afterTime = afterTime.Value.AddSeconds(1);
            }

            // make sure afterTime is at least startTime
            if (afterTime < startTimeUtc)
            {
                afterTime = startTimeUtc;
            }

            // now change to local time zone
            afterTime = TimeZoneUtil.ConvertTime(afterTime.Value, TimeZone);

            // b.Check to see if afterTime is after endTimeOfDay or not. 
            // If yes, then we need to advance to next day as well.
            bool afterTimePastEndTimeOfDay = false;
            if (endTimeOfDay != null)
            {
                afterTimePastEndTimeOfDay = afterTime.Value > endTimeOfDay.GetTimeOfDayForDate(afterTime).Value;
            }

            // c. now we need to move to the next valid day of week if either: 
            // the given time is past the end time of day, or given time is not on a valid day of week
            DateTimeOffset? fireTime = AdvanceToNextDayOfWeekIfNecessary(afterTime.Value, afterTimePastEndTimeOfDay);

            if (fireTime == null)
            {
                return null;
            }

            // apply timezone for this date & time
            fireTime = new DateTimeOffset(fireTime.Value.DateTime, TimeZoneUtil.GetUtcOffset(fireTime.Value, TimeZone));

            // d. Calculate and save fireTimeEndDate variable for later use
            DateTimeOffset fireTimeEndDate;
            if (endTimeOfDay == null)
            {
                fireTimeEndDate = new TimeOfDay(23, 59, 59).GetTimeOfDayForDate(fireTime).Value;
            }
            else
            {
                fireTimeEndDate = endTimeOfDay.GetTimeOfDayForDate(fireTime).Value;
            }

            // apply the proper offset for the end date
            fireTimeEndDate = new DateTimeOffset(fireTimeEndDate.DateTime, TimeZoneUtil.GetUtcOffset(fireTimeEndDate.DateTime, this.TimeZone));

            // e. Check fireTime against startTime or startTimeOfDay to see which go first.
            DateTimeOffset fireTimeStartDate = startTimeOfDay.GetTimeOfDayForDate(fireTime).Value;

            // apply the proper offset for the start date
            fireTimeStartDate = new DateTimeOffset(fireTimeStartDate.DateTime, TimeZoneUtil.GetUtcOffset(fireTimeStartDate.DateTime, this.TimeZone));

            if (fireTime < fireTimeStartDate)
            {
                return fireTimeStartDate.ToUniversalTime();
            }

            // f. Continue to calculate the fireTime by incremental unit of intervals.
            // recall that if fireTime was less that fireTimeStartDate, we didn't get this far
            startTimeUtc = TimeZoneUtil.ConvertTime(fireTimeStartDate, TimeZone);
            long secondsAfterStart = (long)(fireTime.Value - startTimeUtc).TotalSeconds;
            long repeatLong = RepeatInterval;

            DateTimeOffset sTime = fireTimeStartDate;
            IntervalUnit repeatUnit = RepeatIntervalUnit;
            if (repeatUnit == IntervalUnit.Second)
            {
                long jumpCount = secondsAfterStart / repeatLong;
                if (secondsAfterStart % repeatLong != 0)
                {
                    jumpCount++;
                }

                sTime = sTime.AddSeconds(RepeatInterval * (int)jumpCount);
                fireTime = sTime;
            }
            else if (repeatUnit == IntervalUnit.Minute)
            {
                long jumpCount = secondsAfterStart / (repeatLong * 60L);
                if (secondsAfterStart % (repeatLong * 60L) != 0)
                {
                    jumpCount++;
                }
                sTime = sTime.AddMinutes(RepeatInterval * (int)jumpCount);
                fireTime = sTime;
            }
            else if (repeatUnit == IntervalUnit.Hour)
            {
                long jumpCount = secondsAfterStart / (repeatLong * 60L * 60L);
                if (secondsAfterStart % (repeatLong * 60L * 60L) != 0)
                {
                    jumpCount++;
                }
                sTime = sTime.AddHours(RepeatInterval * (int)jumpCount);
                fireTime = sTime;
            }

            // g. Ensure this new fireTime is within the day, or else we need to advance to next day.
            if (fireTime > fireTimeEndDate)
            {
                fireTime = AdvanceToNextDayOfWeekIfNecessary(fireTime.Value, IsSameDay(fireTime.Value, fireTimeEndDate));
                // make sure we hit the startTimeOfDay on the new day
                fireTime = startTimeOfDay.GetTimeOfDayForDate(fireTime);
            }

            // i. Return calculated fireTime.
            if (fireTime == null)
            {
                return null;
            }

            // apply proper offset
            var d = fireTime.Value;
            d = new DateTimeOffset(d.DateTime, TimeZoneUtil.GetUtcOffset(d.DateTime, this.TimeZone));

            return d.ToUniversalTime();
        }

        private bool IsSameDay(DateTimeOffset d1, DateTimeOffset d2)
        {
            DateTimeOffset c1 = CreateCalendarTime(d1);
            DateTimeOffset c2 = CreateCalendarTime(d2);

            return c1.Date == c2.Date;
        }

        /// <summary>
        /// Given fireTime time determine if it is on a valid day of week. If so, simply return it unaltered,
        /// if not, advance to the next valid week day, and set the time of day to the start time of day.
        /// </summary>
        /// <param name="fireTime">given next fireTime.</param>
        /// <param name="forceToAdvanceNextDay">flag to whether to advance day without check existing week day. This scenario
        /// can happen when a caller determine fireTime has passed the endTimeOfDay that fireTime should move to next day anyway.
        /// </param>
        /// <returns>a next day fireTime.</returns>
        private DateTimeOffset? AdvanceToNextDayOfWeekIfNecessary(DateTimeOffset fireTime, bool forceToAdvanceNextDay)
        {
            // a. Advance or adjust to next dayOfWeek if need to first, starting next day with startTimeOfDay.
            TimeOfDay startTimeOfDay = StartTimeOfDay;
            DateTimeOffset fireTimeStartDate = startTimeOfDay.GetTimeOfDayForDate(fireTime).Value;
            DateTimeOffset fireTimeStartDateCal = CreateCalendarTime(fireTimeStartDate);
            DayOfWeek dayOfWeekOfFireTime = fireTimeStartDateCal.DayOfWeek;

            // b2. We need to advance to another day if isAfterTimePassEndTimeOfDay is true, or dayOfWeek is not set.
            ISet<DayOfWeek> daysOfWeek = DaysOfWeek;
            if (forceToAdvanceNextDay || !daysOfWeek.Contains(dayOfWeekOfFireTime))
            {
                // Advance one day at a time until next available date.
                for (int i = 1; i <= 7; i++)
                {
                    fireTimeStartDateCal = fireTimeStartDateCal.AddDays(1);
                    dayOfWeekOfFireTime = fireTimeStartDateCal.DayOfWeek;
                    if (daysOfWeek.Contains(dayOfWeekOfFireTime))
                    {
                        fireTime = fireTimeStartDateCal;
                        break;
                    }
                }
            }

            // Check fireTime not pass the endTime
            DateTimeOffset? endTime = EndTimeUtc;

            if (endTime != null && fireTime > endTime.Value)
            {
                return null;
            }

            return fireTime;
        }

        /// <summary>
        /// Returns the final time at which the <see cref="IDailyTimeIntervalTrigger" /> will
        /// fire, if there is no end time set, null will be returned.
        /// </summary>
        /// <remarks>Note that the return time may be in the past.</remarks>
        /// <returns></returns>
        public override DateTimeOffset? FinalFireTimeUtc
        {
            get
            {
                if (complete || EndTimeUtc == null)
                {
                    return null;
                }

                // We have an endTime, we still need to check to see if there is a endTimeOfDay if that's applicable.
                DateTimeOffset? endTime = EndTimeUtc;
                if (endTimeOfDay != null)
                {
                    DateTimeOffset? endTimeOfDayDate = endTimeOfDay.GetTimeOfDayForDate(endTime);
                    if (endTime < endTimeOfDayDate)
                    {
                        endTime = endTimeOfDayDate;
                    }
                }
                return endTime;
            }
        }

        /// <summary>
        /// Determines whether or not the <see cref="IDailyTimeIntervalTrigger" /> will occur
        /// again.
        /// </summary>
        /// <returns></returns>
        public override bool GetMayFireAgain()
        {
            return GetNextFireTimeUtc() != null;
        }

        /// <summary>
        /// Validates whether the properties of the <see cref="IJobDetail" /> are
        /// valid for submission into a <see cref="IScheduler" />.
        /// </summary>
        public override void Validate()
        {
            base.Validate();

            if (repeatIntervalUnit != IntervalUnit.Second && repeatIntervalUnit != IntervalUnit.Minute && repeatIntervalUnit != IntervalUnit.Hour)
            {
                throw new SchedulerException("Invalid repeat IntervalUnit (must be Second, Minute or Hour).");
            }
            if (repeatInterval < 1)
            {
                throw new SchedulerException("Repeat Interval cannot be zero.");
            }

            // Ensure interval does not exceed 24 hours
            const long SecondsInHour = 24 * 60 * 60L;
            if (repeatIntervalUnit == IntervalUnit.Second && repeatInterval > SecondsInHour)
            {
                throw new SchedulerException("repeatInterval can not exceed 24 hours (" + SecondsInHour + " seconds). Given " + repeatInterval);
            }
            if (repeatIntervalUnit == IntervalUnit.Minute && repeatInterval > SecondsInHour / 60L)
            {
                throw new SchedulerException("repeatInterval can not exceed 24 hours (" + SecondsInHour / 60L + " minutes). Given " + repeatInterval);
            }
            if (repeatIntervalUnit == IntervalUnit.Hour && repeatInterval > 24)
            {
                throw new SchedulerException("repeatInterval can not exceed 24 hours. Given " + repeatInterval + " hours.");
            }

            // Ensure timeOfDay is in order.
            if (EndTimeOfDay != null && EndTimeOfDay.Before(StartTimeOfDay))
            {
                throw new SchedulerException("StartTimeOfDay " + startTimeOfDay + " should not come after endTimeOfDay " + endTimeOfDay);
            }
        }

        /// <summary>
        /// The days of the week upon which to fire.
        /// </summary>
        /// <returns>
        /// A Set containing the integers representing the days of the week, per the values 0-6 as defined by
        /// DayOfWees.Sunday - DayOfWeek.Saturday. 
        /// </returns>
        public ISet<DayOfWeek> DaysOfWeek
        {
            get
            {
                if (daysOfWeek == null)
                {
                    daysOfWeek = new HashSet<DayOfWeek>(DailyTimeIntervalScheduleBuilder.AllDaysOfTheWeek);
                }
                return daysOfWeek;
            }

            set
            {
                if (value == null || value.Count == 0)
                {
                    throw new ArgumentException("DaysOfWeek set must be a set that contains at least one day.");
                }
                if (value.Count == 0)
                {
                    throw new ArgumentException("DaysOfWeek set must contain at least one day.");
                }

                daysOfWeek = value;
            }
        }

        /// <summary>
        /// The time of day to start firing at the given interval.
        /// </summary>
        public TimeOfDay StartTimeOfDay
        {
            get
            {
                if (startTimeOfDay == null)
                {
                    startTimeOfDay = new TimeOfDay(0, 0, 0);
                }
                return startTimeOfDay;
            }
            set
            {
                if (value == null)
                {
                    throw new ArgumentException("Start time of day cannot be null");
                }

                TimeOfDay eTime = EndTimeOfDay;
                if (eTime != null && value != null && eTime.Before(value))
                {
                    throw new ArgumentException(
                        "End time of day cannot be before start time of day");
                }

                startTimeOfDay = value;
            }
        }

        /// <summary>
        /// The time of day to complete firing at the given interval.
        /// </summary>
        public TimeOfDay EndTimeOfDay
        {
            get { return endTimeOfDay; }
            set
            {
                if (value == null)
                {
                    throw new ArgumentException("End time of day cannot be null");
                }

                TimeOfDay sTime = StartTimeOfDay;
                if (sTime != null && endTimeOfDay != null && endTimeOfDay.Before(value))
                {
                    throw new ArgumentException("End time of day cannot be before start time of day");
                }
                endTimeOfDay = value;
            }
        }

        /// <summary>
        /// Get a <see cref="IScheduleBuilder" /> that is configured to produce a 
        /// schedule identical to this trigger's schedule.
        /// </summary>
        /// <returns></returns>
        /// <see cref="TriggerBuilder"/>
        public override IScheduleBuilder GetScheduleBuilder()
        {
            DailyTimeIntervalScheduleBuilder cb = DailyTimeIntervalScheduleBuilder.Create()
                .WithInterval(RepeatInterval, RepeatIntervalUnit)
                .OnDaysOfTheWeek(DaysOfWeek)
                .StartingDailyAt(StartTimeOfDay)
                .EndingDailyAt(EndTimeOfDay)
                .WithRepeatCount(RepeatCount)
                .InTimeZone(TimeZone);

            switch (MisfireInstruction)
            {
                case Quartz.MisfireInstruction.DailyTimeIntervalTrigger.DoNothing:
                    cb.WithMisfireHandlingInstructionDoNothing();
                    break;
                case Quartz.MisfireInstruction.DailyTimeIntervalTrigger.FireOnceNow:
                    cb.WithMisfireHandlingInstructionFireAndProceed();
                    break;
            }

            return cb;
        }

        /// <summary>
        /// This trigger has no additional properties besides what's defined in this class.
        /// </summary>
        /// <returns></returns>
        public override bool HasAdditionalProperties
        {
            get { return false; }
        }

        /// <summary>
        /// Tells whether this Trigger instance can handle events
        /// in millisecond precision.
        /// </summary>
        public override bool HasMillisecondPrecision
        {
            get { return true; }
        }
    }
}