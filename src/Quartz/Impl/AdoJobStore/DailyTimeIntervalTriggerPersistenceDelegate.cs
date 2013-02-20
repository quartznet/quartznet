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
using System.Globalization;
using System.Linq;
using System.Text;

using Quartz.Impl.Triggers;
using Quartz.Spi;
using Quartz.Collection;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Persist a DailyTimeIntervalTrigger by converting internal fields to and from
    /// SimplePropertiesTriggerProperties.
    /// </summary>
    /// <see cref="DailyTimeIntervalScheduleBuilder"/>
    /// <see cref="IDailyTimeIntervalTrigger"/>
    /// <author>Zemian Deng saltnlight5@gmail.com</author>
    /// <author>Nuno Maia (.NET)</author>
    public class DailyTimeIntervalTriggerPersistenceDelegate : SimplePropertiesTriggerPersistenceDelegateSupport
    {
        public override bool CanHandleTriggerType(IOperableTrigger trigger)
        {
            return ((trigger is DailyTimeIntervalTriggerImpl) &&
                    !((DailyTimeIntervalTriggerImpl) trigger).HasAdditionalProperties);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public override string GetHandledTriggerTypeDiscriminator()
        {
            return AdoConstants.TriggerTypeDailyTimeInterval;
        }

        protected override SimplePropertiesTriggerProperties GetTriggerProperties(IOperableTrigger trigger)
        {
            DailyTimeIntervalTriggerImpl dailyTrigger = (DailyTimeIntervalTriggerImpl) trigger;
            SimplePropertiesTriggerProperties props = new SimplePropertiesTriggerProperties();

            props.Int1 = dailyTrigger.RepeatInterval;
            props.String1 = dailyTrigger.RepeatIntervalUnit.ToString();
            props.Int2 = dailyTrigger.TimesTriggered;

            ISet<DayOfWeek> days = dailyTrigger.DaysOfWeek;
            string daysStr = string.Join(",", days.Cast<int>().Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray());
            props.String2 = daysStr;

            StringBuilder timeOfDayBuffer = new StringBuilder();
            TimeOfDay startTimeOfDay = dailyTrigger.StartTimeOfDay;
            if (startTimeOfDay != null)
            {
                timeOfDayBuffer.Append(startTimeOfDay.Hour).Append(",");
                timeOfDayBuffer.Append(startTimeOfDay.Minute).Append(",");
                timeOfDayBuffer.Append(startTimeOfDay.Second).Append(",");
            }
            else
            {
                timeOfDayBuffer.Append(",,,");
            }

            TimeOfDay endTimeOfDay = dailyTrigger.EndTimeOfDay;
            if (endTimeOfDay != null)
            {
                timeOfDayBuffer.Append(endTimeOfDay.Hour).Append(",");
                timeOfDayBuffer.Append(endTimeOfDay.Minute).Append(",");
                timeOfDayBuffer.Append(endTimeOfDay.Second);
            }
            else
            {
                timeOfDayBuffer.Append(",,,");
            }
            props.String3 = timeOfDayBuffer.ToString();
            props.Long1 = dailyTrigger.RepeatCount;
            return props;
        }

        protected override TriggerPropertyBundle GetTriggerPropertyBundle(SimplePropertiesTriggerProperties props)
        {
            int repeatCount = (int) props.Long1;
            int interval = props.Int1;
            string intervalUnitStr = props.String1;
            string daysOfWeekStr = props.String2;
            string timeOfDayStr = props.String3;

            IntervalUnit intervalUnit = (IntervalUnit) Enum.Parse(typeof (IntervalUnit), intervalUnitStr, true);
            DailyTimeIntervalScheduleBuilder scheduleBuilder = DailyTimeIntervalScheduleBuilder.Create()
                .WithInterval(interval, intervalUnit)
                .WithRepeatCount(repeatCount);

            if (daysOfWeekStr != null)
            {
                ISet<DayOfWeek> daysOfWeek = new HashSet<DayOfWeek>();
                string[] nums = daysOfWeekStr.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries);
                if (nums.Length > 0)
                {
                    foreach (String num in nums)
                    {
                        daysOfWeek.Add((DayOfWeek) Int32.Parse(num));
                    }
                    scheduleBuilder.OnDaysOfTheWeek(daysOfWeek);
                }
            }
            else
            {
                scheduleBuilder.OnDaysOfTheWeek(DailyTimeIntervalScheduleBuilder.AllDaysOfTheWeek);
            }

            if (timeOfDayStr != null)
            {
                string[] nums = timeOfDayStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                TimeOfDay startTimeOfDay;
                if (nums.Length >= 3)
                {
                    int hour = Int32.Parse(nums[0]);
                    int min = Int32.Parse(nums[1]);
                    int sec = Int32.Parse(nums[2]);
                    startTimeOfDay = new TimeOfDay(hour, min, sec);
                }
                else
                {
                    startTimeOfDay = TimeOfDay.HourMinuteAndSecondOfDay(0, 0, 0);
                }
                scheduleBuilder.StartingDailyAt(startTimeOfDay);

                TimeOfDay endTimeOfDay;
                if (nums.Length >= 6)
                {
                    int hour = Int32.Parse(nums[3]);
                    int min = Int32.Parse(nums[4]);
                    int sec = Int32.Parse(nums[5]);
                    endTimeOfDay = new TimeOfDay(hour, min, sec);
                }
                else
                {
                    endTimeOfDay = TimeOfDay.HourMinuteAndSecondOfDay(23, 59, 59);
                }
                scheduleBuilder.EndingDailyAt(endTimeOfDay);
            }
            else
            {
                scheduleBuilder.StartingDailyAt(TimeOfDay.HourMinuteAndSecondOfDay(0, 0, 0));
                scheduleBuilder.EndingDailyAt(TimeOfDay.HourMinuteAndSecondOfDay(23, 59, 59));
            }


            int timesTriggered = props.Int2;
            string[] statePropertyNames = {"timesTriggered"};
            object[] statePropertyValues = {timesTriggered};

            return new TriggerPropertyBundle(scheduleBuilder, statePropertyNames, statePropertyValues);
        }
    }
}