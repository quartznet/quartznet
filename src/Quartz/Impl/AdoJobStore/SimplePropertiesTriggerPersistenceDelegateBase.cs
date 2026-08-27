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

using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// A base implementation of <see cref="ITriggerPersistenceDelegate" /> that persists
/// trigger fields in the "QRTZ_SIMPROP_TRIGGERS" table.  This allows extending
/// concrete classes to simply implement a couple methods that do the work of
/// getting/setting the trigger's fields, and creating the <see cref="IScheduleBuilder" />
/// for the particular type of trigger.
/// </summary>
/// <seealso cref="CalendarIntervalTriggerPersistenceDelegate" />
/// <author>jhouse</author>
/// <author>Marko Lahma (.NET)</author>
public abstract class SimplePropertiesTriggerPersistenceDelegateBase : ITriggerPersistenceDelegate
{
    // The table and column names are the schema contract a derived delegate reads its own values back
    // from, so they stay protected. The four statements below are not: they name every column this base
    // class writes, so a subclass replacing one would either be writing the same statement again or
    // writing a statement this class's parameter binding does not match.
    protected const string TableSimplePropertiesTriggers = "SIMPROP_TRIGGERS";

    protected const string ColumnStrProp1 = "STR_PROP_1";
    protected const string ColumnStrProp2 = "STR_PROP_2";
    protected const string ColumnStrProp3 = "STR_PROP_3";
    protected const string ColumnIntProp1 = "INT_PROP_1";
    protected const string ColumnIntProp2 = "INT_PROP_2";
    protected const string ColumnLongProp1 = "LONG_PROP_1";
    protected const string ColumnLongProp2 = "LONG_PROP_2";
    protected const string ColumnDecProp1 = "DEC_PROP_1";
    protected const string ColumnDecProp2 = "DEC_PROP_2";
    protected const string ColumnBoolProp1 = "BOOL_PROP_1";
    protected const string ColumnBoolProp2 = "BOOL_PROP_2";
    protected const string ColumnTimeZoneId = "TIME_ZONE_ID";

    /// <summary>
    /// Every column <see cref="ReadTriggerPropertyBundle" /> reads, and only those. Shared with the
    /// batch lookup in <c>StdAdoConstants</c>, so the single-key and batch read paths cannot drift
    /// apart, and named rather than starred so that a column a migration or a user adds to the table
    /// does not change what comes back.
    /// </summary>
    internal const string SelectColumns =
        ColumnStrProp1 + ", " + ColumnStrProp2 + ", " + ColumnStrProp3 + ", "
        + ColumnIntProp1 + ", " + ColumnIntProp2 + ", "
        + ColumnLongProp1 + ", " + ColumnLongProp2 + ", "
        + ColumnDecProp1 + ", " + ColumnDecProp2 + ", "
        + ColumnBoolProp1 + ", " + ColumnBoolProp2 + ", " + ColumnTimeZoneId;

    private const string SelectSimplePropsTrigger = "SELECT " + SelectColumns + " FROM "
                                                                 + StdAdoConstants.TablePrefixSubst + TableSimplePropertiesTriggers + " WHERE "
                                                                 + AdoConstants.ColumnSchedulerName + " = @schedulerName"
                                                                 + " AND " + AdoConstants.ColumnTriggerName + " = @triggerName AND " + AdoConstants.ColumnTriggerGroup + " = @triggerGroup";

    private const string DeleteSimplePropsTrigger = "DELETE FROM "
                                                      + StdAdoConstants.TablePrefixSubst + TableSimplePropertiesTriggers + " WHERE "
                                                      + AdoConstants.ColumnSchedulerName + " = @schedulerName"
                                                      + " AND " + AdoConstants.ColumnTriggerName + " = @triggerName AND " + AdoConstants.ColumnTriggerGroup + " = @triggerGroup";

    private const string InsertSimplePropsTrigger = "INSERT INTO "
                                                      + StdAdoConstants.TablePrefixSubst + TableSimplePropertiesTriggers + " ("
                                                      + AdoConstants.ColumnSchedulerName + ", "
                                                      + AdoConstants.ColumnTriggerName + ", " + AdoConstants.ColumnTriggerGroup + ", "
                                                      + ColumnStrProp1 + ", " + ColumnStrProp2 + ", " + ColumnStrProp3 + ", "
                                                      + ColumnIntProp1 + ", " + ColumnIntProp2 + ", "
                                                      + ColumnLongProp1 + ", " + ColumnLongProp2 + ", "
                                                      + ColumnDecProp1 + ", " + ColumnDecProp2 + ", "
                                                      + ColumnBoolProp1 + ", " + ColumnBoolProp2 + ", " + ColumnTimeZoneId
                                                      + ") " + " VALUES(@schedulerName" + ", @triggerName, @triggerGroup, @string1, @string2, @string3, @int1, @int2, @long1, @long2, @decimal1, @decimal2, @boolean1, @boolean2, @timeZoneId)";

    private const string UpdateSimplePropsTrigger = "UPDATE "
                                                      + StdAdoConstants.TablePrefixSubst + TableSimplePropertiesTriggers + " SET "
                                                      + ColumnStrProp1 + " = @string1, " + ColumnStrProp2 + " = @string2, " + ColumnStrProp3 + " = @string3, "
                                                      + ColumnIntProp1 + " = @int1, " + ColumnIntProp2 + " = @int2, "
                                                      + ColumnLongProp1 + " = @long1, " + ColumnLongProp2 + " = @long2, "
                                                      + ColumnDecProp1 + " = @decimal1, " + ColumnDecProp2 + " = @decimal2, "
                                                      + ColumnBoolProp1 + " = @boolean1, " + ColumnBoolProp2
                                                      + " = @boolean2, " + ColumnTimeZoneId + " = @timeZoneId WHERE " + AdoConstants.ColumnSchedulerName + " = @schedulerName"
                                                      + " AND " + AdoConstants.ColumnTriggerName
                                                      + " = @triggerName AND " + AdoConstants.ColumnTriggerGroup + " = @triggerGroup";

    public void Initialize(TriggerPersistenceDelegateContext context)
    {
        TablePrefix = context.TablePrefix;
        DbAccessor = context.DbAccessor;
        SchedulerName = context.SchedulerName;
    }

    /// <summary>
    /// Returns whether the trigger type can be handled by delegate.
    /// </summary>
    public abstract bool CanHandleTriggerType(IOperableTrigger trigger);

    /// <summary>
    /// Returns database discriminator value for trigger type.
    /// </summary>
    public abstract string GetHandledTriggerTypeDiscriminator();

    protected abstract SimplePropertiesTriggerProperties GetTriggerProperties(IOperableTrigger trigger);

    protected abstract TriggerPropertyBundle GetTriggerPropertyBundle(SimplePropertiesTriggerProperties properties);

    protected string TablePrefix { get; private set; } = null!;

    protected string SchedulerName { get; private set; } = null!;

    protected IDbAccessor DbAccessor { get; private set; } = null!;

    public async ValueTask<int> DeleteExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefixCached(DeleteSimplePropsTrigger, TablePrefix));
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
        SimplePropertiesTriggerProperties properties = GetTriggerProperties(trigger);

        using var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefixCached(InsertSimplePropsTrigger, TablePrefix));
        DbAccessor.AddCommandParameter(cmd, "schedulerName", SchedulerName);
        DbAccessor.AddCommandParameter(cmd, "triggerName", trigger.Key.Name);
        DbAccessor.AddCommandParameter(cmd, "triggerGroup", trigger.Key.Group);

        DbAccessor.AddCommandParameter(cmd, "string1", properties.String1);
        DbAccessor.AddCommandParameter(cmd, "string2", properties.String2);
        DbAccessor.AddCommandParameter(cmd, "string3", properties.String3);
        DbAccessor.AddCommandParameter(cmd, "int1", properties.Int1);
        DbAccessor.AddCommandParameter(cmd, "int2", properties.Int2);
        DbAccessor.AddCommandParameter(cmd, "long1", properties.Long1);
        DbAccessor.AddCommandParameter(cmd, "long2", properties.Long2);
        DbAccessor.AddCommandParameter(cmd, "decimal1", properties.Decimal1);
        DbAccessor.AddCommandParameter(cmd, "decimal2", properties.Decimal2);
        DbAccessor.AddCommandParameter(cmd, "boolean1", DbAccessor.GetDbBooleanValue(properties.Boolean1));
        DbAccessor.AddCommandParameter(cmd, "boolean2", DbAccessor.GetDbBooleanValue(properties.Boolean2));
        DbAccessor.AddCommandParameter(cmd, "timeZoneId", properties.TimeZoneId);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<TriggerPropertyBundle> LoadExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefixCached(SelectSimplePropsTrigger, TablePrefix));
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

    public TriggerPropertyBundle ReadTriggerPropertyBundle(DbDataReader rs)
    {
        SimplePropertiesTriggerProperties properties = new SimplePropertiesTriggerProperties
        {
            String1 = rs.GetString(ColumnStrProp1),
            String2 = rs.GetString(ColumnStrProp2),
            String3 = rs.GetString(ColumnStrProp3),
            Int1 = rs.GetInt32(ColumnIntProp1),
            Int2 = rs.GetInt32(ColumnIntProp2),
            Long1 = rs.GetInt64(ColumnLongProp1),
            Long2 = rs.GetInt64(ColumnLongProp2),
            Decimal1 = rs.GetDecimal(ColumnDecProp1),
            Decimal2 = rs.GetDecimal(ColumnDecProp2),
            Boolean1 = DbAccessor.GetBooleanFromDbValue(rs[ColumnBoolProp1]),
            Boolean2 = DbAccessor.GetBooleanFromDbValue(rs[ColumnBoolProp2]),
            TimeZoneId = rs.GetString(ColumnTimeZoneId),
        };

        return GetTriggerPropertyBundle(properties);
    }

    public async ValueTask<int> UpdateExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        StoredTriggerState state,
        IJobDetail jobDetail,
        CancellationToken cancellationToken = default)
    {
        using var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefixCached(UpdateSimplePropsTrigger, TablePrefix));
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
            AdoJobStoreUtil.ReplaceTablePrefixCached(UpdateSimplePropsTrigger, TablePrefix),
            BuildUpdateParameters(trigger)));

        return true;
    }

    private List<SqlStatementParameter> BuildUpdateParameters(IOperableTrigger trigger)
    {
        SimplePropertiesTriggerProperties properties = GetTriggerProperties(trigger);

        return
        [
            new SqlStatementParameter("schedulerName", SchedulerName),
            new SqlStatementParameter("string1", properties.String1),
            new SqlStatementParameter("string2", properties.String2),
            new SqlStatementParameter("string3", properties.String3),
            new SqlStatementParameter("int1", properties.Int1),
            new SqlStatementParameter("int2", properties.Int2),
            new SqlStatementParameter("long1", properties.Long1),
            new SqlStatementParameter("long2", properties.Long2),
            new SqlStatementParameter("decimal1", properties.Decimal1),
            new SqlStatementParameter("decimal2", properties.Decimal2),
            new SqlStatementParameter("boolean1", DbAccessor.GetDbBooleanValue(properties.Boolean1)),
            new SqlStatementParameter("boolean2", DbAccessor.GetDbBooleanValue(properties.Boolean2)),
            new SqlStatementParameter("triggerName", trigger.Key.Name),
            new SqlStatementParameter("triggerGroup", trigger.Key.Group),
            new SqlStatementParameter("timeZoneId", properties.TimeZoneId)
        ];
    }
}