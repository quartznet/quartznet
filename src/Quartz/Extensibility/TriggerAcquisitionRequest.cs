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
/// What the scheduler asks an <see cref="IJobStore" /> for when it acquires the next triggers
/// to fire.
/// </summary>
/// <remarks>
/// <para>
/// This is the store-level counterpart of the delegate-level
/// <c>Quartz.Impl.AdoJobStore.TriggerAcquisitionCriteria</c>: a store translates a request into
/// whatever criteria its backing storage understands. Keeping it a record means a future
/// acquisition dimension is an added optional property rather than another overload, and a
/// property added later must default to "no additional filtering" so that a store which ignores
/// it keeps behaving as it did.
/// </para>
/// </remarks>
/// <seealso cref="IJobStore.AcquireNextTriggers" />
public sealed record TriggerAcquisitionRequest
{
    /// <summary>
    /// Highest value of <see cref="ITrigger.NextFireTimeUtc" /> of the triggers to acquire. A
    /// store must not return a trigger that would fire later than this.
    /// </summary>
    public required DateTimeOffset NoLaterThan { get; init; }

    /// <summary>
    /// The maximum number of triggers to return. Must be at least one.
    /// </summary>
    public int MaxCount
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 1;

    /// <summary>
    /// How far past <see cref="NoLaterThan" /> a trigger may fire and still be batched into the
    /// same acquisition. Must not be negative.
    /// </summary>
    public TimeSpan TimeWindow
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero);
            field = value;
        }
    }

    /// <summary>
    /// Per-execution-group thread counts still available, which is the configured
    /// <see cref="Quartz.ExecutionLimits" /> less what is already running here. A limit of
    /// <see langword="null" /> means unlimited and <c>0</c> means the group must not fire.
    /// <see langword="null" /> when no execution limits are configured, in which case a store may
    /// ignore execution groups entirely.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only <see cref="ExecutionLimitScope.Node" /> limits arrive already lowered. A
    /// <see cref="ExecutionLimitScope.Cluster" /> limit arrives as configured, because this node's own
    /// firings are reservations the store is holding and taking them off here as well would count them
    /// twice. A store that means to honour cluster-scoped limits — every store whose
    /// <see cref="IJobStore.Clustered" /> can be <see langword="true" /> — subtracts its own in-flight
    /// count when it builds the ledger, through
    /// <see cref="Quartz.ExecutionLimits.CreateSlots" />. A store that does not, or one that has no
    /// cluster to speak of, simply enforces the configured number, which for a single node is the same
    /// thing.
    /// </para>
    /// </remarks>
    public ExecutionLimits? ExecutionLimits { get; init; }
}
