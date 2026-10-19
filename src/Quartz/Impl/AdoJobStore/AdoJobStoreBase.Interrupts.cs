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

using System.Collections.Concurrent;

using Quartz.Core;

namespace Quartz.Impl.AdoJobStore;

/// <summary>
/// An interrupt asked of a firing another node runs (#4071): recorded on the firing's own
/// <c>QRTZ_FIRED_TRIGGERS</c> row, and read back by the node running it after each of its check-ins.
/// </summary>
/// <remarks>
/// <para>
/// No lock is taken on either side. The request is one <c>UPDATE</c> of a row by its primary key, which
/// only an executing firing's row matches; the read is one <c>SELECT</c> of this node's own rows. A firing
/// that completes deletes its row, request and all, so nothing has to be cleaned up.
/// </para>
/// <para>
/// A node older than 4.6 never reads the column. Which peers do is what the last check-in's read of
/// <c>QUARTZ_VERSION</c> said (#4067), so a request for a firing on an older node is recorded, never
/// answered, and said so once per node.
/// </para>
/// </remarks>
internal abstract partial class AdoJobStoreBase
{
    /// <summary>
    /// The live peers the last check-in read, each with the Quartz version its row carries, or
    /// <see langword="null" /> before the first check-in. What decides whether a peer answers an interrupt
    /// request recorded on one of its firings.
    /// </summary>
    private volatile Dictionary<string, string?>? livePeerVersions;

    /// <summary>
    /// The older nodes a request has already been recorded for, so that each is said once.
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> undeliverableInterruptNodes = new(StringComparer.Ordinal);

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// One <c>SELECT</c> of the node running the firing and one <c>UPDATE</c> of its row, in one transaction
    /// and under no lock — except on SQLite, where every operation is serialized. The row is matched by
    /// entry id and the <c>EXECUTING</c> state, so a reservation or a completed firing records nothing.
    /// </para>
    /// <para>
    /// <see langword="false" /> on a store that is not clustered, which runs no check-in to read a request
    /// by, and through a delegate that does not derive from <see cref="StdAdoDelegate" />, which has no
    /// such statement.
    /// </para>
    /// <para>
    /// A dashboard's store-attached window (#4141) records one too, clustered or not, but only for a firing
    /// on a node that reads it: a live row in <c>QRTZ_SCHEDULER_STATE</c> stamped 4.6 or later. A window
    /// runs no check-in, so it reads that row in the same transaction rather than trusting one it last saw.
    /// For any other firing it records nothing and throws, naming the node and why.
    /// </para>
    /// </remarks>
    /// <exception cref="SchedulerException">
    /// A store-attached window was asked to interrupt a firing on a node that never reads a request: one
    /// that is not clustered, is older than 4.6, or has stopped checking in.
    /// </exception>
    public async ValueTask<bool> RequestInterrupt(string fireInstanceId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fireInstanceId);

        if ((!Clustered && !ClusterObserver) || Delegate is not StdAdoDelegate standard)
        {
            return false;
        }

        RequestedInterrupt requested = await ExecuteWithoutLock(
            conn => Guarded(
                async () =>
                {
                    string? owner = await standard.SelectExecutingFiredTriggerInstance(conn, fireInstanceId, cancellationToken).ConfigureAwait(false);
                    if (owner is null)
                    {
                        return default;
                    }

                    if (ClusterObserver && await WhyNoRequestIsRead(conn, owner, cancellationToken).ConfigureAwait(false) is { } refusal)
                    {
                        return new RequestedInterrupt(owner, refusal);
                    }

                    int written = await standard.UpdateFiredTriggerInterruptRequested(conn, fireInstanceId, timeProvider.GetUtcNow(), cancellationToken).ConfigureAwait(false);
                    return written > 0 ? new RequestedInterrupt(owner, Refusal: null) : default;
                },
                "request an interrupt of fire instance " + fireInstanceId),
            cancellationToken).ConfigureAwait(false);

        if (requested.RunningOn is not { } runningOn)
        {
            return false;
        }

        if (requested.Refusal is { } why)
        {
            Throw.SchedulerException(
                $"Fire instance '{fireInstanceId}' cannot be interrupted through the store-attached window '{InstanceName}': "
                + $"the node running it, '{runningOn}', {why}, so it would never read the request. "
                + "Interrupt it through that node itself: its HTTP API or its agent.");
        }

        ReportInterruptRequested(fireInstanceId, runningOn);
        return true;
    }

    /// <summary>
    /// Why the node <paramref name="instanceId" /> would never read an interrupt request, or
    /// <see langword="null" /> when it would: its row in <c>QRTZ_SCHEDULER_STATE</c> is live and stamped 4.6
    /// or later.
    /// </summary>
    /// <remarks>
    /// Judged as the window judges every node in its listing, by the row alone; see <see cref="FailedIfAfter" />.
    /// </remarks>
    private async ValueTask<string?> WhyNoRequestIsRead(ConnectionAndTransactionHolder conn, string instanceId, CancellationToken cancellationToken)
    {
        List<SchedulerStateRecord> rows = await Delegate.SelectSchedulerStateRecords(conn, instanceId, cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return "writes no check-in row, which a node that is not clustered never does";
        }

        SchedulerStateRecord row = rows[0];
        if (ClassifyClusterNode(row, timeProvider.GetUtcNow()) == ClusterNodeState.Failed)
        {
            return $"has not checked in since {row.CheckinTimestamp:O}";
        }

        return SchedulerNodeVersion.SupportsInterruptRequests(row.QuartzVersion)
            ? null
            : $"runs Quartz {row.QuartzVersion ?? "older than 4.6"}, and nodes before {SchedulerNodeVersion.InterruptRequestsSince} read no requests";
    }

    /// <summary>
    /// What one request came to: the node running the firing, or <see langword="null" /> when nothing is
    /// executing under its id, and why it was not recorded when it was not.
    /// </summary>
    private readonly record struct RequestedInterrupt(string? RunningOn, string? Refusal);

    /// <inheritdoc />
    /// <remarks>
    /// One <c>SELECT</c> of this node's own rows, on <c>IDX_QRTZ_FT_INST_JOB_REQ_RCVRY</c>, under no lock —
    /// except on SQLite. An empty list on a store that is not clustered and through a delegate that does not
    /// derive from <see cref="StdAdoDelegate" />.
    /// </remarks>
    public ValueTask<List<string>> GetInterruptRequests(CancellationToken cancellationToken = default)
    {
        if (!Clustered || Delegate is not StdAdoDelegate standard)
        {
            return new ValueTask<List<string>>(new List<string>());
        }

        return ExecuteWithoutLock(
            conn => Guarded(
                () => standard.SelectFiredTriggerInterruptRequests(conn, InstanceId, cancellationToken),
                "read the interrupt requests of this node's firings"),
            cancellationToken);
    }

    /// <summary>
    /// Whether a request recorded on a firing that <paramref name="instanceId" /> runs is answered: this
    /// node's own always is, and a peer's is when the last check-in read it live and at 4.6 or later.
    /// </summary>
    /// <remarks>
    /// A peer the last check-in did not read — one that joined since, or one that is gone — is not known
    /// to answer, and is treated as a node that does not.
    /// </remarks>
    internal bool ReadsInterruptRequests(string instanceId)
    {
        if (string.Equals(instanceId, InstanceId, StringComparison.Ordinal))
        {
            return true;
        }

        return livePeerVersions is { } peers
               && peers.TryGetValue(instanceId, out string? version)
               && SchedulerNodeVersion.SupportsInterruptRequests(version);
    }

    /// <summary>
    /// Records a request to interrupt a firing another node runs, in the caller's transaction: what a
    /// <see cref="OverlapPolicy.CancelPrevious" /> fire does about the firings it replaces elsewhere.
    /// </summary>
    /// <returns>Whether the firing was still executing, and so carries the request.</returns>
    private async ValueTask<bool> RecordInterruptRequest(
        ConnectionAndTransactionHolder conn,
        StdAdoDelegate standard,
        string fireInstanceId,
        string runningOn,
        CancellationToken cancellationToken)
    {
        int written = await Guarded(
            () => standard.UpdateFiredTriggerInterruptRequested(conn, fireInstanceId, timeProvider.GetUtcNow(), cancellationToken),
            "request an interrupt of fire instance " + fireInstanceId).ConfigureAwait(false);

        if (written == 0)
        {
            return false;
        }

        ReportInterruptRequested(fireInstanceId, runningOn);
        return true;
    }

    /// <summary>
    /// Says that a request was recorded and which node it waits for, and, the first time it is a node that
    /// will never answer, says that too.
    /// </summary>
    private void ReportInterruptRequested(string fireInstanceId, string runningOn)
    {
        Logger.InterruptRequestRecorded(fireInstanceId, runningOn);

        if (livePeerVersions is { } peers
            && peers.TryGetValue(runningOn, out string? version)
            && !SchedulerNodeVersion.SupportsInterruptRequests(version)
            && undeliverableInterruptNodes.TryAdd(runningOn, 0))
        {
            Logger.InterruptRequestsUndeliverable(runningOn, version ?? "none", SchedulerNodeVersion.InterruptRequestsSince);
        }
    }

    /// <summary>
    /// Keeps what one read of the state table said about the live peers' versions, for
    /// <see cref="ReadsInterruptRequests" /> to answer from until the next check-in.
    /// </summary>
    private void RecordLivePeerVersions(List<SchedulerStateRecord> states, List<SchedulerStateRecord> failedInstances)
    {
        Dictionary<string, string?> peers = new(StringComparer.Ordinal);
        foreach (SchedulerStateRecord record in states)
        {
            if (record.SchedulerInstanceId != InstanceId && !failedInstances.Contains(record))
            {
                peers[record.SchedulerInstanceId] = record.QuartzVersion;
            }
        }

        livePeerVersions = peers;
    }

    /// <summary>
    /// Tells the scheduler that a check-in has reached the database, which is when it reads this node's
    /// interrupt requests. Returns at once: the scheduler reads and interrupts off this thread.
    /// </summary>
    internal void SignalInterruptRequestPoll()
    {
        if (signaler is IInterruptRequestPoller poller)
        {
            poller.PollInterruptRequests();
        }
    }
}
