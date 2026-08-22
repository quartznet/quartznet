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
/// All trigger states known to Scheduler.
/// </summary>
/// <remarks>
/// Both the numeric values and the member names are a wire contract. The HTTP API returns this enum as
/// its name and accepts a state filter by name, so members must never be renamed; the numeric values are
/// persisted by job stores and still accepted on the wire, so members must never be renumbered or
/// reordered either. New members are appended.
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
public enum TriggerState
{
    /// <summary>
    /// Indicates that the <see cref="ITrigger" /> is in the "normal" state.
    /// </summary>
    Normal = 0,

    /// <summary>
    /// Indicates that the <see cref="ITrigger" /> is in the "paused" state.
    /// </summary>
    /// <remarks>
    /// A paused trigger reports "paused" even while a previously started execution of its job is still
    /// running; the pause is the actionable fact, so it takes precedence over <see cref="Executing" />.
    /// </remarks>
    Paused = 1,

    /// <summary>
    /// Indicates that the <see cref="ITrigger" /> is in the "complete" state.
    /// </summary>
    /// <remarks>
    /// "Complete" indicates that the trigger has not remaining fire-times in
    /// its schedule. A trigger whose final execution is still in progress reports
    /// <see cref="Executing" /> until that execution finishes.
    /// </remarks>
    Complete = 2,

    /// <summary>
    /// Indicates that the <see cref="ITrigger" /> is in the "error" state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <see cref="ITrigger" /> arrives at the error state when the scheduler
    /// attempts to fire it, but cannot due to an error creating and executing
    /// its related job. Often this is due to the <see cref="IJob" />'s
    /// class not existing in the classpath.
    /// </para>
    /// 
    /// <para>
    /// When the trigger is in the error state, the scheduler will make no
    /// attempts to fire it.
    /// </para>
    /// </remarks>
    Error = 3,

    /// <summary>
    /// Indicates that the <see cref="ITrigger" /> is in the "blocked" state.
    /// </summary>
    /// <remarks>
    /// A <see cref="ITrigger" /> arrives at the blocked state when a <b>different</b> trigger of the same
    /// job is currently executing and that job has a <see cref="DisallowConcurrentExecutionAttribute" />,
    /// so this trigger cannot fire. The trigger that is itself running reports
    /// <see cref="Executing" /> instead.
    /// </remarks>
    /// <seealso cref="DisallowConcurrentExecutionAttribute" />
    Blocked = 4,

    /// <summary>
    /// Indicates that the <see cref="ITrigger" /> does not exist.
    /// </summary>
    None = 5,

    /// <summary>
    /// Indicates that at least one execution started by this <see cref="ITrigger" /> is currently running.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unlike <see cref="IScheduler.GetCurrentlyExecutingJobs" />, which only sees the node it is called
    /// on, this state is observable across every node of a cluster when a persistent job store is used.
    /// </para>
    ///
    /// <para>
    /// Execution is not mutually exclusive with being scheduled: a trigger that allows concurrent
    /// execution can be running one or more jobs and still be due to fire again. It reports "executing"
    /// until the last of those executions finishes. A paused trigger reports <see cref="Paused" />
    /// and a trigger in the error state reports <see cref="Error" /> even while an execution is
    /// still running.
    /// </para>
    /// </remarks>
    Executing = 6
}
