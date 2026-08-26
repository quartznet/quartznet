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

namespace Quartz;

/// <summary>
/// One scheduler node as the job store knows it: the listing projection behind
/// <see cref="IScheduler.QueryClusterNodes" />.
/// </summary>
/// <remarks>
/// <para>
/// A node is one running scheduler instance, named by the instance id it was initialized with — the
/// same value <see cref="IScheduler.SchedulerInstanceId" /> reports on that node and
/// <see cref="FireInstance.SchedulerInstanceId" /> records against the firings it owns. Joining the two
/// listings on that id is how an operator sees which node is running what.
/// </para>
/// <para>
/// The store-side sibling is <see cref="Quartz.Impl.AdoJobStore.SchedulerStateRecord" />, which is one
/// SCHEDULER_STATE row in full, for the ADO.NET store's own check-in and recovery passes. This one is
/// the store-neutral projection every job store can produce, and it carries the verdict — the
/// <see cref="State" /> — that a raw row does not.
/// </para>
/// </remarks>
/// <param name="InstanceId">The node's scheduler instance id.</param>
/// <param name="LastCheckInUtc">When the node last recorded that it was alive, or
/// <see langword="null" /> when the store keeps no check-in history — an in-memory store, or an
/// ADO.NET store that is not clustered, neither of which writes SCHEDULER_STATE.</param>
/// <param name="CheckInInterval">How often the node undertook to check in, or <see langword="null" />
/// for the same reason <see cref="LastCheckInUtc" /> is. This is the node's own configured interval as
/// it recorded it, not the reader's.</param>
/// <param name="State">What the reading node makes of the check-in history above, decided by the same
/// predicate cluster recovery applies.</param>
/// <param name="IsCurrentNode">Whether this row is the node that answered the query. Exactly one row
/// carries <see langword="true" />, and it is always present — even before this node's first check-in
/// has written a row for it.</param>
public sealed record ClusterNode(
    string InstanceId,
    DateTimeOffset? LastCheckInUtc,
    TimeSpan? CheckInInterval,
    ClusterNodeState State,
    bool IsCurrentNode);
