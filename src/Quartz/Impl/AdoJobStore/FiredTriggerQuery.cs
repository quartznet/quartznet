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

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// Selects rows of the FIRED_TRIGGERS table, for both
/// <see cref="IDriverDelegate.SelectFiredTriggerRecords" /> and
/// <see cref="IDriverDelegate.DeleteFiredTriggers(ConnectionAndTransactionHolder, FiredTriggerQuery, CancellationToken)" />.
/// </summary>
/// <remarks>
/// <para>
/// The filters combine with AND, and each one that is left null drops out of the statement entirely.
/// A query with every filter null therefore selects — or deletes — every fired trigger of the scheduler.
/// </para>
/// <para>
/// Deliberately unpaged, unlike the listing queries in <c>Quartz.Queries</c>. FIRED_TRIGGERS holds one row
/// per execution currently in flight or reserved, and every caller is a maintenance pass — recovery,
/// cluster failover, blocked-state checks — that has to see the whole set to act correctly. Handing one
/// of those a page would leave the rest of the rows unrecovered. The paged listing over the same table is
/// <see cref="FireInstanceQuery" />, which is a separate type for exactly this reason.
/// </para>
/// </remarks>
public sealed record FiredTriggerQuery
{
    /// <summary>
    /// Limits the result to the fired triggers of one trigger.
    /// </summary>
    public TriggerKey? Trigger { get; init; }

    /// <summary>
    /// Limits the result to the fired triggers of one job.
    /// </summary>
    public JobKey? Job { get; init; }

    /// <summary>
    /// Limits the result to the fired triggers owned by one scheduler instance, named by its
    /// instance id.
    /// </summary>
    public string? InstanceId { get; init; }
}
