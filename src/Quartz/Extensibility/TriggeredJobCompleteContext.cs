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
/// Everything a job store is told about a firing that has just finished.
/// </summary>
/// <remarks>
/// <para>
/// A context object rather than a parameter list, so that the next thing a completion has to carry
/// is a property rather than a new overload: <see cref="Outcome" /> and <see cref="Exception" />
/// were the first two, and neither had anywhere to go on
/// <see cref="IJobStore.TriggeredJobComplete" />.
/// </para>
/// <para>
/// The three required members are exactly what that method took. <see cref="Outcome" /> defaults to
/// <see cref="ExecutionOutcome.NotExecuted" />, which settles nothing — so a caller that knows only
/// the instruction, and a store reached through the compatibility path, behave as they did before
/// continuations existed.
/// </para>
/// </remarks>
/// <seealso cref="IJobStore.FiringComplete" />
public sealed class TriggeredJobCompleteContext
{
    /// <summary>
    /// The trigger whose firing completed, as the firing left it — its next fire time, its retry
    /// attempt and its fire instance id are the ones the store writes.
    /// </summary>
    public required IOperableTrigger Trigger { get; init; }

    /// <summary>
    /// The job that was fired. Its <see cref="IJobDetail.JobDataMap" /> is the one the execution
    /// leaves behind, which is what a store persisting job data writes.
    /// </summary>
    public required IJobDetail JobDetail { get; init; }

    /// <summary>
    /// What the trigger asked the scheduler to do about itself once the firing was over.
    /// </summary>
    public required SchedulerInstruction Instruction { get; init; }

    /// <summary>
    /// How the firing ended. Defaults to <see cref="ExecutionOutcome.NotExecuted" />: an occurrence
    /// that did not happen settles no continuation.
    /// </summary>
    public ExecutionOutcome Outcome { get; init; } = ExecutionOutcome.NotExecuted;

    /// <summary>
    /// The exception the firing ended with, when it ended with one, and <see langword="null" />
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// Present for a store that records execution history; the settlement of continuations reads
    /// <see cref="Outcome" /> alone, because "why it failed" is not something a schedule can branch
    /// on.
    /// </remarks>
    public Exception? Exception { get; init; }
}
