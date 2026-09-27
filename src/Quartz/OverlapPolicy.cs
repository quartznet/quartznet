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

namespace Quartz;

/// <summary>
/// What a trigger does when one of its firings comes due while an earlier firing of the same trigger
/// is still running.
/// </summary>
/// <remarks>
/// <para>
/// The policy is the trigger's, and it only ever looks at the trigger's own firings: another trigger
/// of the same job running is not an overlap. A job marked
/// <see cref="DisallowConcurrentExecutionAttribute" /> is held back by that attribute first, as it
/// always was — while one of its firings runs, none of its triggers fires, whatever the policy says —
/// so the policy decides only what the attribute lets through.
/// </para>
/// <para>
/// A retry is never skipped, held or cancelled: it continues an occurrence that already started, and
/// the attempt it retries is over by the time it comes due.
/// </para>
/// <para>
/// Persisted as the integer, in the <c>OVERLAP_POLICY</c> column of the triggers table, so the values
/// are a storage contract: members are never renumbered and a new one is appended.
/// </para>
/// </remarks>
/// <seealso cref="ITrigger.OverlapPolicy" />
public enum OverlapPolicy
{
    /// <summary>
    /// The firing starts, alongside the one still running. This is what every trigger did before
    /// overlap policies existed, and what a trigger that names none does.
    /// </summary>
    Default = 0,

    /// <summary>
    /// The firing is dropped. The trigger moves on to the occurrence after it, and the skipped one is
    /// reported to <see cref="ITriggerListener.TriggerSkipped" /> and recorded in the misfire history
    /// with <see cref="MisfireReason.Overlap" />.
    /// </summary>
    /// <remarks>
    /// A skipped firing is not a misfire: the trigger's misfire instruction is not applied, and it is
    /// not counted as one.
    /// </remarks>
    Skip = 1,

    /// <summary>
    /// The firing waits for the running one to end, and then starts. However many occurrences came
    /// due meanwhile, at most one is kept.
    /// </summary>
    /// <remarks>
    /// While one of its firings runs the trigger is <see cref="TriggerState.Blocked" />, exactly as a
    /// trigger of a <see cref="DisallowConcurrentExecutionAttribute" /> job is, and what came due
    /// while it was blocked is settled the same way once it is released: an occurrence missed by more
    /// than the misfire threshold is the misfire instruction's to handle, and the default instruction
    /// fires it once, now.
    /// </remarks>
    BufferOne = 2,

    /// <summary>
    /// The running firing is interrupted — its cancellation token is signalled — and the new firing
    /// starts.
    /// </summary>
    /// <remarks>
    /// An interrupt reaches only a firing on the node that is starting the new one. A running firing on
    /// another node of a cluster cannot be interrupted from here, so the trigger waits for it instead,
    /// as <see cref="BufferOne" /> does. Cancellation is cooperative: a job that ignores its token runs
    /// on beside the new firing.
    /// </remarks>
    CancelPrevious = 3,

    /// <summary>
    /// The firing starts, alongside the one still running — stated explicitly rather than inherited.
    /// </summary>
    /// <remarks>
    /// Behaves as <see cref="Default" /> does. It never overrides
    /// <see cref="DisallowConcurrentExecutionAttribute" />.
    /// </remarks>
    AllowAll = 4,
}
