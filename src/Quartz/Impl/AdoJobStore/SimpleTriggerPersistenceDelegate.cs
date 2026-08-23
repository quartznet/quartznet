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

using System.Data.Common;

using Quartz.Impl.Triggers;
using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Persists the extended properties of an <see cref="ISimpleTrigger" />.
/// </summary>
public sealed class SimpleTriggerPersistenceDelegate : ITriggerPersistenceDelegate
{
    private IDbAccessor DbAccessor { get; set; } = null!;

    private string TablePrefix { get; set; } = null!;

    private string SchedulerName { get; set; } = null!;

    public void Initialize(TriggerPersistenceDelegateContext context)
    {
        TablePrefix = context.TablePrefix;
        SchedulerName = context.SchedulerName;
        DbAccessor = context.DbAccessor;
    }

    public string GetHandledTriggerTypeDiscriminator()
    {
        return AdoConstants.TriggerTypeSimple;
    }

    public bool CanHandleTriggerType(IOperableTrigger trigger)
    {
        return trigger is SimpleTriggerImpl impl && !impl.HasAdditionalProperties;
    }

    public async ValueTask<int> DeleteExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefixCached(StdAdoConstants.SqlDeleteSimpleTrigger, TablePrefix));
        DbAccessor.AddCommandParameter(cmd, "schedulerName", SchedulerName);
        DbAccessor.AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        DbAccessor.AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<int> InsertExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState state,
        IJobDetail jobDetail,
        CancellationToken cancellationToken = default)
    {
        ISimpleTrigger simpleTrigger = (ISimpleTrigger) trigger;

        using var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefixCached(StdAdoConstants.SqlInsertSimpleTrigger, TablePrefix));
        DbAccessor.AddCommandParameter(cmd, "schedulerName", SchedulerName);
        DbAccessor.AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
        DbAccessor.AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);
        DbAccessor.AddCommandParameter(cmd, "triggerRepeatCount", simpleTrigger.RepeatCount);
        DbAccessor.AddCommandParameter(cmd, "triggerRepeatInterval", DbAccessor.GetDbTimeSpanValue(simpleTrigger.RepeatInterval));
        DbAccessor.AddCommandParameter(cmd, "triggerTimesTriggered", simpleTrigger.TimesTriggered);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<TriggerPropertyBundle> LoadExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefixCached(StdAdoConstants.SqlSelectSimpleTrigger, TablePrefix));
        DbAccessor.AddCommandParameter(cmd, "schedulerName", SchedulerName);
        DbAccessor.AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        DbAccessor.AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return ReadTriggerPropertyBundle(rs);
        }

        Throw.InvalidOperationException("No record found for selection of Trigger with key: '" + triggerKey + "' and statement: " + AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectSimpleTrigger, TablePrefix));
        return default;
    }

    public ValueTask<Dictionary<TriggerKey, TriggerPropertyBundle>> LoadExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        return AdoUtil.LoadTriggerPropertyBundles(
            DbAccessor,
            conn,
            StdAdoConstants.SqlSelectSimpleTriggersByKeysPrefix,
            TablePrefix,
            SchedulerName,
            triggerKeys,
            ReadTriggerPropertyBundle,
            cancellationToken);
    }

    public TriggerPropertyBundle ReadTriggerPropertyBundle(DbDataReader rs)
    {
        int repeatCount = rs.GetInt32(AdoConstants.ColumnRepeatCount);
        TimeSpan repeatInterval = DbAccessor.GetTimeSpanFromDbValue(rs[AdoConstants.ColumnRepeatInterval]) ?? TimeSpan.Zero;
        int timesTriggered = rs.GetInt32(AdoConstants.ColumnTimesTriggered);

        SimpleScheduleBuilder sb = SimpleScheduleBuilder.Create()
            .WithRepeatCount(repeatCount)
            .WithInterval(repeatInterval);

        return new TriggerPropertyBundle(sb, t => ((SimpleTriggerImpl) t).TimesTriggered = timesTriggered);
    }

    public async ValueTask<int> UpdateExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState state,
        IJobDetail jobDetail,
        CancellationToken cancellationToken = default)
    {
        using var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefixCached(StdAdoConstants.SqlUpdateSimpleTrigger, TablePrefix));
        foreach (SqlStatementParameter parameter in BuildUpdateParameters(trigger))
        {
            DbAccessor.AddCommandParameter(cmd, parameter.Name, parameter.Value, parameter.DataType);
        }

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public bool TryDescribeUpdateExtendedTriggerProperties(
        IOperableTrigger trigger,
        StoredTriggerState state,
        IJobDetail jobDetail,
        ICollection<SqlStatement> statements)
    {
        statements.Add(new SqlStatement(
            AdoJobStoreUtil.ReplaceTablePrefixCached(StdAdoConstants.SqlUpdateSimpleTrigger, TablePrefix),
            BuildUpdateParameters(trigger)));

        return true;
    }

    private List<SqlStatementParameter> BuildUpdateParameters(IOperableTrigger trigger)
    {
        ISimpleTrigger simpleTrigger = (ISimpleTrigger) trigger;

        return
        [
            new SqlStatementParameter("schedulerName", SchedulerName),
            new SqlStatementParameter("triggerRepeatCount", simpleTrigger.RepeatCount),
            new SqlStatementParameter("triggerRepeatInterval", DbAccessor.GetDbTimeSpanValue(simpleTrigger.RepeatInterval)),
            new SqlStatementParameter("triggerTimesTriggered", simpleTrigger.TimesTriggered),
            new SqlStatementParameter("triggerName", trigger.Key.Name),
            new SqlStatementParameter("triggerGroup", trigger.Key.Group)
        ];
    }
}