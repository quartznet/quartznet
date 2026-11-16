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
using System.Data.Common;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Quartz.Util;

namespace Quartz.Impl.AdoJobStore;

// The read the fire of an acquired trigger makes, which says whether the acquisition is still its own
// (#4323).
//
// Internal rather than a member of IDriverDelegate or INextVersionDelegate, as in 4.x: it stands in for
// SelectTriggerStatus, which a subclass may have overridden, so the store asks it only of a delegate Quartz
// ships, and asks any other delegate SelectTriggerStatus and the fired-trigger rows apart.
public partial class StdAdoDelegate
{
    private bool? shipped;

    /// <summary>
    /// Whether this is one of the delegates Quartz ships, asked once: a subclass of one keeps the
    /// single-trigger members it may override.
    /// </summary>
    internal bool IsShipped => shipped ??= IsShippedDelegate();

    /// <summary>
    /// The state and next fire time of one trigger, as <see cref="SelectTriggerStatus" /> reads them, and
    /// whether the fired-trigger row of <paramref name="fireInstanceId" /> is still there and still the
    /// trigger's <c>ACQUIRED</c> reservation, in one statement.
    /// </summary>
    /// <param name="conn">The fire's transaction.</param>
    /// <param name="triggerKey">The acquired trigger.</param>
    /// <param name="fireInstanceId">The entry id the acquisition reserved the trigger under.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// The state, <see cref="AdoConstants.StateDeleted" /> when there is no such trigger, the next fire time,
    /// and whether the reservation holds it.
    /// </returns>
    internal async Task<(string State, DateTimeOffset? NextFireTimeUtc, bool Reserved)> SelectTriggerStateForFire(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        string? fireInstanceId,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectTriggerStateWithReservation));

        // Statement order, for providers that bind positionally.
        AddCommandParameter(cmd, "triggerEntryId", fireInstanceId);
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

        using DbDataReader rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return (StateDeleted, null, false);
        }

        string state = rs.GetString(ColumnTriggerState)!;
        DateTimeOffset? nextFireTime = GetDateTimeFromDbValue(rs[ColumnNextFireTime]);

        // Providers disagree on the CLR type of a CASE expression, so go through Convert.
        bool reserved = Convert.ToInt32(rs.GetValue(2), CultureInfo.InvariantCulture) != 0;
        return (state, nextFireTime, reserved);
    }

    private bool IsShippedDelegate()
    {
        Type type = GetType();
        return type == typeof(StdAdoDelegate)
               || type == typeof(PostgreSQLDelegate)
               || type == typeof(SqlServerDelegate)
               || type == typeof(MySQLDelegate)
               || type == typeof(OracleDelegate)
               || type == typeof(SQLiteDelegate)
               || type == typeof(FirebirdDelegate);
    }
}
