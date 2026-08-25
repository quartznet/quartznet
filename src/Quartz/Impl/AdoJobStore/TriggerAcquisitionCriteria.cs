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

using Quartz.Extensibility;

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
    /// Job type names to exclude from acquisition, copied from
    /// <see cref="Quartz.Extensibility.TriggerAcquisitionRequest.ExcludedJobTypeNames" /> — or set by
    /// a derived store's <c>CreateAcquisitionCriteria</c>, which is how a deployment says what this
    /// node will not pick up.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="StdAdoDelegate" /> keeps the rows out in the acquisition SQL, where comparison
    /// follows the job-class column's collation. A delegate is free to ignore the property, as it is
    /// with every other one here — <see cref="AdoJobStoreBase" /> post-filters the results ordinally
    /// afterwards, so an uncooperative delegate degrades to a wasted read rather than to running work
    /// the deployment excluded.
    /// </para>
    /// <para>
    /// Entries must be non-blank, and there may be at most 1,000 of them — Oracle's ceiling on an
    /// <c>IN</c> list. Blank is rejected rather than skipped because one would make the clause
    /// <c>NOT IN (…, NULL)</c>, which matches no row at all and would stop the node acquiring
    /// anything.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">An entry is blank, or there are too many of them.</exception>
    public IReadOnlyCollection<string>? ExcludedJobTypeNames
    {
        get;
        init => field = JobTypeExclusions.Validated(value, nameof(value));
    }

    /// <summary>
    /// What the whole cluster already holds in flight per (execution group, trigger group) pair, which
    /// is what a <see cref="ExecutionLimitScope.Cluster" /> limit is counted against.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="AdoJobStoreBase" /> fills this from
    /// <see cref="IDriverDelegate.SelectExecutionGroupsInFlight" /> once per acquisition attempt, and
    /// only when <see cref="Quartz.ExecutionLimits.HasClusterScopedLimits" /> says a cluster-scoped
    /// limit exists — so a configuration that uses none pays nothing. A
    /// <see cref="AdoJobStoreBase.CreateAcquisitionCriteria" /> override that sets this itself is left
    /// alone, which is how a store keeping the count somewhere other than the fired-triggers table says
    /// so.
    /// </para>
    /// <para>
    /// <see langword="null" /> means "not counted", not "nothing in flight": a delegate handed
    /// <see langword="null" /> enforces the configured numbers as written, which is correct when no
    /// limit is cluster-scoped and would be wrong if it were treated as an empty cluster.
    /// </para>
    /// </remarks>
    public IReadOnlyCollection<ExecutionGroupInFlight>? ClusterInFlight { get; init; }

    /// <summary>
    /// Instant before which a node's last check-in is considered stale, releasing its pinned
    /// triggers to other nodes (preferred node / node affinity).
    /// </summary>
    public required DateTimeOffset LiveNodeCutoff { get; init; }
}
