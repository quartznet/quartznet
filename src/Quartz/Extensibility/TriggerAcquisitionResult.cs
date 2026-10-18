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

namespace Quartz.Extensibility;

/// <summary>
/// What <see cref="IJobStore.AcquireNextTriggersAndFireDue" /> acquired: the triggers it fired as it
/// acquired them, what became of each, and the ones it left for the scheduler to fire when they are due.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Due" /> and <see cref="Fired" /> are index-aligned, as a <see cref="IJobStore.TriggersFired" />
/// batch is with its results, and the scheduler reads each pair the way it reads that batch: it runs a
/// fired bundle, releases a trigger that did not fire or failed to, and leaves a declined one alone.
/// </para>
/// <para>
/// A property left unset reads as an empty list, so a store that fires nothing on acquisition sets
/// <see cref="Pending" /> alone.
/// </para>
/// </remarks>
public sealed class TriggerAcquisitionResult
{
    /// <summary>
    /// The acquired triggers the store fired as it acquired them, in the order it fired them.
    /// </summary>
    public List<IOperableTrigger> Due
    {
        get => field ??= [];
        init;
    }

    /// <summary>
    /// What became of each trigger in <see cref="Due" />, at the same index: what
    /// <see cref="IJobStore.TriggersFired" /> would have answered for it.
    /// </summary>
    public List<TriggerFiredResult> Fired
    {
        get => field ??= [];
        init;
    }

    /// <summary>
    /// The acquired triggers not fired yet, in fire-time order. The scheduler waits for the first of
    /// them and fires them with <see cref="IJobStore.TriggersFired" />, as it does what
    /// <see cref="IJobStore.AcquireNextTriggers" /> returns.
    /// </summary>
    /// <remarks>
    /// Like that list, this one may be one the store keeps: the scheduler copies it before working with it.
    /// </remarks>
    public List<IOperableTrigger> Pending
    {
        get => field ??= [];
        init;
    }

    /// <summary>
    /// How many due triggers a running firing holds back from this scheduler: ones the store passed over
    /// because a job of theirs that disallows concurrent execution was executing, and, in a round that
    /// acquired nothing, triggers pinned to this node that a firing on another node holds blocked. A round
    /// that acquired nothing and says some are held has the scheduler look again soon rather than after its
    /// idle wait: it is not told when a firing on another node ends (#3988).
    /// </summary>
    /// <remarks>
    /// Internal, as <see cref="TriggerFiredResult.Blocked" /> is: the clustered ADO.NET store is the one
    /// that can be held back by a firing it does not run. The in-memory store's firings all end on the
    /// scheduler it answers, and their end wakes it.
    /// </remarks>
    internal int Blocked { get; init; }

    /// <summary>
    /// How many due triggers another node claimed first, in a round that acquired nothing because of it.
    /// The round found due work it could not have, and more may be due behind it, so the scheduler looks
    /// again soon rather than after its idle wait (#4067).
    /// </summary>
    /// <remarks>
    /// Rounds that take no lock reach for the same rows on every node, and the loser of each claim finds
    /// out only after the winner commits. Internal, as <see cref="Blocked" /> is: only the clustered
    /// ADO.NET store races another node for a row.
    /// </remarks>
    internal int ClaimedElsewhere { get; init; }

    /// <summary>
    /// Whether this round, which took no lock, lost a claim to another node, so that the rounds after it
    /// take the lock until the backlog clears (#4067). The next round waits on the lock, not on another
    /// node's claims, so a round that took nothing is followed by one at once rather than a look soon.
    /// </summary>
    /// <remarks>Internal, as <see cref="ClaimedElsewhere" /> is.</remarks>
    internal bool Contended { get; init; }

    /// <summary>
    /// When the latest of the firings on another node that hold this node's pinned triggers was fired, if
    /// any does. A later one than the last round saw is the job having ended and been taken again in
    /// between, so the scheduler starts looking soon again rather than less and less often.
    /// </summary>
    internal DateTimeOffset? LatestBlockingFiredUtc { get; init; }

    /// <summary>
    /// The execution groups whose limit held a due trigger back from this round, by the key the limit
    /// is evaluated under; <see langword="null" /> when none did. A firing of such a group ending on the
    /// scheduler's node frees the slot the trigger waited for, so the scheduler looks again at once
    /// rather than after its idle wait (#4033).
    /// </summary>
    /// <remarks>
    /// Internal, as <see cref="Blocked" /> is. The key is <see cref="ExecutionLimits.ResolveGroupKey" />'s,
    /// which is what the scheduler counts a firing against, so that the group a completion frees and the
    /// group the store named are the same string.
    /// </remarks>
    internal HashSet<string>? GroupsAtLimit { get; init; }

    /// <summary>
    /// The job store lock the round ran under, as the <c>LOCK_NAME</c> column spells it, or <c>none</c>
    /// for a round that took none (#4067); <see langword="null" /> for a store that says nothing, which is
    /// every store but the ADO.NET one. The scheduling loop tags its acquisition measurements with it.
    /// </summary>
    internal string? LockName { get; init; }

    /// <summary>
    /// Why the round took the lock, as the <c>quartz.jobstore.lock.reason</c> tag spells it, or
    /// <see langword="null" /> for a round that took none or a store that says nothing.
    /// </summary>
    internal string? LockReason { get; init; }
}
