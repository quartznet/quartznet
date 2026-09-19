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
/// Which outcomes of a parent trigger's firing release the continuation waiting on it.
/// </summary>
/// <remarks>
/// <para>
/// Flags rather than one outcome, so that "whenever it did not succeed" —
/// <c>OnFailure | OnCancellation</c> — needs no member of its own. An outcome the condition does
/// not name discards the continuation rather than leaving it waiting: a continuation settles
/// exactly once, on the firing it was waiting for.
/// </para>
/// <para>
/// Persisted as the integer, in the <c>CONTINUATION_CONDITION</c> column of the triggers table, so
/// the values are a storage contract: members are never renumbered and a new one is appended.
/// </para>
/// </remarks>
/// <seealso cref="Continuation" />
/// <seealso cref="ExecutionOutcome" />
[Flags]
public enum ContinuationCondition
{
    /// <summary>
    /// The parent's job ran to completion without throwing. Matches
    /// <see cref="ExecutionOutcome.Succeeded" />, and is what <see cref="Continuation.After" />
    /// assumes when the caller names no condition.
    /// </summary>
    OnSuccess = 1,

    /// <summary>
    /// The parent's job failed, and had no retry left to take. Matches
    /// <see cref="ExecutionOutcome.Failed" />.
    /// </summary>
    /// <remarks>
    /// A failure the trigger's <see cref="ITrigger.RetryPolicy" /> answers with another attempt
    /// settles nothing: the occurrence is still in flight, and the continuation keeps waiting until
    /// the attempts are spent or one of them succeeds.
    /// </remarks>
    OnFailure = 2,

    /// <summary>
    /// The parent's job was interrupted — its firing's cancellation token was signalled and the job
    /// stopped. Matches <see cref="ExecutionOutcome.Cancelled" />.
    /// </summary>
    OnCancellation = 4,

    /// <summary>
    /// A trigger listener vetoed the parent's firing, so its job never ran. Matches
    /// <see cref="ExecutionOutcome.Vetoed" />.
    /// </summary>
    OnVeto = 8,

    /// <summary>
    /// Every outcome the parent's firing can reach: success, failure, cancellation and veto.
    /// </summary>
    /// <remarks>
    /// The one condition a <em>deleted</em> parent also satisfies. A continuation waiting on a
    /// trigger somebody removed can never learn how that firing ended, so only this condition — which
    /// did not care — is released; anything narrower is parked in
    /// <see cref="TriggerState.Error" /> for an operator to see.
    /// </remarks>
    OnAnyOutcome = OnSuccess | OnFailure | OnCancellation | OnVeto
}
