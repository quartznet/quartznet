using System.Data.Common;

using Quartz.Util;

namespace Quartz.Impl.AdoJobStore;

public partial class StdAdoDelegate
{
    /// <inheritdoc />
    public virtual async ValueTask<int> InsertSchedulerState(
        ConnectionAndTransactionHolder conn,
        string instanceName,
        DateTimeOffset checkInTime,
        TimeSpan interval,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlInsertSchedulerState));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "instanceName", instanceName);
        AddCommandParameter(cmd, "lastCheckinTime", GetDbDateTimeValue(checkInTime));
        AddCommandParameter(cmd, "checkinInterval", GetDbTimeSpanValue(interval));

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> DeleteSchedulerState(
        ConnectionAndTransactionHolder conn,
        string instanceName,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlDeleteSchedulerState));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "instanceName", instanceName);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateSchedulerState(
        ConnectionAndTransactionHolder conn,
        string instanceName,
        DateTimeOffset checkInTime,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlUpdateSchedulerState));
        AddCommandParameter(cmd, "schedulerName", schedulerName);
        AddCommandParameter(cmd, "lastCheckinTime", GetDbDateTimeValue(checkInTime));
        AddCommandParameter(cmd, "instanceName", instanceName);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<SchedulerStateRecord>> SelectSchedulerStateRecords(ConnectionAndTransactionHolder conn, string? instanceName, CancellationToken cancellationToken = default)
    {
        DbCommand cmd;
        List<SchedulerStateRecord> list = [];

        if (instanceName is not null)
        {
            cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectSchedulerState));
            AddCommandParameter(cmd, "instanceName", instanceName);
        }
        else
        {
            cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectSchedulerStates));
        }

        AddCommandParameter(cmd, "schedulerName", schedulerName);

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