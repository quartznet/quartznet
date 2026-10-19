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

using Quartz.Extensibility;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// The three statements an interrupt asked of another node travels by (#4071): who runs a firing, the
/// request written onto its row, and the requests one node's rows carry.
/// </summary>
/// <remarks>
/// Internal, as the progress statement is: they are issued only by this store's own
/// <c>RequestInterrupt</c>, <c>GetInterruptRequests</c> and <c>CancelPrevious</c> fire, every one is
/// portable across the dialects, and keeping them off <see cref="IDriverDelegate" /> leaves a delegate
/// written outside Quartz nothing to implement.
/// </remarks>
public partial class StdAdoDelegate
{
    /// <summary>
    /// The node an executing firing runs on, or <see langword="null" /> when no executing firing has that
    /// entry id.
    /// </summary>
    /// <param name="conn">The unit of work.</param>
    /// <param name="fireInstanceId">The firing's entry id.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal async ValueTask<string?> SelectExecutingFiredTriggerInstance(
        ConnectionAndTransactionHolder conn,
        string fireInstanceId,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectExecutingFiredTriggerInstance));

        // In the order the statement names them: providers that adapt named parameters positionally
        // depend on it.
        AddCommandParameter(cmd, SqlParameters.SchedulerName, schedulerName);
        AddCommandParameter(cmd, SqlParameters.EntryId, fireInstanceId);
        AddCommandParameter(cmd, SqlParameters.ExecutingState, StoredTriggerStates.ToStoredValue(StoredTriggerState.Executing));

        object? result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? null : (string) result;
    }

    /// <summary>
    /// Writes a request to interrupt an executing firing onto its row, by entry id and under no lock.
    /// </summary>
    /// <param name="conn">The unit of work.</param>
    /// <param name="fireInstanceId">The firing's entry id.</param>
    /// <param name="requestedAtUtc">When the request was made.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    /// <returns>The rows written: one, or none when the firing is not executing any more.</returns>
    internal async ValueTask<int> UpdateFiredTriggerInterruptRequested(
        ConnectionAndTransactionHolder conn,
        string fireInstanceId,
        DateTimeOffset requestedAtUtc,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlUpdateFiredTriggerInterruptRequested));

        AddCommandParameter(cmd, SqlParameters.InterruptRequestedTime, GetDbDateTimeValue(requestedAtUtc));
        AddCommandParameter(cmd, SqlParameters.SchedulerName, schedulerName);
        AddCommandParameter(cmd, SqlParameters.EntryId, fireInstanceId);
        AddCommandParameter(cmd, SqlParameters.ExecutingState, StoredTriggerStates.ToStoredValue(StoredTriggerState.Executing));

        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The entry ids of one node's firings that carry an interrupt request.
    /// </summary>
    /// <param name="conn">The unit of work.</param>
    /// <param name="instanceId">The node whose rows are read.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    internal async ValueTask<List<string>> SelectFiredTriggerInterruptRequests(
        ConnectionAndTransactionHolder conn,
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        using DbCommand cmd = PrepareCommand(conn, ReplaceTablePrefix(StdAdoConstants.SqlSelectFiredTriggerInterruptRequests));
        AddCommandParameter(cmd, SqlParameters.SchedulerName, schedulerName);
        AddCommandParameter(cmd, SqlParameters.InstanceName, instanceId);

        List<string> requested = [];
        using DbDataReader rs = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await rs.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            requested.Add(rs.GetString(0));
        }

        return requested;
    }
}
