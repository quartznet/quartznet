using System.Data.Common;

using Quartz.Util;

namespace Quartz.Impl.AdoJobStore;

public partial class StdAdoDelegate
{
    /// <inheritdoc />
    public virtual async ValueTask<int> InsertSchedulerState(
        ConnectionAndTransactionHolder conn,
        string instanceId,
        DateTimeOffset checkInTime,
        TimeSpan interval,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlInsertSchedulerState));
        AddCommandParameter(cmd, SqlParameters.SchedulerName, SchedulerName);
        AddCommandParameter(cmd, SqlParameters.InstanceName, instanceId);
        AddCommandParameter(cmd, SqlParameters.LastCheckinTime, GetDbDateTimeValue(checkInTime));
        AddCommandParameter(cmd, SqlParameters.CheckinInterval, GetDbTimeSpanValue(interval));

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> DeleteSchedulerState(
        ConnectionAndTransactionHolder conn,
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlDeleteSchedulerState));
        AddCommandParameter(cmd, SqlParameters.SchedulerName, SchedulerName);
        AddCommandParameter(cmd, SqlParameters.InstanceName, instanceId);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateSchedulerState(
        ConnectionAndTransactionHolder conn,
        string instanceId,
        DateTimeOffset checkInTime,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlUpdateSchedulerState));
        AddCommandParameter(cmd, SqlParameters.SchedulerName, SchedulerName);
        AddCommandParameter(cmd, SqlParameters.LastCheckinTime, GetDbDateTimeValue(checkInTime));
        AddCommandParameter(cmd, SqlParameters.InstanceName, instanceId);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<SchedulerStateRecord>> SelectSchedulerStateRecords(ConnectionAndTransactionHolder conn, string? instanceId, CancellationToken cancellationToken = default)
    {
        DbCommand cmd;
        List<SchedulerStateRecord> list = [];

        // Bound in the order the statement names them, as every other binder in this file is: a provider
        // that binds by name does not care, but one configured to bind by position would take
        // @schedulerName and @instanceName the other way round.
        if (instanceId is not null)
        {
            cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectSchedulerState));
            AddCommandParameter(cmd, SqlParameters.SchedulerName, SchedulerName);
            AddCommandParameter(cmd, SqlParameters.InstanceName, instanceId);
        }
        else
        {
            cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectSchedulerStates));
            AddCommandParameter(cmd, SqlParameters.SchedulerName, SchedulerName);
        }

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            SchedulerStateRecord rec = new(
                rs.GetString(AdoConstants.ColumnInstanceName)!,
                GetDateTimeFromDbValue(rs[AdoConstants.ColumnLastCheckinTime]) ?? DateTimeOffset.MinValue,
                GetTimeSpanFromDbValue(rs[AdoConstants.ColumnCheckinInterval]) ?? TimeSpan.Zero);

            list.Add(rec);
        }

        return list;
    }
}