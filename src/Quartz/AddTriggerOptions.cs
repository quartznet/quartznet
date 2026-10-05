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

namespace Quartz;

/// <summary>
/// How a trigger is stored on its own, without the job it fires.
/// </summary>
/// <remarks>
/// <para>
/// Defaults are the conservative ones: nothing is replaced, nothing is paused. So
/// <see langword="default"/> — which is what omitting the argument gives — is "store it, replace
/// nothing", and there is no third state between "not given" and "all defaults" for an implementer to
/// have to guess about.
/// </para>
/// <para>
/// This is a store-level type. <see cref="IScheduler" /> has no <c>AddTrigger</c>: a trigger reaches a
/// scheduler through <see cref="IScheduler.ScheduleJob(ITrigger, ScheduleJobOptions, CancellationToken)" />,
/// which is the same operation named for what it accomplishes. <see cref="IJobStore.AddTrigger" /> is
/// the storage half of it, and this is the storage half's options.
/// </para>
/// </remarks>
/// <seealso cref="IJobStore.AddTrigger" />
public readonly record struct AddTriggerOptions
{
    // What Paused was set to, apart from the two texts: either of them says paused too.
    private readonly bool paused;

    /// <summary>
    /// Over-write an already stored trigger with the same key. The name for
    /// <c>new AddTriggerOptions { Replace = true }</c>, which is what nearly every call that passes
    /// these options at all is saying.
    /// </summary>
    public static AddTriggerOptions Replacing => new() { Replace = true };

    /// <summary>
    /// Whether an already stored trigger with the same key is over-written. When false, storing a
    /// trigger whose key already exists throws <see cref="ObjectAlreadyExistsException" />.
    /// </summary>
    public bool Replace { get; init; }

    /// <summary>
    /// Whether the trigger is stored paused, in the same operation that stores it. Reads
    /// <see langword="true" /> when <see cref="PauseReason" /> or <see cref="PauseRequestedBy" /> is set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A store honours it when it says <see cref="IJobStore.SupportsStoringPaused" />: the trigger is
    /// written paused — paused-blocked when its job disallows concurrent execution and is running — with
    /// the pause's record, and is never acquirable in between. The record is the trigger's own, so it is
    /// what <see cref="IJobStore.GetTriggerPause" /> answers over a paused group's, and it replaces the
    /// record of a trigger stored over. A continuation is stored awaiting its parent whatever this says.
    /// </para>
    /// <para>
    /// The same as <see cref="ScheduleJobOptions.Paused" />, which <see cref="IJobStore.ScheduleJobs" />
    /// takes.
    /// </para>
    /// </remarks>
    public bool Paused
    {
        get => paused || !string.IsNullOrWhiteSpace(PauseReason) || !string.IsNullOrWhiteSpace(PauseRequestedBy);
        init => paused = value;
    }

    /// <summary>
    /// Why the trigger is stored paused, recorded as <see cref="PauseInfo.Reason" />. Setting it stores it
    /// paused. Blank reads as unset; longer than <see cref="PauseDetails.MaxReasonLength" /> is cut.
    /// </summary>
    public string? PauseReason { get; init; }

    /// <summary>
    /// Who asked for the trigger to be stored paused, recorded as <see cref="PauseInfo.RequestedBy" />.
    /// Setting it stores it paused. Blank reads as unset; longer than
    /// <see cref="PauseDetails.MaxRequestedByLength" /> is cut.
    /// </summary>
    public string? PauseRequestedBy { get; init; }

    /// <summary>
    /// The pause these options ask for, or <see langword="null" /> when they ask for none. Details that
    /// <see cref="PauseDetails.SaysNothing">say nothing</see> are the reasonless pause.
    /// </summary>
    internal PauseDetails? Pause => Paused ? new PauseDetails { Reason = PauseReason, RequestedBy = PauseRequestedBy } : null;

    /// <summary>
    /// The store-level options for one trigger of a <see cref="ScheduleJobOptions" /> call.
    /// </summary>
    internal static AddTriggerOptions From(ScheduleJobOptions options)
    {
        return new AddTriggerOptions
        {
            Replace = options.Replace,
            Paused = options.Paused,
            PauseReason = options.PauseReason,
            PauseRequestedBy = options.PauseRequestedBy
        };
    }
}
