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
/// <seealso cref="QuartzScheduler" />
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
    /// The trigger's previous UTC fire time, before this fire.
    /// </summary>
    public required DateTimeOffset? PreviousFireTimeUtc { get; init; }

    /// <summary>
    /// The trigger's next UTC fire time, after this fire.
    /// </summary>
    public required DateTimeOffset? NextFireTimeUtc { get; init; }
}