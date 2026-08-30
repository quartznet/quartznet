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
/// Instructs Scheduler what to do with a trigger and job.
/// </summary>
/// <author>Marko Lahma (.NET)</author>
public enum SchedulerInstruction
{
    /// <summary>
    /// Instructs the <see cref="IScheduler" /> that the <see cref="ITrigger" />
    /// has no further instructions.
    /// </summary>
    NoInstruction,

    /// <summary>
    /// Instructs the <see cref="IScheduler" /> that the <see cref="ITrigger" />
    /// wants the <see cref="IJobDetail" /> to re-Execute
    /// immediately. If not in a 'RECOVERING' or 'FAILED_OVER' situation, the
    /// execution context will be re-used (giving the <see cref="IJob" /> the
    /// ability to 'see' anything placed in the context by its last execution).
    /// </summary>      
    ReExecuteJob,

    /// <summary>
    /// Instructs the <see cref="IScheduler" /> that the <see cref="ITrigger" />
    /// should be put in the <see cref="TriggerState.Complete" /> state.
    /// </summary>
    SetTriggerComplete,

    /// <summary>
    /// Instructs the <see cref="IScheduler" /> that the <see cref="ITrigger" />
    /// wants itself deleted.
    /// </summary>
    DeleteTrigger,

    /// <summary>
    /// Instructs the <see cref="IScheduler" /> that all <see cref="ITrigger" />
    /// s referencing the same <see cref="IJobDetail" /> as
    /// this one should be put in the <see cref="TriggerState.Complete" /> state.
    /// </summary>
    SetAllJobTriggersComplete,

    /// <summary>
    /// Instructs the <see cref="IScheduler" /> that all <see cref="ITrigger" />
    /// s referencing the same <see cref="IJobDetail" /> as
    /// this one should be put in the <see cref="TriggerState.Error" /> state.
    /// </summary>
    SetAllJobTriggersError,

    /// <summary>
    /// Instructs the <see cref="IScheduler" /> that the <see cref="ITrigger" />
    /// should be put in the <see cref="TriggerState.Error" /> state.
    /// </summary>
    SetTriggerError,

    /// <summary>
    /// Instructs the <see cref="IScheduler" /> that the <see cref="ITrigger" /> has scheduled a
    /// retry of the occurrence that just failed, and that the job store should store the retry
    /// instant and the attempt count it now carries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A retry needs an instruction of its own because <see cref="NoInstruction" /> means the
    /// opposite: neither store writes the trigger at completion, since the regular next fire time
    /// was already written when the trigger fired. A retry moves that time, so it has to be said.
    /// </para>
    /// <para>
    /// The trigger is left waiting, not in error — a failed occurrence with attempts left is not a
    /// broken trigger. It is decided by <c>TriggerBase.ExecutionComplete</c> from the trigger's
    /// <see cref="ITrigger.RetryPolicy" />, and never returned by a trigger that has none.
    /// </para>
    /// </remarks>
    /// <seealso cref="Quartz.RetryPolicy" />
    RetryTrigger
}