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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl.Triggers;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore
{
    public class SimpleTriggerPersistenceDelegate : ITriggerPersistenceDelegate
    {
        protected StdAdoDelegate DbAccessor { get; private set; }

        protected string TablePrefix { get; private set; }

        protected string SchedNameLiteral { get; private set; }

        public void Initialize(string tablePrefix, string schedName, StdAdoDelegate dbAccessor)
        {
            TablePrefix = tablePrefix;
            SchedNameLiteral = "'" + schedName + "'";
            DbAccessor = dbAccessor;
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
            using (var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlDeleteSimpleTrigger, TablePrefix, SchedNameLiteral)))
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
            ISimpleTrigger simpleTrigger = (ISimpleTrigger) trigger;

            using (var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlInsertSimpleTrigger, TablePrefix, SchedNameLiteral)))
            {
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
            using (var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectSimpleTrigger, TablePrefix, SchedNameLiteral)))
            {
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
                throw new InvalidOperationException("No record found for selection of Trigger with key: '" + triggerKey + "' and statement: " + AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectSimpleTrigger, TablePrefix, SchedNameLiteral));
            }
        }

        public async Task<TriggerPropertyBundle[]> LoadExtendedTriggerProperties(ConnectionAndTransactionHolder conn, TriggerKey[] triggerKeys, CancellationToken cancellationToken = default)
        {
            var propsBundles = new Dictionary<TriggerKey, TriggerPropertyBundle>();
            using (var cmd = DbAccessor.PrepareBatchSelectFromTriggerKeys(
                conn, triggerKeys, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectSimpleTrigger, TablePrefix, SchedNameLiteral), forSelect: true))
            {
                using (var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        int repeatCount = rs.GetInt32(AdoConstants.ColumnRepeatCount);
                        TimeSpan repeatInterval = DbAccessor.GetTimeSpanFromDbValue(rs[AdoConstants.ColumnRepeatInterval]) ?? TimeSpan.Zero;
                        int timesTriggered = rs.GetInt32(AdoConstants.ColumnTimesTriggered);

                        SimpleScheduleBuilder sb = SimpleScheduleBuilder.Create()
                            .WithRepeatCount(repeatCount)
                            .WithInterval(repeatInterval);

                        string[] statePropertyNames = { "timesTriggered" };
                        object[] statePropertyValues = { timesTriggered };

                        propsBundles[DbAccessor.TriggerKeyFromRow(rs)] = new TriggerPropertyBundle(sb, statePropertyNames, statePropertyValues);
                    }
                }
            }

            return triggerKeys.Select(x => propsBundles.TryGetAndReturn(x)).ToArray();
        }

        public async Task<int> UpdateExtendedTriggerProperties(
            ConnectionAndTransactionHolder conn, 
            IOperableTrigger trigger,
            string state, 
            IJobDetail jobDetail,
            CancellationToken cancellationToken = default)
        {
            ISimpleTrigger simpleTrigger = (ISimpleTrigger) trigger;

            using (var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlUpdateSimpleTrigger, TablePrefix, SchedNameLiteral)))
            {
                DbAccessor.AddCommandParameter(cmd, "triggerRepeatCount", simpleTrigger.RepeatCount);
                DbAccessor.AddCommandParameter(cmd, "triggerRepeatInterval", DbAccessor.GetDbTimeSpanValue(simpleTrigger.RepeatInterval));
                DbAccessor.AddCommandParameter(cmd, "triggerTimesTriggered", simpleTrigger.TimesTriggered);
                DbAccessor.AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
                DbAccessor.AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);

                return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task<int> UpdateExtendedTriggerProperties(
            ConnectionAndTransactionHolder conn, 
            IOperableTrigger[] triggers, 
            string[] states, 
            IJobDetail[] jobDetails, 
            CancellationToken cancellationToken = default)
        {
            var paramNames = new[] { "triggerRepeatCount", "triggerRepeatInterval", "triggerTimesTriggered", "triggerName", "triggerGroup" };

            var paramValuesBatch = triggers
                .Select(x => (ISimpleTrigger)x)
                .Select(x =>
                    new[]
                    {
                        DbAccessor.AdoUtil.CreateParamValue(x.RepeatCount),
                        DbAccessor.AdoUtil.CreateParamValue(DbAccessor.GetDbTimeSpanValue(x.RepeatInterval)),
                        DbAccessor.AdoUtil.CreateParamValue(x.TimesTriggered),
                        DbAccessor.AdoUtil.CreateParamValue(x.Key.Name),
                        DbAccessor.AdoUtil.CreateParamValue(x.Key.Group)

                    })
                .ToArray();

            using (var cmd = DbAccessor.AdoUtil.PrepareCommandBatchByTemplateCloning(
                                conn,
                                AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlUpdateSimpleTrigger, TablePrefix, SchedNameLiteral),
                                paramNames,
                                paramValuesBatch))
            {
                return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}