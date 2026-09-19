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

namespace Quartz.Configuration;

/// <summary>
/// What a window can say about the scheduler it is a window onto, read from the check-ins its cluster
/// writes.
/// </summary>
/// <param name="Status">
/// The status a reader is shown. Never the window's own, which is
/// <see cref="SchedulerStatus.Created" /> for as long as it exists.
/// </param>
/// <param name="LiveNodes">How many nodes are still checking in.</param>
/// <param name="Nodes">How many nodes have a check-in row at all, live or convicted.</param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
internal readonly record struct WindowLiveness(SchedulerStatus Status, int LiveNodes, int Nodes)
{
    /// <summary>
    /// Asks a window's store what the cluster's nodes are doing, and turns that into a status.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A window is never started, so its own <see cref="IScheduler.Status" /> says
    /// <see cref="SchedulerStatus.Created" /> forever and means only that this process built it. The
    /// question an operator is actually asking — is anything running this schedule? — is answered by
    /// <c>QRTZ_SCHEDULER_STATE</c>, which is the whole of the liveness a shared store carries.
    /// </para>
    /// <para>
    /// Three answers, and the third is the one that matters. A node the cluster would still trust —
    /// <see cref="ClusterNodeState.Alive" /> or <see cref="ClusterNodeState.Overdue" />, judged by the
    /// same predicate the failover sweep convicts with — means <see cref="SchedulerStatus.Running" />.
    /// Rows that are all convicted mean <see cref="SchedulerStatus.Shutdown" />: every node that ever
    /// checked in has stopped. <em>No rows at all</em> means <see cref="SchedulerStatus.Unknown" />,
    /// because a scheduler whose store is not clustered writes no check-in row and is indistinguishable
    /// here from one that was never started — and reporting it as stopped would be the false-dead
    /// reading every shared-storage dashboard is known for.
    /// </para>
    /// </remarks>
    /// <param name="scheduler">The window, whose store watches the cluster's check-ins.</param>
    /// <param name="cancellationToken">The cancellation instruction.</param>
    public static async ValueTask<WindowLiveness> Read(IScheduler scheduler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheduler);

        List<ClusterNode> nodes = await scheduler.QueryClusterNodes(cancellationToken).ConfigureAwait(false);

        int live = 0;
        foreach (ClusterNode node in nodes)
        {
            if (node.State != ClusterNodeState.Failed)
            {
                live++;
            }
        }

        SchedulerStatus status = live > 0
            ? SchedulerStatus.Running
            : nodes.Count > 0
                ? SchedulerStatus.Shutdown
                : SchedulerStatus.Unknown;

        return new WindowLiveness(status, live, nodes.Count);
    }
}
