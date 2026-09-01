using Microsoft.Extensions.Logging;

namespace Quartz.Impl.AdoJobStore;

public partial class StdAdoDelegate
{
    /// <inheritdoc />
    public virtual async ValueTask<int> InsertCalendar(
        ConnectionAndTransactionHolder conn,
        string calendarName,
        ICalendar calendar,
        CancellationToken cancellationToken = default)
    {
        byte[]? baos = SerializeObject(calendar);

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlInsertCalendar));
        AddCommandParameter(cmd, SqlParameters.SchedulerName, SchedulerName);
        AddCommandParameter(cmd, SqlParameters.CalendarName, calendarName);
        AddCommandParameter(cmd, SqlParameters.Calendar, baos, DbProvider.Metadata.BinaryParameterType);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> UpdateCalendar(
        ConnectionAndTransactionHolder conn,
        string calendarName,
        ICalendar calendar,
        CancellationToken cancellationToken = default)
    {
        byte[]? baos = SerializeObject(calendar);

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlUpdateCalendar));
        AddCommandParameter(cmd, SqlParameters.SchedulerName, SchedulerName);
        AddCommandParameter(cmd, SqlParameters.Calendar, baos, DbProvider.Metadata.BinaryParameterType);
        AddCommandParameter(cmd, SqlParameters.CalendarName, calendarName);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<bool> CalendarExists(
        ConnectionAndTransactionHolder conn,
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectCalendarExistence));
        AddCommandParameter(cmd, SqlParameters.SchedulerName, SchedulerName);
        AddCommandParameter(cmd, SqlParameters.CalendarName, calendarName);
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public virtual async ValueTask<ICalendar?> SelectCalendar(ConnectionAndTransactionHolder conn,
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectCalendar));
        AddCommandParameter(cmd, SqlParameters.SchedulerName, SchedulerName);
        AddCommandParameter(cmd, SqlParameters.CalendarName, calendarName);
        using var rs = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
        ICalendar? calendar = null;
        if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            calendar = await GetObjectFromBlob<ICalendar>(rs, 0, cancellationToken).ConfigureAwait(false);
        }

        if (null == calendar)
        {
            logger.CalendarNotFound(calendarName);
        }

        return calendar;
    }

    /// <inheritdoc />
    public virtual async ValueTask<bool> CalendarIsReferenced(
        ConnectionAndTransactionHolder conn,
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectReferencedCalendar));
        AddCommandParameter(cmd, SqlParameters.SchedulerName, SchedulerName);
        AddCommandParameter(cmd, SqlParameters.CalendarName, calendarName);
        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> DeleteCalendar(
        ConnectionAndTransactionHolder conn,
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlDeleteCalendar));
        AddCommandParameter(cmd, SqlParameters.SchedulerName, SchedulerName);
        AddCommandParameter(cmd, SqlParameters.CalendarName, calendarName);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}