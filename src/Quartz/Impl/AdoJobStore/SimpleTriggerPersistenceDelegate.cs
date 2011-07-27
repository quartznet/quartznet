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
        protected ICommandAccessor CommandAccessor { get; private set; }

        protected string TablePrefix { get; private set; }

        protected string SchedNameLiteral { get; private set; }

        public void Initialize(string tablePrefix, string schedName, ICommandAccessor commandAccessor)
        {
            TablePrefix = tablePrefix;
            SchedNameLiteral = "'" + schedName + "'";
            CommandAccessor = commandAccessor;
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
            using (IDbCommand cmd = CommandAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlDeleteSimpleTrigger, TablePrefix, SchedNameLiteral)))
            {
                CommandAccessor.AddCommandParameter(cmd, "triggerName", triggerKey.Name);
                CommandAccessor.AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

                return cmd.ExecuteNonQuery();
            }
        }

        public int InsertExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail)
        {
            ISimpleTrigger simpleTrigger = (ISimpleTrigger) trigger;

            using (IDbCommand cmd = CommandAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlInsertSimpleTrigger, TablePrefix, SchedNameLiteral)))
            {
                CommandAccessor.AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
                CommandAccessor.AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);
                CommandAccessor.AddCommandParameter(cmd, "triggerRepeatCount", simpleTrigger.RepeatCount);
                CommandAccessor.AddCommandParameter(cmd, "triggerRepeatInterval", simpleTrigger.RepeatInterval.TotalMilliseconds);
                CommandAccessor.AddCommandParameter(cmd, "triggerTimesTriggered", simpleTrigger.TimesTriggered);

                return cmd.ExecuteNonQuery();
            }
        }

        public TriggerPropertyBundle LoadExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            using (IDbCommand cmd = CommandAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectSimpleTrigger, TablePrefix, SchedNameLiteral)))
            {
                CommandAccessor.AddCommandParameter(cmd, "triggerName", triggerKey.Name);
                CommandAccessor.AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

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
                throw new InvalidOperationException("No record found for selection of Trigger with key: '" + triggerKey + "' and statement: " + AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectSimpleTrigger, TablePrefix, SchedNameLiteral));
            }
        }

        public int UpdateExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail)
        {
            ISimpleTrigger simpleTrigger = (ISimpleTrigger) trigger;

            using (IDbCommand cmd = CommandAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlUpdateSimpleTrigger, TablePrefix, SchedNameLiteral)))
            {
                CommandAccessor.AddCommandParameter(cmd, "triggerRepeatCount", simpleTrigger.RepeatCount);
                CommandAccessor.AddCommandParameter(cmd, "triggerRepeatInterval", simpleTrigger.RepeatInterval.TotalMilliseconds);
                CommandAccessor.AddCommandParameter(cmd, "triggerTimesTriggered", simpleTrigger.TimesTriggered);
                CommandAccessor.AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
                CommandAccessor.AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);

                return cmd.ExecuteNonQuery();
            }
        }
    }
}