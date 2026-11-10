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
using System.Threading;
using System.Threading.Tasks;

namespace Quartz.Impl.AdoJobStore;

// The statement the store's check after a failed commit issues before it reads (#4259).
//
// Internal rather than a member of IDriverDelegate or INextVersionDelegate: it is dialect-neutral and it
// is not a seam, so it is not one 4.x promotes to IDriverDelegate either. A delegate written outside
// Quartz that derives from this class gets it without implementing anything. One that does not derive
// from it checks by reading alone, as earlier versions did, and the release of the fire is its fallback.
public partial class StdAdoDelegate
{
    /// <summary>
    /// Writes a trigger's state over itself: a primary-key seek that changes nothing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why it exists: a commit can reach the database and still be settling there when the client hears
    /// that it failed. Until it settles, other transactions do not see it. The store then checks whether
    /// the commit landed, and a check that only reads answers "it did not" for a commit that did (#4259).
    /// </para>
    /// <para>
    /// The commit being checked wrote this row, and holds the row's lock until it lands or rolls back. A
    /// write waits on that lock where a read does not. PostgreSQL waits on the writing transaction, InnoDB
    /// takes the exclusive lock even when nothing changes, SQL Server's writers block under read committed
    /// snapshot, and Oracle waits on the row. So the read after it gets the true answer.
    /// </para>
    /// <para>
    /// Firebird, or an isolation level above read committed, may raise an update conflict instead of
    /// waiting. The check runs in the store's retry loop, which absorbs that.
    /// </para>
    /// </remarks>
    internal static readonly string SqlSettleTriggerRow =
        $"UPDATE {StdAdoConstants.TablePrefixSubst}{AdoConstants.TableTriggers} SET {AdoConstants.ColumnTriggerState} = {AdoConstants.ColumnTriggerState}"
        + $" WHERE {AdoConstants.ColumnSchedulerName} = @schedulerName AND {AdoConstants.ColumnTriggerName} = @triggerName AND {AdoConstants.ColumnTriggerGroup} = @triggerGroup";

    /// <summary>
    /// Waits until no commit still settling on the database holds <paramref name="triggerKey" />'s row;
    /// see <see cref="SqlSettleTriggerRow" />.
    /// </summary>
    /// <param name="conn">The check's unit of work.</param>
    /// <param name="triggerKey">A trigger whose row the commit being checked wrote.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal virtual async Task SettleTriggerRow(
        ConnectionAndTransactionHolder conn,
        TriggerKey triggerKey,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(SqlSettleTriggerRow));

        // Statement order.
        AddCommandParameter(cmd, "schedulerName", schedName);
        AddCommandParameter(cmd, "triggerName", triggerKey.Name);
        AddCommandParameter(cmd, "triggerGroup", triggerKey.Group);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
