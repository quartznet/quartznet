using System.Data.Common;

using Quartz.Impl.AdoJobStore;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A fire that fails on the database after its own writes have gone out, for the shipped delegate of
/// each engine: the fired row is updated, the job's other triggers are moved to <c>BLOCKED</c>, the
/// trigger row is written, and then a statement the database refuses is issued in the same transaction.
/// That is the shape of a constraint violation or a schema drift on the last statement of a fire, and
/// it is what #3931 is about.
/// </summary>
/// <remarks>
/// Static, because the property bridge builds the delegate from its type name and the fixture never
/// holds the instance; the fixtures that use it are <c>[NonParallelizable]</c>.
/// </remarks>
internal static class FireFault
{
    private static readonly List<string> fireAttempts = [];

    /// <summary>The name of the trigger whose every fire fails, or <see langword="null" /> for none.</summary>
    public static string FailFireOf { get; set; }

    /// <summary>Every fire the delegate was asked to write, by trigger name, in order — a rolled-back attempt included.</summary>
    public static List<string> FireAttempts
    {
        get
        {
            lock (fireAttempts)
            {
                return [.. fireAttempts];
            }
        }
    }

    public static void Reset()
    {
        lock (fireAttempts)
        {
            fireAttempts.Clear();
        }

        FailFireOf = null;
        CalendarFault = new MisfireCalendarFault();
    }

    /// <summary>The calendar name a read of which the delegate answers with <see cref="CalendarFault" />'s calendar.</summary>
    public const string FaultyCalendarName = "faulty";

    /// <summary>
    /// The calendar the delegate hands out for <see cref="FaultyCalendarName" />, and the database read of it
    /// failing once when told to (#4006).
    /// </summary>
    public static MisfireCalendarFault CalendarFault { get; private set; } = new();

    /// <summary>
    /// Reads the calendar as the dialect's delegate does, except the faulty one, which is answered from
    /// <see cref="CalendarFault" /> after a read that fails on the database when told to.
    /// </summary>
    public static async ValueTask<ICalendar> SelectCalendar(
        Func<ValueTask<ICalendar>> selectCalendar,
        ConnectionAndTransactionHolder conn,
        string calendarName,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(calendarName, FaultyCalendarName, StringComparison.Ordinal))
        {
            return await selectCalendar().ConfigureAwait(false);
        }

        MisfireCalendarFault fault = CalendarFault;
        if (fault.TakeReadFailure())
        {
            using DbCommand command = conn.Connection.CreateCommand();
            conn.Attach(command);
            command.CommandText = "SELECT 1 FROM QRTZ_NO_SUCH_TABLE";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return fault.Calendar;
    }

    /// <summary>
    /// Writes the fire as the dialect's delegate does, then fails it if it is the one to fail.
    /// </summary>
    public static async ValueTask ApplyTriggerFired(
        Func<ValueTask> applyTriggerFired,
        ConnectionAndTransactionHolder conn,
        TriggerFiredUpdate update,
        CancellationToken cancellationToken)
    {
        string triggerName = update.Trigger.Key.Name;
        lock (fireAttempts)
        {
            fireAttempts.Add(triggerName);
        }

        await applyTriggerFired().ConfigureAwait(false);

        if (!string.Equals(triggerName, FailFireOf, StringComparison.Ordinal))
        {
            return;
        }

        // A real exception from the driver, and one nothing classifies as transient — on PostgreSQL it
        // also leaves the transaction aborted, as any failed statement there does.
        using DbCommand command = conn.Connection.CreateCommand();
        conn.Attach(command);
        command.CommandText = "SELECT 1 FROM QRTZ_NO_SUCH_TABLE";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

public sealed class FaultingPostgreSQLDelegate : PostgreSQLDelegate
{
    public override ValueTask ApplyTriggerFired(ConnectionAndTransactionHolder conn, TriggerFiredUpdate update, CancellationToken cancellationToken = default)
    {
        return FireFault.ApplyTriggerFired(() => base.ApplyTriggerFired(conn, update, cancellationToken), conn, update, cancellationToken);
    }

    public override ValueTask<ICalendar> SelectCalendar(ConnectionAndTransactionHolder conn, string calendarName, CancellationToken cancellationToken = default)
    {
        return FireFault.SelectCalendar(() => base.SelectCalendar(conn, calendarName, cancellationToken), conn, calendarName, cancellationToken);
    }
}

public sealed class FaultingSqlServerDelegate : SqlServerDelegate
{
    public override ValueTask ApplyTriggerFired(ConnectionAndTransactionHolder conn, TriggerFiredUpdate update, CancellationToken cancellationToken = default)
    {
        return FireFault.ApplyTriggerFired(() => base.ApplyTriggerFired(conn, update, cancellationToken), conn, update, cancellationToken);
    }

    public override ValueTask<ICalendar> SelectCalendar(ConnectionAndTransactionHolder conn, string calendarName, CancellationToken cancellationToken = default)
    {
        return FireFault.SelectCalendar(() => base.SelectCalendar(conn, calendarName, cancellationToken), conn, calendarName, cancellationToken);
    }
}

public sealed class FaultingMySQLDelegate : MySQLDelegate
{
    public override ValueTask ApplyTriggerFired(ConnectionAndTransactionHolder conn, TriggerFiredUpdate update, CancellationToken cancellationToken = default)
    {
        return FireFault.ApplyTriggerFired(() => base.ApplyTriggerFired(conn, update, cancellationToken), conn, update, cancellationToken);
    }

    public override ValueTask<ICalendar> SelectCalendar(ConnectionAndTransactionHolder conn, string calendarName, CancellationToken cancellationToken = default)
    {
        return FireFault.SelectCalendar(() => base.SelectCalendar(conn, calendarName, cancellationToken), conn, calendarName, cancellationToken);
    }
}

public sealed class FaultingOracleDelegate : OracleDelegate
{
    public override ValueTask ApplyTriggerFired(ConnectionAndTransactionHolder conn, TriggerFiredUpdate update, CancellationToken cancellationToken = default)
    {
        return FireFault.ApplyTriggerFired(() => base.ApplyTriggerFired(conn, update, cancellationToken), conn, update, cancellationToken);
    }

    public override ValueTask<ICalendar> SelectCalendar(ConnectionAndTransactionHolder conn, string calendarName, CancellationToken cancellationToken = default)
    {
        return FireFault.SelectCalendar(() => base.SelectCalendar(conn, calendarName, cancellationToken), conn, calendarName, cancellationToken);
    }
}

public sealed class FaultingFirebirdDelegate : FirebirdDelegate
{
    public override ValueTask ApplyTriggerFired(ConnectionAndTransactionHolder conn, TriggerFiredUpdate update, CancellationToken cancellationToken = default)
    {
        return FireFault.ApplyTriggerFired(() => base.ApplyTriggerFired(conn, update, cancellationToken), conn, update, cancellationToken);
    }

    public override ValueTask<ICalendar> SelectCalendar(ConnectionAndTransactionHolder conn, string calendarName, CancellationToken cancellationToken = default)
    {
        return FireFault.SelectCalendar(() => base.SelectCalendar(conn, calendarName, cancellationToken), conn, calendarName, cancellationToken);
    }
}

/// <summary>
/// A calendar that throws while it is told to, and the one database read of it that fails when told to.
/// </summary>
/// <remarks>
/// Every clone shares the fault, because a store may keep a copy of the calendar it is given.
/// </remarks>
internal sealed class MisfireCalendarFault
{
    private readonly Lock gate = new();
    private bool throwing;
    private bool failNextRead;
    private int thrown;
    private int reads;

    public MisfireCalendarFault()
    {
        Calendar = new FaultyCalendar(this);
    }

    public ICalendar Calendar { get; }

    /// <summary>How many times the calendar has thrown.</summary>
    public int Thrown
    {
        get
        {
            lock (gate)
            {
                return thrown;
            }
        }
    }

    /// <summary>How many times the calendar has been read from the database, a failed read included.</summary>
    public int Reads
    {
        get
        {
            lock (gate)
            {
                return reads;
            }
        }
    }

    public void ThrowAlways()
    {
        lock (gate)
        {
            throwing = true;
        }
    }

    public void FailNextRead()
    {
        lock (gate)
        {
            failNextRead = true;
        }
    }

    public bool TakeReadFailure()
    {
        lock (gate)
        {
            reads++;
            bool fail = failNextRead;
            failNextRead = false;
            return fail;
        }
    }

    private void Consult()
    {
        lock (gate)
        {
            if (!throwing)
            {
                return;
            }

            thrown++;
        }

        throw new InvalidOperationException("The holiday feed is unreachable.");
    }

    private sealed class FaultyCalendar(MisfireCalendarFault fault) : ICalendar
    {
        public string Description { get; set; }

        public ICalendar CalendarBase { get; set; }

        public bool IsTimeIncluded(DateTimeOffset timeUtc)
        {
            fault.Consult();
            return true;
        }

        public DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc)
        {
            fault.Consult();
            return timeUtc;
        }

        public ICalendar Clone() => new FaultyCalendar(fault) { Description = Description, CalendarBase = CalendarBase };
    }
}
