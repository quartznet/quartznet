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
        private ICommandAccessor commandAccessor;
        protected string tablePrefix;
        private string schedNameLiteral;

        public SimpleTriggerPersistenceDelegate()
        {
        }

        public void Initialize(string tablePrefix, string schedName, ICommandAccessor commandAccessor)
        {
            this.tablePrefix = tablePrefix;
            schedNameLiteral = "'" + schedName + "'";
            this.commandAccessor = commandAccessor;
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
            using (IDbCommand cmd = commandAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlDeleteSimpleTrigger, tablePrefix, schedNameLiteral)))
            {
                commandAccessor.AddCommandParameter(cmd, "triggerName", triggerKey.Name);
                commandAccessor.AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

                return cmd.ExecuteNonQuery();
            }
        }

        public int InsertExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail)
        {
            ISimpleTrigger simpleTrigger = (ISimpleTrigger) trigger;

            using (IDbCommand cmd = commandAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlInsertSimpleTrigger, tablePrefix, schedNameLiteral)))
            {
                commandAccessor.AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
                commandAccessor.AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);
                commandAccessor.AddCommandParameter(cmd, "triggerRepeatCount", simpleTrigger.RepeatCount);
                commandAccessor.AddCommandParameter(cmd, "triggerRepeatInterval", simpleTrigger.RepeatInterval.TotalMilliseconds);
                commandAccessor.AddCommandParameter(cmd, "triggerTimesTriggered", simpleTrigger.TimesTriggered);

                return cmd.ExecuteNonQuery();
            }
        }

        public TriggerPropertyBundle LoadExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            using (IDbCommand cmd = commandAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectSimpleTrigger, tablePrefix, schedNameLiteral)))
            {
                commandAccessor.AddCommandParameter(cmd, "triggerName", triggerKey.Name);
                commandAccessor.AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        int repeatCount = rs.GetInt32(AdoConstants.ColumnRepeatCount);
                        long repeatInterval = rs.GetInt64(AdoConstants.ColumnRepeatInterval);
                        int timesTriggered = rs.GetInt32(AdoConstants.ColumnTimesTriggered);

                        SimpleScheduleBuilder sb = SimpleScheduleBuilder.Create()
                            .WithRepeatCount(repeatCount)
                            .WithInterval(TimeSpan.FromMilliseconds(repeatInterval));

                        string[] statePropertyNames = {"timesTriggered"};
                        object[] statePropertyValues = {timesTriggered};

                        return new TriggerPropertyBundle(sb, statePropertyNames, statePropertyValues);
                    }
                }
                throw new InvalidOperationException("No record found for selection of Trigger with key: '" + triggerKey + "' and statement: " + AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectSimpleTrigger, tablePrefix, schedNameLiteral));
            }
        }

        public int UpdateExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail)
        {
            ISimpleTrigger simpleTrigger = (ISimpleTrigger) trigger;

            using (IDbCommand cmd = commandAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlUpdateSimpleTrigger, tablePrefix, schedNameLiteral)))
            {
                commandAccessor.AddCommandParameter(cmd, "triggerRepeatCount", simpleTrigger.RepeatCount);
                commandAccessor.AddCommandParameter(cmd, "triggerRepeatInterval", simpleTrigger.RepeatInterval.TotalMilliseconds);
                commandAccessor.AddCommandParameter(cmd, "triggerTimesTriggered", simpleTrigger.TimesTriggered);
                commandAccessor.AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
                commandAccessor.AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);

                return cmd.ExecuteNonQuery();
            }
        }
    }
}