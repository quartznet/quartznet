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
    /// <summary>
    /// The table this delegate persists into, kept here as the spelling a derived delegate writes.
    /// </summary>
    /// <remarks>
    /// The value belongs to <see cref="AdoConstants" />, where every table the store reads or writes
    /// is named, because that list is what startup schema validation probes. Declared only here, it
    /// was a table validation did not know about: a database missing this one alone started, and
    /// failed on the first calendar-interval, daily-time-interval or recurrence trigger written to
    /// it (#3564).
    /// </remarks>
    protected const string TableSimplePropertiesTriggers = AdoConstants.TableSimplePropertiesTriggers;

    // The column names are the schema contract a derived delegate reads its own values back from, so
    // they stay protected. The four statements below are not: they name every column this base class
    // writes, so a subclass replacing one would either be writing the same statement again or writing
    // a statement this class's parameter binding does not match.
    /// <summary>
    /// The <c>STR_PROP_1</c> column of <see cref="TableSimplePropertiesTriggers" />.
    /// </summary>
    protected const string ColumnStrProp1 = "STR_PROP_1";

    /// <summary>
    /// The <c>STR_PROP_2</c> column of <see cref="TableSimplePropertiesTriggers" />.
    /// </summary>
    protected const string ColumnStrProp2 = "STR_PROP_2";

    /// <summary>
    /// The <c>STR_PROP_3</c> column of <see cref="TableSimplePropertiesTriggers" />.
    /// </summary>
    protected const string ColumnStrProp3 = "STR_PROP_3";

    /// <summary>
    /// The <c>INT_PROP_1</c> column of <see cref="TableSimplePropertiesTriggers" />.
    /// </summary>
    protected const string ColumnIntProp1 = "INT_PROP_1";

    /// <summary>
    /// The <c>INT_PROP_2</c> column of <see cref="TableSimplePropertiesTriggers" />.
    /// </summary>
    protected const string ColumnIntProp2 = "INT_PROP_2";

    /// <summary>
    /// The <c>LONG_PROP_1</c> column of <see cref="TableSimplePropertiesTriggers" />.
    /// </summary>
    protected const string ColumnLongProp1 = "LONG_PROP_1";

    /// <summary>
    /// The <c>LONG_PROP_2</c> column of <see cref="TableSimplePropertiesTriggers" />.
    /// </summary>
    protected const string ColumnLongProp2 = "LONG_PROP_2";

    /// <summary>
    /// The <c>DEC_PROP_1</c> column of <see cref="TableSimplePropertiesTriggers" />.
    /// </summary>
    protected const string ColumnDecProp1 = "DEC_PROP_1";

    /// <summary>
    /// The <c>DEC_PROP_2</c> column of <see cref="TableSimplePropertiesTriggers" />.
    /// </summary>
    protected const string ColumnDecProp2 = "DEC_PROP_2";

    /// <summary>
    /// The <c>BOOL_PROP_1</c> column of <see cref="TableSimplePropertiesTriggers" />.
    /// </summary>
    protected const string ColumnBoolProp1 = "BOOL_PROP_1";

    /// <summary>
    /// The <c>BOOL_PROP_2</c> column of <see cref="TableSimplePropertiesTriggers" />.
    /// </summary>
    protected const string ColumnBoolProp2 = "BOOL_PROP_2";

    /// <summary>
    /// The <c>TIME_ZONE_ID</c> column of <see cref="TableSimplePropertiesTriggers" />.
    /// </summary>
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
                                                                 + AdoConstants.ColumnSchedulerName + " = @" + SqlParameters.SchedulerName
                                                                 + " AND " + AdoConstants.ColumnTriggerName + " = @" + SqlParameters.TriggerName + " AND " + AdoConstants.ColumnTriggerGroup + " = @" + SqlParameters.TriggerGroup;

    private const string DeleteSimplePropsTrigger = "DELETE FROM "
                                                      + StdAdoConstants.TablePrefixSubst + TableSimplePropertiesTriggers + " WHERE "
                                                      + AdoConstants.ColumnSchedulerName + " = @" + SqlParameters.SchedulerName
                                                      + " AND " + AdoConstants.ColumnTriggerName + " = @" + SqlParameters.TriggerName + " AND " + AdoConstants.ColumnTriggerGroup + " = @" + SqlParameters.TriggerGroup;

    private const string InsertSimplePropsTrigger = "INSERT INTO "
                                                      + StdAdoConstants.TablePrefixSubst + TableSimplePropertiesTriggers + " ("
                                                      + AdoConstants.ColumnSchedulerName + ", "
                                                      + AdoConstants.ColumnTriggerName + ", " + AdoConstants.ColumnTriggerGroup + ", "
                                                      + ColumnStrProp1 + ", " + ColumnStrProp2 + ", " + ColumnStrProp3 + ", "
                                                      + ColumnIntProp1 + ", " + ColumnIntProp2 + ", "
                                                      + ColumnLongProp1 + ", " + ColumnLongProp2 + ", "
                                                      + ColumnDecProp1 + ", " + ColumnDecProp2 + ", "
                                                      + ColumnBoolProp1 + ", " + ColumnBoolProp2 + ", " + ColumnTimeZoneId
                                                      + ") " + " VALUES(@" + SqlParameters.SchedulerName + ", @" + SqlParameters.TriggerName + ", @" + SqlParameters.TriggerGroup + ", @" + SqlParameters.String1 + ", @" + SqlParameters.String2 + ", @" + SqlParameters.String3 + ", @" + SqlParameters.Int1 + ", @" + SqlParameters.Int2 + ", @" + SqlParameters.Long1 + ", @" + SqlParameters.Long2 + ", @" + SqlParameters.Decimal1 + ", @" + SqlParameters.Decimal2 + ", @" + SqlParameters.Boolean1 + ", @" + SqlParameters.Boolean2 + ", @" + SqlParameters.TimeZoneId + ")";

    private const string UpdateSimplePropsTrigger = "UPDATE "
                                                      + StdAdoConstants.TablePrefixSubst + TableSimplePropertiesTriggers + " SET "
                                                      + ColumnStrProp1 + " = @" + SqlParameters.String1 + ", " + ColumnStrProp2 + " = @" + SqlParameters.String2 + ", " + ColumnStrProp3 + " = @" + SqlParameters.String3 + ", "
                                                      + ColumnIntProp1 + " = @" + SqlParameters.Int1 + ", " + ColumnIntProp2 + " = @" + SqlParameters.Int2 + ", "
                                                      + ColumnLongProp1 + " = @" + SqlParameters.Long1 + ", " + ColumnLongProp2 + " = @" + SqlParameters.Long2 + ", "
                                                      + ColumnDecProp1 + " = @" + SqlParameters.Decimal1 + ", " + ColumnDecProp2 + " = @" + SqlParameters.Decimal2 + ", "
                                                      + ColumnBoolProp1 + " = @" + SqlParameters.Boolean1 + ", " + ColumnBoolProp2
                                                      + " = @" + SqlParameters.Boolean2 + ", " + ColumnTimeZoneId + " = @" + SqlParameters.TimeZoneId + " WHERE " + AdoConstants.ColumnSchedulerName + " = @" + SqlParameters.SchedulerName
                                                      + " AND " + AdoConstants.ColumnTriggerName
                                                      + " = @" + SqlParameters.TriggerName + " AND " + AdoConstants.ColumnTriggerGroup + " = @" + SqlParameters.TriggerGroup;

    /// <inheritdoc />
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

    /// <summary>
    /// Reads a trigger of the derived delegate's own family into the generic property columns.
    /// </summary>
    /// <param name="trigger">The trigger being stored.</param>
    protected abstract SimplePropertiesTriggerProperties GetTriggerProperties(IOperableTrigger trigger);

    /// <summary>
    /// Builds the schedule of a trigger of the derived delegate's own family back out of the generic
    /// property columns.
    /// </summary>
    /// <param name="properties">The columns as they were read.</param>
    protected abstract TriggerPropertyBundle GetTriggerPropertyBundle(SimplePropertiesTriggerProperties properties);

    /// <summary>
    /// The table prefix the store was configured with.</summary>
    protected string TablePrefix { get; private set; } = null!;

    /// <summary>
    /// The scheduler whose rows this delegate reads and writes.</summary>
    protected string SchedulerName { get; private set; } = null!;

    /// <summary>
    /// How a value is bound to a command and read back out of a row.</summary>
    protected IDbAccessor DbAccessor { get; private set; } = null!;

    /// <inheritdoc />
    public async ValueTask<int> DeleteExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefixCached(DeleteSimplePropsTrigger, TablePrefix));
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
        SimplePropertiesTriggerProperties properties = GetTriggerProperties(trigger);

        using var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefixCached(InsertSimplePropsTrigger, TablePrefix));
        DbAccessor.AddCommandParameter(cmd, SqlParameters.SchedulerName, SchedulerName);
        DbAccessor.AddCommandParameter(cmd, SqlParameters.TriggerName, trigger.Key.Name);
        DbAccessor.AddCommandParameter(cmd, SqlParameters.TriggerGroup, trigger.Key.Group);

        DbAccessor.AddCommandParameter(cmd, SqlParameters.String1, properties.String1);
        DbAccessor.AddCommandParameter(cmd, SqlParameters.String2, properties.String2);
        DbAccessor.AddCommandParameter(cmd, SqlParameters.String3, properties.String3);
        DbAccessor.AddCommandParameter(cmd, SqlParameters.Int1, properties.Int1);
        DbAccessor.AddCommandParameter(cmd, SqlParameters.Int2, properties.Int2);
        DbAccessor.AddCommandParameter(cmd, SqlParameters.Long1, properties.Long1);
        DbAccessor.AddCommandParameter(cmd, SqlParameters.Long2, properties.Long2);
        DbAccessor.AddCommandParameter(cmd, SqlParameters.Decimal1, properties.Decimal1);
        DbAccessor.AddCommandParameter(cmd, SqlParameters.Decimal2, properties.Decimal2);
        DbAccessor.AddCommandParameter(cmd, SqlParameters.Boolean1, DbAccessor.GetDbBooleanValue(properties.Boolean1));
        DbAccessor.AddCommandParameter(cmd, SqlParameters.Boolean2, DbAccessor.GetDbBooleanValue(properties.Boolean2));
        DbAccessor.AddCommandParameter(cmd, SqlParameters.TimeZoneId, properties.TimeZoneId);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<TriggerPropertyBundle> LoadExtendedTriggerProperties(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using var cmd = DbAccessor.PrepareCommand(conn, AdoJobStoreUtil.ReplaceTablePrefixCached(SelectSimplePropsTrigger, TablePrefix));
        DbAccessor.AddCommandParameter(cmd, SqlParameters.SchedulerName, SchedulerName);
        DbAccessor.AddCommandParameter(cmd, SqlParameters.TriggerName, triggerKey.Name);
        DbAccessor.AddCommandParameter(cmd, SqlParameters.TriggerGroup, triggerKey.Group);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return ReadTriggerPropertyBundle(rs);
        }

        Throw.InvalidOperationException("No record found for selection of Trigger with key: '" + triggerKey + "' and statement: " + AdoJobStoreUtil.ReplaceTablePrefix(StdAdoConstants.SqlSelectSimpleTrigger, TablePrefix));
        return default;
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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
            new SqlStatementParameter(SqlParameters.SchedulerName, SchedulerName),
            new SqlStatementParameter(SqlParameters.String1, properties.String1),
            new SqlStatementParameter(SqlParameters.String2, properties.String2),
            new SqlStatementParameter(SqlParameters.String3, properties.String3),
            new SqlStatementParameter(SqlParameters.Int1, properties.Int1),
            new SqlStatementParameter(SqlParameters.Int2, properties.Int2),
            new SqlStatementParameter(SqlParameters.Long1, properties.Long1),
            new SqlStatementParameter(SqlParameters.Long2, properties.Long2),
            new SqlStatementParameter(SqlParameters.Decimal1, properties.Decimal1),
            new SqlStatementParameter(SqlParameters.Decimal2, properties.Decimal2),
            new SqlStatementParameter(SqlParameters.Boolean1, DbAccessor.GetDbBooleanValue(properties.Boolean1)),
            new SqlStatementParameter(SqlParameters.Boolean2, DbAccessor.GetDbBooleanValue(properties.Boolean2)),
            new SqlStatementParameter(SqlParameters.TriggerName, trigger.Key.Name),
            new SqlStatementParameter(SqlParameters.TriggerGroup, trigger.Key.Group),
            new SqlStatementParameter(SqlParameters.TimeZoneId, properties.TimeZoneId)
        ];
    }
}