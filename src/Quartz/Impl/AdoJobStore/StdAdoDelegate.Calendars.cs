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

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlInsertCalendar));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "calendarName", calendarName);
        AddCommandParameter(cmd, "calendar", baos, DbProvider.Metadata.DbBinaryType);

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

        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlUpdateCalendar));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "calendar", baos, DbProvider.Metadata.DbBinaryType);
        AddCommandParameter(cmd, "calendarName", calendarName);

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<bool> CalendarExists(
        ConnectionAndTransactionHolder conn,
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectCalendarExistence));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "calendarName", calendarName);
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
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectCalendar));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "calendarName", calendarName);
        using var rs = await cmd.ExecuteReaderAsync(System.Data.CommandBehavior.SequentialAccess, cancellationToken).ConfigureAwait(false);
        ICalendar? cal = null;
        if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            cal = await GetObjectFromBlob<ICalendar>(rs, 0, cancellationToken).ConfigureAwait(false);
        }

        if (null == cal)
        {
            logger.LogWarning("Couldn't find calendar with name '{CalendarName}'", calendarName);
        }

        return cal;
    }

    /// <inheritdoc />
    public virtual async ValueTask<bool> CalendarIsReferenced(
        ConnectionAndTransactionHolder conn,
        string calendarName,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectReferencedCalendar));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "calendarName", calendarName);
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
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlDeleteCalendar));
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "calendarName", calendarName);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual async ValueTask<int> SelectNumCalendars(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectNumCalendars));
        AddCommandParameter(cmd, "schedulerName", schedName);

        int count = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
        return count;
    }

    /// <inheritdoc />
    public virtual async ValueTask<List<string>> SelectCalendars(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default)
    {
        using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectCalendars));
        AddCommandParameter(cmd, "schedulerName", schedName);

        using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        List<string> list = [];
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(rs.GetString(0));
        }

        return list;
    }
}