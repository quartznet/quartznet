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

using System.Data.Common;
using System.Globalization;

using Quartz.Extensibility;
using Quartz.Util;

namespace Quartz.Impl.AdoJobStore;

// The read the fire of an acquired trigger makes, which says whether the acquisition is still its own
// (#4323).
//
// Internal rather than a member of IDriverDelegate, as the round reads are: it stands in for
// SelectTriggerHeader, which a subclass may have overridden, so the store asks it only of a delegate
// Quartz ships, and asks any other delegate SelectTriggerHeader and the fired-trigger rows apart.
public partial class StdAdoDelegate
{
    /// <summary>
    /// The header of one trigger, as <see cref="SelectTriggerHeader" /> reads it, and whether the
    /// fired-trigger row of <paramref name="fireInstanceId" /> is still there and still the trigger's
    /// <c>ACQUIRED</c> reservation, in one statement.
    /// </summary>
    /// <param name="conn">The fire's transaction.</param>
    /// <param name="triggerKey">The acquired trigger.</param>
    /// <param name="fireInstanceId">The entry id the acquisition reserved the trigger under.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>
    /// The header, or <see langword="null" /> when there is no such trigger, and whether the reservation
    /// holds it.
    /// </returns>
    internal async ValueTask<(StoredTriggerHeader? Header, bool Reserved)> SelectTriggerHeaderForFire(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        string? fireInstanceId,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectTriggerHeaderWithReservation));

        // Statement order, for providers that bind positionally.
        AddCommandParameter(cmd, SqlParameters.EntryId, fireInstanceId);
        AddCommandParameter(cmd, SqlParameters.SchedulerName, schedulerName);
        AddCommandParameter(cmd, SqlParameters.TriggerName, triggerKey.Name);
        AddCommandParameter(cmd, SqlParameters.TriggerGroup, triggerKey.Group);

        using DbDataReader rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return (null, false);
        }

        StoredTriggerHeader header = new(
            triggerKey,
            new JobKey(rs.GetString(AdoConstants.ColumnJobName)!, rs.GetString(AdoConstants.ColumnJobGroup)!),
            StoredTriggerStates.FromStoredValue(rs.GetString(AdoConstants.ColumnTriggerState)),
            GetDateTimeFromDbValue(rs[AdoConstants.ColumnNextFireTime]),
            rs.GetString(AdoConstants.ColumnTriggerType)!);

        // Providers disagree on the CLR type of a CASE expression, so go through Convert.
        bool reserved = Convert.ToInt32(rs.GetValue(5), CultureInfo.InvariantCulture) != 0;
        return (header, reserved);
    }
}
