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
/// What <see cref="IDriverDelegate.SelectTriggersToAcquire" /> should consider when picking the next
/// triggers to fire.
/// </summary>
/// <remarks>
/// <para>
/// This is the extension point for acquisition filtering (see issue #2238). Every future way of
/// narrowing what a node will pick up — additional trigger predicates, per-node partitioning, more
/// capacity dimensions than the execution groups already here — belongs on this record as another
/// optional property, so that adding one is additive and does not change the delegate's signature or
/// break dialect delegates that override it.
/// </para>
/// <para>
/// Consequently a property added later must default to "no additional filtering", so that an
/// implementation which ignores it keeps behaving as it did.
/// </para>
/// </remarks>
public sealed record TriggerAcquisitionCriteria
{
    /// <summary>
    /// Highest value of <see cref="ITrigger.NextFireTimeUtc" /> of the triggers to acquire.
    /// </summary>
    public required DateTimeOffset NoLaterThan { get; init; }

    /// <summary>
    /// Lowest value of <see cref="ITrigger.NextFireTimeUtc" /> of the triggers to acquire, which is
    /// what keeps a misfired trigger out of the acquisition path. Only applies to triggers that have a
    /// misfire instruction.
    /// </summary>
    public required DateTimeOffset NoEarlierThan { get; init; }

    /// <summary>
    /// Maximum number of triggers to return.
    /// </summary>
    public required int MaxCount { get; init; }

    /// <summary>
    /// Available slots per execution group, or <see langword="null" /> when no execution limits are
    /// configured. The snapshot is immutable; a delegate that counts slots down as it takes rows
    /// works on <c>ToWorkingCopy()</c>, because the caller may reuse this instance across retries.
    /// </summary>
    public ExecutionLimits? ExecutionLimits { get; init; }

    /// <summary>
    /// Instant before which a node's last check-in is considered stale, releasing its pinned
    /// triggers to other nodes (preferred node / node affinity).
    /// </summary>
    public required DateTimeOffset LiveNodeCutoff { get; init; }
}
