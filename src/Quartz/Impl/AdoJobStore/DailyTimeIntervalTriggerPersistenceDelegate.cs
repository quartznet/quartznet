#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
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

using System.Globalization;
using System.Text;

using Quartz.Impl.Triggers;
using Quartz.Extensibility;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Persist a DailyTimeIntervalTrigger by converting internal fields to and from
/// SimplePropertiesTriggerProperties.
/// </summary>
/// <see cref="DailyTimeIntervalScheduleBuilder"/>
/// <see cref="IDailyTimeIntervalTrigger"/>
/// <author>Zemian Deng saltnlight5@gmail.com</author>
/// <author>Nuno Maia (.NET)</author>
public sealed class DailyTimeIntervalTriggerPersistenceDelegate : SimplePropertiesTriggerPersistenceDelegateBase
{
    /// <inheritdoc />
    public override bool CanHandleTriggerType(IOperableTrigger trigger)
    {
        var dailyTimeIntervalTrigger = trigger as DailyTimeIntervalTriggerImpl;
        return dailyTimeIntervalTrigger is not null &&
               !dailyTimeIntervalTrigger.HasAdditionalProperties;
    }

    /// <inheritdoc />
    public override string GetHandledTriggerTypeDiscriminator()
    {
        return AdoConstants.TriggerTypeDailyTimeInterval;
    }

    /// <inheritdoc />
    protected override SimplePropertiesTriggerProperties GetTriggerProperties(IOperableTrigger trigger)
    {
        DailyTimeIntervalTriggerImpl dailyTrigger = (DailyTimeIntervalTriggerImpl) trigger;

        var days = dailyTrigger.DaysOfWeek;
        string daysStr = string.Join(",", days.Cast<int>().Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray());

        StringBuilder timeOfDayBuffer = new StringBuilder();
        TimeOnly startTimeOfDay = dailyTrigger.StartTimeOfDay;
        timeOfDayBuffer.Append(startTimeOfDay.Hour).Append(',');
        timeOfDayBuffer.Append(startTimeOfDay.Minute).Append(',');
        timeOfDayBuffer.Append(startTimeOfDay.Second).Append(',');

        TimeOnly endTimeOfDay = dailyTrigger.EndTimeOfDay;
        timeOfDayBuffer.Append(endTimeOfDay.Hour).Append(',');
        timeOfDayBuffer.Append(endTimeOfDay.Minute).Append(',');
        timeOfDayBuffer.Append(endTimeOfDay.Second);

        return new SimplePropertiesTriggerProperties
        {
            Int1 = dailyTrigger.RepeatInterval,
            String1 = dailyTrigger.RepeatIntervalUnit.ToString(),
            Int2 = dailyTrigger.TimesTriggered,
            String2 = daysStr,
            String3 = timeOfDayBuffer.ToString(),
            Long1 = dailyTrigger.RepeatCount,
            TimeZoneId = dailyTrigger.TimeZone.Id,
        };
    }

    /// <inheritdoc />
    protected override TriggerPropertyBundle GetTriggerPropertyBundle(SimplePropertiesTriggerProperties props)
    {
        int repeatCount = (int) props.Long1;
        int interval = props.Int1;
        var intervalUnitStr = props.String1;
        var daysOfWeekStr = props.String2;
        var timeOfDayStr = props.String3;

        IntervalUnit intervalUnit = (IntervalUnit) Enum.Parse(typeof(IntervalUnit), intervalUnitStr!, true);
        DailyTimeIntervalScheduleBuilder scheduleBuilder = DailyTimeIntervalScheduleBuilder.Create()
            .WithInterval(interval, intervalUnit)
            .WithRepeatCount(repeatCount);

        if (!string.IsNullOrEmpty(props.TimeZoneId) && props.TimeZoneId is not null)
        {
            scheduleBuilder.InTimeZone(TimeZones.FindById(props.TimeZoneId));
        }

        if (daysOfWeekStr is not null)
        {
            var daysOfWeek = new HashSet<DayOfWeek>();
            string[] nums = daysOfWeekStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (nums.Length > 0)
            {
                foreach (string num in nums)
                {
                    daysOfWeek.Add((DayOfWeek) int.Parse(num));
                }
                scheduleBuilder.OnDaysOfTheWeek(daysOfWeek);
            }
        }
        else
        {
            scheduleBuilder.OnDaysOfTheWeek(DailyTimeIntervalScheduleBuilder.AllDaysOfTheWeek);
        }

        if (timeOfDayStr is not null)
        {
            string[] nums = timeOfDayStr.Split(',', StringSplitOptions.RemoveEmptyEntries);
            TimeOnly startTimeOfDay;
            if (nums.Length >= 3)
            {
                int hour = int.Parse(nums[0]);
                int min = int.Parse(nums[1]);
                int sec = int.Parse(nums[2]);
                startTimeOfDay = new TimeOnly(hour, min, sec);
            }
            else
            {
                startTimeOfDay = DailyTimeIntervalTriggerImpl.DefaultStartTimeOfDay;
            }
            scheduleBuilder.StartingDailyAt(startTimeOfDay);

            TimeOnly endTimeOfDay;
            if (nums.Length >= 6)
            {
                int hour = int.Parse(nums[3]);
                int min = int.Parse(nums[4]);
                int sec = int.Parse(nums[5]);
                endTimeOfDay = new TimeOnly(hour, min, sec);
            }
            else
            {
                endTimeOfDay = DailyTimeIntervalTriggerImpl.DefaultEndTimeOfDay;
            }
            scheduleBuilder.EndingDailyAt(endTimeOfDay);
        }
        else
        {
            scheduleBuilder.StartingDailyAt(DailyTimeIntervalTriggerImpl.DefaultStartTimeOfDay);
            scheduleBuilder.EndingDailyAt(DailyTimeIntervalTriggerImpl.DefaultEndTimeOfDay);
        }


        int timesTriggered = props.Int2;

        return new TriggerPropertyBundle(scheduleBuilder, t => ((DailyTimeIntervalTriggerImpl) t).TimesTriggered = timesTriggered);
    }
}