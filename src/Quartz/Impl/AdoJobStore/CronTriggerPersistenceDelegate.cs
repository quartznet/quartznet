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
/// Persist a CronTriggerImpl.
/// </summary>
/// <see cref="CronScheduleBuilder"/>
/// <see cref="ICronTrigger"/>
public sealed class CronTriggerPersistenceDelegate : ITriggerPersistenceDelegate
{
    /// <inheritdoc />
    public void Initialize(TriggerPersistenceDelegateContext context)
    {
        TablePrefix = context.TablePrefix;
        DbAccessor = context.DbAccessor;
        SchedulerName = context.SchedulerName;
    }

    private string TablePrefix { get; set; } = null!;

    private IDbAccessor DbAccessor { get; set; } = null!;

    private string SchedulerName { get; set; } = null!;

    /// <inheritdoc />
    public string GetHandledTriggerTypeDiscriminator()
    {
        return AdoConstants.TriggerTypeCron;
    }

    /// <inheritdoc />
    public bool CanHandleTriggerType(IOperableTrigger trigger)
    {
        return trigger is CronTriggerImpl impl && !impl.HasAdditionalProperties;
    }

    /// <inheritdoc />
    public async ValueTask<int> DeleteExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefixCached(StdAdoConstants.SqlDeleteCronTrigger, TablePrefix));
        DbAccessor.AddCommandParameter(cmd, SqlParameters.SchedulerName, SchedulerName);
        DbAccessor.AddCommandParameter(cmd, SqlParameters.TriggerName, triggerKey.Name);
        DbAccessor.AddCommandParameter(cmd, SqlParameters.TriggerGroup, triggerKey.Group);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<int> InsertExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState state,
        IJobDetail jobDetail,
        CancellationToken cancellationToken = default)
    {
        ICronTrigger cronTrigger = (ICronTrigger) trigger;

        using var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefixCached(StdAdoConstants.SqlInsertCronTrigger, TablePrefix));
        DbAccessor.AddCommandParameter(cmd, SqlParameters.SchedulerName, SchedulerName);
        DbAccessor.AddCommandParameter(cmd, SqlParameters.TriggerName, trigger.Key.Name);
        DbAccessor.AddCommandParameter(cmd, SqlParameters.TriggerGroup, trigger.Key.Group);
        DbAccessor.AddCommandParameter(cmd, SqlParameters.TriggerCronExpression, cronTrigger.CronExpressionString);
        DbAccessor.AddCommandParameter(cmd, SqlParameters.TriggerTimeZone, cronTrigger.TimeZone.Id);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<TriggerPropertyBundle> LoadExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefixCached(StdAdoConstants.SqlSelectCronTriggers, TablePrefix));
        DbAccessor.AddCommandParameter(cmd, SqlParameters.SchedulerName, SchedulerName);
        DbAccessor.AddCommandParameter(cmd, SqlParameters.TriggerName, triggerKey.Name);
        DbAccessor.AddCommandParameter(cmd, SqlParameters.TriggerGroup, triggerKey.Group);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return ReadTriggerPropertyBundle(rs);
        }

        Throw.InvalidOperationException("No record found for selection of Trigger with key: '" + triggerKey + "' and statement: " + AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectCronTriggers, TablePrefix));
        return default;
    }

    /// <inheritdoc />
    public ValueTask<Dictionary<TriggerKey, TriggerPropertyBundle>> LoadExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        IReadOnlyCollection<TriggerKey> triggerKeys,
        CancellationToken cancellationToken = default)
    {
        return AdoUtil.LoadTriggerPropertyBundles(
            DbAccessor,
            conn,
            StdAdoConstants.SqlSelectCronTriggersByKeysPrefix,
            TablePrefix,
            SchedulerName,
            triggerKeys,
            ReadTriggerPropertyBundle,
            cancellationToken);
    }

    /// <inheritdoc />
    public TriggerPropertyBundle ReadTriggerPropertyBundle(DbDataReader rs)
    {
        var cronExpr = rs.GetString(AdoConstants.ColumnCronExpression)!;
        var timeZoneId = rs.GetString(AdoConstants.ColumnTimeZoneId);

        CronScheduleBuilder cb = CronScheduleBuilder.Create(cronExpr);

        if (timeZoneId is not null)
        {
            cb.InTimeZone(TimeZones.FindById(timeZoneId));
        }

        return new TriggerPropertyBundle(cb);
    }

    /// <inheritdoc />
    public async ValueTask<int> UpdateExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState state,
        IJobDetail jobDetail,
        CancellationToken cancellationToken = default)
    {
        using var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefixCached(StdAdoConstants.SqlUpdateCronTrigger, TablePrefix));
        foreach (SqlStatementParameter parameter in BuildUpdateParameters(trigger))
        {
            DbAccessor.AddCommandParameter(cmd, parameter.Name, parameter.Value, parameter.DataType);
        }

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public bool TryDescribeUpdateExtendedTriggerProperties(
        IOperableTrigger trigger,
        StoredTriggerState state,
        IJobDetail jobDetail,
        ICollection<SqlStatement> statements)
    {
        statements.Add(new SqlStatement(
            AdoJobStoreUtil.ReplaceTablePrefixCached(StdAdoConstants.SqlUpdateCronTrigger, TablePrefix),
            BuildUpdateParameters(trigger)));

        return true;
    }

    private List<SqlStatementParameter> BuildUpdateParameters(IOperableTrigger trigger)
    {
        ICronTrigger cronTrigger = (ICronTrigger) trigger;

        return
        [
            new SqlStatementParameter(SqlParameters.SchedulerName, SchedulerName),
            new SqlStatementParameter(SqlParameters.TriggerCronExpression, cronTrigger.CronExpressionString),
            new SqlStatementParameter(SqlParameters.TimeZoneId, cronTrigger.TimeZone.Id),
            new SqlStatementParameter(SqlParameters.TriggerName, trigger.Key.Name),
            new SqlStatementParameter(SqlParameters.TriggerGroup, trigger.Key.Group)
        ];
    }
}