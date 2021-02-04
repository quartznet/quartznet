using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Logging;

namespace Quartz.Impl.AdoJobStore
{
    public partial class StdAdoDelegate
    {
        /// <inheritdoc />
        public virtual async Task<int> InsertCalendar(
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
        public virtual async Task<int> UpdateCalendar(
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
        public virtual async Task<bool> CalendarExists(
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
        public virtual async Task<ICalendar?> SelectCalendar(ConnectionAndTransactionHolder conn,
            string calendarName,
            CancellationToken cancellationToken = default)
        {
            using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectCalendar));
            AddCommandParameter(cmd, "schedulerName", schedName);
            AddCommandParameter(cmd, "calendarName", calendarName);
            using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            ICalendar? cal = null;
            if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                cal = await GetObjectFromBlob<ICalendar>(rs, 0, cancellationToken).ConfigureAwait(false);
            }

            if (null == cal)
            {
                logger.Warn("Couldn't find calendar with name '" + calendarName + "'.");
            }

            return cal;
        }

        /// <inheritdoc />
        public virtual async Task<bool> CalendarIsReferenced(
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
        public virtual async Task<int> DeleteCalendar(
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
        public virtual async Task<int> SelectNumCalendars(
            ConnectionAndTransactionHolder conn,
            CancellationToken cancellationToken = default)
        {
            using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectNumCalendars));
            AddCommandParameter(cmd, "schedulerName", schedName);

            int count = 0;
            using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                count = Convert.ToInt32(rs.GetValue(0), CultureInfo.InvariantCulture);
            }

            return count;
        }

        /// <inheritdoc />
        public virtual async Task<IReadOnlyCollection<string>> SelectCalendars(
            ConnectionAndTransactionHolder conn,
            CancellationToken cancellationToken = default)
        {
            using var cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectCalendars));
            AddCommandParameter(cmd, "schedulerName", schedName);

            using var rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            List<string> list = new List<string>();
            while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                list.Add((string) rs[0]);
            }

            return list;
        }
    }
}