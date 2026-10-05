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

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Impl.AdoJobStore;
using Quartz.Spi;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A fire that fails on the database after its own writes have gone out, for the shipped delegate of
/// each engine: the fired row is updated, the job's other triggers are moved to <c>BLOCKED</c>, the
/// trigger row is written, and then a statement the database refuses is issued in the same transaction.
/// That is the shape of a constraint violation or a schema drift on the last statement of a fire, and
/// it is what #3931 is about.
/// </summary>
/// <remarks>
/// A fire's first write is its fired row and its last is the trigger row, so an attempt is counted at
/// the one and failed after the other. Static, because the store builds the delegate from its type name
/// and the fixture never holds the instance; the fixtures that use it are <c>[NonParallelizable]</c>.
/// </remarks>
internal static class FireFault
{
    private static readonly List<string> fireAttempts = new List<string>();

    /// <summary>The name of the trigger whose every fire fails, or <see langword="null" /> for none.</summary>
    public static string FailFireOf { get; set; }

    /// <summary>Every fire the delegate was asked to write, by trigger name, in order — a rolled-back attempt included.</summary>
    public static List<string> FireAttempts
    {
        get
        {
            lock (fireAttempts)
            {
                return fireAttempts.ToList();
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
    public static MisfireCalendarFault CalendarFault { get; private set; } = new MisfireCalendarFault();

    /// <summary>
    /// Reads the calendar as the dialect's delegate does, except the faulty one, which is answered from
    /// <see cref="CalendarFault" /> after a read that fails on the database when told to.
    /// </summary>
    public static async Task<ICalendar> SelectCalendar(
        Func<Task<ICalendar>> selectCalendar,
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
    /// Counts the fire whose fired row is being written.
    /// </summary>
    public static void RecordFireAttempt(IOperableTrigger trigger)
    {
        lock (fireAttempts)
        {
            fireAttempts.Add(trigger.Key.Name);
        }
    }

    /// <summary>
    /// Writes the trigger row as the dialect's delegate does, then fails the fire if it is the one to fail.
    /// </summary>
    public static async Task<int> UpdateTrigger(
        Func<Task<int>> updateTrigger,
        ConnectionAndTransactionHolder conn,
        IOperableTrigger trigger,
        CancellationToken cancellationToken)
    {
        int updated = await updateTrigger().ConfigureAwait(false);

        if (!string.Equals(trigger.Key.Name, FailFireOf, StringComparison.Ordinal))
        {
            return updated;
        }

        // A real exception from the driver, and one nothing classifies as transient — on PostgreSQL it
        // also leaves the transaction aborted, as any failed statement there does.
        using DbCommand command = conn.Connection.CreateCommand();
        conn.Attach(command);
        command.CommandText = "SELECT 1 FROM QRTZ_NO_SUCH_TABLE";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return updated;
    }
}

public sealed class FaultingPostgreSQLDelegate : PostgreSQLDelegate
{
    public override Task<int> UpdateFiredTrigger(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail job, CancellationToken cancellationToken = default)
    {
        FireFault.RecordFireAttempt(trigger);
        return base.UpdateFiredTrigger(conn, trigger, state, job, cancellationToken);
    }

    public override Task<int> UpdateTrigger(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail, CancellationToken cancellationToken = default)
    {
        return FireFault.UpdateTrigger(() => base.UpdateTrigger(conn, trigger, state, jobDetail, cancellationToken), conn, trigger, cancellationToken);
    }

    public override Task<ICalendar> SelectCalendar(ConnectionAndTransactionHolder conn, string calendarName, CancellationToken cancellationToken = default)
    {
        return FireFault.SelectCalendar(() => base.SelectCalendar(conn, calendarName, cancellationToken), conn, calendarName, cancellationToken);
    }
}

public sealed class FaultingSqlServerDelegate : SqlServerDelegate
{
    public override Task<int> UpdateFiredTrigger(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail job, CancellationToken cancellationToken = default)
    {
        FireFault.RecordFireAttempt(trigger);
        return base.UpdateFiredTrigger(conn, trigger, state, job, cancellationToken);
    }

    public override Task<int> UpdateTrigger(ConnectionAndTransactionHolder conn, IOperableTrigger trigger, string state, IJobDetail jobDetail, CancellationToken cancellationToken = default)
    {
        return FireFault.UpdateTrigger(() => base.UpdateTrigger(conn, trigger, state, jobDetail, cancellationToken), conn, trigger, cancellationToken);
    }

    public override Task<ICalendar> SelectCalendar(ConnectionAndTransactionHolder conn, string calendarName, CancellationToken cancellationToken = default)
    {
        return FireFault.SelectCalendar(() => base.SelectCalendar(conn, calendarName, cancellationToken), conn, calendarName, cancellationToken);
    }
}

/// <summary>
/// The store, with the misfire handler's pass reachable from a test that drives the store by hand.
/// </summary>
public sealed class MisfirePassJobStore : JobStoreTX
{
    public Task<RecoverMisfiredJobsResult> RecoverMisfires() => DoRecoverMisfires(Guid.NewGuid(), CancellationToken.None);
}

/// <summary>
/// A calendar that throws on the calls it is told to, and the one database read of it that fails when
/// told to (#4006).
/// </summary>
/// <remarks>
/// Every clone shares the fault, because a store keeps a copy of the calendar it reads.
/// </remarks>
public sealed class MisfireCalendarFault
{
    private readonly object gate = new object();
    private int remaining;
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

    public void ThrowOnce()
    {
        lock (gate)
        {
            remaining = 1;
        }
    }

    public void ThrowAlways()
    {
        lock (gate)
        {
            remaining = -1;
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
            if (remaining == 0)
            {
                return;
            }

            if (remaining > 0)
            {
                remaining--;
            }

            thrown++;
        }

        throw new InvalidOperationException("The holiday feed is unreachable.");
    }

    private sealed class FaultyCalendar : ICalendar
    {
        private readonly MisfireCalendarFault fault;

        public FaultyCalendar(MisfireCalendarFault fault)
        {
            this.fault = fault;
        }

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
