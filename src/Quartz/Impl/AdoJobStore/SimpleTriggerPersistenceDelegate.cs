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
    public class SimpleTriggerPersistenceDelegate : ITriggerPersistenceDelegate
    {
        protected IDbAccessor DbAccessor { get; private set; } = null!;

        protected string TablePrefix { get; private set; } = null!;

        [Obsolete("Scheduler name is now added to queries as a parameter")]
        protected string SchedNameLiteral { get; private set; } = null!;

        protected string SchedName { get; private set; } = null!;

        public void Initialize(string tablePrefix, string schedName, IDbAccessor dbAccessor)
        {
            TablePrefix = tablePrefix;
            SchedName = schedName;
            DbAccessor = dbAccessor;

            // No longer required
            SchedNameLiteral = "'" + schedName + "'";
        }

        public string GetHandledTriggerTypeDiscriminator()
        {
            return AdoConstants.TriggerTypeSimple;
        }

        public bool CanHandleTriggerType(IOperableTrigger trigger)
        {
            return trigger is SimpleTriggerImpl impl && !impl.HasAdditionalProperties;
        }

        public async Task<int> DeleteExtendedTriggerProperties(
            ConnectionAndTransactionHolder conn, 
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            using (var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlDeleteSimpleTrigger, TablePrefix)))
            {
                DbAccessor.AddCommandParameter(cmd, "schedulerName", SchedName);
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
            ISimpleTrigger simpleTrigger = (ISimpleTrigger) trigger;

            using (var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlInsertSimpleTrigger, TablePrefix)))
            {
                DbAccessor.AddCommandParameter(cmd, "schedulerName", SchedName);
                DbAccessor.AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
                DbAccessor.AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);
                DbAccessor.AddCommandParameter(cmd, "triggerRepeatCount", simpleTrigger.RepeatCount);
                DbAccessor.AddCommandParameter(cmd, "triggerRepeatInterval", DbAccessor.GetDbTimeSpanValue(simpleTrigger.RepeatInterval));
                DbAccessor.AddCommandParameter(cmd, "triggerTimesTriggered", simpleTrigger.TimesTriggered);

                return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<TriggerPropertyBundle> LoadExtendedTriggerProperties(
            ConnectionAndTransactionHolder conn,
            TriggerKey triggerKey,
            CancellationToken cancellationToken = default)
        {
            using (var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectSimpleTrigger, TablePrefix)))
            {
                DbAccessor.AddCommandParameter(cmd, "schedulerName", SchedName);
                DbAccessor.AddCommandParameter(cmd, "triggerName", triggerKey.Name);
                DbAccessor.AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

                using (var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        int repeatCount = rs.GetInt32(AdoConstants.ColumnRepeatCount);
                        TimeSpan repeatInterval = DbAccessor.GetTimeSpanFromDbValue(rs[AdoConstants.ColumnRepeatInterval]) ?? TimeSpan.Zero;
                        int timesTriggered = rs.GetInt32(AdoConstants.ColumnTimesTriggered);

                        SimpleScheduleBuilder sb = SimpleScheduleBuilder.Create()
                            .WithRepeatCount(repeatCount)
                            .WithInterval(repeatInterval);

                        string[] statePropertyNames = {"timesTriggered"};
                        object[] statePropertyValues = {timesTriggered};

                        return new TriggerPropertyBundle(sb, statePropertyNames, statePropertyValues);
                    }
                }
                throw new InvalidOperationException("No record found for selection of Trigger with key: '" + triggerKey + "' and statement: " + AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectSimpleTrigger, TablePrefix));
            }
        }

        public async Task<int> UpdateExtendedTriggerProperties(
            ConnectionAndTransactionHolder conn, 
            IOperableTrigger trigger,
            string state, 
            IJobDetail jobDetail,
            CancellationToken cancellationToken = default)
        {
            ISimpleTrigger simpleTrigger = (ISimpleTrigger) trigger;

            using (var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlUpdateSimpleTrigger, TablePrefix)))
            {
                DbAccessor.AddCommandParameter(cmd, "schedulerName", SchedName);
                DbAccessor.AddCommandParameter(cmd, "triggerRepeatCount", simpleTrigger.RepeatCount);
                DbAccessor.AddCommandParameter(cmd, "triggerRepeatInterval", DbAccessor.GetDbTimeSpanValue(simpleTrigger.RepeatInterval));
                DbAccessor.AddCommandParameter(cmd, "triggerTimesTriggered", simpleTrigger.TimesTriggered);
                DbAccessor.AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
                DbAccessor.AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);

                return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}