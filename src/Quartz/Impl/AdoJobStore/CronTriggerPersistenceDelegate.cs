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

using System;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl.Triggers;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore
{
    /// <summary>
    /// Persist a CronTriggerImpl.
    /// </summary>
    /// <see cref="CronScheduleBuilder"/>
    /// <see cref="ICronTrigger"/>
    public class CronTriggerPersistenceDelegate : ITriggerPersistenceDelegate
    {
        public void Initialize(string tablePrefix, string schedName, IDbAccessor dbAccessor)
        {
            TablePrefix = tablePrefix;
            SchedNameLiteral = "'" + schedName + "'";
            DbAccessor = dbAccessor;
        }

        protected string TablePrefix { get; private set; }

        protected IDbAccessor DbAccessor { get; private set; }

        protected string SchedNameLiteral { get; private set; }

        public string GetHandledTriggerTypeDiscriminator()
        {
            return AdoConstants.TriggerTypeCron;
        }

        public bool CanHandleTriggerType(IOperableTrigger trigger)
        {
            return trigger is CronTriggerImpl impl && !impl.HasAdditionalProperties;
        }

        public async Task<int> DeleteExtendedTriggerProperties(
            ConnectionAndTransactionHolder conn, 
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            using (var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlDeleteCronTrigger, TablePrefix, SchedNameLiteral)))
            {
                DbAccessor.AddCommandParameter(cmd, "triggerName", triggerKey.Name);
                DbAccessor.AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

                return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<int> InsertExtendedTriggerProperties(
            ConnectionAndTransactionHolder conn, 
            IOperableTrigger trigger, 
            string state, 
            IJobDetail jobDetail,
            CancellationToken cancellationToken = default)
        {
            ICronTrigger cronTrigger = (ICronTrigger) trigger;

            using (var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlInsertCronTrigger, TablePrefix, SchedNameLiteral)))
            {
                DbAccessor.AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
                DbAccessor.AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);
                DbAccessor.AddCommandParameter(cmd, "triggerCronExpression", cronTrigger.CronExpressionString);
                DbAccessor.AddCommandParameter(cmd, "triggerTimeZone", cronTrigger.TimeZone.Id);

                return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<TriggerPropertyBundle> LoadExtendedTriggerProperties(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            using (var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectCronTriggers, TablePrefix, SchedNameLiteral)))
            {
                DbAccessor.AddCommandParameter(cmd, "triggerName", triggerKey.Name);
                DbAccessor.AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

                using (var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        string cronExpr = rs.GetString(AdoConstants.ColumnCronExpression);
                        string timeZoneId = rs.GetString(AdoConstants.ColumnTimeZoneId);

                        CronScheduleBuilder cb = CronScheduleBuilder.CronSchedule(cronExpr);
  
                        if (timeZoneId != null)
                        {
                            cb.InTimeZone(TimeZoneUtil.FindTimeZoneById(timeZoneId));
                        }

                        return new TriggerPropertyBundle(cb, null, null);
                    }
                }

                throw new InvalidOperationException("No record found for selection of Trigger with key: '" + triggerKey + "' and statement: " + AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectCronTriggers, TablePrefix, SchedNameLiteral));
            }
        }

        public async Task<int> UpdateExtendedTriggerProperties(
            ConnectionAndTransactionHolder conn, 
            IOperableTrigger trigger, 
            string state, 
            IJobDetail jobDetail,
            CancellationToken cancellationToken = default)
        {
            ICronTrigger cronTrigger = (ICronTrigger) trigger;

            using (var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlUpdateCronTrigger, TablePrefix, SchedNameLiteral)))
            {
                DbAccessor.AddCommandParameter(cmd, "triggerCronExpression", cronTrigger.CronExpressionString);
                DbAccessor.AddCommandParameter(cmd, "timeZoneId", cronTrigger.TimeZone.Id);
                DbAccessor.AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
                DbAccessor.AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);

                return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}