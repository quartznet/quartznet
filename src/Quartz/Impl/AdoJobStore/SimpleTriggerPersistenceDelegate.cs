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
using System.Data;

using Quartz.Impl.Triggers;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore
{
    public class SimpleTriggerPersistenceDelegate : ITriggerPersistenceDelegate
    {
        protected string tablePrefix;
        private AdoUtil adoUtil;
        private string schedNameLiteral;

        public void Initialize(string tablePrefix, string schedName, AdoUtil adoUtil)
        {
            this.tablePrefix = tablePrefix;
            this.schedNameLiteral = "'" + schedName + "'";
            this.adoUtil = adoUtil;
        }

        public string GetHandledTriggerTypeDiscriminator()
        {
            return AdoConstants.TriggerTypeSimple;
        }

        public bool CanHandleTriggerType(IOperableTrigger trigger)
        {
            return ((trigger is SimpleTriggerImpl) && !((SimpleTriggerImpl) trigger).HasAdditionalProperties);
        }

        public int DeleteExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            using (IDbCommand cmd = adoUtil.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlDeleteSimpleTrigger, tablePrefix, schedNameLiteral)))
            {
                adoUtil.AddCommandParameter(cmd, "triggerName", triggerKey.Name);
                adoUtil.AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

                return cmd.ExecuteNonQuery();
            }
        }

        public int InsertExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail)
        {
            ISimpleTrigger simpleTrigger = (ISimpleTrigger) trigger;

            using (IDbCommand cmd = adoUtil.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlInsertSimpleTrigger, tablePrefix, schedNameLiteral)))
            {
                adoUtil.AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
                adoUtil.AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);
                adoUtil.AddCommandParameter(cmd, "triggerRepeatCount", simpleTrigger.RepeatCount);
                adoUtil.AddCommandParameter(cmd, "triggerRepeatInterval", simpleTrigger.RepeatInterval.TotalMilliseconds);
                adoUtil.AddCommandParameter(cmd, "triggerTimesTriggered", simpleTrigger.TimesTriggered);

                return cmd.ExecuteNonQuery();
            }
        }

        public TriggerPropertyBundle LoadExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {

            using (IDbCommand cmd = adoUtil.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectSimpleTrigger, tablePrefix, schedNameLiteral)))
            {
                adoUtil.AddCommandParameter(cmd, "", triggerKey.Name);
                adoUtil.AddCommandParameter(cmd, "", triggerKey.Group);
                IDataReader rs = cmd.ExecuteReader();

                if (rs.Read())
                {
                    int repeatCount = rs.GetInt32(AdoConstants.ColumnRepeatCount);
                    long repeatInterval = rs.GetInt64(AdoConstants.ColumnRepeatInterval);
                    int timesTriggered = rs.GetInt32(AdoConstants.ColumnTimesTriggered);

                    SimpleScheduleBuilder sb = SimpleScheduleBuilder.simpleSchedule()
                        .withRepeatCount(repeatCount)
                        .withIntervalInMilliseconds(repeatInterval);

                    string[] statePropertyNames = {"timesTriggered"};
                    object[] statePropertyValues = {timesTriggered};

                    return new TriggerPropertyBundle(sb, statePropertyNames, statePropertyValues);
                }

                throw new InvalidOperationException("No record found for selection of Trigger with key: '" + triggerKey + "' and statement: " + AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectSimpleTrigger, tablePrefix, schedNameLiteral));
            }
        }

        public int UpdateExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail)
        {
            ISimpleTrigger simpleTrigger = (ISimpleTrigger) trigger;

            using (IDbCommand cmd = adoUtil.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlUpdateSimpleTrigger, tablePrefix, schedNameLiteral)))
            {
                adoUtil.AddCommandParameter(cmd, "triggerRepeatCount", simpleTrigger.RepeatCount);
                adoUtil.AddCommandParameter(cmd, "triggerRepeatInterval", simpleTrigger.RepeatInterval.TotalMilliseconds);
                adoUtil.AddCommandParameter(cmd, "triggerTimesTriggered", simpleTrigger.TimesTriggered);
                adoUtil.AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
                adoUtil.AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);

                return cmd.ExecuteNonQuery();
            }
        }
    }
}