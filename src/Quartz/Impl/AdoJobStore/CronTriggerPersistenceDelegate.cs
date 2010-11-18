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

namespace Quartz.Impl.AdoJobStore
{
    public class CronTriggerPersistenceDelegate : ITriggerPersistenceDelegate
    {
        protected string tablePrefix;
        private AdoUtil adoUtil;

        public void Initialize(string tablePrefix, AdoUtil adoUtil)
        {
            this.tablePrefix = tablePrefix;
            this.adoUtil = adoUtil;
        }

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
            using (IDbCommand cmd = adoUtil.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlDeleteCronTrigger, tablePrefix)))
            {
                adoUtil.AddCommandParameter(cmd, "triggerName", triggerKey.Name);
                adoUtil.AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

                return cmd.ExecuteNonQuery();
            }
        }

        public int InsertExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail)
        {
            ICronTrigger cronTrigger = (ICronTrigger) trigger;

            using (IDbCommand cmd = adoUtil.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlInsertCronTrigger)))
            {
                adoUtil.AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
                adoUtil.AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);
                adoUtil.AddCommandParameter(cmd, "triggerCronExpression", cronTrigger.CronExpressionString);
                adoUtil.AddCommandParameter(cmd, "triggerTimeZone", cronTrigger.TimeZone.Id);

                return cmd.ExecuteNonQuery();
            }
        }

        public TriggerPropertyBundle LoadExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey triggerKey)
        {
            PreparedStatement ps = null;
            ResultSet rs = null;

            ps = conn.prepareStatement(AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectCronTriggers, tablePrefix));
                ps.setString(1, triggerKey.getName());
                ps.setString(2, triggerKey.getGroup());
                rs = ps.executeQuery();

                if (rs.next())
                {
                    string cronExpr = rs.getString(COL_CRON_EXPRESSION);
                    string timeZoneId = rs.getString(COL_TIME_ZONE_ID);

                    CronScheduleBuilder cb = null;
                    try
                    {
                        cb = CronScheduleBuilder.cronSchedule(cronExpr);
                    }
                    catch (FormatException neverHappens)
                    {
                        // Can't happen because the expression must have been valid in order to get persisted
                    }

                    if (timeZoneId != null)
                    {
                        cb.inTimeZone(TimeZoneInfo.getTimeZone(timeZoneId));
                    }

                    return new TriggerPropertyBundle(cb, null, null);
                }

                throw new InvalidOperationException("No record found for selection of Trigger with key: '" + triggerKey + "' and statement: " + Util.rtp(SELECT_CRON_TRIGGER, tablePrefix));

        }

        public int UpdateExtendedTriggerProperties(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail)
        {
            ICronTrigger cronTrigger = (ICronTrigger) trigger;

            using (IDbCommand cmd = adoUtil.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlUpdateCronTrigger, tablePrefix)))
            {
                adoUtil.AddCommandParameter(cmd, "triggerCronExpression", cronTrigger.CronExpressionString);
                adoUtil.AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
                adoUtil.AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);

                return cmd.ExecuteNonQuery();
            }
        }
    }
}