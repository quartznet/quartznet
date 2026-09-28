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
}
