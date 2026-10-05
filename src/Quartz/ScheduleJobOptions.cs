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
/// How jobs and their triggers are stored when they are scheduled together.
/// </summary>
/// <remarks>
/// Defaults are the conservative ones: nothing is replaced, nothing is paused. So
/// <see langword="default"/> — which is what omitting the argument gives — is "store them, replace
/// nothing", and there is no third state between "not given" and "all defaults".
/// <para>
/// This is deliberately not <see cref="AddJobOptions" />. That one also carries
/// <see cref="AddJobOptions.StoreNonDurableWhileAwaitingScheduling" />, which has no meaning here: a
/// trigger is always supplied, so the job is never awaiting scheduling. Its
/// <see cref="AddJobOptions.Replace" /> is about the job alone, where this one covers the job and its
/// triggers together.
/// </para>
/// </remarks>
/// <seealso cref="IScheduler.ScheduleJobs" />
public readonly record struct ScheduleJobOptions
{
    // What Paused was set to, apart from the two texts: either of them says paused too.
    private readonly bool paused;

    /// <summary>
    /// Over-write already stored jobs and triggers with the same keys. The name for
    /// <c>new ScheduleJobOptions { Replace = true }</c>, which is what nearly every call that passes
    /// these options at all is saying.
    /// </summary>
    public static ScheduleJobOptions Replacing => new() { Replace = true };

    /// <summary>
    /// Whether already stored jobs and triggers with the same keys are over-written. When false,
    /// scheduling a job or trigger whose key already exists throws
    /// <see cref="ObjectAlreadyExistsException" />.
    /// </summary>
    public bool Replace { get; init; }

    /// <summary>
    /// Whether the triggers are stored paused, so none of them fires until it is resumed. Reads
    /// <see langword="true" /> when <see cref="PauseReason" /> or <see cref="PauseRequestedBy" /> is set.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The store writes each trigger paused in the operation that stores it, so no trigger is acquirable
    /// in between, as it is between a <c>ScheduleJob</c> and a <c>PauseTrigger</c>. A trigger whose job
    /// disallows concurrent execution and is running is stored paused-blocked. Over a paused group, and
    /// over a trigger it replaces, the pause and the record are this one.
    /// </para>
    /// <para>
    /// A store that does not say <see cref="IJobStore.SupportsStoringPaused" /> stores the triggers as
    /// usual, and the scheduler pauses them straight afterwards: a trigger due at once can fire in
    /// between. Every store Quartz ships says it.
    /// </para>
    /// <para>
    /// A continuation waits for its parent rather than for a resume, so the scheduler refuses one stored
    /// paused.
    /// </para>
    /// </remarks>
    public bool Paused
    {
        get => paused || !string.IsNullOrWhiteSpace(PauseReason) || !string.IsNullOrWhiteSpace(PauseRequestedBy);
        init => paused = value;
    }

    /// <summary>
    /// Why the triggers are stored paused, recorded on each as <see cref="PauseInfo.Reason" />. Setting it
    /// stores them paused. Blank reads as unset; longer than <see cref="PauseDetails.MaxReasonLength" /> is cut.
    /// </summary>
    public string? PauseReason { get; init; }

    /// <summary>
    /// Who asked for the triggers to be stored paused, recorded on each as <see cref="PauseInfo.RequestedBy" />.
    /// Setting it stores them paused. Blank reads as unset; longer than
    /// <see cref="PauseDetails.MaxRequestedByLength" /> is cut.
    /// </summary>
    public string? PauseRequestedBy { get; init; }

    /// <summary>
    /// The pause these options ask for, or <see langword="null" /> when they ask for none. Details that
    /// <see cref="PauseDetails.SaysNothing">say nothing</see> are the reasonless pause.
    /// </summary>
    internal PauseDetails? Pause => Paused ? new PauseDetails { Reason = PauseReason, RequestedBy = PauseRequestedBy } : null;
}
