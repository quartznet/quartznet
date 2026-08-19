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
/// One execution of a job that is currently running somewhere in the scheduler's cluster, as recorded by
/// the job store.
/// </summary>
/// <remarks>
/// <para>
/// This is the cluster-aware complement to <see cref="IScheduler.GetCurrentlyExecutingJobs" />: with a
/// persistent job store, every node's fired triggers are visible here, not only the node the call is made
/// on. It is also richer than <see cref="TriggerState.Executing" />, which only says that at least one
/// execution of a trigger is running — this returns every one of them, each identified by its own
/// <see cref="FireInstanceId" />, so a trigger with several executions in flight at once is not collapsed
/// into a single fact.
/// </para>
/// <para>
/// Deliberately not <see cref="IJobExecutionContext" />: a remote node's job instance, result, merged job
/// data map and cancellation handle cannot be reconstructed from another process, so this type only carries
/// what the job store itself can answer for any node — identity and timing, not the live execution object.
/// </para>
/// </remarks>
/// <seealso cref="IScheduler.GetExecutingFireInstances" />
/// <seealso cref="TriggerState.Executing" />
/// <author>Marko Lahma (.NET)</author>
public sealed record ExecutingFireInstance
{
    /// <summary>
    /// Identifies this execution, and only this one: a trigger that fires again gets a new value.
    /// </summary>
    public required string FireInstanceId { get; init; }

    /// <summary>
    /// The trigger that started this execution.
    /// </summary>
    public required TriggerKey TriggerKey { get; init; }

    /// <summary>
    /// The job being executed.
    /// </summary>
    public required JobKey JobKey { get; init; }

    /// <summary>
    /// The instance id of the scheduler node running this execution.
    /// </summary>
    public required string SchedulerInstanceId { get; init; }

    /// <summary>
    /// When this execution started.
    /// </summary>
    public required DateTimeOffset FireTimeUtc { get; init; }

    /// <summary>
    /// The time the trigger was scheduled to fire at, or <see langword="null" /> if the trigger has no
    /// fixed schedule (e.g. it was fired manually).
    /// </summary>
    public DateTimeOffset? ScheduledFireTimeUtc { get; init; }
}
