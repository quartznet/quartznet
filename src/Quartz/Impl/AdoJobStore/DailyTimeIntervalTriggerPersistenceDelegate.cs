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
    /// <since>2.0.3</since>
    /// <author>Zemian Deng saltnlight5@gmail.com</author>
    /// <author>Nuno Maia (.NET)</author>
    public class DailyTimeIntervalTriggerPersistenceDelegate : SimplePropertiesTriggerPersistenceDelegateSupport
    {
    
        public override bool CanHandleTriggerType(IOperableTrigger trigger) 
        {
            return ((trigger is DailyTimeIntervalTriggerImpl) && !((DailyTimeIntervalTriggerImpl)trigger).HasAdditionalProperties);
        }


        public override string GetHandledTriggerTypeDiscriminator()
        {
            return AdoConstants.TriggerTypeDailyTimeInterval;
        }

        protected override SimplePropertiesTriggerProperties GetTriggerProperties(IOperableTrigger trigger)
        {

            DailyTimeIntervalTriggerImpl dailyTrigger = (DailyTimeIntervalTriggerImpl)trigger;
            SimplePropertiesTriggerProperties props = new SimplePropertiesTriggerProperties();

            props.Int1 = dailyTrigger.RepeatInterval;
            props.String1 = dailyTrigger.RepeatIntervalUnit.ToString();
            props.Int2 = dailyTrigger.TimesTriggered;

            ISet<DayOfWeek> days = dailyTrigger.DaysOfWeek;
            String daysStr = join(days, ",");
            props.String2 = daysStr;

            TimeOfDay startTimeOfDay = dailyTrigger.StartTimeOfDayUtc;
            TimeOfDay endTimeOfDay = dailyTrigger.EndTimeOfDayUtc;
            StringBuilder timeOfDayBuffer = new StringBuilder();
            timeOfDayBuffer.Append(startTimeOfDay.Hour).Append(",");
            timeOfDayBuffer.Append(startTimeOfDay.Minute).Append(",");
            timeOfDayBuffer.Append(startTimeOfDay.Second).Append(",");
            timeOfDayBuffer.Append(endTimeOfDay.Hour).Append(",");
            timeOfDayBuffer.Append(endTimeOfDay.Minute).Append(",");
            timeOfDayBuffer.Append(endTimeOfDay.Second);
            props.String3 = timeOfDayBuffer.ToString();

            return props;
        }

        private String join(ISet<DayOfWeek> days, String sep)
        {
            StringBuilder sb = new StringBuilder();
            if (days == null || days.Count <= 0)
                return "";

            foreach (DayOfWeek itr in days)
            {
                sb.Append(sep).Append(itr);
            }
            
            return sb.ToString();
        }

        protected override TriggerPropertyBundle GetTriggerPropertyBundle(SimplePropertiesTriggerProperties props) {
		    int interval = props.Int1;
		    String intervalUnitStr = props.String1;
		    String daysOfWeekStr = props.String2;
		    String timeOfDayStr = props.String3;

            IntervalUnit intervalUnit = (IntervalUnit) Enum.Parse(typeof(IntervalUnit), props.String1, true);
		    DailyTimeIntervalScheduleBuilder scheduleBuilder = DailyTimeIntervalScheduleBuilder.Create()
        		    .WithInterval(interval, intervalUnit);
        		
		    if (daysOfWeekStr != null) 
            {
	            ISet<DayOfWeek> daysOfWeek = new HashSet<DayOfWeek>();
	            String[] nums = daysOfWeekStr.Split(',');
	            if (nums.Length > 0) 
                {
		            foreach (String num in nums) 
                    {
                        daysOfWeek.Add((DayOfWeek) Int32.Parse(num));
		            }
                    scheduleBuilder.OnDaysOfTheWeek(daysOfWeek);
	            }
		    }
		    if (timeOfDayStr != null) 
            {
	            String[] nums = timeOfDayStr.Split(',');
	            if (nums.Length >= 6) 
                {
	        	    int hour = Int32.Parse(nums[0]);
	        	    int min = Int32.Parse(nums[1]);
	        	    int sec = Int32.Parse(nums[2]);
				    TimeOfDay startTimeOfDay = new TimeOfDay(hour, min, sec);
	        	    scheduleBuilder.StartingDailyAt(startTimeOfDay);
	        	
	        	    hour = Int32.Parse(nums[3]);
	        	    min = Int32.Parse(nums[4]);
                    sec = Int32.Parse(nums[5]);
				    TimeOfDay endTimeOfDay = new TimeOfDay(hour, min, sec);
	        	    scheduleBuilder.EndingDailyAt(endTimeOfDay);
	            }
		}
        
        int timesTriggered = props.Int2;
        String[] statePropertyNames = { "timesTriggered" };
        Object[] statePropertyValues = { timesTriggered };

        return new TriggerPropertyBundle(scheduleBuilder, statePropertyNames, statePropertyValues);
    }

    }
}
