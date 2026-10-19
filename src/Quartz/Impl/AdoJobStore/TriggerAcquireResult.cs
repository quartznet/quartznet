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
/// One candidate returned by <see cref="IDriverDelegate.SelectTriggersToAcquire" />.
/// </summary>
/// <remarks>
/// Deliberately not the trigger itself: acquisition looks at many more rows than it takes, so the
/// candidate carries only what deciding costs — the key to reserve, the job type to load, and the
/// execution group to count against its limit.
/// </remarks>
/// <param name="TriggerKey">The key of the trigger that could be fired.</param>
/// <param name="JobTypeName">The name of the job's type, as the trigger's job row stores it — the same
/// thing <see cref="JobHeader.JobTypeName" /> carries, and what the type loader is handed to load
/// the job. Not to be confused with <see cref="TriggerHeader.TriggerType" />, which is a store
/// discriminator rather than a type name.</param>
/// <param name="ExecutionGroup">The trigger's execution group, if it has one.</param>
public readonly record struct TriggerAcquireResult(TriggerKey TriggerKey, string JobTypeName, string? ExecutionGroup)
{
    /// <summary>
    /// Whether the trigger's job disallows concurrent execution, as its stored row says, or
    /// <see langword="null" /> when the delegate did not read it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Acquisition takes at most one trigger per such job into a batch. The stored flag is the answer to
    /// ask, because it is what the fire path obeys: it records the job's
    /// <see cref="IJobDetail.ConcurrentExecutionDisallowed" />, which a job configured with
    /// <c>DisallowConcurrentExecution()</c> says without any attribute on its type.
    /// </para>
    /// <para>
    /// Every dialect Quartz ships reads it. It is <see langword="null" /> from a delegate of your own
    /// that does not set it, and from a <c>StdAdoDelegate</c> subclass whose overridden acquisition
    /// statement does not project <c>IS_NONCONCURRENT</c>, as one written for 4.2 does not. Either is
    /// answered from the job type's <see cref="DisallowConcurrentExecutionAttribute" />, which is what
    /// acquisition asked before this property existed.
    /// </para>
    /// </remarks>
    public bool? ConcurrentExecutionDisallowed { get; init; }

    /// <summary>
    /// Whether the row was read and then refused because its execution group had no slot left in the
    /// <see cref="TriggerAcquisitionCriteria.ExecutionLimits" /> it was read under.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A candidate the delegate refuses for its limit is returned flagged rather than dropped, so that
    /// the store can see it. The acquisition statement limits its rows to the count asked for, and a
    /// row dropped after that limit is invisible to the store: it cannot tell a read that came back
    /// short because nothing else is due from one that came back short because the rows it did not see
    /// were refused. A node reading one trigger at a time then read the same refused row every round and
    /// never reached the due rows behind it (#3928). A flagged row is counted as skipped, is never read
    /// back or reserved, and is what the store reads past.
    /// </para>
    /// <para>
    /// Every dialect Quartz ships sets it. A delegate of your own that drops the refused rows instead
    /// keeps working as it did, and a <c>StdAdoDelegate</c> subclass that inspects what its base
    /// returns now sees the refused rows among them, flagged.
    /// </para>
    /// </remarks>
    public bool ExecutionGroupAtLimit { get; init; }

    /// <summary>
    /// The trigger's next fire time, as its row holds it, or <see langword="null" /> when the delegate did
    /// not read it.
    /// </summary>
    /// <remarks>
    /// A round that takes no <c>TRIGGER_ACCESS</c> lock (#4067) claims each candidate by this value before
    /// it reads the row in full, so that what it fires is the row as it stands once the claim holds it.
    /// Every dialect Quartz ships reads it; a delegate that does not makes the round take the lock, as a
    /// row the round cannot claim from its first read is a row it has to read before claiming.
    /// </remarks>
    public DateTimeOffset? NextFireTimeUtc { get; init; }

    /// <summary>
    /// The trigger's overlap policy, as its row holds it, or <see langword="null" /> when the delegate did
    /// not read it.
    /// </summary>
    /// <remarks>
    /// <see cref="Quartz.OverlapPolicy.Skip" />, <see cref="Quartz.OverlapPolicy.BufferOne" /> and
    /// <see cref="Quartz.OverlapPolicy.CancelPrevious" /> are decided at the fire from the trigger's running
    /// firings, which only the lock keeps still, so a candidate with one of them makes the round take it
    /// (#4067). Every dialect Quartz ships reads it; <see langword="null" /> is read as "lock", since
    /// nothing is known.
    /// </remarks>
    public OverlapPolicy? OverlapPolicy { get; init; }

    /// <summary>
    /// The trigger group the execution limits derived this candidate's key from, when it is not the
    /// candidate's own: a recovery trigger's original group (#4142). <see langword="null" /> otherwise.
    /// </summary>
    /// <remarks>
    /// Internal: only the store reads it, to name a held-back candidate's group by the key the scheduler
    /// counts that group's firings under, so that one of them ending wakes the scheduler for it (#4033).
    /// </remarks>
    internal string? LimitTriggerGroup { get; init; }

    /// <summary>
    /// Whether the trigger measures its interval from the previous run's completion, as its row's
    /// <c>FIXED_DELAY</c> says (#4140).
    /// </summary>
    /// <remarks>
    /// Such a trigger is held while it runs, as a <see cref="Quartz.OverlapPolicy.BufferOne" /> one is, and its
    /// fire stores it held against the paused groups, which only the lock keeps still, so a candidate that
    /// says so makes the round take it. Every dialect Quartz ships reads it. A delegate that does not
    /// answers <see langword="false" />, and a round that claims such a trigger without the lock rolls back
    /// at the fire and runs again under it.
    /// </remarks>
    public bool MeasureIntervalFromCompletion { get; init; }
}
