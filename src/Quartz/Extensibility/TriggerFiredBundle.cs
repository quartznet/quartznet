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

using Quartz.Core;

namespace Quartz.Extensibility;

/// <summary>
/// What a job store hands the scheduler for one fire: the trigger that fired, the job it starts, and
/// the fire times the execution context reports.
/// </summary>
/// <remarks>
/// A required-init record rather than a positional constructor, because a store builds one of these on
/// its fire path and the shape used to end in three interchangeable <see cref="DateTimeOffset" />
/// values — transposing two compiled cleanly and produced a scheduler that reported wrong fire times
/// to every listener. The sibling SPI request records (<see cref="TriggerAcquisitionRequest" />) made
/// the same choice for the same reason.
/// </remarks>
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
public sealed record TriggerFiredBundle
{
    /// <summary>
    /// The job the fire executes.
    /// </summary>
    public required IJobDetail JobDetail { get; init; }

    /// <summary>
    /// The trigger that fired.
    /// </summary>
    public required IOperableTrigger Trigger { get; init; }

    /// <summary>
    /// The calendar the trigger fires against, when it names one.
    /// </summary>
    public ICalendar? Calendar { get; init; }

    /// <summary>
    /// Whether this fire recovers an execution a failed scheduler instance left behind.
    /// </summary>
    public required bool Recovering { get; init; }

    /// <summary>
    /// The UTC time the trigger actually fired.
    /// </summary>
    public required DateTimeOffset FireTimeUtc { get; init; }

    /// <summary>
    /// The UTC time the trigger was scheduled to fire, which trails <see cref="FireTimeUtc" /> by the
    /// firing latency.
    /// </summary>
    public required DateTimeOffset? ScheduledFireTimeUtc { get; init; }

    /// <summary>
    /// The UTC time this attempt was due: the fire time the trigger was acquired for, read before the fire
    /// moved it on. What <c>quartz.job.execution.start_delay</c> measures from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not <see cref="ScheduledFireTimeUtc" />, which names the occurrence: a retry is due at its own time and
    /// a misfire catch-up at the time its misfire policy gave it, and both keep the occurrence's
    /// <see cref="ScheduledFireTimeUtc" />. It is the scheduled time the persistent store's fired-trigger row
    /// records and the in-memory store's list of executing firings reports.
    /// </para>
    /// <para>
    /// Internal: only the two stores Quartz ships set it, so a store of somebody else's needs no new member to
    /// keep building bundles. Without it the histogram measures from <see cref="ScheduledFireTimeUtc" />.
    /// </para>
    /// </remarks>
    internal DateTimeOffset? DueTimeUtc { get; init; }

    /// <summary>
    /// The trigger's previous UTC fire time, before this fire.
    /// </summary>
    public required DateTimeOffset? PreviousFireTimeUtc { get; init; }

    /// <summary>
    /// The trigger's next UTC fire time, after this fire.
    /// </summary>
    public required DateTimeOffset? NextFireTimeUtc { get; init; }

    /// <summary>
    /// The fire instance ids of the trigger's running firings this fire replaces, or
    /// <see langword="null" /> when it replaces none.
    /// </summary>
    /// <remarks>
    /// Set by a store when the trigger's <see cref="ITrigger.OverlapPolicy" /> is
    /// <see cref="OverlapPolicy.CancelPrevious" /> and earlier firings of it are running on this node.
    /// The scheduler interrupts each of them once the store has recorded this fire and before it runs the
    /// job. A firing on another node is never named here: a clustered store records an interrupt request
    /// on it for that node to act on, or holds the trigger back when that node does not read requests.
    /// </remarks>
    public IReadOnlyList<string>? SupersededFireInstanceIds { get; init; }
}