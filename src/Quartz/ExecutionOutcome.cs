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
/// An occurrence being retried has no outcome yet: a failure the trigger's
/// <see cref="ITrigger.RetryPolicy" /> answers with another attempt is still in flight, and only the
/// attempt that ends it is classified.
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
    /// The job threw, and the trigger had no retry left to take. Matches
    /// <see cref="ContinuationCondition.OnFailure" />.
    /// </summary>
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
