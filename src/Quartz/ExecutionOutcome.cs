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
/// How one firing of a trigger ended.
/// </summary>
/// <remarks>
/// <para>
/// The run shell classifies every firing it completes, and hands the answer to the job store on
/// <see cref="Extensibility.TriggeredJobCompleteContext.Outcome" />. What the store does with it is
/// release or discard the continuations waiting on that trigger.
/// </para>
/// <para>
/// It says what the firing did, and nothing about what the schedule makes of it. A job that ran and
/// threw is <see cref="Failed" /> whether or not the trigger's <see cref="ITrigger.RetryPolicy" />
/// then asked for another attempt — that request is
/// <see cref="SchedulerInstruction.RetryTrigger" />, on
/// <see cref="Extensibility.TriggeredJobCompleteContext.Instruction" />, and it is what tells a store
/// the occurrence is not finished. Settling continuations is skipped on that instruction rather than
/// on the outcome, so a store reading the outcome alone never mistakes an attempt for a verdict.
/// </para>
/// </remarks>
/// <seealso cref="ContinuationCondition" />
public enum ExecutionOutcome
{
    /// <summary>
    /// The job ran and returned. Matches <see cref="ContinuationCondition.OnSuccess" />.
    /// </summary>
    Succeeded,

    /// <summary>
    /// The job ran and threw. Matches <see cref="ContinuationCondition.OnFailure" />.
    /// </summary>
    /// <remarks>
    /// Reported for every failed run, the ones the trigger answers with a retry included: the job did
    /// run and it did throw. What tells the two apart is
    /// <see cref="Extensibility.TriggeredJobCompleteContext.Instruction" /> —
    /// <see cref="SchedulerInstruction.RetryTrigger" /> when the occurrence has an attempt left.
    /// </remarks>
    Failed,

    /// <summary>
    /// The job was interrupted: the firing's cancellation token was signalled and the job stopped
    /// rather than finished. Matches <see cref="ContinuationCondition.OnCancellation" />.
    /// </summary>
    Cancelled,

    /// <summary>
    /// A trigger listener vetoed the firing, so the job never ran. Matches
    /// <see cref="ContinuationCondition.OnVeto" />.
    /// </summary>
    Vetoed,

    /// <summary>
    /// The occurrence did not happen at all — the job could not be instantiated, a listener
    /// abandoned it before execution, or the scheduler could not dispatch it. Matches no
    /// <see cref="ContinuationCondition" />: nothing happened, so nothing is settled and a
    /// continuation waiting on this trigger keeps waiting.
    /// </summary>
    NotExecuted
}
