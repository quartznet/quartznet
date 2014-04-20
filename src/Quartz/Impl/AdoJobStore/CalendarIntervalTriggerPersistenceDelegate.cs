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

using Quartz.Impl.Triggers;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Persist a CalendarIntervalTriggerImpl by converting internal fields to and from
    /// SimplePropertiesTriggerProperties.
    /// </summary>
    /// <see cref="CalendarIntervalScheduleBuilder"/>
    /// <see cref="ICalendarIntervalTrigger"/>
    public class CalendarIntervalTriggerPersistenceDelegate : SimplePropertiesTriggerPersistenceDelegateSupport
    {
        public override bool CanHandleTriggerType(IOperableTrigger trigger)
        {
            return ((trigger is CalendarIntervalTriggerImpl) && !((CalendarIntervalTriggerImpl) trigger).HasAdditionalProperties);
        }

        public override string GetHandledTriggerTypeDiscriminator()
        {
            return AdoConstants.TriggerTypeCalendarInterval;
        }

        protected override SimplePropertiesTriggerProperties GetTriggerProperties(IOperableTrigger trigger)
        {
            CalendarIntervalTriggerImpl calTrig = (CalendarIntervalTriggerImpl) trigger;

            SimplePropertiesTriggerProperties props = new SimplePropertiesTriggerProperties();

            props.Int1 = calTrig.RepeatInterval;
            props.String1 = calTrig.RepeatIntervalUnit.ToString();
            props.Int2 = calTrig.TimesTriggered;
            props.String2 = calTrig.TimeZone.Id;
            props.Boolean1 = calTrig.PreserveHourOfDayAcrossDaylightSavings;
            props.Boolean2 = calTrig.SkipDayIfHourDoesNotExist;

            return props;
        }

        protected override TriggerPropertyBundle GetTriggerPropertyBundle(SimplePropertiesTriggerProperties props)
        {
            TimeZoneInfo tz = null; // if we use null, that's ok as system default tz will be used
            string tzId = props.String2;
            if (tzId != null && tzId.Trim().Length != 0) // there could be null entries from previously released versions
            {
                tz = TimeZoneUtil.FindTimeZoneById(tzId);
            }

            CalendarIntervalScheduleBuilder sb = CalendarIntervalScheduleBuilder.Create()
                .WithInterval(props.Int1, (IntervalUnit) Enum.Parse(typeof(IntervalUnit), props.String1, true))
                .InTimeZone(tz)
                .PreserveHourOfDayAcrossDaylightSavings(props.Boolean1)
                .SkipDayIfHourDoesNotExist(props.Boolean2);

            int timesTriggered = props.Int2;

            string[] statePropertyNames = {"timesTriggered"};
            object[] statePropertyValues = {timesTriggered};

            return new TriggerPropertyBundle(sb, statePropertyNames, statePropertyValues);
        }
    }
}