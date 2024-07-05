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
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlInsertSchedulerState));
        AddCommandParameter(cmd, "schedulerName", schedName);
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
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteSchedulerState));
        AddCommandParameter(cmd, "schedulerName", schedName);
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
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateSchedulerState));
        AddCommandParameter(cmd, "schedulerName", schedName);
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
            cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectSchedulerState));
            AddCommandParameter(cmd, "instanceName", instanceName);
        }
        else
        {
            cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectSchedulerStates));
        }

        AddCommandParameter(cmd, "schedulerName", schedName);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            SchedulerStateRecord rec = new(
                rs.GetString(ColumnInstanceName)!,
                GetDateTimeFromDbValue(rs[ColumnLastCheckinTime]) ?? DateTimeOffset.MinValue,
                GetTimeSpanFromDbValue(rs[ColumnCheckinInterval]) ?? TimeSpan.Zero);

            list.Add(rec);
        }

        return list;
    }
}