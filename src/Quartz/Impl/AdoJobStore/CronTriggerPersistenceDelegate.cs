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
    public class CronTriggerPersistenceDelegate : ITriggerPersistenceDelegate
    {
        public void Initialize(string tablePrefix, string schedName, ICommandAccessor commandAccessor)
        {
            TablePrefix = tablePrefix;
            SchedNameLiteral = "'" + schedName + "'";
            CommandAccessor = commandAccessor;
        }

        protected string TablePrefix { get; private set; }

        protected ICommandAccessor CommandAccessor { get; private set; }

        protected string SchedNameLiteral { get; private set; }

        public string GetHandledTriggerTypeDiscriminator()
        {
            return AdoConstants.TriggerTypeCron;
        }

        public bool CanHandleTriggerType(IOperableTrigger trigger)
        {
            return ((trigger is CronTriggerImpl) && !((CronTriggerImpl) trigger).HasAdditionalProperties);
        }

        public int DeleteExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            using (IDbCommand cmd = CommandAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlDeleteCronTrigger, TablePrefix, SchedNameLiteral)))
            {
                CommandAccessor.AddCommandParameter(cmd, "triggerName", triggerKey.Name);
                CommandAccessor.AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

                return cmd.ExecuteNonQuery();
            }
        }

        public int InsertExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail)
        {
            ICronTrigger cronTrigger = (ICronTrigger) trigger;

            using (IDbCommand cmd = CommandAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlInsertCronTrigger, TablePrefix, SchedNameLiteral)))
            {
                CommandAccessor.AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
                CommandAccessor.AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);
                CommandAccessor.AddCommandParameter(cmd, "triggerCronExpression", cronTrigger.CronExpressionString);
                CommandAccessor.AddCommandParameter(cmd, "triggerTimeZone", cronTrigger.TimeZone.Id);

                return cmd.ExecuteNonQuery();
            }
        }

        public TriggerPropertyBundle LoadExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            using (IDbCommand cmd = CommandAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectCronTriggers, TablePrefix, SchedNameLiteral)))
            {
                CommandAccessor.AddCommandParameter(cmd, "triggerName", triggerKey.Name);
                CommandAccessor.AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

                using (IDataReader rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        string cronExpr = rs.GetString(AdoConstants.ColumnCronExpression);
                        string timeZoneId = rs.GetString(AdoConstants.ColumnTimeZoneId);

                        CronScheduleBuilder cb = CronScheduleBuilder.CronSchedule(cronExpr);
  
                        if (timeZoneId != null)
                        {
                            cb.InTimeZone(TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));
                        }

                        return new TriggerPropertyBundle(cb, null, null);
                    }
                }

                throw new InvalidOperationException("No record found for selection of Trigger with key: '" + triggerKey + "' and statement: " + AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectCronTriggers, TablePrefix, SchedNameLiteral));
            }
        }

        public int UpdateExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail)
        {
            ICronTrigger cronTrigger = (ICronTrigger) trigger;

            using (IDbCommand cmd = CommandAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlUpdateCronTrigger, TablePrefix, SchedNameLiteral)))
            {
                CommandAccessor.AddCommandParameter(cmd, "triggerCronExpression", cronTrigger.CronExpressionString);
                CommandAccessor.AddCommandParameter(cmd, "timeZoneId", cronTrigger.TimeZone.Id);
                CommandAccessor.AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
                CommandAccessor.AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);

                return cmd.ExecuteNonQuery();
            }
        }
    }
}