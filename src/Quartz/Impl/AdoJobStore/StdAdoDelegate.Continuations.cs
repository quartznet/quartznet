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

namespace Quartz.Impl.AdoJobStore;

// The statement the misfire pass issues to find continuations whose parent is gone.
//
// Internal rather than a member of IDriverDelegate, as the history statements are: it is dialect-neutral
// and it is not a seam. A delegate written outside Quartz that derives from this class gets the sweep
// without implementing anything; one that does not derive from it has no sweep, and its completions
// settle continuations under the lock as 4.2's did.
public partial class StdAdoDelegate
{
    /// <summary>
    /// The <c>AWAITING</c> rows whose parent trigger has no row and no fired row: what a continuation
    /// added to a one-off parent in the last moments of the parent's lock-free completion is left as,
    /// and what a parent completing on a 4.1 node leaves behind.
    /// </summary>
    /// <remarks>
    /// The outer scan is the same one <see cref="StdAdoConstants.SqlSelectAwaitingContinuations" />
    /// makes, served by the two leading columns of <c>IDX_QRTZ_T_NFT_ST</c>, and the <c>AWAITING</c> rows
    /// are few. The two probes are each a primary-key or <c>IDX_QRTZ_FT_T_G</c> lookup. The fired-row
    /// probe is what keeps a parent whose row went while a firing of it still runs out of the result:
    /// that firing's completion settles by parent key and does not need the row.
    /// </remarks>
    internal static readonly string SqlSelectStrandedContinuations =
        $"SELECT t.{AdoConstants.ColumnTriggerName}, t.{AdoConstants.ColumnTriggerGroup}, t.{AdoConstants.ColumnContinuesTriggerName}, t.{AdoConstants.ColumnContinuesTriggerGroup}, t.{AdoConstants.ColumnContinuationCondition}"
        + $" FROM {StdAdoConstants.TablePrefixSubst}{AdoConstants.TableTriggers} t"
        + $" WHERE t.{AdoConstants.ColumnSchedulerName} = @{SqlParameters.SchedulerName} AND t.{AdoConstants.ColumnTriggerState} = @{SqlParameters.State} AND t.{AdoConstants.ColumnContinuesTriggerName} IS NOT NULL"
        + $" AND NOT EXISTS (SELECT 1 FROM {StdAdoConstants.TablePrefixSubst}{AdoConstants.TableTriggers} p WHERE p.{AdoConstants.ColumnSchedulerName} = t.{AdoConstants.ColumnSchedulerName} AND p.{AdoConstants.ColumnTriggerName} = t.{AdoConstants.ColumnContinuesTriggerName} AND p.{AdoConstants.ColumnTriggerGroup} = t.{AdoConstants.ColumnContinuesTriggerGroup})"
        + $" AND NOT EXISTS (SELECT 1 FROM {StdAdoConstants.TablePrefixSubst}{AdoConstants.TableFiredTriggers} f WHERE f.{AdoConstants.ColumnSchedulerName} = t.{AdoConstants.ColumnSchedulerName} AND f.{AdoConstants.ColumnTriggerName} = t.{AdoConstants.ColumnContinuesTriggerName} AND f.{AdoConstants.ColumnTriggerGroup} = t.{AdoConstants.ColumnContinuesTriggerGroup})";

    /// <summary>
    /// Reads the continuations whose parent is gone; see <see cref="SqlSelectStrandedContinuations" />.
    /// </summary>
    /// <param name="conn">The unit of work.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual async ValueTask<List<StrandedContinuation>> SelectStrandedContinuations(
        ConnectionAndTransactionHolder conn,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSelectStrandedContinuations));

        // Statement order.
        AddCommandParameter(cmd, SqlParameters.SchedulerName, schedulerName);
        AddCommandParameter(cmd, SqlParameters.State, StoredTriggerStates.ToStoredValue(StoredTriggerState.Awaiting));

        List<StrandedContinuation> stranded = [];

        using DbDataReader rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            int? condition = rs.IsDBNull(4) ? null : Convert.ToInt32(rs.GetValue(4), CultureInfo.InvariantCulture);

            // Through Continuation, as SelectAwaitingContinuations reads, so an absent or unreadable
            // condition is "however it ends" here too.
            Continuation continuation = Continuation.FromStored(rs.GetString(2), rs.GetString(3), condition);
            stranded.Add(new StrandedContinuation(new TriggerKey(rs.GetString(0), rs.GetString(1)), continuation.Parent!, continuation.When));
        }

        return stranded;
    }
}
